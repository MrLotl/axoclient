using System.Reflection;
using System.Text.Json;
using AxoClient.Common;
using AxoClient.DataModel.Content;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;

namespace AxoClient.Game
{
    /// <summary>
    /// Liefert die passende AxoMod-Jar aus den mitgelieferten Dateien (<c>axomod-versions.json</c>). Die Mod kommt mit
    /// jedem Launcher-Update mit, wird nie geteilt und nie in .mrpack-Exporte aufgenommen.
    /// </summary>
    public sealed class AxoModInstaller : IAxoModInstaller
    {
        private readonly Assembly _assembly;
        private readonly ITextHelper _textHelper;
        private readonly IAppInfo _appInfo;
        private readonly IJsonHelper _jsonHelper;
        public const string FilePrefix = "axoclient-";
        private const string ResourcePrefix = "axomod/";
        private readonly AxoModManifest _manifest;

        public AxoModInstaller(ITextHelper textHelper, IAppInfo appInfo, IJsonHelper jsonHelper)
            : this(typeof(AxoModInstaller).Assembly, textHelper, appInfo, jsonHelper)
        {
        }

        internal AxoModInstaller(
            Assembly assembly,
            ITextHelper textHelper,
            IAppInfo appInfo,
            IJsonHelper jsonHelper,
            AxoModManifest? manifest = null)
        {
            _assembly = assembly;
            _textHelper = textHelper;
            _appInfo = appInfo;
            _jsonHelper = jsonHelper;
            _manifest = manifest ?? loadManifest(assembly);
        }

        private AxoModManifest loadManifest(Assembly assembly)
        {
            using var stream = assembly.GetManifestResourceStream(ResourcePrefix + "axomod-versions.json");
            if (stream == null)
            {
                return new AxoModManifest();
            }

            return JsonSerializer.Deserialize<AxoModManifest>(stream, _jsonHelper.Options) ?? new AxoModManifest();
        }

        public AxoModEntry? Find(string minecraftVersion, LoaderType loader)
        {
            // Quilt nutzt den Fabric-Build
            var effective = loader == LoaderType.Quilt ? LoaderType.Fabric : loader;
            return _manifest.Entries.FirstOrDefault(e => e.Minecraft.Contains(minecraftVersion) && e.Loaders.Contains(effective)
                                                         && _assembly.GetManifestResourceInfo(ResourcePrefix + e.File) != null);
        }

        public bool IsSupported(string minecraftVersion, LoaderType loader) => Find(minecraftVersion, loader) != null;

        public IReadOnlyList<string> SupportedMinecraftVersions =>
            _manifest.Entries.Where(e => _assembly.GetManifestResourceInfo(ResourcePrefix + e.File) != null)
                .SelectMany(e => e.Minecraft).Distinct().ToList();

        public async Task<byte[]> ReadJar(AxoModEntry entry, CancellationToken ct)
        {
            await using var stream = _assembly.GetManifestResourceStream(ResourcePrefix + entry.File)
                                     ?? throw new AxoException("axomod.missing");
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct);
            var bytes = buffer.ToArray();
            if (entry.Sha256 is { Length: > 0 } expected
                && !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new AxoException("axomod.missing");
            }

            return bytes;
        }

        /// <summary>
        /// Ob die Jar unter <paramref name="path"/> genau die mitgelieferte ist (SHA-256). Verglichen wird der Inhalt, nicht
        /// die Launcher-Version: ein neuer Mod-Build ohne neue Launcher-Version (z. B. 1.0.0-dev) wird so trotzdem ersetzt.
        /// </summary>
        public bool IsCurrent(AxoModEntry entry, string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var expected = entry.Sha256;
            if (expected is not { Length: > 0 })
            {
                using var embedded = _assembly.GetManifestResourceStream(ResourcePrefix + entry.File);
                if (embedded == null)
                {
                    return true; // nichts zum Vergleichen da: die vorhandene Jar behalten
                }

                expected = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(embedded));
            }
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Legt die richtige Jar in <c>mods/</c> und entfernt alte AxoMod-Jars.</summary>
        public async Task<ContentEntry?> Sync(string modsDir, string minecraftVersion, LoaderType loader, bool enabled,
            CancellationToken ct)
        {
            Directory.CreateDirectory(modsDir);
            var entry = enabled ? Find(minecraftVersion, loader) : null;
            foreach (var old in Directory.EnumerateFiles(modsDir, FilePrefix + "*"))
            {
                if (entry == null || !Path.GetFileName(old).Equals(entry.File, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(old);
                }
            }

            if (entry == null)
            {
                return null;
            }

            var bytes = await ReadJar(entry, ct);
            var target = Path.Combine(modsDir, entry.File);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).ToLowerInvariant();
            if (!File.Exists(target) || !_textHelper.Sha1Hex(new MemoryStream(await File.ReadAllBytesAsync(target, ct))).Equals(hash))
            {
                await File.WriteAllBytesAsync(target, bytes, ct);
            }

            var info = new FileInfo(target);
            return new ContentEntry
            {
                File = entry.File,
                Name = "AxoClient",
                Version = _appInfo.Version,
                Source = ContentSource.AxoClient,
                Hashes = new ContentHashes(hash, null),
                Size = bytes.Length,
                Modified = info.LastWriteTimeUtc
            };
        }

        public bool IsAxoModFile(string fileName) => fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase);
    }
}
