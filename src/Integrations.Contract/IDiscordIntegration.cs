namespace AxoClient.Integrations.Contract
{
    public interface IDiscordIntegration : IDisposable
    {
        /// <summary>Jemand hat in Discord auf "Beitreten" geklickt: Adresse und MC-Version.</summary>
        event Action<string, string?>? JoinRequested;

        /// <summary>Nach dem Laden der Einstellungen: Status im Launcher setzen, falls gewünscht.</summary>
        void Start();
    }
}
