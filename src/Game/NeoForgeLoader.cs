using System.Xml.Linq;
using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;
using CmlLib.Core;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installer.NeoForge;
using CmlLib.Core.Installer.NeoForge.Installers;

namespace AxoClient.Game
{
    public sealed class NeoForgeLoader : ILoaderInstaller
    {
        private readonly IApiClient _api;
        private readonly ILoaderNames _loaderNames;
        private readonly IGameVersionHelper _gameVersionHelper;
        private const string Metadata = "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";

        // NeoForge für 1.20.1 liegt noch unter dem alten Namen net.neoforged:forge und ist ein Forge-Installer
        private const string LegacyMaven = "https://maven.neoforged.net/releases/net/neoforged/forge";
        private const string ForgeMetadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";
        public const string LegacyMinecraft = "1.20.1";

        public NeoForgeLoader(IApiClient api, ILoaderNames loaderNames, IGameVersionHelper gameVersionHelper)
        {
            _api = api;
            _loaderNames = loaderNames;
            _gameVersionHelper = gameVersionHelper;
        }

        public LoaderType Type => LoaderType.NeoForge;

        /// <summary>
        /// NeoForge-Versionen für 1.20.1, neueste zuerst. Builds, die es mit gleicher Nummer auch bei Forge gibt,
        /// fallen weg – beide würden sonst denselben versions-Ordner belegen.
        /// </summary>
        internal List<string> LegacyVersions(XDocument neoForge, XDocument forge)
        {
            var prefix = LegacyMinecraft + "-";
            var taken = forge.Descendants("version").Select(element => element.Value).ToHashSet(StringComparer.Ordinal);
            return neoForge.Descendants("version").Select(element => element.Value)
                .Where(version => version.StartsWith(prefix, StringComparison.Ordinal) && !taken.Contains(version))
                .Select(version => version[prefix.Length..])
                .Where(_gameVersionHelper.IsSafeVersion)
                .OrderByDescending(version => System.Version.TryParse(version, out var parsed) ? parsed : new System.Version())
                .ToList();
        }

        private async Task<List<string>> legacyVersions(CancellationToken cancellationToken)
        {
            var neoForge = XDocument.Parse(await _api.GetString($"{LegacyMaven}/maven-metadata.xml", TimeSpan.FromHours(6), cancellationToken));
            var forge = XDocument.Parse(await _api.GetString(ForgeMetadata, TimeSpan.FromHours(6), cancellationToken));
            return LegacyVersions(neoForge, forge);
        }

        /// <summary>MC 1.21.4 → NeoForge 21.4.x; MC 26.1 → NeoForge 26.1.x.</summary>
        internal string? Prefix(string minecraftVersion)
        {
            var parsed = _gameVersionHelper.Parse(minecraftVersion);
            if (parsed == null)
            {
                return null;
            }

            var (major, minor, patch) = parsed.Value;
            return major == 1 ? $"{minor}.{patch}." : $"{major}.{minor}.";
        }

        public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(string minecraftVersion, CancellationToken cancellationToken)
        {
            if (minecraftVersion == LegacyMinecraft)
            {
                var legacy = await legacyVersions(cancellationToken);
                return legacy.Select((version, index) => new LoaderVersionInfo(version, true, index == 0)).ToList();
            }
            var prefix = Prefix(minecraftVersion);
            if (prefix == null)
            {
                return [];
            }

            var xml = XDocument.Parse(await _api.GetString(Metadata, TimeSpan.FromHours(6), cancellationToken));
            var versions = xml.Descendants("version").Select(element => element.Value)
                .Where(version => version.StartsWith(prefix, StringComparison.Ordinal))
                .Reverse()
                .ToList();
            var recommended = versions.FirstOrDefault(version => !version.Contains("beta") && !version.Contains("alpha")) ?? versions.FirstOrDefault();
            return versions.Select(version => new LoaderVersionInfo(version, !version.Contains("beta") && !version.Contains("alpha"), version == recommended)).ToList();
        }

        public async Task<string> Install(LoaderInstallContext context)
        {
            context.Status.Report("loader.installing:NeoForge");
            if (context.MinecraftVersion == LegacyMinecraft)
            {
                return await installLegacy(context);
            }

            var neoForge = new NeoForgeInstaller(context.Launcher);
            var options = new NeoForgeInstallOptions
            {
                FileProgress = context.Files,
                ByteProgress = context.Bytes,
                InstallerOutput = new Progress<string>(_ => { }),
                SkipIfAlreadyInstalled = true,
                CancellationToken = context.Token
            };
            return _gameVersionHelper.IsSafeVersion(context.LoaderVersion)
                ? await neoForge.Install(context.MinecraftVersion, context.LoaderVersion!, options)
                : await neoForge.Install(context.MinecraftVersion, options);
        }

        /// <summary>1.20.1: der Forge-Installer von CmlLib, nur mit dem Installer-Jar von maven.neoforged.net.</summary>
        private async Task<string> installLegacy(LoaderInstallContext context)
        {
            var minecraftVersion = context.MinecraftVersion;
            var version = _gameVersionHelper.IsSafeVersion(context.LoaderVersion)
                ? context.LoaderVersion!
                : (await legacyVersions(context.Token)).FirstOrDefault()
                  ?? throw new AxoException("game.loaderUnsupported", new() { ["loader"] = _loaderNames.Display(Type), ["version"] = minecraftVersion });
            var forge = new ForgeInstaller(context.Launcher);
            var options = new ForgeInstallOptions
            {
                FileProgress = context.Files,
                ByteProgress = context.Bytes,
                InstallerOutput = new Progress<string>(_ => { }),
                SkipIfAlreadyInstalled = true,
                CancellationToken = context.Token
            };
            return await forge.Install(LegacyForgeVersion(minecraftVersion, version), options);
        }

        internal CmlLib.Core.Installer.Forge.Versions.ForgeVersion LegacyForgeVersion(string minecraftVersion, string version)
        {
            var full = $"{minecraftVersion}-{version}";
            return new CmlLib.Core.Installer.Forge.Versions.ForgeVersion(minecraftVersion, version)
            {
                Files = [new ForgeVersionFile { Type = "installer", DirectUrl = $"{LegacyMaven}/{full}/forge-{full}-installer.jar" }]
            };
        }

        public bool IsInstalled(MinecraftPath path, string minecraftVersion, string? loaderVersion) =>
            loaderVersion != null && File.Exists(path.GetVersionJsonPath(_gameVersionHelper.NeoForgeVersionId(minecraftVersion, loaderVersion)));
    }
}
