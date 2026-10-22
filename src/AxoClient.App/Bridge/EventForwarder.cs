using System.Collections.Concurrent;
using AxoClient.Core.Common;

namespace AxoClient.App.Bridge;

/// <summary>
/// Leitet Core-Ereignisse an die UI weiter. Log-Zeilen werden gebündelt (alle 100 ms), Fortschritt und
/// Listenänderungen gehen sofort raus.
/// </summary>
public sealed class EventForwarder : IDisposable
{
    private readonly EventHub _events;
    private readonly MessageRouter _router;
    private readonly ConcurrentQueue<object> _lines = new();
    private readonly Timer _timer;

    public EventForwarder(EventHub events, MessageRouter router)
    {
        _events = events;
        _router = router;
        _events.Published += OnEvent;
        _timer = new Timer(_ => Flush(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    private void OnEvent(string type, object? data)
    {
        if (type == "console.line")
        {
            if (data != null && _lines.Count < 5000)
                _lines.Enqueue(data);
            return;
        }
        // "notify" wird im AppHost in die Glocke übersetzt
        if (type == "notify")
            return;
        _router.Emit(type, data);
    }

    private void Flush()
    {
        if (_lines.IsEmpty)
            return;
        var batch = new List<object>();
        while (batch.Count < 2000 && _lines.TryDequeue(out var line))
            batch.Add(line);
        _router.Emit("console.lines", batch);
    }

    public void Dispose()
    {
        _events.Published -= OnEvent;
        _timer.Dispose();
    }
}
