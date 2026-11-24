using System.IO.Compression;
using System.Text.Json;
using AxoClient.Common;

namespace AxoClient.App.Overlay
{
    /// <summary>Lädt die Minecraft-Schrift aus der Jar einer Instanz.</summary>
    public class McFontLoader
    {
        private readonly IAppLog _appLog;

        public McFontLoader(IAppLog appLog)
        {
            _appLog = appLog;
        }

        /// <summary>Lädt die Schrift aus der Jar oder <c>null</c>, wenn sie dort nicht lesbar ist.</summary>
        public McFont? Load(string jarPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(jarPath);
                var font = new McFont(zip);
                return font.IsEmpty ? null : font;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                _appLog.Warn("Minecraft-Schrift für das private Overlay nicht lesbar: " + jarPath, ex);
                return null;
            }
        }
    }
}
