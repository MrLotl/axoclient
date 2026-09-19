using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace McLauncher;

/// <summary>Ein Eintrag der Freundesliste (nur AxoClient-Nutzer).</summary>
public class FriendInfo
{
    public required string Uuid { get; init; }
    public required string Name { get; init; }

    /// <summary>"friend" (gegenseitig), "outgoing" (du hast angefragt) oder "incoming" (Anfrage an dich).</summary>
    public required string State { get; init; }

    public bool Playing { get; init; }
    public string? Server { get; init; }
    public string? Version { get; init; }

    public string HeadUrl => $"https://mc-heads.net/avatar/{Uuid}/64";
    public bool IsIncoming => State == "incoming";
    public bool CanJoin => Playing && Server != null;

    public string StatusText => State switch
    {
        "incoming" => "Möchte dich als Freund hinzufügen",
        "outgoing" => "Anfrage gesendet",
        _ when Server != null => $"Spielt auf {Server}" + (Version != null ? $" · {Version}" : ""),
        _ when Playing => "Im Spiel (Menü oder Einzelspieler)",
        _ => "Nicht im Spiel"
    };

    public string RemoveText => State switch
    {
        "incoming" => "Ablehnen",
        "outgoing" => "Anfrage zurückziehen",
        _ => "Freund entfernen"
    };

    /// <summary>Sortierung: wer auf einem Server spielt zuerst, Anfragen an dich zuletzt oben ... usw.</summary>
    public int SortKey => CanJoin ? 0 : Playing ? 1 : State == "incoming" ? 2 : State == "friend" ? 3 : 4;
}

/// <summary>
/// Freundesliste und eigener Spielstatus beim AxoClient-Dienst (derselbe Cloudflare-Dienst wie das Tabliste-Symbol).
/// Angemeldet wird über ein Token, das der Dienst nach dem Nachweis per Mojang-Spielerzertifikat ausstellt.
/// </summary>
public class FriendsService(AppState app)
{
    public bool Available => Badge.IsConfigured(app.Settings) && app.Session != null;

    private string Api => app.Settings.BadgeApiUrl!.Trim().TrimEnd('/');

    /// <summary>Meldet den Spieler beim Dienst an (aktualisiert "zuletzt gesehen") und merkt sich das Token.</summary>
    public async Task RegisterAsync()
    {
        var session = await app.GetFreshSessionAsync();
        var token = await Badge.RegisterAsync(app.Http, session, Api);
        app.Settings.BadgeToken = token;
        app.Settings.BadgeTokenUuid = session.UUID;
        app.Save();
    }

    public async Task<List<FriendInfo>> GetFriendsAsync()
    {
        using var json = await PostAsync("/friends", new());
        return json.RootElement.GetProperty("friends").EnumerateArray().Select(f => new FriendInfo
            {
                Uuid = f.GetProperty("uuid").GetString()!,
                Name = f.GetProperty("name").GetString()!,
                State = f.GetProperty("state").GetString()!,
                Playing = f.GetProperty("playing").GetBoolean(),
                Server = f.GetProperty("server").GetString(),
                Version = f.GetProperty("version").GetString()
            })
            .OrderBy(f => f.SortKey).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Fügt einen Freund hinzu bzw. nimmt seine Anfrage an. Liefert den richtig geschriebenen Namen.</summary>
    public async Task<string> AddAsync(string name)
    {
        using var json = await PostAsync("/friends/add", new() { ["name"] = name });
        return json.RootElement.GetProperty("name").GetString()!;
    }

    public async Task RemoveAsync(string uuid)
    {
        using var _ = await PostAsync("/friends/remove", new() { ["uuid"] = uuid });
    }

    /// <summary>Eigener Status für die Freunde; <paramref name="playing"/> = false meldet ab.</summary>
    public async Task SetStatusAsync(bool playing, string? server, string? version)
    {
        using var _ = await PostAsync("/status", new()
        {
            ["playing"] = playing,
            ["server"] = server,
            ["version"] = version
        });
    }

    private async Task<JsonDocument> PostAsync(string path, Dictionary<string, object?> body, bool retried = false)
    {
        if (!Available)
            throw new InvalidOperationException("Nicht angemeldet oder AxoClient-Dienst nicht eingerichtet.");
        // Token fehlt oder gehört zu einem anderen Konto: neu anmelden
        if (app.Settings.BadgeToken == null || app.Settings.BadgeTokenUuid != app.Session!.UUID)
            await RegisterAsync();
        if (app.Settings.BadgeToken == null)
            throw new InvalidOperationException(
                "Der AxoClient-Dienst ist veraltet: bitte den aktuellen Code aus service/worker.js deployen.");

        body["token"] = app.Settings.BadgeToken;
        using var response = await app.Http.PostAsJsonAsync(Api + path, body);
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode == HttpStatusCode.Unauthorized && !retried)
        {
            app.Settings.BadgeToken = null; // abgelaufen: neu anmelden und einmal wiederholen
            return await PostAsync(path, body, retried: true);
        }
        if (!response.IsSuccessStatusCode)
        {
            string? error = null;
            try
            {
                using var doc = JsonDocument.Parse(text);
                error = doc.RootElement.GetProperty("error").GetString();
            }
            catch
            {
                // keine Fehlermeldung im JSON
            }
            throw new InvalidOperationException(error ?? $"Der Dienst antwortet mit Status {(int)response.StatusCode}.");
        }
        return JsonDocument.Parse(text);
    }
}
