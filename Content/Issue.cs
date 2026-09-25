namespace AxoClient.Content;

public enum IssueSeverity
{
    Error,
    Warning,
    Info
}

public sealed class Issue
{
    public required IssueSeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public string? FixText { get; init; }
    public Func<WorkProgress, Task<string>>? Fix { get; init; }
    public bool Selected { get; set; } = true;

    public bool CanFix => Fix != null;

    public string Icon => Severity switch
    {
        IssueSeverity.Error => "",
        IssueSeverity.Warning => "",
        _ => ""
    };

    public string Marker => Severity switch
    {
        IssueSeverity.Error => "✖",
        IssueSeverity.Warning => "▲",
        _ => "•"
    };

    public static Func<WorkProgress, Task<string>> Do(Action action, string result = "") => _ =>
    {
        action();
        return Task.FromResult(result);
    };

    public static Func<WorkProgress, Task<string>> Do(Func<WorkProgress, Task> action, string result = "") =>
        async progress =>
        {
            await action(progress);
            return result;
        };

    public static async Task<(List<string> Done, List<string> Failed)> FixAllAsync(IEnumerable<Issue> issues,
        WorkProgress progress)
    {
        var done = new List<string>();
        var failed = new List<string>();
        foreach (var issue in issues.Where(i => i.CanFix))
        {
            progress.Cancel.ThrowIfCancellationRequested();
            try
            {
                progress.Text.Report(issue.FixText ?? issue.Title);
                done.Add(await issue.Fix!(progress));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed.Add($"{issue.Title}: {ErrorReport.Short(ex)}");
            }
        }
        return (done, failed);
    }
}
