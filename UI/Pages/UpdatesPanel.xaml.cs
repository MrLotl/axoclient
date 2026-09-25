using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Pages;

public partial class UpdatesPanel : UserControl
{
    private AppServices _app = null!;
    private List<PendingUpdate> _updates = [];

    public event Action? Closed;

    public UpdatesPanel()
    {
        InitializeComponent();
    }

    public async void Show(AppServices app)
    {
        _app = app;
        _updates = [];
        UpdateList.ItemsSource = null;
        StatusText.Text = "";
        SubHeader.Text = "";
        EmptyText.Visibility = Visibility.Collapsed;
        await ScanAsync();
    }

    private async Task ScanAsync()
    {
        var running = _app.Games.RunningInstances;
        var candidates = _app.Instances.All.Except(running).ToList();
        if (candidates.Count == 0)
        {
            ShowEmpty(running.Count > 0
                ? "Alle Instanzen laufen gerade. Beende die Spiele, dann lässt sich nach Updates suchen."
                : "Es gibt noch keine Instanz.");
            return;
        }

        SetBusy(true);
        var result = await UiRun.RunAsync(_app, "Suche nach Updates",
            progress => UpdateScanner.ScanAsync(_app, candidates, progress), "Update-Suche fehlgeschlagen");
        SetBusy(false);
        if (result == null)
            return;

        _updates = result.Updates;
        UpdateList.ItemsSource = _updates;

        var notes = new List<string>();
        if (running.Count > 0)
            notes.Add($"Übersprungen, weil sie laufen: {string.Join(", ", running.Select(i => i.Name))}.");
        notes.AddRange(result.Skipped.Select(s => "Nicht geprüft – " + s));
        StatusText.Text = string.Join(" ", notes);

        if (_updates.Count == 0)
        {
            SubHeader.Text = "";
            ShowEmpty("Alles ist auf dem neuesten Stand. Geprüft wurden Mods, Ressourcenpakete und Shader, " +
                      "die Modrinth kennt – manuell hinzugefügte Dateien werden dabei automatisch zugeordnet.");
            return;
        }

        EmptyText.Visibility = Visibility.Collapsed;
        SubHeader.Text = _updates.Count == 1
            ? "1 Update in 1 Instanz"
            : $"{_updates.Count} Updates in {result.InstanceCount} Instanz(en)";
        UpdateAllButton.IsEnabled = true;
    }

    private void ShowEmpty(string text)
    {
        EmptyText.Text = text;
        EmptyText.Visibility = Visibility.Visible;
        UpdateAllButton.IsEnabled = false;
    }

    private void SetBusy(bool busy)
    {
        RescanButton.IsEnabled = !busy;
        UpdateAllButton.IsEnabled = !busy && _updates.Count > 0;
    }

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync();

    private async void UpdateOne_Click(object sender, RoutedEventArgs e) => await ApplyAsync([Ui.DataOf<PendingUpdate>(sender)]);

    private async void UpdateAll_Click(object sender, RoutedEventArgs e) => await ApplyAsync(_updates.Where(u => u.CanUpdate).ToList());

    private async Task ApplyAsync(List<PendingUpdate> updates)
    {
        if (updates.Count == 0)
            return;

        var blocked = updates.Where(u => _app.Games.IsRunning(u.Installation)).Select(u => u.InstanceName).Distinct().ToList();
        if (blocked.Count > 0)
        {
            await _app.Dialogs.ShowMessageAsync("Minecraft läuft",
                $"Beende zuerst: {string.Join(", ", blocked)}. Solange ein Spiel läuft, lassen sich seine " +
                "Dateien nicht ersetzen.");
            updates = updates.Where(u => !_app.Games.IsRunning(u.Installation)).ToList();
            if (updates.Count == 0)
                return;
        }

        SetBusy(true);
        var failed = await UiRun.RunAsync(_app, "Updates werden eingespielt",
            progress => UpdateScanner.ApplyAsync(_app, updates, progress), "Aktualisieren fehlgeschlagen");
        SetBusy(false);
        UpdateList.Items.Refresh();

        var succeeded = updates.Count(u => u.IsDone);
        StatusText.Text = succeeded == updates.Count
            ? $"{succeeded} Eintrag/Einträge aktualisiert."
            : $"{succeeded} von {updates.Count} aktualisiert.";
        if (failed is { Count: > 0 })
            await UiRun.ShowReportAsync(_app, "Nicht alles konnte aktualisiert werden", failed);
        if (_updates.All(u => u.IsDone))
            UpdateAllButton.IsEnabled = false;
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();
}
