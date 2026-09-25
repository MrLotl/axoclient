using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Dialogs;

public static class DropHandler
{
    private const int MaxFiles = 40;

    public static bool Busy { get; private set; }

    public static async Task HandleAsync(AppServices app, IEnumerable<string> paths)
    {
        if (Busy)
            return;
        Busy = true;
        try
        {
            await UiRun.GuardAsync(app, "Datei konnte nicht übernommen werden", () => HandleCoreAsync(app, paths));
        }
        finally
        {
            Busy = false;
        }
    }

    private static async Task HandleCoreAsync(AppServices app, IEnumerable<string> paths)
    {
        var files = DropRouter.Classify(paths.Take(MaxFiles + 1));
        if (files.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync("Nichts zu tun",
                "Es wurden keine Dateien erkannt. Ordner lassen sich nicht auf den Launcher ziehen – " +
                "pack eine Welt vorher in eine ZIP-Datei.");
            return;
        }
        if (files.Count > MaxFiles)
        {
            await app.Dialogs.ShowMessageAsync("Zu viele Dateien", $"Bitte höchstens {MaxFiles} Dateien auf einmal.");
            return;
        }

        var unknown = files.Where(f => f.Kind == DropKind.Unknown).ToList();
        var known = files.Except(unknown).ToList();
        if (known.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync("Unbekannter Dateityp",
                $"Mit {Names(unknown)} kann der Launcher nichts anfangen.\n\n" +
                "Erkannt werden: Mods (.jar), Ressourcenpakete und Shader (.zip), gesicherte Welten (.zip), " +
                "Modpacks (.mrpack), geteilte AxoClient-Pakete (.json) und Bilder.");
            return;
        }

        foreach (var file in known.Where(f => f.CreatesInstance))
            await ShareDialogs.ImportPathAsync(app, file.Path);

        var forInstance = known.Where(f => !f.CreatesInstance).ToList();
        if (forInstance.Count > 0)
            await HandleForInstanceAsync(app, forInstance);

        if (unknown.Count > 0)
            await app.Dialogs.ShowMessageAsync("Nicht alles war brauchbar",
                $"Übersprungen, weil der Typ nicht erkannt wurde: {Names(unknown)}.");
    }

    private static async Task HandleForInstanceAsync(AppServices app, List<DroppedFile> files)
    {
        var instances = app.Instances.All;
        if (instances.Count == 0)
        {
            await app.Dialogs.ShowMessageAsync("Keine Instanz", "Lege zuerst eine Instanz an, dann kannst du Dateien hineinziehen.");
            return;
        }

        var images = files.Where(f => f.Kind == DropKind.Image).ToList();
        var others = files.Except(images).ToList();

        var instanceBox = Ui.Combo(instances);
        instanceBox.SelectedItem = app.Instances.Selected ?? instances[0];
        instanceBox.DisplayMemberPath = nameof(Installation.Name);
        var asSkin = Ui.Check("Bild als Skin hochladen statt als Instanzbild", false);
        var problem = Ui.Problem();

        var form = Ui.Stack(
            Ui.Note(files.Count == 1 ? $"\"{files[0].Name}\" wurde als {files[0].KindText} erkannt." : $"{files.Count} Dateien erkannt:", 8),
            files.Count > 1 ? Ui.Scroll(FileList(files), 150) : null,
            Ui.Label("In welche Instanz?"), instanceBox,
            images.Count > 0 ? asSkin : null,
            images.Count > 0 ? Ui.Note("Ein Skin muss 64×64 Pixel groß sein; als Instanzbild geht jedes Bild.", 8, 11) : null,
            problem);

        void UpdateProblem()
        {
            if (instanceBox.SelectedItem is not Installation target)
            {
                Ui.ShowProblem(problem, null);
                return;
            }
            Ui.ShowProblem(problem, others.Any(f => f.Kind == DropKind.Mod) && !target.CanUseMods
                ? $"\"{target.Name}\" läuft als Vanilla – Mods werden dort nicht geladen. Der Mod wird trotzdem abgelegt."
                : app.Games.IsRunning(target)
                    ? $"\"{target.Name}\" läuft gerade. Änderungen werden erst nach einem Neustart des Spiels wirksam."
                    : null);
        }

        instanceBox.SelectionChanged += (_, _) => UpdateProblem();
        UpdateProblem();

        if (!await app.Dialogs.ShowFormAsync(files.Count == 1 ? "Datei übernehmen" : "Dateien übernehmen", form, "Übernehmen"))
            return;

        var inst = (Installation)instanceBox.SelectedItem!;
        var report = new List<string>();
        foreach (var file in others)
            report.Add(await ApplyAsync(inst, file));
        foreach (var image in images)
            report.Add(await ApplyImageAsync(app, inst, image, asSkin.IsChecked == true));

        app.Instances.NotifyChanged();
        await UiRun.ShowReportAsync(app, "Übernommen", report);
    }

    private static async Task<string> ApplyAsync(Installation inst, DroppedFile file)
    {
        try
        {
            switch (file.Kind)
            {
                case DropKind.Mod or DropKind.ResourcePack or DropKind.Shader:
                    var target = await Task.Run(() => DropRouter.CopyIntoInstance(inst, file));
                    return $"{file.KindText} \"{file.Name}\" liegt jetzt in {Path.GetFileName(Path.GetDirectoryName(target))}.";
                case DropKind.World:
                    var folder = await new WorldStore(inst).ImportAsync(file.Path);
                    return $"Welt \"{folder}\" wurde nach \"{inst.Name}\" importiert.";
                case DropKind.InstanceBackup:
                    return "Instanz-Sicherungen werden unter \"Sicherungen\" in der Instanz eingespielt. " +
                           $"Lege \"{file.Name}\" dazu in den Sicherungsordner (dort über das Ordnersymbol erreichbar).";
                default:
                    return $"\"{file.Name}\" wurde übersprungen.";
            }
        }
        catch (Exception ex)
        {
            return $"\"{file.Name}\" fehlgeschlagen: {ErrorReport.Short(ex)}";
        }
    }

    private static async Task<string> ApplyImageAsync(AppServices app, Installation inst, DroppedFile image, bool asSkin)
    {
        try
        {
            if (!asSkin)
            {
                InstanceIcons.SetCustom(inst, image.Path);
                return $"\"{image.Name}\" ist jetzt das Bild von \"{inst.Name}\".";
            }
            if (app.Accounts.Session == null)
                return "Für einen Skin musst du zuerst angemeldet sein.";
            await app.Accounts.UploadSkinAsync(await File.ReadAllBytesAsync(image.Path), slim: false);
            return $"\"{image.Name}\" wurde als Skin hochgeladen (klassisches Modell). " +
                   "Das schmale Modell stellst du unter \"Skins\" ein.";
        }
        catch (Exception ex)
        {
            return $"\"{image.Name}\" fehlgeschlagen: {ErrorReport.Short(ex)}";
        }
    }

    private static UIElement FileList(IEnumerable<DroppedFile> files)
    {
        var list = new StackPanel();
        foreach (var file in files)
        {
            var row = Ui.Note($"{file.Name}  ·  {file.KindText}", 4, 12);
            row.TextTrimming = TextTrimming.CharacterEllipsis;
            row.TextWrapping = TextWrapping.NoWrap;
            list.Children.Add(row);
        }
        return list;
    }

    private static string Names(IEnumerable<DroppedFile> files) => string.Join(", ", files.Select(f => $"\"{f.Name}\""));
}
