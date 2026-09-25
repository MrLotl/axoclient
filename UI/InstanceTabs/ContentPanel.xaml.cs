using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AxoClient.UI.InstanceTabs;

public partial class ContentPanel : UserControl
{
    private record VersionTarget(string ProjectId, string Title);

    private readonly SearchPager _pager;
    private AppServices _app = null!;
    private Installation _inst = null!;
    private ContentStore _store = null!;
    private ContentType _type;
    private Action? _hintAction;
    private List<InstalledItem> _installed = [];
    private VersionTarget? _versionTarget;
    private int _viewRequest;
    private string _query = "";
    private bool _ready;
    private bool _busy;
    private bool _split;
    private bool _adding;
    private bool _searched;

    public ContentPanel()
    {
        InitializeComponent();
        _pager = new SearchPager(new PagerControls(ResultsList, ResultsEmpty, Pager, PrevPage, NextPage, PageText,
            TotalText, StatusText));
    }

    private bool ModsPossible => _type != ContentType.Mod || _inst.CanUseMods;

    private IProgress<string> Status => new Progress<string>(t => StatusText.Text = t);

    public void Show(AppServices app, Installation inst, ContentType type)
    {
        _ready = false;
        _app = app;
        _inst = inst;
        _type = type;
        _store = app.ContentOf(inst);
        SearchBox.Text = "";
        StatusText.Text = "";
        UpdateBanner.Visibility = Visibility.Collapsed;

        RepairButton.ToolTip = type switch
        {
            ContentType.Mod => "Mods reparieren: prüfen, ob alle Mods zusammenpassen",
            ContentType.ResourcePack => "Pakete prüfen: eingeschaltet, lesbar, und sind die nötigen Mods da?",
            _ => "Shader prüfen: ist Iris/Oculus da und ein Paket ausgewählt?"
        };
        RepairButton.IsEnabled = UpdatesButton.IsEnabled = ModsPossible;

        (app.Settings.ContentAsTiles ? TileViewToggle : ListViewToggle).IsChecked = true;
        _ready = true;
        _adding = false;
        _searched = false;
        InstalledEmpty.Text = "Noch nichts installiert. Mit \"Hinzufügen\" kannst du etwas suchen und installieren.";

        SetSplit(false);
        ShowView(SearchView);
        RefreshInstalled();
    }

    private void ShowView(FrameworkElement view)
    {
        _viewRequest++;
        if (view != SearchView)
            SetSplit(true);
        foreach (var candidate in new FrameworkElement[] { SearchView, VersionsView, RepairView })
            Ui.Show(candidate, candidate == view);
    }

    private void SetSplit(bool split)
    {
        _split = split;
        ListCol.Width = split ? new GridLength(400) : new GridLength(1, GridUnitType.Star);
        GapCol.Width = new GridLength(split ? 20 : 0);
        RightCol.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Ui.Show(RightPane, split);
        Ui.Show(AddButton, !split);
        Ui.Show(DoneButton, split);
        Ui.Show(ViewToggles, !split);
        ApplyViewMode();
    }

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _app.Settings.ContentAsTiles = TileViewToggle.IsChecked == true;
        _app.SaveSettings();
        ApplyViewMode();
    }

    private void ApplyViewMode()
    {
        var tiles = TileViewToggle.IsChecked == true && !_split;
        InstalledList.ItemTemplate = (DataTemplate)FindResource(tiles ? "TileTemplate" : "ListTemplate");
        InstalledList.ItemsPanel = (ItemsPanelTemplate)FindResource(tiles ? "TilePanel" : "ListPanel");
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        _adding = true;
        SetSplit(true);
        ShowView(SearchView);
        RefreshResultStates();
        if (_searched)
            return;
        _searched = true;
        _ = SearchAsync();
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _adding = false;
        SetSplit(false);
    }

    private void BackToSearch_Click(object sender, RoutedEventArgs e)
    {
        if (!_adding)
        {
            SetSplit(false);
            return;
        }
        ShowView(SearchView);
        RefreshResultStates();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdatesButton.IsEnabled = RepairButton.IsEnabled = !busy && ModsPossible;
        UpdateAllButton.IsEnabled = FixButton.IsEnabled = RecheckButton.IsEnabled = !busy;
    }

    private void RefreshInstalled()
    {
        var updates = _installed.Where(i => i.Update != null).ToDictionary(i => i.FileName, i => i.Update);
        _installed = _store.GetInstalled(_type);
        foreach (var item in _installed)
            if (updates.TryGetValue(item.FileName, out var update))
                item.Update = update;

        InstalledList.ItemsSource = _installed;
        InstalledHeader.Text = $"Installiert ({_installed.Count})";
        Ui.Show(InstalledEmpty, _installed.Count == 0);
        UpdateUpdateBanner();
        UpdateHint();
        _ = _store.LoadMissingIconsAsync(_installed);
    }

    private async void ToggleInstalled_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<InstalledItem>(sender);
        try
        {
            _store.SetEnabled(item, !item.Enabled);
            StatusText.Text = $"{item.DisplayName} " + (item.Enabled ? "deaktiviert." : "aktiviert.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowErrorAsync("Fehler", ex);
        }
        RefreshInstalled();
    }

    private async void DeleteInstalled_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<InstalledItem>(sender);
        if (!await _app.Dialogs.ConfirmAsync("Löschen", $"\"{item.DisplayName}\" wirklich löschen?", "Löschen", danger: true))
            return;
        try
        {
            _store.Delete(item);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowErrorAsync("Fehler", ex);
        }
        RefreshInstalled();
        RefreshResultStates();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e) => Shell.OpenFolder(_inst.ContentDir(_type), create: true);

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        SetBusy(true);
        try
        {
            StatusText.Text = "Erkenne manuell hinzugefügte Dateien...";
            await _store.IdentifyUnknownAsync(_type);
            RefreshInstalled();

            StatusText.Text = "Suche nach Updates...";
            var count = await _store.CheckUpdatesAsync(_installed);
            UpdateUpdateBanner();

            var unknown = _installed.Count(i => i.Entry == null);
            var foreign = _installed.Count(i => i.Entry is { IsFromModrinth: false });
            StatusText.Text = (count == 0 ? "Alles ist auf dem neuesten Stand." : $"{count} Update(s) gefunden.")
                              + (unknown > 0 ? $" {unknown} unbekannte Datei(en) konnten nicht geprüft werden." : "")
                              + (foreign > 0 ? $" {foreign} Einträge von CurseForge können nicht geprüft werden." : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update-Suche fehlgeschlagen: " + ErrorReport.Short(ex);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateUpdateBanner()
    {
        var count = _installed.Count(i => i.HasUpdate);
        Ui.Show(UpdateBanner, count > 0);
        UpdateBannerText.Text = count == 1 ? "1 Update verfügbar" : $"{count} Updates verfügbar";
    }

    private async void UpdateOne_Click(object sender, RoutedEventArgs e) => await UpdateItemsAsync([Ui.DataOf<InstalledItem>(sender)]);

    private async void UpdateAll_Click(object sender, RoutedEventArgs e) =>
        await UpdateItemsAsync(_installed.Where(i => i.HasUpdate).ToList());

    private async Task UpdateItemsAsync(List<InstalledItem> items)
    {
        if (_busy || items.Count == 0)
            return;
        SetBusy(true);
        var failed = new List<string>();
        foreach (var item in items)
        {
            try
            {
                await _store.ApplyUpdateAsync(item, Status);
            }
            catch (Exception ex)
            {
                failed.Add($"{item.Entry?.Title ?? item.DisplayName}: {ErrorReport.Short(ex)}");
            }
        }
        SetBusy(false);
        RefreshInstalled();
        RefreshResultStates();
        StatusText.Text = failed.Count == 0 ? $"{items.Count} Eintrag/Einträge aktualisiert." : "";
        if (failed.Count > 0)
            await _app.Dialogs.ShowMessageAsync("Nicht alles konnte aktualisiert werden", string.Join("\n", failed));
    }

    private async void ChooseVersion_Click(object sender, RoutedEventArgs e)
    {
        var project = Ui.DataOf<ContentProject>(sender);
        await ShowVersionsAsync(new VersionTarget(project.Id, project.Title));
    }

    private async void ShareInstalled_Click(object sender, RoutedEventArgs e)
    {
        var item = Ui.DataOf<InstalledItem>(sender);
        if (item.Entry is { IsFromModrinth: true } entry)
            await ShareDialogs.ShareContentAsync(_app, ContentPayload.Of(item.Type, entry));
    }

    private async void ChangeVersion_Click(object sender, RoutedEventArgs e)
    {
        if (Ui.DataOf<InstalledItem>(sender).Entry is not { } entry)
            return;
        if (!entry.IsFromModrinth)
        {
            await _app.Dialogs.ShowMessageAsync("Nicht möglich",
                "Einträge von CurseForge unterstützt AxoClient nicht mehr. Lösche die Mod und installiere sie über die Suche (Modrinth) neu.");
            return;
        }
        await ShowVersionsAsync(new VersionTarget(entry.ProjectId, entry.Title));
    }

    private async Task ShowVersionsAsync(VersionTarget target)
    {
        _versionTarget = target;
        ShowView(VersionsView);
        var request = _viewRequest;
        VersionsTitle.Text = $"Versionen von {target.Title}";
        VersionsSubtitle.Text = _type == ContentType.Mod
            ? $"Passend zu {_inst.Loader} {_inst.MinecraftVersion} · Modrinth"
            : $"Passend zu Minecraft {_inst.MinecraftVersion} · Modrinth";
        VersionsList.ItemsSource = null;
        VersionsEmpty.Visibility = Visibility.Collapsed;
        StatusText.Text = "Lade Versionen...";

        try
        {
            var versions = await _app.Modrinth.GetVersionsAsync(target.ProjectId, _type, _inst);
            if (request != _viewRequest)
                return;
            var current = _store.CurrentOf(_type, target.ProjectId);
            var newest = ContentVersion.Newest(versions);
            VersionsList.ItemsSource = versions
                .Select(v => new VersionRow(v, current != null && (v.Id == current.VersionId || v.FileName == current.FileName),
                    v == newest))
                .ToList();
            StatusText.Text = "";
            if (versions.Count == 0)
            {
                VersionsEmpty.Text = "Keine Version passt zu dieser Instanz.";
                VersionsEmpty.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            if (request == _viewRequest)
                StatusText.Text = "Versionen konnten nicht geladen werden: " + ErrorReport.Short(ex);
        }
    }

    private async void InstallVersion_Click(object sender, RoutedEventArgs e)
    {
        var row = Ui.DataOf<VersionRow>(sender);
        if (_versionTarget is not { } target || _busy)
            return;

        SetBusy(true);
        row.IsBusy = true;
        try
        {
            await _store.InstallAsync(target.ProjectId, target.Title, _type, Status, version: row.Version, replace: true);
            if (VersionsList.ItemsSource is IEnumerable<VersionRow> rows)
                foreach (var r in rows)
                    r.IsCurrent = r == row;
            StatusText.Text = $"{target.Title} {row.Version.Name} installiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ErrorReport.Short(ex);
            await _app.Dialogs.ShowErrorAsync("Installation fehlgeschlagen", ex);
        }
        finally
        {
            row.IsBusy = false;
            SetBusy(false);
            foreach (var item in _installed.Where(i => i.Entry?.ProjectId == target.ProjectId))
                item.Update = null;
            RefreshInstalled();
        }
    }

    private async void Repair_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async Task AnalyzeAsync()
    {
        if (_busy)
            return;
        ShowView(RepairView);
        var request = _viewRequest;
        IssueList.ItemsSource = null;
        RepairTitle.Text = _type switch
        {
            ContentType.Mod => "Mods reparieren",
            ContentType.ResourcePack => "Ressourcenpakete prüfen",
            _ => "Shader prüfen"
        };
        RepairSubtitle.Text = _type switch
        {
            ContentType.Mod => $"Prüfe, ob alle Mods zu {_inst.Loader} {_inst.MinecraftVersion} und zueinander passen...",
            ContentType.ResourcePack => "Prüfe, ob die Pakete lesbar und eingeschaltet sind und ob die nötigen Mods da sind...",
            _ => "Prüfe, ob Shader in dieser Instanz überhaupt laufen können..."
        };
        FixButton.Visibility = Visibility.Collapsed;
        SetBusy(true);
        try
        {
            var issues = _type == ContentType.Mod
                ? await new ModRepair(_store, _app.Modrinth).AnalyzeAsync(Status)
                : await new PackRepair(_store).AnalyzeAsync(_type, Status);
            if (request != _viewRequest)
                return;

            IssueList.ItemsSource = issues;
            var problems = issues.Count(i => i.Severity != IssueSeverity.Info);
            var nothingFound = _type switch
            {
                ContentType.Mod => $"Keine Probleme gefunden: Alle geprüften Mods passen zu {_inst.Loader} {_inst.MinecraftVersion}.",
                ContentType.ResourcePack => "Keine Probleme gefunden: Die Pakete sind lesbar und alles Nötige ist da.",
                _ => "Keine Probleme gefunden: Shader können in dieser Instanz laufen."
            };
            RepairSubtitle.Text = issues.Count == 0 ? nothingFound
                : problems == 0 ? nothingFound + " Ein paar Hinweise gibt es trotzdem."
                : $"{problems} Problem(e) gefunden. Wähle aus, was behoben werden soll.";
            Ui.Show(FixButton, issues.Any(i => i.CanFix));
            StatusText.Text = "";
            RefreshInstalled();
        }
        catch (Exception ex)
        {
            if (request == _viewRequest)
            {
                RepairSubtitle.Text = "Prüfung fehlgeschlagen: " + ErrorReport.Short(ex);
                StatusText.Text = "";
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Fix_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || IssueList.ItemsSource is not List<Issue> issues)
            return;
        var selected = issues.Where(i => i.CanFix && i.Selected).ToList();
        if (selected.Count == 0)
            return;

        SetBusy(true);
        var (_, failed) = await Issue.FixAllAsync(selected, WorkProgress.For(Status));
        SetBusy(false);
        RefreshInstalled();

        if (failed.Count > 0)
            await _app.Dialogs.ShowMessageAsync("Nicht alles konnte behoben werden", string.Join("\n", failed));
        await AnalyzeAsync();
    }

    private void UpdateHint()
    {
        string? text = null, button = null;
        _hintAction = null;

        switch (_type)
        {
            case ContentType.Mod when !_inst.CanUseMods:
                text = "Vanilla-Instanzen können keine Mods laden. Dafür braucht die Instanz einen Mod-Loader " +
                       "wie Fabric (oder Forge, unter \"Einstellungen\").";
                button = "Auf Fabric umstellen";
                _hintAction = () => _ = SwitchToFabricAsync(installIris: false);
                break;

            case ContentType.Shader when !_inst.CanUseMods:
                text = "Shader brauchen einen Mod-Loader mit Iris. Die Instanz kann dafür auf Fabric umgestellt werden.";
                button = "Fabric + Iris einrichten";
                _hintAction = () => _ = SwitchToFabricAsync(installIris: true);
                break;

            case ContentType.Shader when !_store.HasModMatching("iris", "oculus"):
                var (mod, slug) = _inst.Loader == LoaderType.Fabric ? ("Iris", "iris") : ("Oculus", "oculus");
                text = $"Damit Shader funktionieren, wird die Mod {mod} benötigt (nicht für jede Minecraft-Version verfügbar).";
                button = $"{mod} installieren";
                _hintAction = () => _ = InstallShaderModAsync(slug);
                break;

            case ContentType.ResourcePack when ContentTypes.UsesTexturePacks(_inst.MinecraftVersion):
                text = $"In Minecraft {_inst.MinecraftVersion} heißen Ressourcenpakete noch \"Texture Packs\". " +
                       "Sie werden im Ordner \"texturepacks\" abgelegt.";
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
            StatusText.Text = "Prüfe, ob es Fabric für diese Version gibt...";
            var fabricVersions = await VersionCatalog.GetVersionsAsync(_app.Http, LoaderType.Fabric, snapshots: true, oldVersions: false);
            if (!fabricVersions.Contains(inst.MinecraftVersion))
            {
                StatusText.Text = "";
                await _app.Dialogs.ShowMessageAsync("Fabric nicht verfügbar",
                    $"Für Minecraft {inst.MinecraftVersion} gibt es kein Fabric. Wähle unter \"Einstellungen\" " +
                    "eine neuere Version oder Forge.");
                return;
            }

            if (!await _app.Dialogs.ConfirmAsync("Auf Fabric umstellen",
                    $"\"{inst.Name}\" wird von Vanilla auf Fabric {inst.MinecraftVersion} umgestellt." +
                    (installIris ? " Danach wird Iris (mit Sodium) installiert." : "") +
                    "\n\nWelten, Einstellungen und Ressourcenpakete bleiben erhalten. Fabric wird beim nächsten Start installiert.",
                    "Umstellen"))
            {
                StatusText.Text = "";
                return;
            }

            inst.Loader = LoaderType.Fabric;
            _app.Instances.NotifyChanged();
            if (installIris)
                await _app.ContentOf(inst).InstallBySlugAsync("iris", ContentType.Mod, Status);
            StatusText.Text = installIris ? "Auf Fabric umgestellt und Iris installiert." : "Auf Fabric umgestellt.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ErrorReport.Short(ex);
            await _app.Dialogs.ShowErrorAsync("Einrichten fehlgeschlagen", ex);
        }
        finally
        {
            HintButton.IsEnabled = true;
            if (inst == _inst && type == _type)
            {
                var status = StatusText.Text;
                Show(_app, inst, type);
                StatusText.Text = status;
            }
        }
    }

    private async Task InstallShaderModAsync(string slug)
    {
        HintButton.IsEnabled = false;
        try
        {
            await _store.InstallBySlugAsync(slug, ContentType.Mod, Status);
            StatusText.Text = "Installiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ErrorReport.Short(ex);
            await _app.Dialogs.ShowErrorAsync("Installation fehlgeschlagen", ex);
        }
        finally
        {
            HintButton.IsEnabled = true;
            RefreshInstalled();
        }
    }

    private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _ = SearchAsync();
    }

    private Task SearchAsync()
    {
        _query = SearchBox.Text.Trim();
        return LoadPageAsync(0);
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) => _ = LoadPageAsync(_pager.Page - 1);

    private void NextPage_Click(object sender, RoutedEventArgs e) => _ = LoadPageAsync(_pager.Page + 1);

    private Task LoadPageAsync(int page)
    {
        if (_type == ContentType.Mod && !_inst.CanUseMods)
        {
            _pager.Clear();
            return Task.CompletedTask;
        }
        var (type, inst, store) = (_type, _inst, _store);
        return _pager.LoadAsync(page,
            p => _app.Modrinth.SearchAsync(_query, type, inst, p, _pager.PageSize),
            "Suche auf Modrinth...", "Treffer",
            $"Keine Ergebnisse für Minecraft {inst.MinecraftVersion}" + (type == ContentType.Mod ? $" mit {inst.Loader}." : "."),
            items => items.ForEach(project => project.IsInstalled = store.IsInstalled(project.Id)));
    }

    private void RefreshResultStates()
    {
        if (ResultsList.ItemsSource is IEnumerable<ContentProject> results)
            foreach (var project in results)
                project.IsInstalled = _store.IsInstalled(project.Id);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var project = Ui.DataOf<ContentProject>(sender);
        var (store, type) = (_store, _type);
        project.IsBusy = true;
        try
        {
            await store.InstallAsync(project.Id, project.Title, type, Status);
            StatusText.Text = $"{project.Title} installiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ErrorReport.Short(ex);
            await _app.Dialogs.ShowErrorAsync("Installation fehlgeschlagen", ex);
        }
        finally
        {
            project.IsBusy = false;
            if (store == _store && type == _type)
            {
                RefreshInstalled();
                RefreshResultStates();
            }
        }
    }

    private void Website_Click(object sender, RoutedEventArgs e) => Shell.OpenUrl(Ui.DataOf<ContentProject>(sender).WebsiteUrl);
}
