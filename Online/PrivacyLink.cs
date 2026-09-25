using System.IO.Pipes;
using System.Text.Json;

namespace AxoClient.Online;

public record PrivacyDraw
{
    public string T { get; init; } = "r";

    public double X { get; init; }
    public double Y { get; init; }

    public double W { get; init; }

    public double H { get; init; }

    public long C { get; init; }

    public double R { get; init; }

    public double S { get; init; } = 1;

    public bool Sh { get; init; }

    public string V { get; init; } = "";
}

public record PrivacyFrame
{
    public int Pid { get; init; }

    public double Scale { get; init; } = 1;

    public int GuiWidth { get; init; }

    public int GuiHeight { get; init; }

    public List<PrivacyDraw> Cmds { get; init; } = [];

    public DateTime ReceivedAt { get; init; } = DateTime.UtcNow;
}

public sealed class PrivacyLink : IDisposable
{
    public const string PipeName = "axoclient-privathud";

    private const int MaxGames = 4;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly CancellationTokenSource _stop = new();

    private readonly Dictionary<int, PrivacyFrame> _frames = [];

    private readonly object _lock = new();

    public event Action<PrivacyFrame>? FrameReceived;

    public event Action<int>? GameGone;

    public PrivacyLink()
    {
        for (var i = 0; i < MaxGames; i++)
            _ = Task.Run(ListenAsync);
    }

    public PrivacyFrame? FrameOf(int pid)
    {
        lock (_lock)
            return _frames.GetValueOrDefault(pid);
    }

    private async Task ListenAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var pid = 0;
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, MaxGames,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_stop.Token);

                using var reader = new StreamReader(pipe);
                while (await reader.ReadLineAsync(_stop.Token) is { } line)
                {
                    if (line.Length == 0)
                        continue;
                    var frame = JsonSerializer.Deserialize<PrivacyFrame>(line, JsonOptions);
                    if (frame == null)
                        continue;
                    pid = frame.Pid;
                    lock (_lock)
                        _frames[frame.Pid] = frame;
                    FrameReceived?.Invoke(frame);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                ErrorReport.Log("Privates Overlay: Verbindung zum Spiel", ex);
                await Task.Delay(500, CancellationToken.None);
            }
            finally
            {
                if (pid != 0)
                {
                    lock (_lock)
                        _frames.Remove(pid);
                    GameGone?.Invoke(pid);
                }
            }
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
    }
}
