using AxoClient.Core.Common;

namespace AxoClient.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Ganz am Anfang: als Setup installieren, deinstallieren oder nach einem Update aufräumen
        if (Installer.Handle(args, out var exitCode))
            return exitCode;

        using var single = SingleInstance.TryAcquire(args);
        if (single == null)
            return 0; // Ein anderer AxoClient läuft schon und holt sein Fenster nach vorn

        var paths = AppPaths.Default();
        AppHost host;
        try
        {
            host = new AppHost(paths);
        }
        catch (Exception ex)
        {
            AppLog.Error("Start fehlgeschlagen", ex);
            NativeMessage.Show("AxoClient konnte nicht starten", ex.Message);
            return 1;
        }

        AppLog.Info($"AxoClient {AppInfo.Version} startet");
        var window = new MainWindow(host, single);
        try
        {
            window.Run();
        }
        catch (Exception ex)
        {
            AppLog.Error("Unerwarteter Fehler im Fenster", ex);
            NativeMessage.Show("AxoClient wurde unerwartet beendet", ex.Message);
            return 1;
        }
        finally
        {
            // Beim Beenden kurz aufräumen (Discord, Bridge, Update vormerken). Main hat keinen
            // Synchronisationskontext mehr, Warten ist hier ausnahmsweise unbedenklich.
#pragma warning disable VSTHRD002
            host.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
#pragma warning restore VSTHRD002
        }
        return 0;
    }
}
