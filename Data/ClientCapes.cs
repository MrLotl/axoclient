using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace McLauncher;

/// <summary>Ein AxoClient-Umhang aus dem GitHub-Repository (Ordner capes/).</summary>
public record ClientCape(string Id, string Name, byte[]? Png);

/// <summary>
/// Liste der AxoClient-Umhänge: capes/capes.json im Repository, die Bilder daneben als &lt;id&gt;.png.
/// Neue Umhänge brauchen daher kein neues Release – ein Push reicht.
/// </summary>
public static partial class ClientCapes
{
    [GeneratedRegex("^[a-z0-9_-]{1,32}$")]
    private static partial Regex ValidId();

    public static async Task<List<ClientCape>> LoadAsync(HttpClient http, string baseUrl)
    {
        using var json = JsonDocument.Parse(await http.GetStringAsync(baseUrl + "capes.json"));
        var entries = json.RootElement.EnumerateArray()
            .Select(e => (Id: e.GetProperty("id").GetString() ?? "", Name: e.TryGetProperty("name", out var n) ? n.GetString() : null))
            .Where(e => ValidId().IsMatch(e.Id))
            .ToList();

        var capes = await Task.WhenAll(entries.Select(async e =>
        {
            byte[]? png;
            try
            {
                png = await http.GetByteArrayAsync(baseUrl + e.Id + ".png");
            }
            catch
            {
                png = null; // Liste trotzdem zeigen, nur ohne Vorschau
            }
            return new ClientCape(e.Id, e.Name ?? e.Id, png);
        }));
        return capes.ToList();
    }
}
