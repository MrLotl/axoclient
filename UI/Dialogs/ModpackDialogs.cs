namespace AxoClient.UI.Dialogs;

public static class ModpackDialogs
{
    private sealed record ProjectInfo(string Title, List<ContentVersion> Versions);

    private sealed record VersionChoice(ContentVersion Version)
    {
        public override string ToString()
        {
            var loaders = string.Join("/", Version.Loaders.Where(l => l is "fabric" or "forge")
                .Select(l => l == "fabric" ? "Fabric" : "Forge"));
            var minecraft = Version.GameVersions.Count > 0 ? "Minecraft " + Version.GameVersions[^1] : "";
            return string.Join(" · ", new[] { Version.Name, minecraft, loaders, Version.ChannelText }.Where(p => p.Length > 0));
        }
    }

    private const string TrustNote = "Mods sind Programme, die auf deinem Rechner laufen. ";

    public static async Task<bool> InstallProjectAsync(AppServices app, string projectId, string title,
        string? preferredVersionId = null)
    {
        var info = await UiRun.RunAsync(app, "Modpack wird gesucht", async progress =>
        {
            progress.Text.Report("Frage Modrinth nach Versionen...");
            var (_, realTitle) = await app.Modrinth.GetProjectInfoAsync(projectId);
            return new ProjectInfo(realTitle, await app.Modrinth.GetProjectVersionsAsync(projectId));
        }, "Modpack nicht gefunden");
        if (info == null)
            return false;

        var usable = info.Versions
            .Where(v => v.DownloadUrl != null && v.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)
                        && (v.Loaders.Any(l => l is "fabric" or "forge") || !v.Loaders.Any(l => l is "neoforge" or "quilt")))
            .Select(v => new VersionChoice(v))
            .ToList();
        if (usable.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync("Nicht installierbar",
                $"\"{info.Title}\" hat keine Version für Fabric oder Forge. AxoClient kann derzeit nur diese beiden " +
                "Mod-Loader starten (NeoForge und Quilt fehlen noch).");
            return false;
        }

        var versionBox = Ui.Combo(usable);
        versionBox.SelectedItem = usable.FirstOrDefault(v => v.Version.Id == preferredVersionId)
                                  ?? usable.FirstOrDefault(v => v.Version.IsRelease)
                                  ?? usable[0];
        var nameBox = Ui.Input(info.Title);
        var form = Ui.Stack(
            Ui.Note("Das Modpack wird als neue Instanz mit eigenem Ordner angelegt. Alle Mods und " +
                    "Einstellungen kommen direkt von Modrinth.", 6),
            Ui.Note(TrustNote + "Installiere nur Modpacks, denen du vertraust.", 10, 11),
            Ui.Label("Version"), versionBox,
            Ui.Label("Name der Instanz"), nameBox);

        if (!await app.Dialogs.ShowFormAsync($"{info.Title} installieren", form, "Installieren",
                () => nameBox.Text.Trim().Length > 0 && versionBox.SelectedItem != null))
            return false;

        var version = ((VersionChoice)versionBox.SelectedItem!).Version;
        var name = nameBox.Text.Trim();
        return await InstallAsync(app, progress => new ModpackInstaller(app).InstallFromVersionAsync(version, info.Title, name, progress));
    }

    public static async Task<bool> InstallFileAsync(AppServices app, string path)
    {
        var nameBox = Ui.Input(Path.GetFileNameWithoutExtension(path));
        var form = Ui.Stack(
            Ui.Note($"Modpack-Datei: {Path.GetFileName(path)}\n\nSie wird als neue Instanz installiert. " +
                    "Der Name kann geändert werden.", 6),
            Ui.Note(TrustNote + "Öffne nur Modpacks aus Quellen, denen du vertraust.", 10, 11),
            Ui.Label("Name der Instanz"), nameBox);

        if (!await app.Dialogs.ShowFormAsync("Modpack-Datei installieren", form, "Installieren",
                () => nameBox.Text.Trim().Length > 0))
            return false;

        var name = nameBox.Text.Trim();
        return await InstallAsync(app, progress => new ModpackInstaller(app).InstallFromFileAsync(path, name, progress));
    }

    private static async Task<bool> InstallAsync(AppServices app, Func<WorkProgress, Task<InstanceResult>> install)
    {
        var result = await UiRun.RunAsync(app, "Modpack wird installiert", install, "Installation fehlgeschlagen");
        if (result == null)
            return false;
        await UiRun.ShowReportAsync(app, "Modpack installiert", result.Report);
        return true;
    }
}
