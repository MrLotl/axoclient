using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace McLauncher.Pages;

/// <summary>Modpacks von Modrinth suchen und als neue Instanz installieren.</summary>
public partial class ModpackPanel : UserControl
{
    private const int PageSize = 20;

    private AppState _app = null!;
    private ModrinthProvider _modrinth = null!;
    private int _searchRequest; // verwirft veraltete Suchergebnisse
    private string _query = "";
    private int _page;
    private bool _busy;

    /// <summary>Zurück zur Instanzübersicht (auch nach einer erfolgreichen Installation).</summary>
    public event Action? Closed;

    public ModpackPanel()
    {
        InitializeComponent();
    }

    public void Show(AppState app)
    {
        _app = app;
        _modrinth = new ModrinthProvider(app.Http);
        SearchBox.Text = "";
        StatusText.Text = "";
        _ = SearchAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var open = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Modpack öffnen",
            Filter = "Modrinth-Modpack (*.mrpack)|*.mrpack"
        };
        if (open.ShowDialog(Window.GetWindow(this)) != true)
            return;
        if (await ModpackUi.InstallFileAsync(_app, open.FileName))
            Closed?.Invoke();
    }

    // ================= Suche =================

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

    private void PrevPage_Click(object sender, RoutedEventArgs e) => _ = LoadPageAsync(_page - 1);

    private void NextPage_Click(object sender, RoutedEventArgs e) => _ = LoadPageAsync(_page + 1);

    private async Task LoadPageAsync(int page)
    {
        var request = ++_searchRequest;
        ResultsEmpty.Visibility = Visibility.Collapsed;
        PrevPage.IsEnabled = NextPage.IsEnabled = false;
        if (page == 0)
        {
            ResultsList.ItemsSource = null;
            Pager.Visibility = Visibility.Collapsed;
        }

        StatusText.Text = "Suche auf Modrinth...";
        try
        {
            var result = await _modrinth.SearchModpacksAsync(_query, page, PageSize);
            if (request != _searchRequest)
                return;

            ResultsList.ItemsSource = result.Items;
            if (result.Items.Count > 0)
                ResultsList.ScrollIntoView(result.Items[0]);
            StatusText.Text = "";

            _page = page;
            var totalPages = Math.Max(1, (result.TotalHits + PageSize - 1) / PageSize);
            PageText.Text = $"Seite {page + 1} von {totalPages:N0}";
            TotalText.Text = $"{result.TotalHits:N0} Modpacks";
            PrevPage.IsEnabled = page > 0;
            NextPage.IsEnabled = page + 1 < totalPages;
            Pager.Visibility = result.TotalHits > 0 ? Visibility.Visible : Visibility.Collapsed;

            if (result.TotalHits == 0)
            {
                ResultsEmpty.Text = "Keine Modpacks für Fabric oder Forge gefunden.";
                ResultsEmpty.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            if (request != _searchRequest)
                return;
            StatusText.Text = "Suche fehlgeschlagen: " + ex.Message;
            PrevPage.IsEnabled = _page > 0;
            NextPage.IsEnabled = true;
        }
    }

    // ================= Aktionen =================

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var project = (ContentProject)((FrameworkElement)sender).DataContext;
        _busy = true;
        try
        {
            if (await ModpackUi.InstallProjectAsync(_app, project.Id, project.Title))
                Closed?.Invoke();
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var project = (ContentProject)((FrameworkElement)sender).DataContext;
        await ShareUi.ShareContentAsync(_app, new ContentPayload
        {
            Kind = SharedContentKind.Modpack,
            ProjectId = project.Id,
            Title = project.Title
        });
    }

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        var project = (ContentProject)((FrameworkElement)sender).DataContext;
        if (project.WebsiteUrl != null)
            Process.Start(new ProcessStartInfo(project.WebsiteUrl) { UseShellExecute = true });
    }
}
