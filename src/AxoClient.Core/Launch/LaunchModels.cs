using AxoClient.Core.Accounts;
using AxoClient.Core.Common;
using AxoClient.Core.Content;
using AxoClient.Core.Instances;
using CmlLib.Core;

namespace AxoClient.Core.Launch;

public enum Severity
{
    Ok,
    AutoFix,
    Warning,
    Blocker
}

public enum LaunchPhase
{
    Environment,
    GameFiles,
    Content,
    Compatibility,
    Resources
}

/// <summary>Eine Maßnahme, die der Nutzer im Dialog wählen kann (oder die als Auto-Fix still läuft).</summary>
public sealed record CheckAction(string Id, TextRef Label, Func<LaunchContext, JobContext, Task> Run, bool Primary = false);

/// <summary>Ergebnis einer Prüfung. <see cref="Key"/> dient zum Stummschalten (<c>version-mismatch:appleskin@ab12</c>).</summary>
public sealed record CheckResult(
    string CheckId,
    Severity Severity,
    TextRef Message,
    IReadOnlyList<CheckAction> Actions,
    string? Key = null,
    bool CanSilence = true,
    TextRef? Detail = null)
{
    public static CheckResult Ok(string checkId) => new(checkId, Severity.Ok, new TextRef("pipeline.ok"), []);

    public CheckAction? AutoFix => Severity == Severity.AutoFix ? Actions.FirstOrDefault() : null;
}

/// <summary>Was die UI im Dialog zeigt.</summary>
public sealed record IssueView(string Id, Severity Severity, TextRef Message, TextRef? Detail, IReadOnlyList<ActionView> Actions, bool CanSilence);

public sealed record ActionView(string Id, TextRef Label, bool Primary);

public enum PromptChoice
{
    Cancel,
    StartAnyway,
    Fix
}

/// <summary>Antwort aus dem Dialog: welche Maßnahmen ausgeführt und welche Warnungen stummgeschaltet werden.</summary>
public sealed record PromptDecision(PromptChoice Choice, IReadOnlyDictionary<string, string>? Actions = null, IReadOnlyCollection<string>? Silence = null);

/// <summary>Brücke zur UI: zeigt Warnungen und Blocker einer Phase gesammelt in einem Dialog.</summary>
public interface ILaunchPrompt
{
    Task<PromptDecision> AskAsync(string launchId, string instanceId, LaunchPhase phase, IReadOnlyList<IssueView> issues,
        CancellationToken ct);
}

/// <summary>Schnellstart in eine Welt oder auf einen Server.</summary>
public sealed record QuickPlay(string? World = null, string? Server = null);

/// <summary>Alles, was die Prüfungen über einen Start wissen müssen.</summary>
public sealed class LaunchContext
{
    public required string LaunchId { get; init; }
    public required Instance Instance { get; set; }
    public required ContentDocument Content { get; set; }
    public required LauncherSettings Settings { get; init; }
    public required MinecraftPath GamePath { get; init; }
    public required string GameDir { get; init; }
    public QuickPlay? QuickPlay { get; init; }

    /// <summary>Nach der Kontoprüfung gesetzt.</summary>
    public GameSession? Session { get; set; }

    /// <summary>Kein Internet, alle Dateien vorhanden: offline starten.</summary>
    public bool Offline { get; set; }

    /// <summary>Versions-ID für CmlLib nach der Loader-Installation (z. B. "fabric-loader-0.16.10-1.21.4").</summary>
    public string? VersionName { get; set; }

    /// <summary>Muss die Version (oder der Loader) noch installiert werden?</summary>
    public bool NeedsInstall { get; set; }

    /// <summary>Eigener Java-Pfad (aus Einstellungen), <c>null</c> = automatisch.</summary>
    public string? JavaOverride { get; set; }

    public int MemoryMb => Instance.MemoryMb ?? Settings.Defaults.MemoryMb;

    /// <summary>Wurde die Instanz seit der letzten Kompatibilitätsprüfung verändert?</summary>
    public bool ContentChanged => Content.CheckedAt == null || Content.ChangedAt == null || Content.ChangedAt > Content.CheckedAt;

    /// <summary>Hinweise ohne Dialog (z. B. "AxoClient-Funktionen nicht verfügbar").</summary>
    public List<TextRef> Hints { get; } = [];

    /// <summary>Weitergabe von Zwischenergebnissen zwischen Prüfungen.</summary>
    public Dictionary<string, object> Bag { get; } = new();
}

/// <summary>Eine Prüfung vor dem Start. Testbar: liefert nur Ergebnisse, Reparaturen sind Maßnahmen.</summary>
public interface ILaunchCheck
{
    string Id { get; }
    LaunchPhase Phase { get; }

    Task<IReadOnlyList<CheckResult>> RunAsync(LaunchContext ctx, CancellationToken ct);
}
