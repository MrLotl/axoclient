using System.Text.Json;
using AxoClient.Core.Common;

namespace AxoClient.App.Bridge;

/// <summary>Anfrage aus der UI: <c>{ "id": 12, "type": "instance.launch", "data": { … } }</c>.</summary>
public sealed record BridgeRequest(long Id, string Type, JsonElement Data);

/// <summary>
/// Nimmt Nachrichten der Svelte-UI an und leitet sie an den passenden Manager weiter. Feste Liste von Nachrichtentypen,
/// alle Parameter werden geprüft, Pfade kommen nie aus der UI (nur IDs). Antworten und Ereignisse gehen per
/// <see cref="Send"/> zurück.
/// </summary>
public sealed class MessageRouter
{
    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object?>>> _handlers = new();
    private readonly Action<string> _send;

    public MessageRouter(Action<string> send)
    {
        _send = send;
    }

    public IReadOnlyCollection<string> Types => _handlers.Keys;

    /// <summary>Handler mit typisierten Daten.</summary>
    public void On<TData>(string type, Func<TData, CancellationToken, Task<object?>> handler)
    {
        _handlers.Add(type, async (json, ct) =>
        {
            TData data;
            try
            {
                data = json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? JsonSerializer.Deserialize<TData>("{}", Json.Options)!
                    : json.Deserialize<TData>(Json.Options)!;
            }
            catch (JsonException)
            {
                throw new AxoException("errors.invalidInput");
            }
            if (data == null)
                throw new AxoException("errors.invalidInput");
            return await handler(data, ct);
        });
    }

    /// <summary>Handler ohne Daten.</summary>
    public void On(string type, Func<CancellationToken, Task<object?>> handler) =>
        _handlers.Add(type, (_, ct) => handler(ct));

    /// <summary>Synchroner Handler ohne Daten.</summary>
    public void On(string type, Func<object?> handler) =>
        _handlers.Add(type, (_, _) => Task.FromResult(handler()));

    public void Handle(string message)
    {
        BridgeRequest? request;
        try
        {
            if (message.Length > 64 * 1024 * 1024)
                return;
            request = JsonSerializer.Deserialize<BridgeRequest>(message, Json.Options);
        }
        catch (JsonException)
        {
            return;
        }
        if (request == null)
            return;
        _ = ExecuteAsync(request);
    }

    private async Task ExecuteAsync(BridgeRequest request)
    {
        if (!_handlers.TryGetValue(request.Type, out var handler))
        {
            Reply(request.Id, false, null, new TextRef("errors.unknownMessage"));
            return;
        }
        try
        {
            var result = await Task.Run(() => handler(request.Data, CancellationToken.None));
            Reply(request.Id, true, result, null);
        }
        catch (OperationCanceledException)
        {
            Reply(request.Id, false, null, new TextRef("errors.cancelled"));
        }
        catch (Exception ex)
        {
            if (ex is not AxoException)
                AppLog.Error($"Bridge {request.Type} fehlgeschlagen", ex);
            Reply(request.Id, false, null, AxoException.Describe(ex));
        }
    }

    private void Reply(long id, bool ok, object? data, TextRef? error) =>
        _send(JsonSerializer.Serialize(new { kind = "reply", id, ok, data, error }, Json.Compact));

    /// <summary>Ereignis an die UI.</summary>
    public void Emit(string type, object? data) =>
        _send(JsonSerializer.Serialize(new { kind = "event", type, data }, Json.Compact));
}
