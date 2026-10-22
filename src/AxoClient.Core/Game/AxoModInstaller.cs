using System.Reflection;
using System.Text.Json;
using AxoClient.Core.Common;
using AxoClient.Core.Content;

namespace AxoClient.Core.Game;

public sealed class AxoModManifest
{
    public int FormatVersion { get; set; } = 1;
    public List<AxoModEntry> Entries { get; set; } = [];
}

/// <summary>Eine Jar der AxoMod: für welche MC-Versionen und Loader sie gilt.</summary>
public sealed class AxoModEntry
{
    public List<string> Minecraft { get; set; } = [];
    public List<LoaderType> Loaders { get; set; } = [];
    public string File { get; set; } = "";

    /// <summary><c>full</c> oder <c>reduced</c> (1.8.9: ohne Axo-Menü).</summary>
    public string Level { get; set; } = "full";

    /// <summary>SHA-256 der Jar (klein, hex); schreibt <c>mod/build-all.ps1</c>.</summary>
    public string? Sha256 { get; set; }
}

/// <summary>
/// Liefert die passende AxoMod-Jar aus den mitgelieferten Dateien (<c>axomod-versions.json</c>). Die Mod kommt mit
/// jedem Launcher-Update mit, wird nie geteilt und nie in .mrpack-Exporte aufgenommen.
/// </summary>
public sealed class AxoModInstaller
{
    public const string FilePrefix = "axoclient-";
    private const string ResourcePrefix = "axomod/";
    private readonly Assembly _assembly;
    private readonly AxoModManifest _manifest;

    public AxoModInstaller() : this(typeof(AxoModInstaller).Assembly)
    {
    }

    internal AxoModInstaller(Assembly assembly, AxoModManifest? manifest = null)
    {
        _assembly = assembly;
        _manifest = manifest ?? LoadManifest(assembly);
    }

    private static AxoModManifest LoadManifest(Assembly assembly)
    {
        using var stream = assembly.GetManifestResourceStream(ResourcePrefix + "axomod-versions.json");
        if (stream == null)
            return new AxoModManifest();
        return JsonSerializer.Deserialize<AxoModManifest>(stream, Json.Options) ?? new AxoModManifest();
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

    public async Task<byte[]> ReadJarAsync(AxoModEntry entry, CancellationToken ct)
    {
        await using var stream = _assembly.GetManifestResourceStream(ResourcePrefix + entry.File)
                                 ?? throw new AxoException("axomod.missing");
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        if (entry.Sha256 is { Length: > 0 } expected
            && !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new AxoException("axomod.missing");
        return bytes;
    }

    /// <summary>Legt die richtige Jar in <c>mods/</c> und entfernt alte AxoMod-Jars.</summary>
    public async Task<ContentEntry?> SyncAsync(string modsDir, string minecraftVersion, LoaderType loader, bool enabled,
        CancellationToken ct)
    {
        Directory.CreateDirectory(modsDir);
        var entry = enabled ? Find(minecraftVersion, loader) : null;
        foreach (var old in Directory.EnumerateFiles(modsDir, FilePrefix + "*"))
            if (entry == null || !Path.GetFileName(old).Equals(entry.File, StringComparison.OrdinalIgnoreCase))
                File.Delete(old);
        if (entry == null)
            return null;

        var bytes = await ReadJarAsync(entry, ct);
        var target = Path.Combine(modsDir, entry.File);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(bytes)).ToLowerInvariant();
        if (!File.Exists(target) || !Text.Sha1Hex(new MemoryStream(await File.ReadAllBytesAsync(target, ct))).Equals(hash))
            await File.WriteAllBytesAsync(target, bytes, ct);
        var info = new FileInfo(target);
        return new ContentEntry
        {
            File = entry.File,
            Name = "AxoClient",
            Version = AppInfo.Version,
            Source = ContentSource.AxoClient,
            Hashes = new ContentHashes(hash, null),
            Size = bytes.Length,
            Modified = info.LastWriteTimeUtc
        };
    }

    public static bool IsAxoModFile(string fileName) => fileName.StartsWith(FilePrefix, StringComparison.OrdinalIgnoreCase);
}
