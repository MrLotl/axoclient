using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AxoClient.UI.Dialogs;

public static class ErrorDialog
{
    public static void Report(string title, Exception ex, string? hint = null)
    {
        if (Application.Current?.MainWindow is MainWindow { IsLoaded: true } window)
        {
            _ = Application.Current.Dispatcher.InvokeAsync(() => window.Dialogs.ShowErrorAsync(title, ex, hint));
            return;
        }
        ErrorReport.Log(title, ex);
        MessageBox.Show(ErrorReport.Describe(ex) + (hint == null ? "" : "\n\n" + hint), title,
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    public static FrameworkElement Build(string message, string details)
    {
        var detailArea = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0) };
        var detailBox = ReadOnlyText(details);
        detailBox.FontFamily = Ui.Resource<FontFamily>("MonoFont");
        detailBox.FontSize = 11.5;
        detailBox.Foreground = Ui.Resource<Brush>("TextSecondary");
        detailArea.Children.Add(new Border
        {
            Background = Ui.Resource<Brush>("DeepBg"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 10, 4, 10),
            Child = Ui.Scroll(detailBox, 220)
        });

        Button? copy = null;
        copy = Ui.Button("Kopieren", () =>
        {
            try
            {
                Clipboard.SetText(message + "\n\n" + details);
                copy!.Content = "Kopiert";
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Fehlerdetails kopieren", ex);
                copy!.Content = "Kopieren ging nicht";
            }
        });
        Button? openLog = null;
        openLog = Ui.Button("Protokoll öffnen", () =>
        {
            if (File.Exists(ErrorReport.LogPath))
                Shell.ShowFile(ErrorReport.LogPath);
            else
                openLog!.Content = "Noch kein Protokoll vorhanden.";
        });
        var buttons = Ui.Row(copy, openLog);
        buttons.Margin = new Thickness(0, 10, 0, 0);
        detailArea.Children.Add(buttons);

        Button? toggle = null;
        toggle = new Button { Content = "Einzelheiten anzeigen", Style = Ui.Resource<Style>("LinkButton") };
        toggle.Click += (_, _) =>
        {
            var show = detailArea.Visibility != Visibility.Visible;
            Ui.Show(detailArea, show);
            toggle!.Content = show ? "Einzelheiten ausblenden" : "Einzelheiten anzeigen";
        };
        toggle.HorizontalAlignment = HorizontalAlignment.Left;
        toggle.Margin = new Thickness(0, 12, 0, 0);

        return Ui.Stack(ReadOnlyText(message), toggle, detailArea);
    }

    private static TextBox ReadOnlyText(string text) => new()
    {
        Text = text,
        Foreground = Ui.Resource<Brush>("SubtleText"),
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        IsReadOnly = true,
        TextWrapping = TextWrapping.Wrap,
        Padding = new Thickness(0)
    };
}
