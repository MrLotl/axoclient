using System.Windows;

namespace AxoClient.Core;

public interface IDialogService
{
    Task<bool> ConfirmAsync(string title, string text, string confirmText = "OK", bool danger = false);

    Task ShowMessageAsync(string title, string text);

    Task ShowErrorAsync(string title, Exception ex, string? hint = null);

    Task<bool> ShowFormAsync(string title, FrameworkElement content, string confirmText, Func<bool>? validate = null,
        double width = 440);

    Task<T> RunWithProgressAsync<T>(string title, Func<WorkProgress, Task<T>> work);
}
