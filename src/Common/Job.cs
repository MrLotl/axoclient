namespace AxoClient.Common
{
    internal sealed class Job
    {
        private readonly Action<Job> _changed;
        private DateTime _lastPublish;

        public Job(string id, string kind, TextRef title, string? subject, CancellationTokenSource cts, Action<Job> changed)
        {
            _changed = changed;
            Id = id;
            Kind = kind;
            Title = title;
            Subject = subject;
            Cts = cts;
        }

        public string Id { get; }
        public string Kind { get; }
        public TextRef Title { get; }
        public string? Subject { get; }
        public CancellationTokenSource Cts { get; }
        public JobState State { get; set; } = JobState.Running;
        public double? Progress { get; private set; }
        public TextRef? Status { get; private set; }
        public TextRef? Error { get; set; }

        public void Update(double? progress, TextRef? status)
        {
            Progress = progress is { } knownProgress ? Math.Clamp(knownProgress, 0, 1) : null;
            var statusChanged = status != Status;
            Status = status;
            // Fortschritt höchstens ~15 mal pro Sekunde an die UI
            var now = DateTime.UtcNow;
            if (!statusChanged && (now - _lastPublish).TotalMilliseconds < 66)
            {
                return;
            }

            _lastPublish = now;
            _changed(this);
        }

        public JobInfo ToInfo() => new(Id, Kind, Title, State, Progress, Status, Error, Subject);
    }
}
