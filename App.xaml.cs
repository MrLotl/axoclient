using System.Windows;

namespace AxoClient;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            ErrorDialog.Report("Unerwarteter Fehler", args.Exception);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            args.SetObserved();
            if (args.Exception.InnerExceptions.All(inner => inner is OperationCanceledException))
                return;
            Current?.Dispatcher.InvokeAsync(() => ErrorDialog.Report("Unerwarteter Fehler im Hintergrund", args.Exception));
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            ErrorReport.Log("Abbruch", ex);
            MessageBox.Show(
                ErrorReport.Describe(ex) + "\n\nAxoClient muss beendet werden.\nProtokoll: " + ErrorReport.LogPath,
                AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);
    }
}
