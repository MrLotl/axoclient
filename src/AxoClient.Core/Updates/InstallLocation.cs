namespace AxoClient.Core.Updates;

/// <summary>
/// Wo der Launcher installiert ist: eine einzige <c>AxoClient.exe</c> in <c>%LocalAppData%\Programs\AxoClient</c>
/// (keine Admin-Rechte nötig). Die Daten liegen getrennt davon in <c>%AppData%\AxoClient</c>.
/// </summary>
public static class InstallLocation
{
    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "AxoClient");

    public static string ExePath { get; } = Path.Combine(Directory, "AxoClient.exe");

    /// <summary>Hier landet die laufende EXE beim Austausch; Windows erlaubt das Umbenennen einer laufenden Datei.</summary>
    public static string OldExePath { get; } = ExePath + ".old";

    /// <summary>Läuft gerade die installierte EXE (und nicht z. B. ein Entwickler-Build)?</summary>
    public static bool IsInstalled =>
        Environment.ProcessPath is { } exe && string.Equals(Path.GetFullPath(exe), ExePath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Als Einzeldatei veröffentlicht (nur Release-Builds); Entwickler-Builds installieren sich nie selbst.</summary>
    public static bool IsSingleFile => string.IsNullOrEmpty(typeof(InstallLocation).Assembly.Location);

    /// <summary>Neue EXE an ihren Platz: die alte (auch laufende) wird zu <c>.old</c> umbenannt.</summary>
    public static void ReplaceExe(string newExe, bool copy)
    {
        System.IO.Directory.CreateDirectory(Directory);
        TryDeleteOld();
        if (File.Exists(ExePath))
            File.Move(ExePath, OldExePath, overwrite: true);
        try
        {
            if (copy)
                File.Copy(newExe, ExePath, overwrite: true);
            else
                File.Move(newExe, ExePath, overwrite: true);
        }
        catch
        {
            // Zurück zum alten Stand, damit nie gar keine EXE da ist
            if (!File.Exists(ExePath) && File.Exists(OldExePath))
                File.Move(OldExePath, ExePath);
            throw;
        }
    }

    public static void TryDeleteOld()
    {
        try
        {
            if (File.Exists(OldExePath))
                File.Delete(OldExePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Läuft noch (gerade ausgetauscht); beim nächsten Start
        }
    }
}
