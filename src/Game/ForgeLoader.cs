using System.Text.Json;
using System.Xml.Linq;
using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;
using CmlLib.Core;
using CmlLib.Core.Installer.Forge;

namespace AxoClient.Game
{
    public sealed class ForgeLoader : ILoaderInstaller
    {
        private readonly IApiClient _api;
        private readonly IGameVersionHelper _gameVersionHelper;
        private const string Promotions = "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json";
        private const string Metadata = "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

        public ForgeLoader(IApiClient api, IGameVersionHelper gameVersionHelper)
        {
            _api = api;
            _gameVersionHelper = gameVersionHelper;
        }

        public LoaderType Type => LoaderType.Forge;

        public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(string minecraftVersion, CancellationToken cancellationToken)
        {
            var xml = XDocument.Parse(await _api.GetString(Metadata, TimeSpan.FromHours(6), cancellationToken));
            var prefix = minecraftVersion + "-";
            var versions = xml.Descendants("version").Select(element => element.Value)
                .Where(version => version.StartsWith(prefix, StringComparison.Ordinal))
                .Select(version => version[prefix.Length..].Split('-')[0])
                .Distinct()
                .ToList();
            string? recommended = null;
            try
            {
                using var promos = JsonDocument.Parse(await _api.GetString(Promotions, TimeSpan.FromHours(6), cancellationToken));
                var promosElement = promos.RootElement.GetProperty("promos");
                recommended = promosElement.TryGetProperty(minecraftVersion + "-recommended", out var recommendedElement) ? recommendedElement.GetString()
                    : promosElement.TryGetProperty(minecraftVersion + "-latest", out var latestElement) ? latestElement.GetString() : null;
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException)
            {
            }
            recommended ??= versions.FirstOrDefault();
            return versions.Select(version => new LoaderVersionInfo(version, true, version == recommended)).ToList();
        }

        public async Task<string> Install(LoaderInstallContext context)
        {
            context.Status.Report("loader.installing:Forge");
            var forge = new ForgeInstaller(context.Launcher);
            var options = new ForgeInstallOptions
            {
                FileProgress = context.Files,
                ByteProgress = context.Bytes,
                InstallerOutput = new Progress<string>(_ => { }),
                SkipIfAlreadyInstalled = true,
                CancellationToken = context.Token
            };
            return _gameVersionHelper.IsSafeVersion(context.LoaderVersion)
                ? await forge.Install(context.MinecraftVersion, context.LoaderVersion!, options)
                : await forge.Install(context.MinecraftVersion, options);
        }

        public bool IsInstalled(MinecraftPath path, string minecraftVersion, string? loaderVersion) =>
            loaderVersion != null && (File.Exists(path.GetVersionJsonPath($"{minecraftVersion}-forge-{loaderVersion}"))
                                      || File.Exists(path.GetVersionJsonPath($"{minecraftVersion}-forge{minecraftVersion}-{loaderVersion}")));
    }
}
