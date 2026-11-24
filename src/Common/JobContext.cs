namespace AxoClient.Common
{
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
        /// <summary>Ohne Fortschrittsanzeige (für Aufrufe außerhalb eines Jobs).</summary>
        public JobContext(CancellationToken ct = default)
            : this((_, _) => { }, ct)
        {
        }

        public CancellationToken Token { get; }

        /// <summary>Fortschritt von 0 bis 1, <c>null</c> = unbestimmt.</summary>
        public void Report(double? progress, TextRef? status = null)
        {
            _progress = progress;
            _status = status ?? _status;
            _update(progress, _status);
        }

        public void Status(string code, params (string, object?)[] args) => Report(_progress, new TextRef(code, args));

        /// <summary>Teilfortschritt für einen Abschnitt [from, to] des Gesamtfortschritts.</summary>
        public IProgress<double> Range(double from, double to) =>
            new SyncProgress(p => Report(from + (to - from) * Math.Clamp(p, 0, 1)));

        /// <summary>Ein Kontext, dessen 0..1 auf [from, to] dieses Kontexts abgebildet wird.</summary>
        public JobContext Sub(double from, double to) =>
            new((p, s) => Report(p is { } v ? from + (to - from) * Math.Clamp(v, 0, 1) : _progress, s), Token);

        /// <summary>Ruft sofort auf (Progress&lt;T&gt; würde über den SynchronizationContext posten).</summary>
        private sealed class SyncProgress : IProgress<double>
        {
            private readonly Action<double> _report;

            public SyncProgress(Action<double> report)
            {
                _report = report;
            }

            public void Report(double value) => _report(value);
        }
    }
}
