namespace McLauncher;

/// <summary>
/// Fortschritt und Abbruch einer längeren Arbeit (Import, Modpack-Installation), die in einem Dialog angezeigt wird.
/// Die Meldungen kommen im UI-Thread an, auch wenn sie aus einem Hintergrund-Thread gesendet werden.
/// </summary>
public sealed class WorkProgress(IProgress<string> text, IProgress<double> fraction, CancellationToken cancel)
{
    /// <summary>Was gerade passiert, z.B. "Lade Sodium...".</summary>
    public IProgress<string> Text { get; } = text;

    /// <summary>Anteil der Arbeit (0 bis 1); ohne Meldung bleibt der Balken unbestimmt.</summary>
    public IProgress<double> Fraction { get; } = fraction;

    /// <summary>Wird ausgelöst, wenn der Nutzer "Abbrechen" klickt.</summary>
    public CancellationToken Cancel { get; } = cancel;

    /// <summary>Ohne Anzeige und ohne Abbruch, für Aufrufe außerhalb eines Dialogs.</summary>
    public static WorkProgress Silent => new(new Progress<string>(), new Progress<double>(), CancellationToken.None);
}
