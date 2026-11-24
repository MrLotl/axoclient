using System.Text.Json;
using AxoClient.Common;
using AxoClient.DataModel.Game;
using AxoClient.Game.Contract;
using CmlLib.Core;

namespace AxoClient.Game
{
    /// <summary>Fabric und Quilt schreiben ein Profil-JSON, den Rest lädt CmlLib.</summary>
    public abstract class MetaProfileLoader : ILoaderInstaller
    {
        private readonly IApiClient _api;
        private readonly ILoaderNames _loaderNames;
        private readonly IGameVersionHelper _gameVersionHelper;

        protected MetaProfileLoader(IApiClient api, ILoaderNames loaderNames, IGameVersionHelper gameVersionHelper)
        {
            _api = api;
            _loaderNames = loaderNames;
            _gameVersionHelper = gameVersionHelper;
        }

        public abstract LoaderType Type { get; }
        protected abstract string MetaUrl { get; }
        protected abstract string ProfilePrefix { get; }

        public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(string mc, CancellationToken ct)
        {
            var text = await _api.GetString($"{MetaUrl}/{Uri.EscapeDataString(mc)}", TimeSpan.FromHours(6), ct);
            using var doc = JsonDocument.Parse(text);
            var list = doc.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("loader"))
                .Select(l => (Version: l.GetProperty("version").GetString()!,
                    Stable: !l.TryGetProperty("stable", out var s) || s.GetBoolean()))
                .ToList();
            var recommended = PickRecommended(mc, list.Select(l => (l.Version, l.Stable)).ToList());
            return list.Select(l => new LoaderVersionInfo(l.Version, l.Stable && !l.Version.Contains('-'), l.Version == recommended)).ToList();
        }

        protected virtual string? PickRecommended(string mc, List<(string Version, bool Stable)> versions) =>
            versions.FirstOrDefault(v => v.Stable).Version ?? versions.FirstOrDefault().Version;

        public async Task<string> Install(LoaderInstallContext ctx)
        {
            var version = ctx.LoaderVersion;
            if (!_gameVersionHelper.IsSafeVersion(version))
            {
                version = (await ListVersions(ctx.MinecraftVersion, ctx.Token)).FirstOrDefault(v => v.Recommended)?.Version;
            }

            if (version == null)
            {
                throw new AxoException("game.loaderUnsupported",
                    new() { ["loader"] = _loaderNames.Display(Type), ["version"] = ctx.MinecraftVersion });
            }

            var id = profileId(ctx.MinecraftVersion, version);
            var jsonPath = ctx.Path.GetVersionJsonPath(id);
            if (File.Exists(jsonPath))
            {
                return id;
            }

            ctx.Status.Report($"loader.installing:{_loaderNames.Display(Type)}");
            var profile = await _api.GetString(
                $"{MetaUrl}/{Uri.EscapeDataString(ctx.MinecraftVersion)}/{Uri.EscapeDataString(version)}/profile/json",
                ct: ctx.Token);
            using var profileJson = JsonDocument.Parse(profile);
            id = profileJson.RootElement.GetProperty("id").GetString()!;
            if (!_gameVersionHelper.IsSafeVersion(id))
            {
                throw new AxoException("errors.badResponse", new() { ["host"] = new Uri(MetaUrl).Host });
            }

            jsonPath = ctx.Path.GetVersionJsonPath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            await File.WriteAllTextAsync(jsonPath, profile, ctx.Token);
            return id;
        }

        public bool IsInstalled(MinecraftPath path, string mc, string? loaderVersion) =>
            loaderVersion != null && File.Exists(path.GetVersionJsonPath(profileId(mc, loaderVersion)));

        private string profileId(string mc, string loader) => $"{ProfilePrefix}-{loader}-{mc}";
    }
}
