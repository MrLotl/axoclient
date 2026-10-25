namespace AxoClient.Core.Common;

/// <summary>
/// Ereignisse aus dem Core (Fortschritt, Log-Zeilen, Freundesstatus …). Die App leitet sie an die UI weiter,
/// der Core kennt weder Photino noch Svelte.
/// </summary>
public sealed class EventHub
{
    public event Action<string, object?>? Published;

    /// <summary>Wie <see cref="Publish"/>, gibt aber zurück, ob die Zustellung geklappt hat.</summary>
    public bool TryPublish(string type, object? data = null)
    {
        try
        {
            Published?.Invoke(type, data);
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Ereignis {type} konnte nicht zugestellt werden", ex);
            return false;
        }
    }

    public void Publish(string type, object? data = null)
    {
        try
        {
            Published?.Invoke(type, data);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Ereignis {type} konnte nicht zugestellt werden", ex);
        }
    }
}
