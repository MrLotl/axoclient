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

        public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(string mc, CancellationToken ct)
        {
            var xml = XDocument.Parse(await _api.GetString(Metadata, TimeSpan.FromHours(6), ct));
            var prefix = mc + "-";
            var versions = xml.Descendants("version").Select(v => v.Value)
                .Where(v => v.StartsWith(prefix, StringComparison.Ordinal))
                .Select(v => v[prefix.Length..].Split('-')[0])
                .Distinct()
                .ToList();
            string? recommended = null;
            try
            {
                using var promos = JsonDocument.Parse(await _api.GetString(Promotions, TimeSpan.FromHours(6), ct));
                var p = promos.RootElement.GetProperty("promos");
                recommended = p.TryGetProperty(mc + "-recommended", out var r) ? r.GetString()
                    : p.TryGetProperty(mc + "-latest", out var l) ? l.GetString() : null;
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
            {
            }
            recommended ??= versions.FirstOrDefault();
            return versions.Select(v => new LoaderVersionInfo(v, true, v == recommended)).ToList();
        }

        public async Task<string> Install(LoaderInstallContext ctx)
        {
            ctx.Status.Report("loader.installing:Forge");
            var forge = new ForgeInstaller(ctx.Launcher);
            var options = new ForgeInstallOptions
            {
                FileProgress = ctx.Files,
                ByteProgress = ctx.Bytes,
                InstallerOutput = new Progress<string>(_ => { }),
                SkipIfAlreadyInstalled = true,
                CancellationToken = ctx.Token
            };
            return _gameVersionHelper.IsSafeVersion(ctx.LoaderVersion)
                ? await forge.Install(ctx.MinecraftVersion, ctx.LoaderVersion!, options)
                : await forge.Install(ctx.MinecraftVersion, options);
        }

        public bool IsInstalled(MinecraftPath path, string mc, string? loaderVersion) =>
            loaderVersion != null && (File.Exists(path.GetVersionJsonPath($"{mc}-forge-{loaderVersion}"))
                                      || File.Exists(path.GetVersionJsonPath($"{mc}-forge{mc}-{loaderVersion}")));
    }
}
