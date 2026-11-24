using System.Text.Json;

namespace AxoClient.Common
{
    public interface IJsonHelper
    {
        /// <summary>Lesbar eingerückt, für Dateien.</summary>
        JsonSerializerOptions Options { get; }

        /// <summary>Ohne Einrückung, für Nachrichten.</summary>
        JsonSerializerOptions Compact { get; }

        string Serialize<T>(T value, bool indented = true);

        T? Deserialize<T>(string json);
    }
}
