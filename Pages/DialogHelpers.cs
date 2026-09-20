using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace McLauncher.Pages;

/// <summary>Bausteine für Formulare, die im Code zusammengesetzt und im Dialog angezeigt werden.</summary>
internal static class Ui
{
    private static object Resource(string key) => Application.Current.FindResource(key);

    public static TextBlock Label(string text) =>
        new() { Text = text, Style = (Style)Resource("FieldLabel") };

    /// <summary>Erklärender, umbrechender Text in gedämpfter Farbe.</summary>
    public static TextBlock Note(string text, double bottom = 10, double size = 12) => new()
    {
        Text = text,
        Foreground = (Brush)Resource("SubtleText"),
        FontSize = size,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, bottom)
    };

    /// <summary>Hinweis, der nur bei Bedarf erscheint (z.B. warum "Teilen" gerade nicht geht).</summary>
    public static TextBlock Problem() => new()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0xE3, 0x6D, 0x6F)),
        FontSize = 12,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0),
        Visibility = Visibility.Collapsed
    };

    public static TextBox Input(string text = "") =>
        new() { Text = text, Style = (Style)Resource("LauncherTextBox"), Margin = new Thickness(0, 0, 0, 12) };

    public static ComboBox Combo(IEnumerable<object> items) => new()
    {
        Style = (Style)Resource("LauncherCombo"),
        ItemsSource = items.ToList(),
        Margin = new Thickness(0, 0, 0, 12)
    };

    public static CheckBox Check(string text, bool isChecked = true) =>
        new() { Content = text, IsChecked = isChecked, Margin = new Thickness(0, 0, 0, 8) };

    /// <summary>Umschalter im Stil der Tabs; alle einer Gruppe gehören zusammen.</summary>
    public static RadioButton Choice(string text, string group, bool isChecked = false) => new()
    {
        Content = text,
        GroupName = group,
        IsChecked = isChecked,
        Style = (Style)Resource("SegmentButton"),
        Padding = new Thickness(12, 6, 12, 6)
    };

    public static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    /// <summary>Scrollbare Liste für viele Einträge; die Leiste im Stil des Launchers.</summary>
    public static ScrollViewer Scroll(UIElement content, double maxHeight)
    {
        var viewer = new ScrollViewer
        {
            Content = content,
            MaxHeight = maxHeight,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 0, 10)
        };
        viewer.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), Resource("SlimScrollBar"));
        return viewer;
    }
}

/// <summary>Zeigt eine längere Arbeit im Fortschrittsdialog und meldet ihr Ergebnis einheitlich.</summary>
internal static class UiRun
{
    /// <summary>
    /// Führt <paramref name="work"/> mit Fortschrittsdialog aus. Liefert das Ergebnis oder null, wenn der Nutzer
    /// abgebrochen hat oder ein Fehler auftrat (dieser wurde dann schon in einem Dialog gemeldet).
    /// </summary>
    public static async Task<T?> RunAsync<T>(AppState app, string title, Func<WorkProgress, Task<T>> work,
        string failureTitle) where T : class
    {
        try
        {
            return await app.Dialogs.RunWithProgressAsync(title, async progress =>
            {
                try
                {
                    return await work(progress);
                }
                catch (OperationCanceledException) when (!progress.Cancel.IsCancellationRequested)
                {
                    // Nicht der Nutzer: HttpClient meldet ein Zeitlimit ebenfalls so
                    throw new InvalidOperationException(
                        "Die Verbindung hat zu lange gebraucht. Bitte versuche es später noch einmal.");
                }
            });
        }
        catch (OperationCanceledException)
        {
            return null; // vom Nutzer abgebrochen
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowMessageAsync(failureTitle, ex is System.Net.Http.HttpRequestException
                ? "Die Verbindung ist fehlgeschlagen: " + ex.Message
                : ex.Message);
            return null;
        }
    }

    public static Task ShowReportAsync(AppState app, string title, IEnumerable<string> lines) =>
        app.Dialogs.ShowMessageAsync(title, string.Join("\n\n", lines));
}
