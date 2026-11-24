using AxoClient.Common;
using Microsoft.Extensions.DependencyInjection;

namespace AxoClient.App
{
    internal class Program
    {
        /// <summary>Einstiegspunkt: der einzige statische Teil, er übergibt sofort an eine Instanz.</summary>
        [STAThread]
        private static int Main(string[] arguments)
        {
            return new Program().Run(arguments);
        }

        public int Run(string[] arguments)
        {
            var paths = new AppPaths();
            var services = new ServiceCollection();
            new Startup(paths).ConfigureServices(services);
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

            var appLog = provider.GetRequiredService<IAppLog>();
            var nativeMessage = provider.GetRequiredService<NativeMessage>();

            // Ganz am Anfang: als Setup installieren, deinstallieren oder nach einem Update aufräumen
            if (provider.GetRequiredService<Installer>().Handle(arguments, out var exitCode))
            {
                return exitCode;
            }

            using var single = provider.GetRequiredService<SingleInstance>();
            if (!single.TryAcquire(arguments))
            {
                return 0; // Ein anderer AxoClient läuft schon und holt sein Fenster nach vorn
            }

            AppHost host;
            MainWindow window;
            try
            {
                paths.EnsureCreated();
                appLog.Initialize(paths.Logs);
                appLog.Prune();
                host = provider.GetRequiredService<AppHost>();
                window = provider.GetRequiredService<MainWindow>();
            }
            catch (Exception exception)
            {
                appLog.Error("Start fehlgeschlagen", exception);
                nativeMessage.Show("AxoClient konnte nicht starten", exception.Message);
                return 1;
            }

            appLog.Info($"AxoClient {provider.GetRequiredService<IAppInfo>().Version} startet");
            try
            {
                window.Run();
            }
            catch (Exception exception)
            {
                appLog.Error("Unerwarteter Fehler im Fenster", exception);
                nativeMessage.Show("AxoClient wurde unerwartet beendet", exception.Message);
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
}
