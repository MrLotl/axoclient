using System.Collections.Concurrent;

namespace AxoClient.Core.Common;

public enum JobState
{
    Running,
    Succeeded,
    Failed,
    Cancelled
}

/// <summary>Was die UI über einen laufenden Vorgang weiß.</summary>
public sealed record JobInfo(
    string Id,
    string Kind,
    TextRef Title,
    JobState State,
    double? Progress,
    TextRef? Status,
    TextRef? Error,
    string? Subject);

/// <summary>Wird an jeden Job übergeben: Fortschritt melden und auf Abbruch reagieren.</summary>
public sealed class JobContext
{
    private readonly Action<double?, TextRef?> _update;
    private double? _progress;
    private TextRef? _status;

    public JobContext(Action<double?, TextRef?> update, CancellationToken ct)
    {
        _update = update;
        Token = ct;
    }

    internal JobContext(Job job, CancellationToken ct) : this(job.Update, ct)
    {
    }

    /// <summary>Für Tests und Aufrufe ohne Fortschrittsanzeige.</summary>
    public static JobContext None(CancellationToken ct = default) => new((_, _) => { }, ct);

    public CancellationToken Token { get; }

    /// <summary>Fortschritt von 0 bis 1, <c>null</c> = unbestimmt.</summary>
    public void Report(double? progress, TextRef? status = null)
    {
        _progress = progress;
        _status = status ?? _status;
        _update(progress, _status);
    }

    public void Status(string code, params (string, object?)[] args) => Report(_progress, TextRef.Of(code, args));

    /// <summary>Teilfortschritt für einen Abschnitt [from, to] des Gesamtfortschritts.</summary>
    public IProgress<double> Range(double from, double to) =>
        new SyncProgress(p => Report(from + (to - from) * Math.Clamp(p, 0, 1)));

    /// <summary>Ein Kontext, dessen 0..1 auf [from, to] dieses Kontexts abgebildet wird.</summary>
    public JobContext Sub(double from, double to) =>
        new((p, s) => Report(p is { } v ? from + (to - from) * Math.Clamp(v, 0, 1) : _progress, s), Token);

    /// <summary>Ruft sofort auf (Progress&lt;T&gt; würde über den SynchronizationContext posten).</summary>
    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}

internal sealed class Job(string id, string kind, TextRef title, string? subject, CancellationTokenSource cts, Action<Job> changed)
{
    private DateTime _lastPublish;

    public string Id { get; } = id;
    public string Kind { get; } = kind;
    public TextRef Title { get; } = title;
    public string? Subject { get; } = subject;
    public CancellationTokenSource Cts { get; } = cts;
    public JobState State { get; set; } = JobState.Running;
    public double? Progress { get; private set; }
    public TextRef? Status { get; private set; }
    public TextRef? Error { get; set; }

    public void Update(double? progress, TextRef? status)
    {
        Progress = progress is { } p ? Math.Clamp(p, 0, 1) : null;
        var statusChanged = status != Status;
        Status = status;
        // Fortschritt höchstens ~15 mal pro Sekunde an die UI
        var now = DateTime.UtcNow;
        if (!statusChanged && (now - _lastPublish).TotalMilliseconds < 66)
            return;
        _lastPublish = now;
        changed(this);
    }

    public JobInfo ToInfo() => new(Id, Kind, Title, State, Progress, Status, Error, Subject);
}

/// <summary>
/// Lange Vorgänge (Downloads, Installation, Export, Backup, Diagnose) laufen als Job: mit Fortschritt,
/// Abbrechen und Ergebnis. Die Oberfläche blockiert nie.
/// </summary>
public sealed class JobManager(EventHub events)
{
    private readonly ConcurrentDictionary<string, Job> _jobs = new();

    public IReadOnlyList<JobInfo> Active => _jobs.Values.Where(j => j.State == JobState.Running).Select(j => j.ToInfo()).ToList();

    public bool IsRunning(string kind, string? subject = null) =>
        _jobs.Values.Any(j => j.State == JobState.Running && j.Kind == kind && (subject == null || j.Subject == subject));

    public Task<T> RunAsync<T>(string kind, TextRef title, Func<JobContext, Task<T>> work, string? subject = null,
        CancellationToken outer = default)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
        var job = new Job(Guid.NewGuid().ToString("N")[..12], kind, title, subject, cts, Publish);
        _jobs[job.Id] = job;
        Publish(job);
        return Execute(job, work);
    }

    public Task RunAsync(string kind, TextRef title, Func<JobContext, Task> work, string? subject = null,
        CancellationToken outer = default) =>
        RunAsync<bool>(kind, title, async ctx =>
        {
            await work(ctx);
            return true;
        }, subject, outer);

    public bool Cancel(string jobId)
    {
        if (!_jobs.TryGetValue(jobId, out var job) || job.State != JobState.Running)
            return false;
        job.Cts.Cancel();
        return true;
    }

    private async Task<T> Execute<T>(Job job, Func<JobContext, Task<T>> work)
    {
        try
        {
            var result = await Task.Run(() => work(new JobContext(job, job.Cts.Token)), job.Cts.Token);
            job.State = JobState.Succeeded;
            return result;
        }
        catch (OperationCanceledException) when (job.Cts.IsCancellationRequested)
        {
            job.State = JobState.Cancelled;
            throw;
        }
        catch (Exception ex)
        {
            job.State = JobState.Failed;
            job.Error = AxoException.Describe(ex);
            if (ex is not AxoException)
                AppLog.Error($"Job {job.Kind} fehlgeschlagen", ex);
            throw;
        }
        finally
        {
            Publish(job);
            job.Cts.Dispose();
            // Fertige Jobs kurz behalten, damit die UI das Ergebnis noch abholen kann
            _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(_ => _jobs.TryRemove(job.Id, out var _),
                TaskScheduler.Default);
        }
    }

    private void Publish(Job job) => events.Publish("job.changed", job.ToInfo());
}
