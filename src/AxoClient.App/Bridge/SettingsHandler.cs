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

        public override void Register(MessageRouter r, MainWindow window)
        {
            r.On("settings.get", () => _settings.Current);

            r.On<SettingsData>("settings.save", async (d, _) =>
            {
                await _settings.Save(d.Settings);
                applyAutostart(d.Settings.Autostart);
                return _settings.Current;
            });

            r.On<StateData>("state.update", async (d, _) =>
            {
                string? View(string? v) => v is "grid" or "list" ? v : null;
                string? Sort(string? v) => v is "popular" or "relevance" or "newest" or "updated" ? v : null;
                double? Size(double? v) => v is { } n && double.IsFinite(n) ? Math.Clamp(n, 0, 1) : null;
                string? InstanceSort(string? v) => v is "recent" or "name" or "playtime" or "mods" or "custom" ? v : null;
                await _settings.UpdateState(s =>
                {
                    s.Views.Instances = View(d.Instances) ?? s.Views.Instances;
                    s.Views.LocalServers = View(d.LocalServers) ?? s.Views.LocalServers;
                    s.Views.Modpacks = View(d.Modpacks) ?? s.Views.Modpacks;
                    s.Views.Content = View(d.Content) ?? s.Views.Content;
                    s.Views.ContentGridSize = Size(d.ContentGridSize) ?? s.Views.ContentGridSize;
                    s.Views.ContentListSize = Size(d.ContentListSize) ?? s.Views.ContentListSize;
                    s.Sort.Modpacks = Sort(d.SortModpacks) ?? s.Sort.Modpacks;
                    s.Sort.AddMods = Sort(d.SortAddMods) ?? s.Sort.AddMods;
                    s.Views.InstancesSize = Size(d.InstancesSize) ?? s.Views.InstancesSize;
                    s.Sort.Instances = InstanceSort(d.SortInstances) ?? s.Sort.Instances;
                    // Favorit umschalten; nur echte Instanz-IDs, höchstens 200 Einträge
                    if (d.Favorite is { Length: > 0 and <= 64 } fav)
                    {
                        if (!s.Favorites.Remove(fav) && s.Favorites.Count < 200)
                        {
                            s.Favorites.Add(fav);
                        }
                    }
                    // Eigene Reihenfolge: nur plausible IDs, ohne Doppelte, höchstens 500
                    if (d.Order is { Length: <= 500 } order)
                    {
                        s.InstanceOrder = order.Where(id => id is { Length: > 0 and <= 64 }).Distinct().ToList();
                    }
                });
                return _settings.State;
            });

            r.On<AccentData>("appearance.contrast", (d, _) =>
            {
                if (!_appearance.IsColor(d.From) || !_appearance.IsColor(d.To))
                {
                    throw new AxoException("errors.invalidInput");
                }

                var accent = new AccentGradient(d.From, d.To, d.Angle);
                return Task.FromResult<object?>(new
                {
                    ratio = Math.Round(_appearance.MinContrastWithWhite(accent), 2),
                    ok = _appearance.HasEnoughContrast(accent)
                });
            });

            r.On<JavaRefData>("settings.checkJava", async (d, ct) =>
            {
                var path = _files.Resolve(d.FileRef);
                var major = await _java.ReadMajor(path, ct);
                return new { path, major };
            });

            r.On("java.list", async ct => await _java.FindAll(ct));

            // ---- Updates ----
            r.On("update.state", () => new { available = _launcherUpdates.Available, installed = _launcherUpdates.IsInstalled });
            r.On("update.check", async ct => await _launcherUpdates.Check(ct));
            r.On("update.apply", () => StartJob(_jobs.Run("launcher.update", new TextRef("updates.installing"),
                job => _launcherUpdates.ApplyAndRestart(job))));

            // ---- Diagnose und Speicher ----
            r.On("diagnostics.run", async _ => await _jobs.Run("diagnostics", new TextRef("diagnostics.title"), job => _diagnostics.Run(job)));

            r.On("storage.scan", async ct => await _storage.Scan(ct));
            r.On<CleanData>("storage.clean", async (d, ct) =>
            {
                var freed = d.Area switch
                {
                    "backups" => _storage.CleanBackups(),
                    "game" => await _storage.CleanVersions(ct),
                    "java" => _processes.AnyRunning() ? throw new AxoException("storage.busy") : _storage.CleanJava(),
                    "cache" => _storage.CleanCache(),
                    "logs" => _storage.CleanLogs(),
                    _ => throw new AxoException("errors.invalidInput")
                };
                return new { freed, overview = await _storage.Scan(ct) };
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

                if (enabled && Environment.ProcessPath is { } exe)
                {
                    key.SetValue("AxoClient", $"\"{exe}\" --minimized");
                }
                else
                {
                    key.DeleteValue("AxoClient", throwOnMissingValue: false);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
            {
                AppLog.Warn("Autostart setzen", ex);
            }
        }
    }
}
