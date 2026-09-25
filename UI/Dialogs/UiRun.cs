namespace AxoClient.UI.Dialogs;

public static class UiRun
{
    public static async Task<T?> RunAsync<T>(AppServices app, string title, Func<WorkProgress, Task<T>> work,
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
                    throw new InvalidOperationException(
                        "Die Verbindung hat zu lange gebraucht. Bitte versuche es später noch einmal.");
                }
            });
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowErrorAsync(failureTitle, ex);
            return null;
        }
    }

    public static Task ShowReportAsync(AppServices app, string title, IEnumerable<string> lines) =>
        app.Dialogs.ShowMessageAsync(title, string.Join("\n\n", lines));

    public static async Task GuardAsync(AppServices app, string failureTitle, Func<Task> action) =>
        await GuardAsync(app, failureTitle, async () =>
        {
            await action();
            return true;
        });

    public static async Task<bool> GuardAsync(AppServices app, string failureTitle, Func<Task<bool>> action)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            await app.Dialogs.ShowErrorAsync(failureTitle, ex);
            return false;
        }
    }
}
