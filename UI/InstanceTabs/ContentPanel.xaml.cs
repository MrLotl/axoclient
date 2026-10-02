using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.InstanceTabs;

public partial class ContentPanel : UserControl
{
    private static readonly Dictionary<string, (string Signature, List<Issue> Issues)> IssueCache = new();

    private AppServices _app = null!;
    private Installation _inst = null!;
    private ContentStore _store = null!;
    private ContentType _type;
    private Action? _hintAction;
    private List<ContentRow> _rows = [];
    private bool _ready;
    private bool _checkedUpdates;
    private int _analysis;

    public ContentPanel()
    {
        InitializeComponent();
    }

    private bool ModsPossible => _type != ContentType.Mod || _inst.CanUseMods;

    private IProgress<string> Status => new Progress<string>(ShowStatus);

    public void Show(AppServices app, Installation inst, ContentType type)
    {
        _ready = false;
        var sameTarget = _inst == inst && _type == type;
        _app = app;
        _inst = inst;
        _type = type;
        _store = app.ContentOf(inst);
        if (!sameTarget)
        {
            FilterBox.Text = "";
            _checkedUpdates = false;
        }
        Field.SetHint(FilterBox, type switch
        {
            ContentType.Mod => "Mods durchsuchen…",
            ContentType.ResourcePack => "Ressourcepacks durchsuchen…",
            _ => "Shader durchsuchen…"
        });
        AddText.Text = type switch
        {
            ContentType.Mod => "Mods hinzufügen",
            ContentType.ResourcePack => "Packs hinzufügen",
            _ => "Shader hinzufügen"
        };
        RepairButton.ToolTip = type switch
        {
            ContentType.Mod => "Prüfen, ob alle Mods zueinander und zu dieser Version passen",
            ContentType.ResourcePack => "Prüfen, ob die Pakete lesbar und eingeschaltet sind",
            _ => "Prüfen, ob Shader in dieser Instanz laufen können"
        };
        Ui.Show(ProfilesButton, type == ContentType.ResourcePack);
        RepairButton.IsEnabled = UpdatesButton.IsEnabled = AddButton.IsEnabled = ModsPossible;
        ShowStatus("");
        (app.Settings.ContentAsTiles ? GridToggle : ListToggle).IsChecked = true;
        ApplyViewMode();
        _ready = true;
        Refresh();
        _ = AnalyzeQuietlyAsync();
    }

    public void Refresh()
    {
        var updates = _rows.Where(r => r.Item.Update != null).ToDictionary(r => r.Item.FileName, r => r.Item.Update);
        var installed = _store.GetInstalled(_type);
        foreach (var item in installed)
            if (updates.TryGetValue(item.FileName, out var update))
                item.Update = update;

        var activePacks = _type == ContentType.ResourcePack
            ? MinecraftOptions.ReadList(_inst.OptionsFile, MinecraftOptions.ResourcePacks)?
                .Select(MinecraftOptions.PackFileName).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;
        var shader = _type == ContentType.Shader ? IrisConfig.ReadShader(_inst) : null;
        _rows = installed.Select(item => new ContentRow(item, _type switch
        {
            ContentType.Mod => item.Enabled,
            ContentType.ResourcePack => activePacks?.Contains(item.FileName) ?? false,
            _ => shader != null && shader.Equals(item.FileName, StringComparison.OrdinalIgnoreCase)
        })).ToList();

        ApplyFilter();
        UpdateSummary();
        UpdateHint();
        _ = _store.LoadMissingIconsAsync(installed);
    }

    private void ApplyFilter()
    {
        var query = FilterBox.Text.Trim();
        var shown = query.Length == 0
            ? _rows
            : _rows.Where(r => r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                               || r.Item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        Items.ItemsSource = shown;
        EmptyText.Text = _rows.Count == 0
            ? $"Noch keine {ContentTypes.PluralLabel(_type)}. Mit „{AddText.Text}“ findest du welche auf Modrinth."
            : $"Keine {ContentTypes.PluralLabel(_type)} gefunden.";
        Ui.Show(EmptyText, shown.Count == 0);
    }

    private void UpdateSummary()
    {
        var active = _rows.Count(r => r.Active);
        SummaryText.Text = _rows.Count == 0 ? ""
            : _type == ContentType.Shader
                ? $"{Formats.Count(_rows.Count, "Shader", "Shader")} · {(active > 0 ? "1 aktiv" : "keiner aktiv")}"
                : $"{active} von {_rows.Count} aktiv";
        var updates = _rows.Count(r => r.HasUpdate);
        UpdatesText.Text = !_checkedUpdates ? "Nach Updates suchen"
            : updates > 0 ? Formats.Count(updates, "Update verfügbar", "Updates verfügbar")
            : "Alles aktuell";
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        Ui.Show(StatusText, text.Length > 0);
    }

    private void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready)
            ApplyFilter();
    }

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _app.Settings.ContentAsTiles = GridToggle.IsChecked == true;
        _app.SaveSettings();
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var grid = GridToggle.IsChecked == true;
        Items.ItemTemplate = (DataTemplate)FindResource(grid ? "CardTemplate" : "ListTemplate");
        Items.ItemsPanel = (ItemsPanelTemplate)FindResource(grid ? "GridPanel" : "ListPanel");
    }

    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ContentRow>(sender);
        try
        {
            if (_type == ContentType.Mod)
            {
                _store.SetEnabled(row.Item, !row.Active);
            }
            else
            {
                var path = _inst.OptionsFile;
                var list = MinecraftOptions.ReadList(path, MinecraftOptions.ResourcePacks) ?? ["vanilla"];
                list.RemoveAll(p => MinecraftOptions.PackFileName(p).Equals(row.Item.FileName, StringComparison.OrdinalIgnoreCase));
                if (!row.Active)
                    list.Add("file/" + row.Item.FileName);
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(_inst.GameDir);
                    File.WriteAllText(path, "");
                }
                MinecraftOptions.WriteListOrThrow(path, MinecraftOptions.ResourcePacks, list);
            }
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Umschalten fehlgeschlagen", ex);
        }
        Refresh();
    }

    private async void Activate_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ContentRow>(sender);
        if (!IrisConfig.WriteShader(_inst, row.Item.FileName))
            await _app.Dialogs.ShowMessageAsync("Nicht möglich",
                "Die Shader-Einstellung konnte nicht geschrieben werden. Läuft Minecraft noch?");
        Refresh();
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ContentRow>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Löschen", $"„{row.Name}“ wirklich löschen?", "Löschen", danger: true))
            return;
        await UiRun.GuardAsync(_app, "Löschen fehlgeschlagen", () =>
        {
            _store.Delete(row.Item);
            return Task.CompletedTask;
        });
        Refresh();
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ContentRow>(sender);
        if (row.Item.Entry is { IsFromModrinth: true } entry)
            await ShareDialogs.ShareContentAsync(_app, ContentPayload.Of(row.Item.Type, entry));
    }

    private async void Version_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<ContentRow>(sender);
        if (row.Item.Entry is not { } entry)
            return;
        if (!entry.IsFromModrinth)
        {
            await _app.Dialogs.ShowMessageAsync("Nicht möglich",
                "Einträge von CurseForge unterstützt AxoClient nicht mehr. Lösche sie und installiere sie über „Hinzufügen“ (Modrinth) neu.");
            return;
        }
        if (await ContentDialogs.ChooseVersionAsync(_app, _inst, _store, _type, row))
            Refresh();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_inst.ContentDir(_type), create: true);

    private async void Profiles_Click(object sender, RoutedEventArgs e)
    {
        await PackProfileDialog.ShowAsync(_app, _inst);
        Refresh();
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        await ContentDialogs.AddAsync(_app, _inst, _store, _type);
        Refresh();
        _ = AnalyzeQuietlyAsync(force: true);
    }

    private async void Repair_Click(object sender, RoutedEventArgs e)
    {
        await ContentDialogs.RepairAsync(_app, _inst, _store, _type);
        Refresh();
        _ = AnalyzeQuietlyAsync(force: true);
    }

    private async void Updates_Click(object sender, RoutedEventArgs e) => await OpenUpdatesAsync();

    public async Task OpenUpdatesAsync()
    {
        var items = _rows.Select(r => r.Item).ToList();
        await ContentDialogs.UpdatesAsync(_app, _inst, _store, _type, items);
        _checkedUpdates = true;
        Refresh();
    }

    private async Task AnalyzeQuietlyAsync(bool force = false)
    {
        Ui.Show(IssueBadge, false);
        if (!ModsPossible || _rows.Count == 0)
            return;
        var request = ++_analysis;
        var (inst, type, store) = (_inst, _type, _store);
        var key = inst.Id + ":" + type;
        var signature = string.Join("|", _rows.Select(r => r.Item.FileName + (r.Active ? "+" : "-")));
        List<Issue> issues;
        if (!force && IssueCache.TryGetValue(key, out var cached) && cached.Signature == signature)
        {
            issues = cached.Issues;
        }
        else
        {
            try
            {
                issues = type == ContentType.Mod
                    ? await new ModRepair(store, _app.Modrinth).AnalyzeAsync(new Progress<string>())
                    : await new PackRepair(store).AnalyzeAsync(type, new Progress<string>());
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Inhalte im Hintergrund prüfen", ex);
                return;
            }
            IssueCache[key] = (signature, issues);
        }
        if (request != _analysis || inst != _inst || type != _type)
            return;
        var problems = issues.Count(i => i.Severity != IssueSeverity.Info);
        IssueCount.Text = problems.ToString();
        Ui.Show(IssueBadge, problems > 0);
    }

    private void UpdateHint()
    {
        string? text = null, button = null;
        _hintAction = null;

        switch (_type)
        {
            case ContentType.Mod when !_inst.CanUseMods:
                text = "Vanilla-Instanzen können keine Mods laden. Dafür braucht die Instanz einen Mod-Loader wie Fabric.";
                button = "Auf Fabric umstellen";
                _hintAction = () => _ = SwitchToFabricAsync(installIris: false);
                break;

            case ContentType.Shader when !_inst.CanUseMods:
                text = "Shader brauchen einen Mod-Loader mit Iris. Die Instanz kann dafür auf Fabric umgestellt werden.";
                button = "Fabric + Iris einrichten";
                _hintAction = () => _ = SwitchToFabricAsync(installIris: true);
                break;

            case ContentType.Shader when !_store.HasModMatching("iris", "oculus"):
                var (mod, slug) = _inst.Loader is LoaderType.Forge or LoaderType.NeoForge ? ("Oculus", "oculus") : ("Iris", "iris");
                text = $"Damit Shader funktionieren, wird die Mod {mod} benötigt.";
                button = $"{mod} installieren";
                _hintAction = () => _ = InstallShaderModAsync(slug);
                break;

            case ContentType.ResourcePack when ContentTypes.UsesTexturePacks(_inst.MinecraftVersion):
                text = $"In Minecraft {_inst.MinecraftVersion} heißen Ressourcepacks noch „Texture Packs“ und liegen im Ordner „texturepacks“.";
                break;
        }

        Ui.Show(HintPanel, text != null);
        HintText.Text = text;
        HintButton.Content = button;
        Ui.Show(HintButton, button != null);
    }

    private void HintButton_Click(object sender, RoutedEventArgs e) => _hintAction?.Invoke();

    private async Task SwitchToFabricAsync(bool installIris)
    {
        var (inst, type) = (_inst, _type);
        HintButton.IsEnabled = false;
        try
        {
            ShowStatus("Prüfe, ob es Fabric für diese Version gibt …");
            var fabricVersions = await VersionCatalog.GetVersionsAsync(_app.Http, LoaderType.Fabric, snapshots: true, oldVersions: false);
            if (!fabricVersions.Contains(inst.MinecraftVersion))
            {
                ShowStatus("");
                await _app.Dialogs.ShowMessageAsync("Fabric nicht verfügbar",
                    $"Für Minecraft {inst.MinecraftVersion} gibt es kein Fabric. Wähle unter „Einstellungen“ eine neuere Version.");
                return;
            }

            if (!await _app.Dialogs.ConfirmAsync("Auf Fabric umstellen",
                    $"„{inst.Name}“ wird von Vanilla auf Fabric {inst.MinecraftVersion} umgestellt." +
                    (installIris ? " Danach wird Iris (mit Sodium) installiert." : "") +
                    "\n\nWelten, Einstellungen und Ressourcepacks bleiben erhalten. Fabric wird beim nächsten Start installiert.",
                    "Umstellen"))
            {
                ShowStatus("");
                return;
            }

            inst.Loader = LoaderType.Fabric;
            _app.Instances.NotifyChanged();
            if (installIris)
                await _app.ContentOf(inst).InstallBySlugAsync("iris", ContentType.Mod, Status);
            ShowStatus(installIris ? "Auf Fabric umgestellt und Iris installiert." : "Auf Fabric umgestellt.");
        }
        catch (Exception ex)
        {
            ShowStatus("");
            await _app.Dialogs.ShowErrorAsync("Einrichten fehlgeschlagen", ex);
        }
        finally
        {
            HintButton.IsEnabled = true;
            if (inst == _inst && type == _type)
                Show(_app, inst, type);
        }
    }

    private async Task InstallShaderModAsync(string slug)
    {
        HintButton.IsEnabled = false;
        try
        {
            await _store.InstallBySlugAsync(slug, ContentType.Mod, Status);
            ShowStatus("Installiert.");
        }
        catch (Exception ex)
        {
            ShowStatus("");
            await _app.Dialogs.ShowErrorAsync("Installation fehlgeschlagen", ex);
        }
        finally
        {
            HintButton.IsEnabled = true;
            Refresh();
        }
    }
}
