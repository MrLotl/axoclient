namespace AxoClient.Common
{
    public interface IEventHub
    {
        event Action<string, object?>? Published;

        /// <summary>Wie <see cref="Publish"/>, gibt aber zurück, ob die Zustellung geklappt hat.</summary>
        bool TryPublish(string type, object? data = null);

        void Publish(string type, object? data = null);
    }
}
