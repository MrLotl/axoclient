namespace AxoClient.Common
{
    public interface ISystemInfo
    {
        /// <summary>(Gesamt, frei) in MB. Fällt auf GC-Werte zurück, falls die Abfrage scheitert.</summary>
        (long TotalMb, long FreeMb) Memory();

        long FreeDiskBytes(string path);

        /// <summary>
        /// Empfohlener RAM für eine Instanz: Vanilla 2 GB, mit Mods mehr, nie mehr als die Hälfte des PCs.
        /// </summary>
        int RecommendedMemoryMb(int modCount, long totalMb);

        /// <summary>Minimum, unter dem es mit dieser Mod-Anzahl ruckelt oder abstürzt.</summary>
        int MinimumMemoryMb(int modCount);

        string WindowsVersion { get; }
    }
}
