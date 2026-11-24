namespace AxoClient.Social.Contract
{
    public interface IOnlineClient
    {
        string BaseUrl { get; set; }

        /// <summary>War der Service bei der letzten Anfrage erreichbar? Sonst "nicht verfügbar", kein Dialog.</summary>
        bool Available { get; }

        /// <summary>Hat der Server ein Launcher-Update erzwungen (<c>update_required</c>)?</summary>
        bool UpdateRequired { get; }

        bool CanUse { get; }

        Task<T?> Send<T>(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default);

        Task Send(HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default);

        Task<HttpResponseMessage> SendRaw(HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken,
        bool allowAnonymous = false);

        /// <summary>Anmeldung beim Service mit einem Mojang-Spielerzertifikat. Gibt das neue Geräte-Token zurück.</summary>
        Task<string> Register(CancellationToken cancellationToken);

        /// <summary>Geräte-Token des aktiven Kontos (meldet bei Bedarf neu an), z. B. für den Echtzeit-Kanal.</summary>
        Task<string> Token(CancellationToken cancellationToken);

        /// <summary>Token verwerfen (vom Service abgelehnt); die nächste Anfrage meldet still neu an.</summary>
        void ForgetToken();

        /// <summary>Dieses Gerät abmelden (Token beim Service widerrufen und lokal löschen).</summary>
        Task SignOutDevice(string uuid, CancellationToken cancellationToken);
    }
}
