using AxoClient.Core.Accounts;
using AxoClient.Core.Common;

namespace AxoClient.App.Bridge;

public static partial class Handlers
{
    private sealed record EdgeData(string Edge);

    private sealed record UrlData(string Url);

    private sealed record FolderData(string Kind, string? Id, string? Sub);

    private sealed record JobData(string Id);

    private sealed record OpenFileData(string Purpose, bool Multi = false);

    private sealed record SaveFileData(string Purpose, string? Name);

    private sealed record UploadData(string Name, string Base64);

    private sealed record TextData(string Text);

    private static void RegisterApp(MessageRouter r, HandlerContext ctx)
    {
        var host = ctx.Host;

        r.On("app.init", () => new
        {
            version = AppInfo.Version,
            shortVersion = AppInfo.ShortVersion,
            settings = host.Settings.Current,
            state = host.Settings.State,
            accounts = host.Accounts.Views(),
            needsLogin = host.Accounts.Active == null || host.Accounts.ActiveNeedsLogin,
            setupDone = host.Settings.Current.SetupDone,
            devAuth = DevAuthProvider.Enabled,
            curseForge = AppInfo.CurseForgeEnabled,
            unread = host.Notifications.UnreadCount,
            update = host.LauncherUpdates.Available,
            jobs = host.Jobs.Active,
            running = host.Games.Running,
            serviceAvailable = host.Online.Available,
            totalRamMb = SystemInfo.Memory().TotalMb,
            axoModVersions = host.AxoMod.SupportedMinecraftVersions,
            presets = Appearance.Presets
        });

        r.On("window.minimize", () =>
        {
            ctx.Window.Minimize();
            return null;
        });
        r.On("window.toggleMaximize", () =>
        {
            ctx.Window.ToggleMaximize();
            return null;
        });
        r.On("window.close", () =>
        {
            ctx.Window.Close();
            return null;
        });
        r.On("window.drag", () =>
        {
            ctx.Window.Drag();
            return null;
        });
        r.On<EdgeData>("window.resize", (d, _) =>
        {
            ctx.Window.Resize(d.Edge);
            return Task.FromResult<object?>(null);
        });
        r.On("window.state", () => new { maximized = ctx.Window.IsMaximized });

        r.On<UrlData>("shell.openUrl", (d, _) =>
        {
            OpenUrl(d.Url);
            return Task.FromResult<object?>(null);
        });

        r.On<FolderData>("shell.openFolder", async (d, ct) =>
        {
            var p = host.Paths;
            string path = d.Kind switch
            {
                "instance" => p.GameDir(Text.RequireId(d.Id)),
                "instanceSub" when d.Sub is "mods" or "resourcepacks" or "shaderpacks" or "saves" or "screenshots" or "config" or "logs"
                    or "crash-reports" => Path.Combine(p.GameDir(Text.RequireId(d.Id)), d.Sub),
                "world" => host.Worlds.WorldDir(Text.RequireId(d.Id), d.Sub ?? ""),
                "server" => p.ServerData(Text.RequireId(d.Id)),
                "serverSub" when d.Sub is "plugins" or "mods" or "logs" or "world" => Path.Combine(p.ServerData(Text.RequireId(d.Id)), d.Sub),
                "servers" => p.Servers,
                "logs" => p.Logs,
                "data" => p.Root,
                _ => throw new AxoException("errors.invalidInput")
            };
            Directory.CreateDirectory(path);
            OpenInExplorer(path);
            await Task.CompletedTask;
            return null;
        });

        r.On("jobs.list", () => host.Jobs.Active);
        r.On<JobData>("jobs.cancel", (d, _) => Task.FromResult<object?>(host.Jobs.Cancel(d.Id)));

        r.On<OpenFileData>("dialog.openFile", async (d, _) =>
        {
            (string, string[])[] filters = d.Purpose switch
            {
                "import" => [("Instanzen (.mrpack, .zip)", ["*.mrpack", "*.zip"])],
                "mod" => [("Mods (.jar)", ["*.jar"])],
                "pack" => [("Ressourcepacks und Shader (.zip)", ["*.zip"])],
                "world" => [("Welten (.zip)", ["*.zip"])],
                "image" => [("Bilder (.png, .jpg)", ["*.png", "*.jpg", "*.jpeg"])],
                "skin" or "cape" => [("PNG-Bild", ["*.png"])],
                "emote" => [("Animationen (.json)", ["*.json"])],
                "java" => [("Java (javaw.exe)", ["javaw.exe", "java.exe"])],
                _ => throw new AxoException("errors.invalidInput")
            };
            var files = await ctx.Window.OpenFileAsync(d.Purpose switch
            {
                "import" => "Instanz importieren",
                "java" => "Java auswählen",
                _ => "Datei auswählen"
            }, filters, d.Multi);
            return files.Where(File.Exists).Select(f => new { id = ctx.Files.Add(f), name = Path.GetFileName(f), size = new FileInfo(f).Length })
                .ToList();
        });

        r.On<SaveFileData>("dialog.saveFile", async (d, _) =>
        {
            (string, string[])[] filters = d.Purpose switch
            {
                "mrpack" => [("Modrinth-Modpack (.mrpack)", ["*.mrpack"])],
                "backup" => [("Backup (.zip)", ["*.zip"])],
                "log" => [("Textdatei (.txt)", ["*.txt"])],
                _ => throw new AxoException("errors.invalidInput")
            };
            var name = Text.Clean(d.Name, 80);
            var path = await ctx.Window.SaveFileAsync("Speichern unter", PathGuard.IsSafeFileName(name) ? name : "AxoClient", filters);
            return path == null ? null : new { id = ctx.Files.Add(path), name = Path.GetFileName(path) };
        });

        r.On<UploadData>("files.upload", async (d, ct) =>
        {
            var id = await ctx.Files.UploadAsync(d.Name, d.Base64, ct);
            return new { id, name = ctx.Files.NameOf(id) };
        });

        // Texte zum Kopieren immer geschwärzt (Tokens, Spielername, UUID, IP)
        r.On<TextData>("text.redact", (d, _) => Task.FromResult<object?>(LogRedactor.RedactForSharing(d.Text)));

        // Zwischenablage, falls der Browser sie im eigenen Schema nicht erlaubt (WinForms braucht einen STA-Thread)
        r.On<TextData>("clipboard.write", (d, _) =>
        {
            var text = d.Text.Length > 2_000_000 ? d.Text[..2_000_000] : d.Text;
            var tcs = new TaskCompletionSource<object?>();
            var thread = new Thread(() =>
            {
                try
                {
                    System.Windows.Forms.Clipboard.SetText(text);
                    tcs.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        });
    }
}
