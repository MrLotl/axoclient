using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class TransferPanel : UserControl
{
    private AppServices _app = null!;
    private Installation? _target;

    public TransferPanel()
    {
        InitializeComponent();
    }

    public void Show(AppServices app, Installation? target)
    {
        _app = app;
        _target = target;
        StatusText.Text = "";
        Ui.Show(TransferButton, target != null);
        IntroText.Text = target == null
            ? "Optional: Übernimm Welten, Einstellungen und Inhalte aus einer bestehenden Instanz oder dem offiziellen Minecraft Launcher."
            : "Kopiere Welten, Einstellungen und Inhalte aus einer anderen Instanz oder dem offiziellen Minecraft Launcher in diese Instanz.";

        var sources = new List<TransferSource> { new() { Name = "Nichts übernehmen", GameDir = "" } };
        sources.AddRange(TransferSource.Available(app.Instances.All, target));
        SourceBox.ItemsSource = sources;
        SourceBox.SelectedIndex = target == null ? 0 : Math.Min(1, sources.Count - 1);
    }

    private TransferSource? SelectedSource => SourceBox.SelectedItem is TransferSource { GameDir.Length: > 0 } s ? s : null;

    private Dictionary<TransferItems, CheckBox> Boxes => new()
    {
        [TransferItems.Worlds] = WorldsCheck,
        [TransferItems.Options] = OptionsCheck,
        [TransferItems.Servers] = ServersCheck,
        [TransferItems.ResourcePacks] = ResourcePacksCheck,
        [TransferItems.Shaders] = ShadersCheck,
        [TransferItems.Mods] = ModsCheck
    };

    private TransferItems SelectedItems =>
        Boxes.Where(b => b.Value.IsChecked == true).Aggregate(TransferItems.None, (all, b) => all | b.Key);

    public bool HasSelection => SelectedSource != null && SelectedItems != TransferItems.None;

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var source = SelectedSource;
        Ui.Show(ItemsPanel, source != null);
        if (source == null)
            return;

        var dir = source.GameDir;
        var worlds = WorldStore.Count(Path.Combine(dir, "saves"));
        var servers = new ServerStore(dir).Load().Count;
        var packs = FileOps.CountEntries(source.ContentDir(ContentType.ResourcePack));
        var shaders = FileOps.CountEntries(source.ContentDir(ContentType.Shader));
        var mods = FileOps.CountEntries(Path.Combine(dir, "mods"), "*.jar*");
        var hasOptions = File.Exists(Path.Combine(dir, "options.txt"));

        Ui.SetOption(WorldsCheck, $"Welten ({worlds})", worlds > 0);
        Ui.SetOption(OptionsCheck, hasOptions ? "Einstellungen & Tastenbelegung" : "Einstellungen (keine vorhanden)", hasOptions);
        Ui.SetOption(ServersCheck, $"Server ({servers})", servers > 0);
        Ui.SetOption(ResourcePacksCheck, $"Ressourcenpakete ({packs})", packs > 0);
        Ui.SetOption(ShadersCheck, $"Shader ({shaders})", shaders > 0);
        Ui.SetOption(ModsCheck, $"Mods ({mods})", mods > 0);
        UpdateHint(source.Installation);
    }

    private void UpdateHint(Installation? source)
    {
        var lines = new List<string>
        {
            "Vorhandenes bleibt erhalten: Gleichnamige Welten werden als Kopie abgelegt, alte Einstellungen als .bak gesichert."
        };
        if (_target != null && source != null && (source.Loader != _target.Loader || source.MinecraftVersion != _target.MinecraftVersion))
            lines.Add($"Mods: Quelle ist {source.Description}, Ziel {_target.Description}. Über den Launcher installierte Mods " +
                      "werden passend neu heruntergeladen, manuell hinzugefügte übersprungen.");
        else if (_target == null)
            lines.Add("Mods: Bei gleicher Version und gleichem Loader werden sie kopiert, sonst passend neu heruntergeladen.");
        lines.Add("Welten nicht in eine ältere Minecraft-Version übertragen, das kann sie beschädigen.");
        HintText.Text = string.Join("\n", lines);
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (_target != null && HasSelection)
            await RunAsync(_target);
    }

    public async Task RunAsync(Installation target)
    {
        var items = SelectedItems;
        if (SelectedSource is not { } source || items == TransferItems.None)
            return;

        TransferButton.IsEnabled = SourceBox.IsEnabled = false;
        try
        {
            var report = await new InstanceTransfer(_app.Modrinth).TransferAsync(source, target, items,
                new Progress<string>(t => StatusText.Text = t));
            StatusText.Text = "";
            await UiRun.ShowReportAsync(_app, $"Übertragung von \"{source.Name}\" abgeschlossen", report);
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowErrorAsync("Übertragung fehlgeschlagen", ex, "Läuft das Spiel noch? Dann sind manche Dateien gesperrt.");
        }
        finally
        {
            TransferButton.IsEnabled = SourceBox.IsEnabled = true;
        }
    }
}
