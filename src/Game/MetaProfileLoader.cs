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

        public async Task<IReadOnlyList<LoaderVersionInfo>> ListVersions(string minecraftVersion, CancellationToken cancellationToken)
        {
            var text = await _api.GetString($"{MetaUrl}/{Uri.EscapeDataString(minecraftVersion)}", TimeSpan.FromHours(6), cancellationToken);
            using var document = JsonDocument.Parse(text);
            var list = document.RootElement.EnumerateArray()
                .Select(entryElement => entryElement.GetProperty("loader"))
                .Select(loaderElement => (Version: loaderElement.GetProperty("version").GetString()!,
                    Stable: !loaderElement.TryGetProperty("stable", out var stable) || stable.GetBoolean()))
                .ToList();
            var recommended = PickRecommended(minecraftVersion, list.Select(loader => (loader.Version, loader.Stable)).ToList());
            return list.Select(loader => new LoaderVersionInfo(loader.Version, loader.Stable && !loader.Version.Contains('-'), loader.Version == recommended)).ToList();
        }

        protected virtual string? PickRecommended(string minecraftVersion, List<(string Version, bool Stable)> versions) =>
            versions.FirstOrDefault(version => version.Stable).Version ?? versions.FirstOrDefault().Version;

        public async Task<string> Install(LoaderInstallContext context)
        {
            var version = context.LoaderVersion;
            if (!_gameVersionHelper.IsSafeVersion(version))
            {
                version = (await ListVersions(context.MinecraftVersion, context.Token)).FirstOrDefault(loaderVersion => loaderVersion.Recommended)?.Version;
            }

            if (version == null)
            {
                throw new AxoException("game.loaderUnsupported",
                    new() { ["loader"] = _loaderNames.Display(Type), ["version"] = context.MinecraftVersion });
            }

            var id = profileId(context.MinecraftVersion, version);
            var jsonPath = context.Path.GetVersionJsonPath(id);
            if (File.Exists(jsonPath))
            {
                return id;
            }

            context.Status.Report($"loader.installing:{_loaderNames.Display(Type)}");
            var profile = await _api.GetString(
                $"{MetaUrl}/{Uri.EscapeDataString(context.MinecraftVersion)}/{Uri.EscapeDataString(version)}/profile/json",
                cancellationToken: context.Token);
            using var profileJson = JsonDocument.Parse(profile);
            id = profileJson.RootElement.GetProperty("id").GetString()!;
            if (!_gameVersionHelper.IsSafeVersion(id))
            {
                throw new AxoException("errors.badResponse", new() { ["host"] = new Uri(MetaUrl).Host });
            }

            jsonPath = context.Path.GetVersionJsonPath(id);
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            await File.WriteAllTextAsync(jsonPath, profile, context.Token);
            return id;
        }

        public bool IsInstalled(MinecraftPath path, string minecraftVersion, string? loaderVersion) =>
            loaderVersion != null && File.Exists(path.GetVersionJsonPath(profileId(minecraftVersion, loaderVersion)));

        private string profileId(string minecraftVersion, string loader) => $"{ProfilePrefix}-{loader}-{minecraftVersion}";
    }
}
