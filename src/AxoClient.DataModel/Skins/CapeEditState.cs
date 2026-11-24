using AxoClient.Common;

namespace AxoClient.DataModel.Skins
{
    /// <summary><c>capes/&lt;id&gt;.edit.json</c>: Zustand des Editors, damit man später weiterarbeiten kann.</summary>
    public sealed class CapeEditState : IVersionedDocument
    {
        public int FormatVersion { get; set; } = 1;
        public int Width { get; set; } = 10;
        public int Height { get; set; } = 16;
        public CapeImport? Import { get; set; }

        /// <summary>Pixel der Vorderseite als Hex-Farben (Zeilen von oben), "" = transparent.</summary>
        public List<string>? Pixels { get; set; }
    }
}
