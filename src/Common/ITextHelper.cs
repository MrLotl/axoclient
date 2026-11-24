namespace AxoClient.Common
{
    public interface ITextHelper
    {
        /// <summary>Kürzt fremde Texte, entfernt Steuerzeichen. Für alles, was aus dem Netz oder von Freunden kommt.</summary>
        string Clean(string? text, int maxLength);

        /// <summary>Mehrzeiliger fremder Text (Beschreibungen, Changelogs): Zeilenumbrüche bleiben.</summary>
        string CleanMultiline(string? text, int maxLength);

        /// <summary>"Survival mit Freunden" → "survival-mit-freunden-7f3a".</summary>
        string NewId(string name);

        string Slug(string name);

        /// <summary>Gültige IDs, wie sie von der UI kommen dürfen.</summary>
        bool IsValidId(string? id);

        string RequireId(string? id);

        string Sha1Hex(Stream stream);

        string Sha512Hex(Stream stream);

        string Sha256Hex(byte[] data);
    }
}
