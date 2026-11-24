using System.Text;
using AxoClient.Common;
using AxoClient.DataModel.Diagnostics;
using AxoClient.Diagnostics.Contract;

namespace AxoClient.Diagnostics
{
    /// <summary>"Diagnose starten" in den Einstellungen: der Launcher prüft sich selbst.</summary>
    public sealed class DiagnosticsService : IDiagnosticsService
    {
        private readonly IEnumerable<IDiagnosticCheck> _checks;
        private readonly ILogRedactor _logRedactor;
        private readonly ISystemInfo _systemInfo;
        private readonly IAppInfo _appInfo;
        private readonly IExceptionHelper _exceptionHelper;

        public DiagnosticsService(
            IEnumerable<IDiagnosticCheck> checks,
            ILogRedactor logRedactor,
            ISystemInfo systemInfo,
            IAppInfo appInfo,
            IExceptionHelper exceptionHelper)
        {
            _checks = checks;
            _logRedactor = logRedactor;
            _systemInfo = systemInfo;
            _appInfo = appInfo;
            _exceptionHelper = exceptionHelper;
        }

        public async Task<IReadOnlyList<DiagnosticItem>> Run(JobContext job)
        {
            var list = _checks.ToList();
            var results = new List<DiagnosticItem>();
            for (var i = 0; i < list.Count; i++)
            {
                job.Report((double)i / list.Count, new TextRef("diagnostics.running", ("area", list[i].Area)));
                try
                {
                    results.AddRange(await list[i].Run(job.Token));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    results.Add(new DiagnosticItem(list[i].Area, DiagnosticStatus.Error, new TextRef("diagnostics.checkFailed"),
                        _exceptionHelper.Describe(ex)));
                }
            }
            job.Report(1);
            return results;
        }

        /// <summary>"Bericht kopieren": lesbar und geschwärzt (Tokens, Spielername, UUID, IP-Adressen).</summary>
        public string Report(IReadOnlyList<DiagnosticItem> items, Func<TextRef, string> translate)
        {
            var text = new StringBuilder()
                .AppendLine($"AxoClient {_appInfo.Version} · Diagnose {DateTime.Now:yyyy-MM-dd HH:mm}")
                .AppendLine(_systemInfo.WindowsVersion)
                .AppendLine();
            foreach (var group in items.GroupBy(i => i.Area))
            {
                text.AppendLine($"[{group.Key}]");
                foreach (var item in group)
                {
                    text.Append(item.Status switch { DiagnosticStatus.Ok => "  OK      ", DiagnosticStatus.Warning => "  WARNUNG ", _ => "  FEHLER  " })
                        .Append(translate(item.Title));
                    if (item.Detail != null)
                    {
                        text.Append(" – ").Append(translate(item.Detail));
                    }

                    text.AppendLine();
                }
            }
            return _logRedactor.RedactForSharing(text.ToString());
        }
    }
}
