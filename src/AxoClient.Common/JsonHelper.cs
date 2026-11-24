using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AxoClient.Common
{
    /// <summary>Gemeinsame JSON-Einstellungen: camelCase, Auswahlwerte als Strings, lesbar eingerückt.</summary>
    public class JsonHelper : IJsonHelper
    {
        public JsonHelper()
        {
            Options = create(indented: true);
            Compact = create(indented: false);
        }

        /// <summary>Lesbar eingerückt, für Dateien.</summary>
        public JsonSerializerOptions Options { get; }

        /// <summary>Ohne Einrückung, für Nachrichten.</summary>
        public JsonSerializerOptions Compact { get; }

        private JsonSerializerOptions create(bool indented)
        {
            var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = indented,
                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                NumberHandling = JsonNumberHandling.Strict
            };
            options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
            return options;
        }

        public string Serialize<T>(T value, bool indented = true) =>
            JsonSerializer.Serialize(value, indented ? Options : Compact);

        public T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
}
