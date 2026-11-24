using AxoClient.Common;

namespace AxoClient.DataModel.Settings
{
    /// <summary><c>launcher.json</c>: alle Einstellungen des Launchers.</summary>
    public sealed class LauncherSettings : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public bool Autostart { get; set; }
        public AfterLaunch AfterLaunch { get; set; } = AfterLaunch.Minimize;
        public GameSettings Game { get; set; } = new();
        public DefaultSettings Defaults { get; set; } = new();
        public IntegrationSettings Integrations { get; set; } = new();
        public SocialSettings Social { get; set; } = new();
        public AppearanceSettings Appearance { get; set; } = new();
        public NotificationSettings Notifications { get; set; } = new();
        public UpdateSettings Updates { get; set; } = new();

        /// <summary>Wurde der Erst-Start-Assistent abgeschlossen?</summary>
        public bool SetupDone { get; set; }
    }
}
