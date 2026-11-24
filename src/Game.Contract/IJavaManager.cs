using AxoClient.DataModel.Game;

namespace AxoClient.Game.Contract
{
    public interface IJavaManager
    {
        /// <summary>Welche Java-Hauptversion braucht diese MC-Version? Erst aus dem Versions-JSON, sonst geschätzt.</summary>
        int RequiredMajor(string versionId);

        /// <summary>Alle gefundenen Runtimes: mitgelieferte zuerst, dann installierte.</summary>
        Task<IReadOnlyList<JavaRuntime>> FindAll(CancellationToken ct = default);

        /// <summary>Startet <c>java -version</c> und liest die Hauptversion. <c>null</c> = nicht startbar.</summary>
        Task<int?> ReadMajor(string javawPath, CancellationToken ct = default);

        /// <summary>Passt ein eigener Java-Pfad zur MC-Version? (Start-Pipeline: Warnung)</summary>
        bool IsCompatible(int javaMajor, int required);
    }
}
