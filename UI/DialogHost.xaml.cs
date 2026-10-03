using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AxoClient.UI;

public partial class DialogHost : UserControl, IDialogService
{
    private const double DefaultWidth = 440;

    private TaskCompletionSource<bool>? _result;
    private Func<bool>? _validate;
    private CancellationTokenSource? _progressCancel;
    private bool _panelMode;
    private Func<Size, double>? _panelFit;
    private readonly Stack<(FrameworkElement Panel, double Width, Func<Size, double>? Fit, TaskCompletionSource<bool> Result)> _suspended = new();

    public DialogHost()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => OpenChanged?.Invoke(IsOpen);
        SizeChanged += (_, _) => ApplyPanelFit();
    }

    public event Action<bool>? OpenChanged;

    public bool IsOpen => Visibility == Visibility.Visible;

    public Task<bool> ConfirmAsync(string title, string text, string confirmText = "OK", bool danger = false) =>
        Open(title, text, confirmText, showCancel: true, danger, icon: danger ? "Warning" : null);

    public Task ShowMessageAsync(string title, string text) =>
        Open(title, text, "OK", showCancel: false, danger: false);

    public Task ShowErrorAsync(string title, Exception ex, string? hint = null)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.InvokeAsync(() => ShowErrorAsync(title, ex, hint)).Task.Unwrap();

        ErrorReport.Log(title, ex);
        var text = ErrorReport.Describe(ex) + (string.IsNullOrWhiteSpace(hint) ? "" : "\n\n" + hint);
        return Open(title, null, "OK", showCancel: false, danger: false,
            ErrorDialog.Build(text, ErrorReport.Details(ex, title)), icon: "Warning", iconDanger: true);
    }

    public Task<bool> ShowFormAsync(string title, FrameworkElement content, string confirmText, Func<bool>? validate = null,
        double width = DefaultWidth, string? subtitle = null, string? icon = null)
    {
        var task = Open(title, null, confirmText, showCancel: true, danger: false, content, validate, subtitle, icon);
        DialogBox.Width = FitWidth(width);
        Dispatcher.BeginInvoke(() => FindFirst<TextBox>(content)?.Focus(), DispatcherPriority.Input);
        return task;
    }

    public Task ShowPanelAsync(FrameworkElement panel, double width = DefaultWidth)
    {
        ReplaceOpenDialog();
        _result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _panelMode = true;
        StandardLayout.Visibility = Visibility.Collapsed;
        CustomContent.Content = panel;
        CustomContent.Visibility = Visibility.Visible;
        DialogBox.Width = FitWidth(width);
        Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => FindFirst<TextBox>(panel)?.Focus(), DispatcherPriority.Input);
        return _result.Task;
    }

    public Task ShowPanelAsync(FrameworkElement panel, Func<Size, double> fit)
    {
        var task = ShowPanelAsync(panel);
        _panelFit = fit;
        ApplyPanelFit();
        return task;
    }

    private void ApplyPanelFit()
    {
        if (!_panelMode || _panelFit == null)
            return;
        var host = Window.GetWindow(this) is { } window ? new Size(window.ActualWidth, window.ActualHeight) : new Size(1200, 760);
        DialogBox.Width = FitWidth(_panelFit(host));
    }

    public void ClosePanel()
    {
        if (_panelMode)
            Close(false);
    }

    public async Task<T> RunWithProgressAsync<T>(string title, Func<WorkProgress, Task<T>> work)
    {
        ReplaceOpenDialog();
        using var cts = new CancellationTokenSource();
        _progressCancel = cts;

        Reset(title, showCancel: true, icon: null);
        DialogTextScroller.Visibility = Visibility.Collapsed;
        DialogOk.Visibility = Visibility.Collapsed;
        CloseButton.Visibility = Visibility.Collapsed;
        DialogProgress.IsIndeterminate = true;
        DialogProgress.Value = 0;
        DialogProgressText.Text = "Bitte warten …";
        DialogProgressPercent.Text = "";
        ProgressPanel.Visibility = Visibility.Visible;
        Visibility = Visibility.Visible;

        var progress = new WorkProgress(
            new Progress<string>(text =>
            {
                if (!cts.IsCancellationRequested)
                    DialogProgressText.Text = text;
            }),
            new Progress<double>(fraction =>
            {
                DialogProgress.IsIndeterminate = false;
                DialogProgress.Value = Math.Clamp(fraction, 0, 1) * 100;
                DialogProgressPercent.Text = $"{Math.Round(DialogProgress.Value)} %";
            }),
            cts.Token);
        try
        {
            return await work(progress);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
            _progressCancel = null;
            if (!RestoreSuspended())
                Visibility = Visibility.Collapsed;
        }
    }

    public void HandleKey(KeyEventArgs e)
    {
        if (!IsOpen || e.Key is not (Key.Escape or Key.Enter))
            return;
        if (_progressCancel != null)
        {
            if (e.Key == Key.Escape)
                CancelProgress();
        }
        else if (_panelMode)
        {
            if (e.Key != Key.Escape)
                return;
            Close(false);
        }
        else
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement is TextBox { AcceptsReturn: true })
                return;
            Close(e.Key == Key.Enter);
        }
        e.Handled = true;
    }

    private Task<bool> Open(string title, string? text, string confirmText, bool showCancel, bool danger,
        FrameworkElement? content = null, Func<bool>? validate = null, string? subtitle = null, string? icon = null,
        bool iconDanger = false)
    {
        ReplaceOpenDialog();
        _result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _validate = validate;

        Reset(title, showCancel, icon, iconDanger || danger);
        DialogSubtitle.Text = subtitle ?? "";
        Ui.Show(DialogSubtitle, !string.IsNullOrEmpty(subtitle));
        ProgressPanel.Visibility = Visibility.Collapsed;
        DialogText.Text = text ?? "";
        DialogTextScroller.Visibility = content == null ? Visibility.Visible : Visibility.Collapsed;
        DialogContent.Content = content;
        DialogContent.Visibility = content == null ? Visibility.Collapsed : Visibility.Visible;
        DialogOk.Visibility = Visibility.Visible;
        DialogOk.Content = confirmText;
        DialogOk.Style = Ui.Resource<Style>(danger ? "DangerFillButton" : "PrimaryButton");
        Visibility = Visibility.Visible;
        DialogOk.Focus();
        return _result.Task;
    }

    private void Reset(string title, bool showCancel, string? icon, bool iconDanger = false)
    {
        _panelMode = false;
        _panelFit = null;
        StandardLayout.Visibility = Visibility.Visible;
        CustomContent.Content = null;
        CustomContent.Visibility = Visibility.Collapsed;
        DialogBox.Width = DefaultWidth;
        DialogTitle.Text = title;
        DialogSubtitle.Visibility = Visibility.Collapsed;
        DialogContent.Content = null;
        DialogContent.Visibility = Visibility.Collapsed;
        DialogCancel.Content = "Abbrechen";
        DialogCancel.IsEnabled = true;
        DialogCancel.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        IconTile.Visibility = icon == null ? Visibility.Collapsed : Visibility.Visible;
        TitleIcon.Kind = icon ?? "";
        IconTile.Background = Ui.Resource<Brush>(iconDanger ? "DangerSoft" : "AccentSoft");
        TitleIcon.Foreground = iconDanger ? Ui.Frozen(Color.FromRgb(0xFF, 0x8A, 0x80)) : Ui.Resource<Brush>("AccentText");
    }

    private double FitWidth(double width) =>
        Math.Max(360, Math.Min(width, (Window.GetWindow(this)?.ActualWidth ?? 1200) - 48));

    private void ReplaceOpenDialog()
    {
        if (_panelMode && _result != null && CustomContent.Content is FrameworkElement panel && IsOpen)
        {
            _suspended.Push((panel, DialogBox.Width, _panelFit, _result));
            CustomContent.Content = null;
            _result = null;
            _panelMode = false;
            _panelFit = null;
            return;
        }
        var previous = _result;
        _result = null;
        _validate = null;
        previous?.TrySetResult(false);
    }

    private void Close(bool result)
    {
        if (result && _validate != null && !_validate())
            return;
        DialogContent.Content = null;
        CustomContent.Content = null;
        _validate = null;
        _panelMode = false;
        _panelFit = null;
        var finished = _result;
        _result = null;
        if (!RestoreSuspended())
            Visibility = Visibility.Collapsed;
        finished?.TrySetResult(result);
    }

    private bool RestoreSuspended()
    {
        if (_suspended.Count == 0)
            return false;
        var (panel, width, fit, result) = _suspended.Pop();
        _panelMode = true;
        _panelFit = fit;
        _result = result;
        StandardLayout.Visibility = Visibility.Collapsed;
        CustomContent.Content = panel;
        CustomContent.Visibility = Visibility.Visible;
        DialogBox.Width = width;
        Visibility = Visibility.Visible;
        ApplyPanelFit();
        return true;
    }

    private void CancelProgress()
    {
        if (_progressCancel is not { IsCancellationRequested: false } cts)
            return;
        DialogCancel.IsEnabled = false;
        DialogProgressText.Text = "Wird abgebrochen …";
        cts.Cancel();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close(true);

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_progressCancel != null)
            CancelProgress();
        else
            Close(false);
    }

    private void Backdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_progressCancel == null && e.OriginalSource == Backdrop)
            Close(false);
    }

    private void DialogBox_MouseDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private static T? FindFirst<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
                return match;
            if (FindFirst<T>(child) is { } found)
                return found;
        }
        return null;
    }
}
