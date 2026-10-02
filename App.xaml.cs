using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

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

        var font = (FontFamily)FindResource("UiFont");
        TextElement.FontFamilyProperty.OverrideMetadata(typeof(TextElement), new FrameworkPropertyMetadata(font));
        TextBlock.FontFamilyProperty.OverrideMetadata(typeof(TextBlock), new FrameworkPropertyMetadata(font));

        base.OnStartup(e);
        new MainWindow().Show();
    }
}
