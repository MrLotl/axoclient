using AxoClient.App.Bridge.Requests;
using AxoClient.Common;
using AxoClient.DataModel.Settings;
using AxoClient.Diagnostics.Contract;
using AxoClient.Game.Contract;
using AxoClient.Settings.Contract;
using AxoClient.Updates.Contract;
using Microsoft.Win32;

namespace AxoClient.App.Bridge
{
    /// <summary>Nachrichten der UI zum Bereich Settings.</summary>
    public class SettingsHandler : BridgeHandler
    {
        private readonly ISettingsService _settings;
        private readonly IJavaManager _java;
        private readonly IUpdateService _launcherUpdates;
        private readonly IJobManager _jobs;
        private readonly IDiagnosticsService _diagnostics;
        private readonly IStorageService _storage;
        private readonly IProcessRunner _processes;
        private readonly FileRefs _files;
        private readonly IAppearance _appearance;

        public SettingsHandler(
            ISettingsService settings,
            IJavaManager java,
            IUpdateService launcherUpdates,
            IJobManager jobs,
            IDiagnosticsService diagnostics,
            IStorageService storage,
            IProcessRunner processes,
            FileRefs files,
            IAppearance appearance,
            IAppLog appLog,
            ITextHelper textHelper)
            : base(appLog, textHelper)
        {
            _settings = settings;
            _java = java;
            _launcherUpdates = launcherUpdates;
            _jobs = jobs;
            _diagnostics = diagnostics;
            _storage = storage;
            _processes = processes;
            _files = files;
            _appearance = appearance;
        }

        public override void Register(MessageRouter router, MainWindow window)
        {
            router.On("settings.get", () => _settings.Current);

            router.On<SettingsData>("settings.save", async (request, _) =>
            {
                await _settings.Save(request.Settings);
                applyAutostart(request.Settings.Autostart);
                return _settings.Current;
            });

            router.On<StateData>("state.update", async (request, _) =>
            {
                string? View(string? viewMode) => viewMode is "grid" or "list" ? viewMode : null;
                string? Sort(string? sortMode) => sortMode is "popular" or "relevance" or "newest" or "updated" ? sortMode : null;
                double? Size(double? requestedSize) => requestedSize is { } finiteSize && double.IsFinite(finiteSize) ? Math.Clamp(finiteSize, 0, 1) : null;
                string? InstanceSort(string? sortMode) => sortMode is "recent" or "name" or "playtime" or "mods" or "custom" ? sortMode : null;
                await _settings.UpdateState(state =>
                {
                    state.Views.Instances = View(request.Instances) ?? state.Views.Instances;
                    state.Views.LocalServers = View(request.LocalServers) ?? state.Views.LocalServers;
                    state.Views.Modpacks = View(request.Modpacks) ?? state.Views.Modpacks;
                    state.Views.Content = View(request.Content) ?? state.Views.Content;
                    state.Views.ContentGridSize = Size(request.ContentGridSize) ?? state.Views.ContentGridSize;
                    state.Views.ContentListSize = Size(request.ContentListSize) ?? state.Views.ContentListSize;
                    state.Sort.Modpacks = Sort(request.SortModpacks) ?? state.Sort.Modpacks;
                    state.Sort.AddMods = Sort(request.SortAddMods) ?? state.Sort.AddMods;
                    state.Views.InstancesSize = Size(request.InstancesSize) ?? state.Views.InstancesSize;
                    state.Sort.Instances = InstanceSort(request.SortInstances) ?? state.Sort.Instances;
                    // Favorit umschalten; nur echte Instanz-IDs, höchstens 200 Einträge
                    if (request.Favorite is { Length: > 0 and <= 64 } fav)
                    {
                        if (!state.Favorites.Remove(fav) && state.Favorites.Count < 200)
                        {
                            state.Favorites.Add(fav);
                        }
                    }
                    // Eigene Reihenfolge: nur plausible IDs, ohne Doppelte, höchstens 500
                    if (request.Order is { Length: <= 500 } order)
                    {
                        state.InstanceOrder = order.Where(id => id is { Length: > 0 and <= 64 }).Distinct().ToList();
                    }
                });
                return _settings.State;
            });

            router.On<AccentData>("appearance.contrast", (request, _) =>
            {
                if (!_appearance.IsColor(request.From) || !_appearance.IsColor(request.To))
                {
                    throw new AxoException("errors.invalidInput");
                }

                var accent = new AccentGradient(request.From, request.To, request.Angle);
                return Task.FromResult<object?>(new
                {
                    ratio = Math.Round(_appearance.MinContrastWithWhite(accent), 2),
                    ok = _appearance.HasEnoughContrast(accent)
                });
            });

            router.On<JavaRefData>("settings.checkJava", async (request, cancellationToken) =>
            {
                var path = _files.Resolve(request.FileRef);
                var major = await _java.ReadMajor(path, cancellationToken);
                return new { path, major };
            });

            router.On("java.list", async cancellationToken => await _java.FindAll(cancellationToken));

            // ---- Updates ----
            router.On("update.state", () => new { available = _launcherUpdates.Available, installed = _launcherUpdates.IsInstalled });
            router.On("update.check", async cancellationToken => await _launcherUpdates.Check(cancellationToken));
            router.On("update.apply", () => StartJob(_jobs.Run("launcher.update", new TextRef("updates.installing"),
                job => _launcherUpdates.ApplyAndRestart(job))));

            // ---- Diagnose und Speicher ----
            router.On("diagnostics.run", async _ => await _jobs.Run("diagnostics", new TextRef("diagnostics.title"), job => _diagnostics.Run(job)));

            router.On("storage.scan", async cancellationToken => await _storage.Scan(cancellationToken));
            router.On<CleanData>("storage.clean", async (request, cancellationToken) =>
            {
                var freed = request.Area switch
                {
                    "backups" => _storage.CleanBackups(),
                    "game" => await _storage.CleanVersions(cancellationToken),
                    "java" => _processes.AnyRunning() ? throw new AxoException("storage.busy") : _storage.CleanJava(),
                    "cache" => _storage.CleanCache(),
                    "logs" => _storage.CleanLogs(),
                    _ => throw new AxoException("errors.invalidInput")
                };
                return new { freed, overview = await _storage.Scan(cancellationToken) };
            });
        }

        /// <summary>Autostart über den Run-Schlüssel des Benutzers (keine Admin-Rechte nötig).</summary>
        private void applyAutostart(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
                if (key == null)
                {
                    return;
                }

                if (enabled && Environment.ProcessPath is { } exePath)
                {
                    key.SetValue("AxoClient", $"\"{exePath}\" --minimized");
                }
                else
                {
                    key.DeleteValue("AxoClient", throwOnMissingValue: false);
                }
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                AppLog.Warn("Autostart setzen", exception);
            }
        }
    }
}
