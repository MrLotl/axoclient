using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;

namespace McLauncher.Pages;

/// <summary>
/// Mods, Ressourcenpakete oder Shader einer Instanz: installierte Liste (mit Version ändern, Updates, Reparatur)
/// und rechts Suche auf Modrinth, Versionsauswahl oder Reparatur-Ergebnis.
/// </summary>
public partial class ContentPanel : UserControl
{
    private const int PageSize = 20;

    /// <summary>Wofür gerade die Versionsliste angezeigt wird.</summary>
    private record VersionTarget(IContentProvider Provider, string ProjectId, string Title);

    private AppState _app = null!;
    private Installation _inst = null!;
    private ContentStore _store = null!;
    private ModrinthProvider _modrinth = null!;
    private ContentType _type;
    private Action? _hintAction;
    private List<InstalledItem> _installed = [];
    private VersionTarget? _versionTarget;

    private int _searchRequest; // verwirft veraltete Suchergebnisse
    private int _viewRequest;   // verwirft veraltete Versions-/Reparaturergebnisse
    private string _query = "";
    private int _page;
    private bool _ready;
    private bool _busy;
    private bool _split;
    private bool _adding;
    private bool _searched;

    public ContentPanel()
    {
        InitializeComponent();
    }

    /// <summary>Zeigt die Inhalte einer Instanz für den gewählten Typ an.</summary>
    public void Show(AppState app, Installation inst, ContentType type)
    {
        _ready = false;
        _app = app;
        _inst = inst;
        _type = type;
        _store = new ContentStore(inst, app.Http);
        _modrinth = new ModrinthProvider(app.Http);
        SearchBox.Text = "";
        StatusText.Text = "";
        UpdateBanner.Visibility = Visibility.Collapsed;

        var modsPossible = type != ContentType.Mod || inst.Loader != LoaderType.Vanilla;
        RepairButton.Visibility = type == ContentType.Mod ? Visibility.Visible : Visibility.Collapsed;
        RepairButton.IsEnabled = UpdatesButton.IsEnabled = modsPossible;

        (app.Settings.ContentAsTiles ? TileViewToggle : ListViewToggle).IsChecked = true;
        _ready = true;
        _adding = false;
        _searched = false;
        InstalledEmpty.Text = "Noch nichts installiert. Mit \"Hinzufügen\" kannst du etwas suchen und installieren.";

        SetSplit(false);
        ShowView(SearchView);
        RefreshInstalled();
    }

    /// <summary>Anbieter für bereits installierte Einträge; CurseForge-Einträge lassen sich ohne Schlüssel nicht aktualisieren.</summary>
    private IContentProvider? ProviderFor(ContentSource source) =>
        source == ContentSource.Modrinth ? _modrinth
        : string.IsNullOrWhiteSpace(_app.Settings.CurseForgeApiKey) ? null
        : new CurseForgeProvider(_app.Http, _app.Settings.CurseForgeApiKey);

    /// <summary>Gesucht wird auf Modrinth (CurseForge bräuchte einen eigenen API-Schlüssel).</summary>
    private IContentProvider? CurrentProvider => _modrinth;

    private IProgress<string> Status => new Progress<string>(t => StatusText.Text = t);

    private void ShowView(FrameworkElement view)
    {
        _viewRequest++;
        if (view != SearchView)
            SetSplit(true);
        SearchView.Visibility = view == SearchView ? Visibility.Visible : Visibility.Collapsed;
        VersionsView.Visibility = view == VersionsView ? Visibility.Visible : Visibility.Collapsed;
        RepairView.Visibility = view == RepairView ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Wechselt zwischen der breiten Liste und der geteilten Ansicht (links Liste, rechts Suche usw.).</summary>
    private void SetSplit(bool split)
    {
        _split = split;
        ListCol.Width = split ? new GridLength(320) : new GridLength(1, GridUnitType.Star);
        GapCol.Width = split ? new GridLength(20) : new GridLength(0);
        RightCol.Width = split ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        RightPane.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        AddButton.Visibility = split ? Visibility.Collapsed : Visibility.Visible;
        DoneButton.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        ViewToggles.Visibility = split ? Visibility.Collapsed : Visibility.Visible;
        ApplyViewMode();
    }

    private void ViewToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (!_ready)
            return;
        _app.Settings.ContentAsTiles = TileViewToggle.IsChecked == true;
        _app.Save();
        ApplyViewMode();
    }

    /// <summary>Kacheln gibt es nur in der breiten Liste; neben der Suche (schmal) bleibt es die Liste.</summary>
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
        if (!_searched)
        {
            _searched = true;
            _ = SearchAsync();
        }
    }

    private void Done_Click(object sender, RoutedEventArgs e)
    {
        _adding = false;
        SetSplit(false);
    }

    private void BackToSearch_Click(object sender, RoutedEventArgs e)
    {
        if (_adding)
        {
            ShowView(SearchView);
            RefreshResultStates();
        }
        else
        {
            SetSplit(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        var modsPossible = _type != ContentType.Mod || _inst.Loader != LoaderType.Vanilla;
        UpdatesButton.IsEnabled = RepairButton.IsEnabled = !busy && modsPossible;
        UpdateAllButton.IsEnabled = FixButton.IsEnabled = RecheckButton.IsEnabled = !busy;
    }

    // ================= Installierte Inhalte =================

    private void RefreshInstalled()
    {
        // Gefundene Updates beim Neuladen der Liste behalten
        var updates = _installed.Where(i => i.Update != null).ToDictionary(i => i.FileName, i => i.Update);
        _installed = _store.GetInstalled(_type);
        foreach (var item in _installed)
            if (updates.TryGetValue(item.FileName, out var update))
                item.Update = update;

        InstalledList.ItemsSource = _installed;
        InstalledHeader.Text = $"Installiert ({_installed.Count})";
        InstalledEmpty.Visibility = _installed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateUpdateBanner();
        UpdateHint();
    }

    private async void ToggleInstalled_Click(object sender, RoutedEventArgs e)
    {
        var item = (InstalledItem)((FrameworkElement)sender).DataContext;
        try
        {
            _store.SetEnabled(item, !item.Enabled);
            StatusText.Text = $"{item.DisplayName} " + (item.Enabled ? "deaktiviert." : "aktiviert.");
        }
        catch (Exception ex)
        {
            StatusText.Text = "";
            await _app.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
        RefreshInstalled();
    }

    private async void DeleteInstalled_Click(object sender, RoutedEventArgs e)
    {
        var item = (InstalledItem)((FrameworkElement)sender).DataContext;
        if (!await _app.Dialogs.ConfirmAsync("Löschen", $"\"{item.DisplayName}\" wirklich löschen?", "Löschen", danger: true))
            return;
        try
        {
            _store.Delete(item);
        }
        catch (Exception ex)
        {
            await _app.Dialogs.ShowMessageAsync("Fehler", ex.Message);
        }
        RefreshInstalled();
        RefreshResultStates();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var folder = _store.FolderOf(_type);
        Directory.CreateDirectory(folder);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
    }

    // ================= Updates =================

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        SetBusy(true);
        try
        {
            // Manuell hinzugefügte Dateien per Hash bei Modrinth zuordnen, damit auch sie Updates bekommen
            StatusText.Text = "Erkenne manuell hinzugefügte Dateien...";
            await _store.IdentifyUnknownAsync(_type, _modrinth);
            RefreshInstalled();

            StatusText.Text = "Suche nach Updates...";
            var count = await _store.CheckUpdatesAsync(_installed, ProviderFor);
            UpdateUpdateBanner();

            var unchecked_ = _installed.Count(i => i.Entry == null);
            var missingKey = _installed.Count(i => i.Entry?.Source == ContentSource.CurseForge && ProviderFor(ContentSource.CurseForge) == null);
            StatusText.Text = (count == 0 ? "Alles ist auf dem neuesten Stand." : $"{count} Update(s) gefunden.")
                              + (unchecked_ > 0 ? $" {unchecked_} unbekannte Datei(en) konnten nicht geprüft werden." : "")
                              + (missingKey > 0 ? $" {missingKey} Einträge von CurseForge können nicht geprüft werden." : "");
        }
        catch (Exception ex)
        {
            StatusText.Text = "Update-Suche fehlgeschlagen: " + ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateUpdateBanner()
    {
        var count = _installed.Count(i => i.HasUpdate);
        UpdateBanner.Visibility = count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateBannerText.Text = count == 1 ? "1 Update verfügbar" : $"{count} Updates verfügbar";
    }

    private async void UpdateOne_Click(object sender, RoutedEventArgs e)
    {
        var item = (InstalledItem)((FrameworkElement)sender).DataContext;
        await UpdateItemsAsync([item]);
    }

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
            if (item is not { Entry: { } entry, Update: { } update } || ProviderFor(entry.Source) is not { } provider)
                continue;
            try
            {
                await _store.InstallAsync(provider, entry.ProjectId, entry.Title, _type, Status,
                    version: update, replace: true);
                item.Update = null;
            }
            catch (Exception ex)
            {
                failed.Add($"{entry.Title}: {ex.Message}");
            }
        }
        SetBusy(false);
        RefreshInstalled();
        RefreshResultStates();
        StatusText.Text = failed.Count == 0 ? $"{items.Count} Eintrag/Einträge aktualisiert." : "";
        if (failed.Count > 0)
            await _app.Dialogs.ShowMessageAsync("Nicht alles konnte aktualisiert werden", string.Join("\n", failed));
    }

    // ================= Version auswählen / ändern =================

    private async void ChooseVersion_Click(object sender, RoutedEventArgs e)
    {
        var project = (ContentProject)((FrameworkElement)sender).DataContext;
        if (ProviderFor(project.Source) is { } provider)
            await ShowVersionsAsync(new VersionTarget(provider, project.Id, project.Title));
    }

    private async void ChangeVersion_Click(object sender, RoutedEventArgs e)
    {
        var item = (InstalledItem)((FrameworkElement)sender).DataContext;
        if (item.Entry is not { } entry)
            return;
        if (ProviderFor(entry.Source) is not { } provider)
        {
            await _app.Dialogs.ShowMessageAsync("Nicht möglich",
                "Einträge von CurseForge unterstützt AxoClient nicht mehr. Lösche die Mod und installiere sie über die Suche (Modrinth) neu.");
            return;
        }
        await ShowVersionsAsync(new VersionTarget(provider, entry.ProjectId, entry.Title));
    }

    private async Task ShowVersionsAsync(VersionTarget target)
    {
        _versionTarget = target;
        ShowView(VersionsView);
        var request = _viewRequest;
        VersionsTitle.Text = $"Versionen von {target.Title}";
        VersionsSubtitle.Text = _type == ContentType.Mod
            ? $"Passend zu {_inst.Loader} {_inst.MinecraftVersion} · {target.Provider.Source}"
            : $"Passend zu Minecraft {_inst.MinecraftVersion} · {target.Provider.Source}";
        VersionsList.ItemsSource = null;
        VersionsEmpty.Visibility = Visibility.Collapsed;
        StatusText.Text = "Lade Versionen...";

        try
        {
            var versions = await target.Provider.GetVersionsAsync(target.ProjectId, _type, _inst);
            if (request != _viewRequest)
                return;
            var current = CurrentVersionOf(target);
            var newest = versions.FirstOrDefault(v => v.Channel == "release") ?? versions.FirstOrDefault();
            VersionsList.ItemsSource = versions
                .Select(v => new VersionRow(v, IsSameVersion(v, current), v == newest))
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
                StatusText.Text = "Versionen konnten nicht geladen werden: " + ex.Message;
        }
    }

    private InstalledContent? CurrentVersionOf(VersionTarget target) =>
        _store.GetIndex().FirstOrDefault(i => i.Type == _type && i.Source == target.Provider.Source
                                              && i.ProjectId == target.ProjectId);

    private static bool IsSameVersion(ContentVersion v, InstalledContent? current) =>
        current != null && (v.Id == current.VersionId || v.FileName == current.FileName);

    private async void InstallVersion_Click(object sender, RoutedEventArgs e)
    {
        var row = (VersionRow)((FrameworkElement)sender).DataContext;
        if (_versionTarget is not { } target || _busy)
            return;

        SetBusy(true);
        row.IsBusy = true;
        try
        {
            await _store.InstallAsync(target.Provider, target.ProjectId, target.Title, _type, Status,
                version: row.Version, replace: true);
            if (VersionsList.ItemsSource is IEnumerable<VersionRow> rows)
                foreach (var r in rows)
                    r.IsCurrent = r == row;
            StatusText.Text = $"{target.Title} {row.Version.Name} installiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ex.Message;
            await _app.Dialogs.ShowMessageAsync("Installation fehlgeschlagen", ex.Message);
        }
        finally
        {
            row.IsBusy = false;
            SetBusy(false);
            // Eine gerade installierte Version ist kein Update mehr
            foreach (var item in _installed.Where(i => i.Entry?.ProjectId == target.ProjectId))
                item.Update = null;
            RefreshInstalled();
        }
    }

    // ================= Mods reparieren =================

    private async void Repair_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async Task AnalyzeAsync()
    {
        if (_busy)
            return;
        ShowView(RepairView);
        var request = _viewRequest;
        IssueList.ItemsSource = null;
        RepairSubtitle.Text = $"Prüfe, ob alle Mods zu {_inst.Loader} {_inst.MinecraftVersion} und zueinander passen...";
        FixButton.Visibility = Visibility.Collapsed;
        SetBusy(true);
        try
        {
            var curseForge = ProviderFor(ContentSource.CurseForge) as CurseForgeProvider;
            var issues = await new ModRepair(_store, _modrinth, curseForge).AnalyzeAsync(Status);
            if (request != _viewRequest)
                return;

            IssueList.ItemsSource = issues;
            var problems = issues.Count(i => i.Severity != IssueSeverity.Info);
            RepairSubtitle.Text = problems == 0
                ? $"Keine Probleme gefunden: Alle geprüften Mods passen zu {_inst.Loader} {_inst.MinecraftVersion}."
                : $"{problems} Problem(e) gefunden. Wähle aus, was behoben werden soll.";
            FixButton.Visibility = issues.Any(i => i.CanFix) ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = "";
            RefreshInstalled(); // erkannte Herkunft manueller Mods anzeigen
        }
        catch (Exception ex)
        {
            if (request == _viewRequest)
            {
                RepairSubtitle.Text = "Prüfung fehlgeschlagen: " + ex.Message;
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
        if (_busy || IssueList.ItemsSource is not List<RepairIssue> issues)
            return;
        var selected = issues.Where(i => i.CanFix && i.Selected).ToList();
        if (selected.Count == 0)
            return;

        SetBusy(true);
        var failed = new List<string>();
        foreach (var issue in selected)
        {
            try
            {
                StatusText.Text = issue.FixText;
                await issue.Fix!(Status);
            }
            catch (Exception ex)
            {
                failed.Add($"{issue.Title}: {ex.Message}");
            }
        }
        SetBusy(false);
        RefreshInstalled();

        if (failed.Count > 0)
            await _app.Dialogs.ShowMessageAsync("Nicht alles konnte behoben werden", string.Join("\n", failed));
        await AnalyzeAsync(); // Ergebnis kontrollieren (z.B. neue Abhängigkeiten der Ersatzversionen)
    }

    // ================= Hinweise =================

    private void UpdateHint()
    {
        string? text = null, button = null;
        _hintAction = null;

        switch (_type)
        {
            case ContentType.Mod when _inst.Loader == LoaderType.Vanilla:
                text = "Vanilla-Instanzen können keine Mods laden. Dafür braucht die Instanz einen Mod-Loader " +
                       "wie Fabric (oder Forge, unter \"Einstellungen\").";
                button = "Auf Fabric umstellen";
                _hintAction = () => _ = SwitchToFabricAsync(installIris: false);
                break;

            case ContentType.Shader when _inst.Loader == LoaderType.Vanilla:
                text = "Shader brauchen einen Mod-Loader mit Iris. Die Instanz kann dafür auf Fabric umgestellt werden.";
                button = "Fabric + Iris einrichten";
                _hintAction = () => _ = SwitchToFabricAsync(installIris: true);
                break;

            case ContentType.Shader when !_store.HasModMatching("iris", "oculus"):
                var (mod, slug) = _inst.Loader == LoaderType.Fabric ? ("Iris", "iris") : ("Oculus", "oculus");
                text = $"Damit Shader funktionieren, wird die Mod {mod} benötigt " +
                       "(nicht für jede Minecraft-Version verfügbar).";
                button = $"{mod} installieren";
                _hintAction = () => _ = InstallShaderModAsync(slug);
                break;

            case ContentType.ResourcePack when ContentTypes.UsesTexturePacks(_inst.MinecraftVersion):
                text = $"In Minecraft {_inst.MinecraftVersion} heißen Ressourcenpakete noch \"Texture Packs\". " +
                       "Sie werden im Ordner \"texturepacks\" abgelegt.";
                break;
        }

        HintPanel.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        HintText.Text = text;
        HintButton.Content = button;
        HintButton.Visibility = button == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void HintButton_Click(object sender, RoutedEventArgs e) => _hintAction?.Invoke();

    /// <summary>Stellt eine Vanilla-Instanz auf Fabric um (Welten/Einstellungen bleiben) und installiert optional Iris.</summary>
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
            _app.NotifyInstallationsChanged();

            if (installIris)
                await new ContentStore(inst, _app.Http).InstallBySlugAsync(_modrinth, "iris", ContentType.Mod, Status);
            StatusText.Text = installIris ? "Auf Fabric umgestellt und Iris installiert." : "Auf Fabric umgestellt.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ex.Message;
            await _app.Dialogs.ShowMessageAsync("Einrichten fehlgeschlagen", ex.Message);
        }
        finally
        {
            HintButton.IsEnabled = true;
            // Panel mit dem neuen Loader neu aufbauen (Suche, Hinweise, Buttons)
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
            // Iris/Oculus gibt es zuverlässig auf Modrinth, unabhängig von der gewählten Suchquelle
            await _store.InstallBySlugAsync(_modrinth, slug, ContentType.Mod, Status);
            StatusText.Text = "Installiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ex.Message;
            await _app.Dialogs.ShowMessageAsync("Installation fehlgeschlagen", ex.Message);
        }
        finally
        {
            HintButton.IsEnabled = true;
            RefreshInstalled();
        }
    }

    // ================= Suche =================

    private void Search_Click(object sender, RoutedEventArgs e) => _ = SearchAsync();

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            _ = SearchAsync();
    }

    /// <summary>Neue Suche (Suchbegriff, Kategorie oder Quelle geändert): beginnt auf Seite 1.</summary>
    private Task SearchAsync()
    {
        _query = SearchBox.Text.Trim();
        return LoadPageAsync(0);
    }

    private void PrevPage_Click(object sender, RoutedEventArgs e) => _ = LoadPageAsync(_page - 1);

    private void NextPage_Click(object sender, RoutedEventArgs e) => _ = LoadPageAsync(_page + 1);

    private async Task LoadPageAsync(int page)
    {
        var request = ++_searchRequest;
        var type = _type;
        ResultsEmpty.Visibility = Visibility.Collapsed;
        PrevPage.IsEnabled = NextPage.IsEnabled = false;
        if (page == 0)
        {
            // Neue Suche: alte Treffer sofort entfernen (könnten zu einer anderen Kategorie gehören)
            ResultsList.ItemsSource = null;
            Pager.Visibility = Visibility.Collapsed;
        }

        if ((type == ContentType.Mod && _inst.Loader == LoaderType.Vanilla) || CurrentProvider is not { } provider)
        {
            // Vanilla-Instanz ohne Mod-Loader
            ResultsList.ItemsSource = null;
            Pager.Visibility = Visibility.Collapsed;
            return;
        }

        StatusText.Text = $"Suche auf {provider.Source}...";
        try
        {
            var result = await provider.SearchAsync(_query, type, _inst, page, PageSize);
            if (request != _searchRequest)
                return;

            foreach (var project in result.Items)
                project.IsInstalled = _store.IsInstalled(project.Source, project.Id);
            ResultsList.ItemsSource = result.Items;
            if (result.Items.Count > 0)
                ResultsList.ScrollIntoView(result.Items[0]); // neue Seite oben beginnen
            StatusText.Text = "";

            _page = page;
            var totalPages = Math.Max(1, (result.TotalHits + PageSize - 1) / PageSize);
            PageText.Text = $"Seite {page + 1} von {totalPages:N0}";
            TotalText.Text = $"{result.TotalHits:N0} Treffer";
            PrevPage.IsEnabled = page > 0;
            NextPage.IsEnabled = page + 1 < totalPages;
            Pager.Visibility = result.TotalHits > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (result.TotalHits == 0)
            {
                ResultsEmpty.Text = $"Keine Ergebnisse für Minecraft {_inst.MinecraftVersion}" +
                                    (type == ContentType.Mod ? $" mit {_inst.Loader}." : ".");
                ResultsEmpty.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            if (request != _searchRequest)
                return;
            StatusText.Text = "Suche fehlgeschlagen: " + ex.Message;
            // Bisherige Seite bleibt stehen, Blättern wieder erlauben
            PrevPage.IsEnabled = _page > 0;
            NextPage.IsEnabled = true;
        }
    }

    private void RefreshResultStates()
    {
        if (ResultsList.ItemsSource is not IEnumerable<ContentProject> results)
            return;
        foreach (var project in results)
            project.IsInstalled = _store.IsInstalled(project.Source, project.Id);
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        var project = (ContentProject)((FrameworkElement)sender).DataContext;
        if (ProviderFor(project.Source) is not { } provider)
            return;

        var (store, type) = (_store, _type);
        project.IsBusy = true;
        try
        {
            await store.InstallAsync(provider, project.Id, project.Title, type, Status);
            StatusText.Text = $"{project.Title} installiert.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Fehler: " + ex.Message;
            await _app.Dialogs.ShowMessageAsync("Installation fehlgeschlagen", ex.Message);
        }
        finally
        {
            project.IsBusy = false;
            // Nur aktualisieren, wenn inzwischen nicht zu einer anderen Instanz/Kategorie gewechselt wurde
            if (store == _store && type == _type)
            {
                RefreshInstalled();
                RefreshResultStates(); // Abhängigkeiten können ebenfalls in der Liste stehen
            }
        }
    }

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        var project = (ContentProject)((FrameworkElement)sender).DataContext;
        if (project.WebsiteUrl != null)
            OpenUrl(project.WebsiteUrl);
    }

    private static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
