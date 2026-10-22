using AxoClient.Core.Common;
using AxoClient.Core.Diagnostics;
using Microsoft.Win32;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record SettingsData(LauncherSettings Settings);

    private sealed record StateData(string? Instances, string? LocalServers, string? Modpacks, string? SortModpacks, string? SortAddMods);

    private sealed record AccentData(string From, string To, int Angle);

    private sealed record CleanData(string Area);

    private sealed record JavaRefData(string FileRef);

    private static void RegisterSettings(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On("settings.get", () => host.Settings.Current);

        r.On<SettingsData>("settings.save", async (d, _) =>
        {
            await host.Settings.SaveAsync(d.Settings);
            ApplyAutostart(d.Settings.Autostart);
            return host.Settings.Current;
        });

        r.On<StateData>("state.update", async (d, _) =>
        {
            static string? View(string? v) => v is "grid" or "list" ? v : null;
            static string? Sort(string? v) => v is "popular" or "relevance" or "newest" or "updated" ? v : null;
            await host.Settings.UpdateStateAsync(s =>
            {
                s.Views.Instances = View(d.Instances) ?? s.Views.Instances;
                s.Views.LocalServers = View(d.LocalServers) ?? s.Views.LocalServers;
                s.Views.Modpacks = View(d.Modpacks) ?? s.Views.Modpacks;
                s.Sort.Modpacks = Sort(d.SortModpacks) ?? s.Sort.Modpacks;
                s.Sort.AddMods = Sort(d.SortAddMods) ?? s.Sort.AddMods;
            });
            return host.Settings.State;
        });

        r.On<AccentData>("appearance.contrast", (d, _) =>
        {
            if (!Appearance.IsColor(d.From) || !Appearance.IsColor(d.To))
                throw new AxoException("errors.invalidInput");
            var accent = new AccentGradient(d.From, d.To, d.Angle);
            return Task.FromResult<object?>(new
            {
                ratio = Math.Round(Appearance.MinContrastWithWhite(accent), 2), ok = Appearance.HasEnoughContrast(accent)
            });
        });

        r.On<JavaRefData>("settings.checkJava", async (d, ct) =>
        {
            var path = ctx.Files.Resolve(d.FileRef);
            var major = await Core.Game.JavaManager.ReadMajorAsync(path, ct);
            return new { path, major };
        });

        r.On("java.list", async ct => await host.Java.FindAllAsync(ct));

        // ---- Updates ----
        r.On("update.state", () => new { available = host.LauncherUpdates.Available, installed = host.LauncherUpdates.IsInstalled });
        r.On("update.check", async ct => await host.LauncherUpdates.CheckAsync(ct));
        r.On("update.apply", () => StartJob(host.Jobs.RunAsync("launcher.update", new TextRef("updates.installing"),
            job => host.LauncherUpdates.ApplyAndRestartAsync(job))));

        // ---- Diagnose und Speicher ----
        r.On("diagnostics.run", async _ => await host.Jobs.RunAsync("diagnostics", new TextRef("diagnostics.title"), job => host.Diagnostics.RunAsync(job)));

        r.On("storage.scan", async ct => await host.Storage.ScanAsync(ct));
        r.On<CleanData>("storage.clean", async (d, ct) =>
        {
            var freed = d.Area switch
            {
                "backups" => host.Storage.CleanBackups(),
                "game" => await host.Storage.CleanVersionsAsync(ct),
                "java" => host.Processes.AnyRunning() ? throw new AxoException("storage.busy") : host.Storage.CleanJava(),
                "cache" => host.Storage.CleanCache(),
                "logs" => host.Storage.CleanLogs(),
                _ => throw new AxoException("errors.invalidInput")
            };
            return new { freed, overview = await host.Storage.ScanAsync(ct) };
        });
    }

    /// <summary>Autostart über den Run-Schlüssel des Benutzers (keine Admin-Rechte nötig).</summary>
    private static void ApplyAutostart(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key == null)
                return;
            if (enabled && Environment.ProcessPath is { } exe)
                key.SetValue("AxoClient", $"\"{exe}\" --minimized");
            else
                key.DeleteValue("AxoClient", throwOnMissingValue: false);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            AppLog.Warn("Autostart setzen", ex);
        }
    }
}
