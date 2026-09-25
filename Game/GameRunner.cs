using System.Diagnostics;

namespace AxoClient.Game;

public sealed class GameRunner(AppServices app)
{
    private readonly Dictionary<string, Process> _running = new();
    private readonly HashSet<string> _stopRequested = [];

    public event Action? Changed;
    public event Action<Installation>? Crashed;
    public event Action<Installation, Process>? Started;

    public bool IsRunning(Installation inst)
    {
        lock (_running)
            return _running.TryGetValue(inst.Id, out var process) && !process.HasExited;
    }

    public bool AnyRunning
    {
        get
        {
            lock (_running)
                return _running.Values.Any(p => !p.HasExited);
        }
    }

    public List<Installation> RunningInstances => app.Instances.All.Where(IsRunning).ToList();

    public async Task LaunchAsync(Installation inst, QuickPlay? quickPlay, LaunchProgress progress)
    {
        var session = await app.Accounts.GetFreshSessionAsync();
        GameStatusWatcher.Reset(inst);
        var process = await app.Installer.PrepareAsync(inst, session, app.Settings, progress, quickPlay);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => app.Discord.GameExited();
        process.Start();
        app.Discord.GameStarted(app.Settings, inst, quickPlay);
        if (app.Settings.MaximizeOnLaunch && !app.Settings.FullScreen)
            NativeWindows.MaximizeWhenReady(process);
        GameStatusWatcher.Watch(app, inst, quickPlay, process);
        Track(inst, process);
        _ = app.Axo.RegisterQuietlyAsync();
        Started?.Invoke(inst, process);
    }

    public void Stop(Installation inst)
    {
        Process? process;
        lock (_running)
            _running.TryGetValue(inst.Id, out process);
        if (process == null || process.HasExited)
            return;
        _stopRequested.Add(inst.Id);
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            _stopRequested.Remove(inst.Id);
            throw;
        }
    }

    private void Track(Installation inst, Process process)
    {
        var startedUtc = DateTime.UtcNow;
        var startedLocal = DateTime.Now;
        inst.LaunchCount++;
        inst.LastPlayedUtc = startedUtc;
        app.SaveSettings();
        lock (_running)
            _running[inst.Id] = process;
        var ui = SynchronizationContext.Current ?? new SynchronizationContext();
        process.Exited += (_, _) => ui.Post(_ => OnExited(inst, process, startedUtc, startedLocal), null);
        Changed?.Invoke();
    }

    private void OnExited(Installation inst, Process process, DateTime startedUtc, DateTime startedLocal)
    {
        var played = (long)Math.Max(0, (DateTime.UtcNow - startedUtc).TotalSeconds);
        inst.PlayTimeSeconds += played;
        PlayHistory.Add(inst, startedLocal, played);

        var crashed = false;
        try
        {
            crashed = process.ExitCode != 0 && !_stopRequested.Remove(inst.Id);
        }
        catch (Exception ex)
        {
            ErrorReport.Log($"Exit-Code von \"{inst.Name}\" lesen", ex);
        }
        if (crashed)
        {
            inst.CrashCount++;
            inst.LastCrashUtc = DateTime.UtcNow;
        }
        app.SaveSettings();

        lock (_running)
            if (_running.TryGetValue(inst.Id, out var tracked) && tracked == process)
                _running.Remove(inst.Id);
        Changed?.Invoke();
        if (crashed)
            Crashed?.Invoke(inst);
    }
}
