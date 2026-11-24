namespace AxoClient.Updates.Contract
{
    public interface IInstallLocation
    {
        string Directory { get; }

        string ExePath { get; }

        /// <summary>Hier landet die laufende EXE beim Austausch; Windows erlaubt das Umbenennen einer laufenden Datei.</summary>
        string OldExePath { get; }

        /// <summary>Läuft gerade die installierte EXE (und nicht z. B. ein Entwickler-Build)?</summary>
        bool IsInstalled { get; }

        /// <summary>Als Einzeldatei veröffentlicht (nur Release-Builds); Entwickler-Builds installieren sich nie selbst.</summary>
        bool IsSingleFile { get; }

        /// <summary>Neue EXE an ihren Platz: die alte (auch laufende) wird zu <c>.old</c> umbenannt.</summary>
        void ReplaceExe(string newExePath, bool copy);

        void TryDeleteOld();
    }
}
