using System.Diagnostics;
using AxoClient.Common;

namespace AxoClient.App.Bridge
{
    /// <summary>Basis aller Handler: meldet Nachrichtentypen beim <see cref="MessageRouter"/> an und bietet gemeinsame Hilfen.</summary>
    public abstract class BridgeHandler
    {
        protected BridgeHandler(IAppLog appLog, ITextHelper textHelper)
        {
            AppLog = appLog;
            TextHelper = textHelper;
        }

        protected IAppLog AppLog { get; }
        protected ITextHelper TextHelper { get; }

        /// <summary>Meldet alle Nachrichtentypen dieses Bereichs an (feste Liste).</summary>
        public abstract void Register(MessageRouter router, MainWindow window);

        /// <summary>Langer Vorgang als Job: die UI bekommt sofort die Job-ID, Fortschritt kommt per Ereignis.</summary>
        protected object StartJob(Task task)
        {
            _ = task.ContinueWith(finishedTask =>
            {
                if (finishedTask.Exception?.InnerException is { } exception and not AxoException and not OperationCanceledException)
                {
                    AppLog.Error("Job fehlgeschlagen", exception);
                }
            }, TaskScheduler.Default);
            return new { started = true };
        }

        protected void OpenInExplorer(string path)
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
            }
            else if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select," + path }, UseShellExecute = false });
            }
            else
            {
                throw new AxoException("errors.notFound");
            }
        }

        /// <summary>Externe Links nur im normalen Browser und nur als HTTPS.</summary>
        protected void OpenUrl(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new AxoException("errors.insecureUrl", new() { ["host"] = TextHelper.Clean(url, 60) });
            }
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
    }
}
