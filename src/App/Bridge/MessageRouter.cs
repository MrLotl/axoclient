using System.Text.Json;
using AxoClient.Common;

namespace AxoClient.App.Bridge
{
    /// <summary>Anfrage aus der UI: <c>{ "id": 12, "type": "instance.launch", "data": { … } }</c>.</summary>
    public sealed record BridgeRequest(long Id, string Type, JsonElement Data);

    /// <summary>
    /// Nimmt Nachrichten der Svelte-UI an und leitet sie an den passenden Manager weiter. Feste Liste von Nachrichtentypen,
    /// alle Parameter werden geprüft, Pfade kommen nie aus der UI (nur IDs). Antworten und Ereignisse gehen per
    /// <see cref="Send"/> zurück.
    /// </summary>
    public sealed class MessageRouter
    {
        private readonly Action<string> _send;
        private readonly IJsonHelper _jsonHelper;
        private readonly IAppLog _appLog;
        private readonly IExceptionHelper _exceptionHelper;
        private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object?>>> _handlers = new();

        public MessageRouter(Action<string> send, IJsonHelper jsonHelper, IAppLog appLog, IExceptionHelper exceptionHelper)
        {
            _send = send;
            _jsonHelper = jsonHelper;
            _appLog = appLog;
            _exceptionHelper = exceptionHelper;
        }

        public IReadOnlyCollection<string> Types => _handlers.Keys;

        /// <summary>Handler mit typisierten Daten.</summary>
        public void On<TData>(string type, Func<TData, CancellationToken, Task<object?>> handler)
        {
            _handlers.Add(type, async (json, cancellationToken) =>
            {
                TData data;
                try
                {
                    data = json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                        ? JsonSerializer.Deserialize<TData>("{}", _jsonHelper.Options)!
                        : json.Deserialize<TData>(_jsonHelper.Options)!;
                }
                catch (JsonException)
                {
                    throw new AxoException("errors.invalidInput");
                }
                if (data == null)
                {
                    throw new AxoException("errors.invalidInput");
                }

                return await handler(data, cancellationToken);
            });
        }

        /// <summary>Handler ohne Daten.</summary>
        public void On(string type, Func<CancellationToken, Task<object?>> handler) =>
            _handlers.Add(type, (_, cancellationToken) => handler(cancellationToken));

        /// <summary>Synchroner Handler ohne Daten.</summary>
        public void On(string type, Func<object?> handler) =>
            _handlers.Add(type, (_, _) => Task.FromResult(handler()));

        public void Handle(string message)
        {
            BridgeRequest? request;
            try
            {
                if (message.Length > 64 * 1024 * 1024)
                {
                    return;
                }

                request = JsonSerializer.Deserialize<BridgeRequest>(message, _jsonHelper.Options);
            }
            catch (JsonException)
            {
                return;
            }
            if (request == null)
            {
                return;
            }

            _ = execute(request);
        }

        private async Task execute(BridgeRequest request)
        {
            if (!_handlers.TryGetValue(request.Type, out var handler))
            {
                reply(request.Id, false, null, new TextRef("errors.unknownMessage"));
                return;
            }
            try
            {
                var result = await Task.Run(() => handler(request.Data, CancellationToken.None));
                reply(request.Id, true, result, null);
            }
            catch (OperationCanceledException)
            {
                reply(request.Id, false, null, new TextRef("errors.cancelled"));
            }
            catch (Exception exception)
            {
                if (exception is not AxoException)
                {
                    _appLog.Error($"Bridge {request.Type} fehlgeschlagen", exception);
                }

                reply(request.Id, false, null, _exceptionHelper.Describe(exception));
            }
        }

        private void reply(long id, bool success, object? data, TextRef? error) =>
            _send(JsonSerializer.Serialize(new { kind = "reply", id, ok = success, data, error }, _jsonHelper.Compact));

        /// <summary>Ereignis an die UI.</summary>
        public void Emit(string type, object? data) =>
            _send(JsonSerializer.Serialize(new { kind = "event", type, data }, _jsonHelper.Compact));
    }
}
