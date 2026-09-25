using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AxoClient.UI.Pages;

public partial class ModpackPanel : UserControl
{
    private readonly SearchPager _pager;
    private AppServices _app = null!;
    private string _query = "";
    private bool _busy;

    public event Action? Closed;

    public ModpackPanel()
    {
        InitializeComponent();
        _pager = new SearchPager(new PagerControls(ResultsList, ResultsEmpty, Pager, PrevPage, NextPage, PageText,
            TotalText, StatusText));
    }

    public void Show(AppServices app)
    {
        _app = app;
        SearchBox.Text = "";
        StatusText.Text = "";
        _ = SearchAsync();
    }

    private void Back_Click(object sender, RoutedEventArgs e) => Closed?.Invoke();

    private async void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        var open = new Microsoft.Win32.OpenFileDialog { Title = "Modpack öffnen", Filter = "Modrinth-Modpack (*.mrpack)|*.mrpack" };
        if (open.ShowDialog(Window.GetWindow(this)) == true && await ModpackDialogs.InstallFileAsync(_app, open.FileName))
            Closed?.Invoke();
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

    private Task LoadPageAsync(int page) =>
        _pager.LoadAsync(page, p => _app.Modrinth.SearchModpacksAsync(_query, p, _pager.PageSize),
            "Suche auf Modrinth...", "Modpacks", "Keine Modpacks für Fabric oder Forge gefunden.");

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
            return;
        var project = Ui.DataOf<ContentProject>(sender);
        _busy = true;
        try
        {
            if (await ModpackDialogs.InstallProjectAsync(_app, project.Id, project.Title))
                Closed?.Invoke();
        }
        finally
        {
            _busy = false;
        }
    }

    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        var project = Ui.DataOf<ContentProject>(sender);
        await ShareDialogs.ShareContentAsync(_app,
            new ContentPayload { Kind = SharedContentKind.Modpack, ProjectId = project.Id, Title = project.Title });
    }

    private void Website_Click(object sender, RoutedEventArgs e) => Shell.OpenUrl(Ui.DataOf<ContentProject>(sender).WebsiteUrl);
}
