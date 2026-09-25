using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class BackupPanel : UserControl
{
    private AppServices _app = null!;
    private Installation? _inst;
    private int _loadRequest;

    public BackupPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation inst)
    {
        _app = app;
        _inst = inst;
        StatusText.Text = "";
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_inst is not { } inst)
            return;
        var request = ++_loadRequest;
        Header.Text = "Sicherungen (lädt...)";
        var backups = await InstanceBackup.ListAsync(inst);
        if (request != _loadRequest)
            return;

        BackupList.ItemsSource = backups;
        Header.Text = $"Sicherungen ({backups.Count})";
        SubHeader.Text = backups.Count == 0
            ? "Welten, Mods und Einstellungen dieser Instanz als ZIP-Datei"
            : $"Belegen zusammen {Formats.Size(backups.Sum(b => b.SizeBytes))}";
        Ui.Show(EmptyText, backups.Count == 0);
        Ui.Show(AsideBanner, InstanceBackup.HasAside(inst));
        var running = _app.Games.IsRunning(inst);
        CreateButton.IsEnabled = !running;
        CreateButton.ToolTip = running ? "Beende das Spiel zuerst – sonst wären die Welten in der Sicherung nur halb geschrieben." : null;
    }

    private static List<(CheckBox Box, BackupParts Part)> PartChoices() =>
    [
        (Ui.Check("Welten (saves)"), BackupParts.Worlds),
        (Ui.Check("Mods, Ressourcenpakete und Shader"), BackupParts.Content),
        (Ui.Check("Einstellungen, Serverliste und Overlay-Profile"), BackupParts.Settings)
    ];

    private static BackupParts Chosen(IEnumerable<(CheckBox Box, BackupParts Part)> parts) =>
        parts.Where(p => Ui.IsChosen(p.Box)).Aggregate(BackupParts.None, (all, p) => all | p.Part);

    private async Task<bool> CheckNotRunningAsync(Installation inst, string action)
    {
        if (!_app.Games.IsRunning(inst))
            return true;
        await _app.Dialogs.ShowMessageAsync("Minecraft läuft", $"\"{inst.Name}\" läuft gerade. {action}");
        return false;
    }

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        if (_inst is not { } inst || !await CheckNotRunningAsync(inst,
                "Beende das Spiel, sonst landen halb geschriebene Welten in der Sicherung."))
            return;

        var parts = PartChoices();
        var note = Ui.Input();
        var problem = Ui.Problem();
        var form = Ui.Stack(new[] { Ui.Note("Was soll in die Sicherung?", 8) }
            .Concat<UIElement>(parts.Select(p => p.Box))
            .Append(Ui.Label("Notiz (freiwillig, z.B. „vor dem Update auf 1.21“)"))
            .Append(note)
            .Append(problem)
            .ToArray());

        bool Validate() => Ui.ShowProblem(problem, Chosen(parts) == BackupParts.None ? "Wähle mindestens einen Teil aus." : null);

        foreach (var (box, _) in parts)
            box.Click += (_, _) => Validate();
        Validate();
        if (!await _app.Dialogs.ShowFormAsync($"Sicherung von \"{inst.Name}\"", form, "Sichern", Validate))
            return;

        var chosen = Chosen(parts);
        var path = await UiRun.RunAsync(_app, "Sicherung wird erstellt",
            progress => InstanceBackup.CreateAsync(inst, chosen, note.Text.Trim(), progress), "Sicherung fehlgeschlagen");
        if (path != null)
            StatusText.Text = $"Sicherung erstellt: {Path.GetFileName(path)}";
        await RefreshAsync();
    }

    private async void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (_inst is not { } inst)
            return;
        var backup = Ui.DataOf<InstanceBackupInfo>(sender);
        if (!await CheckNotRunningAsync(inst, "Beende das Spiel, bevor du eine Sicherung zurückspielst."))
            return;

        var parts = PartChoices();
        foreach (var (box, part) in parts.Where(p => !backup.Parts.HasFlag(p.Part)))
        {
            box.IsChecked = false;
            box.IsEnabled = false;
            box.Content += "  (nicht in dieser Sicherung)";
        }
        var form = Ui.Stack(new[] { Ui.Note($"Aus der Sicherung vom {backup.CreatedText} zurückspielen " +
                                            $"(enthält: {InstanceBackup.PartsText(backup.Parts)}).", 8) }
            .Concat<UIElement>(parts.Select(p => p.Box))
            .Append(Ui.Note("Was jetzt im Spielordner liegt, wird nicht gelöscht, sondern in Ordner mit der " +
                            "Endung „.vor-backup“ umbenannt. Wenn alles passt, kannst du die danach hier entfernen.", 0, 11))
            .ToArray());

        if (!await _app.Dialogs.ShowFormAsync("Sicherung wiederherstellen", form, "Wiederherstellen"))
            return;

        var chosen = Chosen(parts);
        if (chosen == BackupParts.None)
        {
            StatusText.Text = "Nichts ausgewählt – es wurde nichts geändert.";
            return;
        }

        var report = await UiRun.RunAsync(_app, "Sicherung wird zurückgespielt",
            progress => InstanceBackup.RestoreAsync(inst, backup, chosen, progress), "Wiederherstellen fehlgeschlagen");
        if (report != null)
        {
            StatusText.Text = "Wiederhergestellt.";
            await UiRun.ShowReportAsync(_app, "Sicherung wiederhergestellt", report);
        }
        await RefreshAsync();
    }

    private async void CleanAside_Click(object sender, RoutedEventArgs e)
    {
        if (_inst is not { } inst)
            return;
        if (!await _app.Dialogs.ConfirmAsync("Endgültig löschen",
                "Die bei der Wiederherstellung zur Seite gelegten Ordner („.vor-backup“) werden gelöscht. " +
                "Das lässt sich nicht rückgängig machen.", "Löschen", danger: true))
            return;
        var removed = await Task.Run(() => InstanceBackup.CleanUpAside(inst));
        StatusText.Text = removed > 0 ? $"{removed} Ordner gelöscht." : "Es war nichts mehr da.";
        await RefreshAsync();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var backup = Ui.DataOf<InstanceBackupInfo>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Sicherung löschen",
                $"\"{backup.FileName}\" ({Formats.Size(backup.SizeBytes)}) wirklich löschen?", "Löschen", danger: true))
            return;
        try
        {
            File.Delete(backup.FilePath);
            StatusText.Text = "Sicherung gelöscht.";
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Löschen fehlgeschlagen", ex);
        }
        await RefreshAsync();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_inst != null)
            Shell.OpenFolder(_inst.BackupDir, create: true);
    }
}
