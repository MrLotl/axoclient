using System.IO;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>Dialoge zum Installieren von Modpacks (aus der Modrinth-Suche, einem Freund oder einer Datei).</summary>
internal static class ModpackUi
{
    /// <summary>Name und Versionen eines Modpacks von Modrinth.</summary>
    private sealed record ProjectInfo(string Title, List<ContentVersion> Versions);

    /// <summary>Eine Zeile in der Versionsauswahl.</summary>
    private sealed record VersionChoice(ContentVersion Version)
    {
        public override string ToString()
        {
            var loaders = string.Join("/", Version.Loaders.Where(l => l is "fabric" or "forge")
                .Select(l => l == "fabric" ? "Fabric" : "Forge"));
            var minecraft = Version.GameVersions.Count > 0 ? "Minecraft " + Version.GameVersions[^1] : "";
            return string.Join(" · ", new[] { Version.Name, minecraft, loaders, Version.ChannelText }
                .Where(part => part.Length > 0));
        }
    }

    /// <summary>
    /// Lässt eine Version des Modpacks wählen und installiert es als neue Instanz.
    /// </summary>
    /// <param name="preferredVersionId">Diese Version vorauswählen (z.B. die, die ein Freund benutzt).</param>
    /// <returns>true, wenn eine Instanz angelegt wurde.</returns>
    public static async Task<bool> InstallProjectAsync(AppState app, string projectId, string title,
        string? preferredVersionId = null)
    {
        var modrinth = new ModrinthProvider(app.Http);
        var info = await UiRun.RunAsync(app, "Modpack wird gesucht", async progress =>
        {
            progress.Text.Report("Frage Modrinth nach Versionen...");
            var (_, realTitle) = await modrinth.GetProjectInfoAsync(projectId); // der echte Name, nicht der aus einer Nachricht
            return new ProjectInfo(realTitle, await modrinth.GetProjectVersionsAsync(projectId));
        }, "Modpack nicht gefunden");
        if (info == null)
            return false;

        // Nur Versionen mit einer .mrpack-Datei, die nicht eindeutig für NeoForge oder Quilt sind. Manche Versionen
        // nennen bei den Loadern nur "mrpack"; der wahre Loader steht dann erst in der Pack-Datei und wird beim
        // Installieren geprüft (mit verständlicher Meldung).
        static bool Supported(ContentVersion v) =>
            v.Loaders.Any(l => l is "fabric" or "forge") || !v.Loaders.Any(l => l is "neoforge" or "quilt");
        var usable = info.Versions
            .Where(v => v.DownloadUrl != null && v.FileName.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase)
                        && Supported(v))
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
                                  ?? usable.FirstOrDefault(v => v.Version.Channel == "release")
                                  ?? usable[0];
        var nameBox = Ui.Input(info.Title);
        var form = new StackPanel();
        form.Children.Add(Ui.Note("Das Modpack wird als neue Instanz mit eigenem Ordner angelegt. Alle Mods und " +
                                  "Einstellungen kommen direkt von Modrinth.", 6));
        form.Children.Add(Ui.Note("Mods sind Programme, die auf deinem Rechner laufen. Installiere nur Modpacks, denen du " +
                                  "vertraust.", 10, 11));
        form.Children.Add(Ui.Label("Version"));
        form.Children.Add(versionBox);
        form.Children.Add(Ui.Label("Name der Instanz"));
        form.Children.Add(nameBox);

        if (!await app.Dialogs.ShowFormAsync($"{info.Title} installieren", form, "Installieren",
                () => nameBox.Text.Trim().Length > 0 && versionBox.SelectedItem != null))
            return false;

        var version = ((VersionChoice)versionBox.SelectedItem!).Version;
        var name = nameBox.Text.Trim();
        var result = await UiRun.RunAsync(app, "Modpack wird installiert",
            progress => new ModpackInstaller(app).InstallFromVersionAsync(version, info.Title, name, progress),
            "Installation fehlgeschlagen");
        if (result == null)
            return false;

        await UiRun.ShowReportAsync(app, "Modpack installiert", result.Report);
        return true;
    }

    /// <summary>Installiert eine .mrpack-Datei, die schon auf dem Rechner liegt.</summary>
    public static async Task<bool> InstallFileAsync(AppState app, string path)
    {
        var nameBox = Ui.Input(Path.GetFileNameWithoutExtension(path));
        var form = new StackPanel();
        form.Children.Add(Ui.Note($"Modpack-Datei: {Path.GetFileName(path)}\n\nSie wird als neue Instanz installiert. " +
                                  "Der Name kann geändert werden.", 6));
        form.Children.Add(Ui.Note("Mods sind Programme, die auf deinem Rechner laufen. Öffne nur Modpacks aus Quellen, " +
                                  "denen du vertraust.", 10, 11));
        form.Children.Add(Ui.Label("Name der Instanz"));
        form.Children.Add(nameBox);

        if (!await app.Dialogs.ShowFormAsync("Modpack-Datei installieren", form, "Installieren",
                () => nameBox.Text.Trim().Length > 0))
            return false;

        var name = nameBox.Text.Trim();
        var result = await UiRun.RunAsync(app, "Modpack wird installiert",
            progress => new ModpackInstaller(app).InstallFromFileAsync(path, name, progress),
            "Installation fehlgeschlagen");
        if (result == null)
            return false;

        await UiRun.ShowReportAsync(app, "Modpack installiert", result.Report);
        return true;
    }
}
