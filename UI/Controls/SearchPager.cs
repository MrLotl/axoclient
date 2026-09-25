using System.Windows;
using System.Windows.Controls;

namespace AxoClient.UI.Controls;

public sealed record PagerControls(ListBox Results, TextBlock Empty, FrameworkElement Pager, Button Previous,
    Button Next, TextBlock PageText, TextBlock TotalText, TextBlock Status);

public sealed class SearchPager(PagerControls controls, int pageSize = 20)
{
    private int _request;

    public int Page { get; private set; }
    public int PageSize => pageSize;

    public void Clear()
    {
        _request++;
        controls.Results.ItemsSource = null;
        controls.Pager.Visibility = Visibility.Collapsed;
        controls.Empty.Visibility = Visibility.Collapsed;
    }

    public async Task LoadAsync(int page, Func<int, Task<SearchPage>> fetch, string status, string totalLabel,
        string emptyText, Action<List<ContentProject>>? prepare = null)
    {
        var request = ++_request;
        controls.Empty.Visibility = Visibility.Collapsed;
        controls.Previous.IsEnabled = controls.Next.IsEnabled = false;
        if (page == 0)
        {
            controls.Results.ItemsSource = null;
            controls.Pager.Visibility = Visibility.Collapsed;
        }

        controls.Status.Text = status;
        try
        {
            var result = await fetch(page);
            if (request != _request)
                return;

            prepare?.Invoke(result.Items);
            controls.Results.ItemsSource = result.Items;
            if (result.Items.Count > 0)
                controls.Results.ScrollIntoView(result.Items[0]);
            controls.Status.Text = "";

            Page = page;
            var totalPages = Math.Max(1, (result.TotalHits + pageSize - 1) / pageSize);
            controls.PageText.Text = $"Seite {page + 1} von {totalPages:N0}";
            controls.TotalText.Text = $"{result.TotalHits:N0} {totalLabel}";
            controls.Previous.IsEnabled = page > 0;
            controls.Next.IsEnabled = page + 1 < totalPages;
            Ui.Show(controls.Pager, result.TotalHits > 0);

            if (result.TotalHits == 0)
            {
                controls.Empty.Text = emptyText;
                controls.Empty.Visibility = Visibility.Visible;
            }
        }
        catch (Exception ex)
        {
            if (request != _request)
                return;
            controls.Status.Text = "Suche fehlgeschlagen: " + ErrorReport.Short(ex);
            controls.Previous.IsEnabled = Page > 0;
            controls.Next.IsEnabled = true;
        }
    }
}
