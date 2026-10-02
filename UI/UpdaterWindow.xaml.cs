using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace AxoClient.UI;

public partial class UpdaterWindow : Window
{
    private static readonly string[] StepLabels = ["Herunterladen", "Installieren", "Neustarten"];

    private readonly HttpClient _http;
    private readonly UpdateInfo _update;
    private CancellationTokenSource? _cancel;
    private int _phase;
    private bool _failed;

    public UpdaterWindow(HttpClient http, UpdateInfo update)
    {
        InitializeComponent();
        _http = http;
        _update = update;
        FromVersion.Text = "v" + AppInfo.ShortVersion;
        ToVersion.Text = "v" + update.Version;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await RunAsync();

    private async Task RunAsync()
    {
        _failed = false;
        _phase = 0;
        _cancel = new CancellationTokenSource();
        TitleText.Text = "AxoClient wird aktualisiert";
        Ui.Show(CancelButton, true);
        Ui.Show(RetryButton, false);
        Ui.Show(StartButton, false);
        SetProgress(0);
        RenderSteps();

        var watch = Stopwatch.StartNew();
        var total = _update.Size;
        try
        {
            StatusText.Text = "Lade Update herunter …";
            await Updater.DownloadAsync(_http, _update, new Progress<long>(done =>
            {
                var mbps = done / 1048576.0 / Math.Max(watch.Elapsed.TotalSeconds, 0.1);
                DetailText.Text = total > 0
                    ? $"{Mb(done)} von {Mb(total)} MB · {mbps.ToString("0.0", CultureInfo.GetCultureInfo("de-DE"))} MB/s"
                    : $"{Mb(done)} MB";
                if (total > 0)
                    SetProgress(Math.Min(0.9, done * 0.9 / total));
            }), _cancel.Token);

            _phase = 1;
            RenderSteps();
            StatusText.Text = "Installiere Dateien …";
            DetailText.Text = "";
            SetProgress(0.95);
            await Task.Run(Updater.Apply);

            _phase = 3;
            SetProgress(1);
            RenderSteps();
            TitleText.Text = "Update installiert";
            StatusText.Text = $"Fertig! Viel Spaß mit v{_update.Version}";
            Ui.Show(CancelButton, false);
            Ui.Show(StartButton, true);
            StartButton.Focus();
        }
        catch (Exception ex)
        {
            _failed = true;
            var cancelled = ex is OperationCanceledException;
            if (!cancelled)
                ErrorReport.Log("Update installieren", ex);
            TitleText.Text = "Update angehalten";
            StatusText.Text = cancelled ? "Update abgebrochen" : "Update fehlgeschlagen: " + ErrorReport.Short(ex);
            DetailText.Text = "Es wurde nichts geändert";
            Fill.Background = Ui.Frozen(Color.FromRgb(0x4A, 0x4A, 0x4A));
            Fill.Effect = null;
            RenderSteps();
            Ui.Show(CancelButton, false);
            Ui.Show(RetryButton, true);
        }
    }

    private static string Mb(long bytes) => (bytes / 1048576.0).ToString("0.0", CultureInfo.GetCultureInfo("de-DE"));

    private void SetProgress(double fraction)
    {
        Fill.Background = Ui.Resource<Brush>("AccentHorizontal");
        Fill.Width = Math.Max(0, 456 * Math.Clamp(fraction, 0, 1));
    }

    private void RenderSteps()
    {
        Steps.Children.Clear();
        for (var i = 0; i < StepLabels.Length; i++)
        {
            var done = _phase > i;
            var current = !done && _phase == i && !_failed;
            var dot = new Border
            {
                Width = 14,
                Height = 14,
                CornerRadius = new CornerRadius(7),
                BorderThickness = done ? new Thickness(0) : new Thickness(2),
                BorderBrush = Ui.Frozen(current ? Color.FromRgb(0xD3, 0x6A, 0xD8) : Color.FromRgb(0x3A, 0x3A, 0x3A)),
                Background = done ? Ui.Resource<Brush>("Accent") : Brushes.Transparent,
                Margin = new Thickness(0, 0, 6, 0),
                Child = done ? new Icon { Kind = "Check", Size = 9, Foreground = Brushes.White } : null
            };
            var label = new TextBlock
            {
                Text = StepLabels[i],
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Ui.Frozen(done ? Color.FromRgb(0xD9, 0xD9, 0xD9)
                    : current ? Color.FromRgb(0xEC, 0xC6, 0xF2) : Color.FromRgb(0x5F, 0x5F, 0x5F))
            };
            var step = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            step.Children.Add(dot);
            step.Children.Add(label);
            Steps.Children.Add(step);
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cancel?.Cancel();

    private async void Retry_Click(object sender, RoutedEventArgs e) => await RunAsync();

    private void Start_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Updater.Launch();
        }
        catch (Exception ex)
        {
            ErrorReport.Log("Neue Version starten", ex);
        }
        Close();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _cancel?.Cancel();
        if (_phase == 3)
        {
            Start_Click(sender, e);
            return;
        }
        Close();
        if (_failed || _phase < 3)
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
    }
}
