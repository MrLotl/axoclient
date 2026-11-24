namespace AxoClient.Common
{
    public interface IPathGuard
    {
        /// <summary>Gibt den vollen Pfad zurück oder <c>null</c>, wenn <paramref name="relative"/> nicht sicher ist.</summary>
        string? SafeCombine(string baseDir, string? relative);

        /// <summary>Wie <see cref="SafeCombine"/>, wirft aber einen übersetzbaren Fehler.</summary>
        string Combine(string baseDir, string? relative);

        string? ResolveInside(string parent, string relative);

        bool IsInside(string parent, string path);

        /// <summary>Ein einzelner, harmloser Dateiname (keine Ordner).</summary>
        bool IsSafeFileName(string? name);
    }
}
