using System.Collections.Concurrent;

namespace AxoClient.Common
{
    /// <summary>
    /// Lange Vorgänge (Downloads, Installation, Export, Backup, Diagnose) laufen als Job: mit Fortschritt,
    /// Abbrechen und Ergebnis. Die Oberfläche blockiert nie.
    /// </summary>
    public sealed class JobManager : IJobManager
    {
        private readonly IEventHub _events;
        private readonly IAppLog _appLog;
        private readonly IExceptionHelper _exceptionHelper;
        private readonly ConcurrentDictionary<string, Job> _jobs = new();

        public JobManager(IEventHub events, IAppLog appLog, IExceptionHelper exceptionHelper)
        {
            _events = events;
            _appLog = appLog;
            _exceptionHelper = exceptionHelper;
        }

        public IReadOnlyList<JobInfo> Active => _jobs.Values.Where(j => j.State == JobState.Running).Select(j => j.ToInfo()).ToList();

        public bool IsRunning(string kind, string? subject = null) =>
            _jobs.Values.Any(j => j.State == JobState.Running && j.Kind == kind && (subject == null || j.Subject == subject));

        public Task<T> Run<T>(string kind, TextRef title, Func<JobContext, Task<T>> work, string? subject = null,
            CancellationToken outer = default)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(outer);
            var job = new Job(Guid.NewGuid().ToString("N")[..12], kind, title, subject, cts, publish);
            _jobs[job.Id] = job;
            publish(job);
            return execute(job, work);
        }

        public Task Run(string kind, TextRef title, Func<JobContext, Task> work, string? subject = null,
            CancellationToken outer = default) =>
            Run<bool>(kind, title, async ctx =>
            {
                await work(ctx);
                return true;
            }, subject, outer);

        public bool Cancel(string jobId)
        {
            if (!_jobs.TryGetValue(jobId, out var job) || job.State != JobState.Running)
            {
                return false;
            }

            job.Cts.Cancel();
            return true;
        }

        private async Task<T> execute<T>(Job job, Func<JobContext, Task<T>> work)
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
                job.Error = _exceptionHelper.Describe(ex);
                if (ex is not AxoException)
                {
                    _appLog.Error($"Job {job.Kind} fehlgeschlagen", ex);
                }

                throw;
            }
            finally
            {
                publish(job);
                job.Cts.Dispose();
                // Fertige Jobs kurz behalten, damit die UI das Ergebnis noch abholen kann
                _ = Task.Delay(TimeSpan.FromMinutes(2)).ContinueWith(_ => _jobs.TryRemove(job.Id, out var _),
                    TaskScheduler.Default);
            }
        }

        private void publish(Job job) => _events.Publish("job.changed", job.ToInfo());
    }
}
