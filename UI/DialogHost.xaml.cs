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

    public DialogHost()
    {
        InitializeComponent();
    }

    public bool IsOpen => Visibility == Visibility.Visible;

    public Task<bool> ConfirmAsync(string title, string text, string confirmText = "OK", bool danger = false) =>
        Open(title, text, confirmText, showCancel: true, danger);

    public Task ShowMessageAsync(string title, string text) =>
        Open(title, text, "OK", showCancel: false, danger: false);

    public Task ShowErrorAsync(string title, Exception ex, string? hint = null)
    {
        if (!Dispatcher.CheckAccess())
            return Dispatcher.InvokeAsync(() => ShowErrorAsync(title, ex, hint)).Task.Unwrap();

        ErrorReport.Log(title, ex);
        var text = ErrorReport.Describe(ex) + (string.IsNullOrWhiteSpace(hint) ? "" : "\n\n" + hint);
        return Open(title, null, "OK", showCancel: false, danger: false,
            ErrorDialog.Build(text, ErrorReport.Details(ex, title)));
    }

    public Task<bool> ShowFormAsync(string title, FrameworkElement content, string confirmText, Func<bool>? validate = null,
        double width = DefaultWidth)
    {
        var task = Open(title, null, confirmText, showCancel: true, danger: false, content, validate);
        DialogBox.Width = Math.Max(DefaultWidth, Math.Min(width, (Window.GetWindow(this)?.ActualWidth ?? 0) - 48));
        Dispatcher.BeginInvoke(() => FindFirst<TextBox>(content)?.Focus(), DispatcherPriority.Input);
        return task;
    }

    public async Task<T> RunWithProgressAsync<T>(string title, Func<WorkProgress, Task<T>> work)
    {
        ReplaceOpenDialog();
        using var cts = new CancellationTokenSource();
        _progressCancel = cts;

        Reset(title, showCancel: true);
        DialogTextScroller.Visibility = Visibility.Collapsed;
        DialogOk.Visibility = Visibility.Collapsed;
        DialogProgress.IsIndeterminate = true;
        DialogProgress.Value = 0;
        DialogProgressText.Text = "";
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
            }),
            cts.Token);
        try
        {
            return await work(progress);
        }
        finally
        {
            Visibility = Visibility.Collapsed;
            ProgressPanel.Visibility = Visibility.Collapsed;
            _progressCancel = null;
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
        else
        {
            Close(e.Key == Key.Enter);
        }
        e.Handled = true;
    }

    private Task<bool> Open(string title, string? text, string confirmText, bool showCancel, bool danger,
        FrameworkElement? content = null, Func<bool>? validate = null)
    {
        ReplaceOpenDialog();
        _result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _validate = validate;

        Reset(title, showCancel);
        ProgressPanel.Visibility = Visibility.Collapsed;
        DialogText.Text = text ?? "";
        DialogTextScroller.Visibility = content == null ? Visibility.Visible : Visibility.Collapsed;
        DialogContent.Content = content;
        DialogContent.Visibility = content == null ? Visibility.Collapsed : Visibility.Visible;
        DialogOk.Visibility = Visibility.Visible;
        DialogOk.Content = confirmText;
        DialogOk.Background = Ui.Resource<Brush>(danger ? "Danger" : "Accent");
        Visibility = Visibility.Visible;
        DialogOk.Focus();
        return _result.Task;
    }

    private void Reset(string title, bool showCancel)
    {
        DialogBox.Width = DefaultWidth;
        DialogTitle.Text = title;
        DialogContent.Content = null;
        DialogContent.Visibility = Visibility.Collapsed;
        DialogCancel.Content = "Abbrechen";
        DialogCancel.IsEnabled = true;
        DialogCancel.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReplaceOpenDialog()
    {
        var previous = _result;
        _result = null;
        _validate = null;
        previous?.TrySetResult(false);
    }

    private void Close(bool result)
    {
        if (result && _validate != null && !_validate())
            return;
        Visibility = Visibility.Collapsed;
        DialogContent.Content = null;
        _validate = null;
        var finished = _result;
        _result = null;
        finished?.TrySetResult(result);
    }

    private void CancelProgress()
    {
        if (_progressCancel is not { IsCancellationRequested: false } cts)
            return;
        DialogCancel.IsEnabled = false;
        DialogProgressText.Text = "Wird abgebrochen...";
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
