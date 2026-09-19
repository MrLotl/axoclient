using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace McLauncher.Pages;

/// <summary>
/// Auswahl einer Quelle und der zu übertragenden Daten. Wird als eigener Tab einer Instanz genutzt
/// und beim Anlegen einer neuen Instanz im Editor (dort ohne eigenen Übertragen-Button).
/// </summary>
public partial class TransferPanel : UserControl
{
    private AppState _app = null!;
    private Installation? _target;

    public TransferPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Bereitet das Panel vor. <paramref name="target"/> = null: neue Instanz (Aufrufer startet die Übertragung
    /// nach dem Speichern selbst über <see cref="RunAsync"/>).
    /// </summary>
    public void Show(AppState app, Installation? target)
    {
        _app = app;
        _target = target;
        StatusText.Text = "";
        TransferButton.Visibility = target == null ? Visibility.Collapsed : Visibility.Visible;
        IntroText.Text = target == null
            ? "Optional: Übernimm Welten, Einstellungen und Inhalte aus einer bestehenden Instanz oder dem offiziellen Minecraft Launcher."
            : "Kopiere Welten, Einstellungen und Inhalte aus einer anderen Instanz oder dem offiziellen Minecraft Launcher in diese Instanz.";

        var sources = new List<TransferSource?> { null }; // null = nichts übernehmen
        sources.AddRange(TransferSource.Available(app.Settings, target));
        SourceBox.ItemsSource = sources.Select(s => s ?? new TransferSource { Name = "Nichts übernehmen", GameDir = "" }).ToList();
        SourceBox.SelectedIndex = target == null ? 0 : Math.Min(1, sources.Count - 1);
    }

    private TransferSource? SelectedSource =>
        SourceBox.SelectedItem is TransferSource { GameDir.Length: > 0 } s ? s : null;

    public bool HasSelection => SelectedSource != null && SelectedItems != TransferItems.None;

    private TransferItems SelectedItems =>
        (WorldsCheck.IsChecked == true ? TransferItems.Worlds : 0) |
        (OptionsCheck.IsChecked == true ? TransferItems.Options : 0) |
        (ServersCheck.IsChecked == true ? TransferItems.Servers : 0) |
        (ResourcePacksCheck.IsChecked == true ? TransferItems.ResourcePacks : 0) |
        (ShadersCheck.IsChecked == true ? TransferItems.Shaders : 0) |
        (ModsCheck.IsChecked == true ? TransferItems.Mods : 0);

    private void SourceBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var source = SelectedSource;
        ItemsPanel.Visibility = source == null ? Visibility.Collapsed : Visibility.Visible;
        if (source == null)
            return;

        // Anzahl je Kategorie anzeigen, damit man sieht, was in der Quelle vorhanden ist
        var dir = source.GameDir;
        var version = source.Installation?.MinecraftVersion ?? "26.2";
        int Count(string sub, string pattern = "*") =>
            Directory.Exists(Path.Combine(dir, sub))
                ? Directory.GetFileSystemEntries(Path.Combine(dir, sub), pattern).Length
                : 0;

        var worlds = Directory.Exists(Path.Combine(dir, "saves"))
            ? Directory.GetDirectories(Path.Combine(dir, "saves")).Count(d => File.Exists(Path.Combine(d, "level.dat")))
            : 0;
        var servers = new ServerStore(dir).Load().Count;
        var packs = Count(ContentTypes.Folder(ContentType.ResourcePack, version));
        var shaders = Count("shaderpacks");
        var mods = Count("mods", "*.jar*");
        var hasOptions = File.Exists(Path.Combine(dir, "options.txt"));

        SetItem(WorldsCheck, $"Welten ({worlds})", worlds > 0);
        SetItem(OptionsCheck, hasOptions ? "Einstellungen & Tastenbelegung" : "Einstellungen (keine vorhanden)", hasOptions);
        SetItem(ServersCheck, $"Server ({servers})", servers > 0);
        SetItem(ResourcePacksCheck, $"Ressourcenpakete ({packs})", packs > 0);
        SetItem(ShadersCheck, $"Shader ({shaders})", shaders > 0);
        SetItem(ModsCheck, $"Mods ({mods})", mods > 0);
        UpdateHint();
    }

    private static void SetItem(CheckBox box, string label, bool available)
    {
        box.Content = label;
        box.IsEnabled = available;
        box.IsChecked = available;
    }

    private void UpdateHint()
    {
        var src = SelectedSource?.Installation;
        var lines = new List<string>
        {
            "Vorhandenes bleibt erhalten: Gleichnamige Welten werden als Kopie abgelegt, alte Einstellungen als .bak gesichert."
        };
        if (_target != null && src != null && (src.Loader != _target.Loader || src.MinecraftVersion != _target.MinecraftVersion))
            lines.Add($"Mods: Quelle ist {src.Description}, Ziel {_target.Description}. Über den Launcher installierte Mods " +
                      "werden passend neu heruntergeladen, manuell hinzugefügte übersprungen.");
        else if (_target == null)
            lines.Add("Mods: Bei gleicher Version und gleichem Loader werden sie kopiert, sonst passend neu heruntergeladen.");
        lines.Add("Welten nicht in eine ältere Minecraft-Version übertragen, das kann sie beschädigen.");
        HintText.Text = string.Join("\n", lines);
    }

    private async void Transfer_Click(object sender, RoutedEventArgs e)
    {
        if (_target == null || !HasSelection)
            return;
        await RunAsync(_target);
    }

    /// <summary>Führt die Übertragung in die Ziel-Instanz aus und zeigt den Bericht an.</summary>
    public async Task RunAsync(Installation target)
    {
        if (SelectedSource is not { } source)
            return;
        var items = SelectedItems;
        if (items == TransferItems.None)
            return;

        TransferButton.IsEnabled = false;
        SourceBox.IsEnabled = false;
        try
        {
            var transfer = new InstanceTransfer(_app.Http, _app.Settings.CurseForgeApiKey);
            var report = await transfer.TransferAsync(source, target, items,
                new Progress<string>(t => StatusText.Text = t));
            StatusText.Text = "";
            await _app.Dialogs.ShowMessageAsync($"Übertragung von \"{source.Name}\" abgeschlossen",
                string.Join("\n\n", report));
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowMessageAsync("Übertragung fehlgeschlagen",
                ex.Message + "\n\nLäuft das Spiel noch? Dann sind manche Dateien gesperrt.");
        }
        finally
        {
            TransferButton.IsEnabled = true;
            SourceBox.IsEnabled = true;
        }
    }
}
