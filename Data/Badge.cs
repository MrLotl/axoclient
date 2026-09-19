using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CmlLib.Core.Auth;

namespace McLauncher;

/// <summary>
/// Axolotl-Symbol in der Tabliste: legt die passende Badge-Mod in Fabric-Instanzen und meldet den Spieler
/// beim Symbol-Dienst an. Der Nachweis läuft über Mojangs "join"/"hasJoined": das Minecraft-Token geht dabei
/// nur an Mojang, nie an den Dienst.
/// </summary>
public static class Badge
{
    /// <summary>Dateiname der Mod im mods-Ordner (vom Launcher verwaltet, nicht in der Mod-Liste angezeigt).</summary>
    public const string ModFileName = "mclauncher-badge.jar";

    public static bool IsConfigured(LauncherSettings settings) =>
        settings.BadgeEnabled && !string.IsNullOrWhiteSpace(settings.BadgeApiUrl);

    /// <summary>Für welche Minecraft-Versionen der Launcher eine Badge-Mod mitbringt.</summary>
    public static IEnumerable<string> SupportedVersions => Assembly.GetExecutingAssembly().GetManifestResourceNames()
        .Where(n => n.StartsWith("badge-mods/mclauncher-badge-") && n.EndsWith(".jar"))
        .Select(n => n["badge-mods/mclauncher-badge-".Length..^".jar".Length]);

    public static bool IsActiveFor(Installation inst, LauncherSettings settings) =>
        IsConfigured(settings) && inst.Loader == LoaderType.Fabric && SupportedVersions.Contains(inst.MinecraftVersion);

    /// <summary>Legt die Mod in den mods-Ordner bzw. entfernt sie, wenn sie nicht (mehr) passt oder abgeschaltet ist.</summary>
    public static void SyncMod(Installation inst, LauncherSettings settings)
    {
        var target = Path.Combine(inst.GameDir, "mods", ModFileName);
        if (!IsActiveFor(inst, settings))
        {
            if (File.Exists(target))
                File.Delete(target);
            return;
        }

        using var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream($"badge-mods/mclauncher-badge-{inst.MinecraftVersion}.jar")!;
        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        var bytes = buffer.ToArray();

        // Nur schreiben, wenn sich die Mod geändert hat (z.B. nach einem Launcher-Update)
        if (File.Exists(target) && SHA1.HashData(File.ReadAllBytes(target)).SequenceEqual(SHA1.HashData(bytes)))
            return;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, bytes);
    }

    /// <summary>JVM-Argument, mit dem die Mod die Adresse des Dienstes erfährt.</summary>
    public static string JvmArgument(LauncherSettings settings) => $"-Dmclauncher.badge.api={settings.BadgeApiUrl!.Trim()}";

    /// <summary>Prüft, ob der Dienst unter der Adresse antwortet.</summary>
    public static async Task CheckServiceAsync(HttpClient http, string apiUrl)
    {
        using var response = await http.GetAsync(apiUrl.Trim().TrimEnd('/') + "/");
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode || !text.Contains("mclauncher-badge"))
            throw new InvalidOperationException(
                $"Unter dieser Adresse antwortet kein Badge-Dienst (Status {(int)response.StatusCode}).");
    }

    /// <summary>
    /// Meldet den angemeldeten Spieler beim Dienst als Launcher-Nutzer an. Nachweis über das Spieler-Zertifikat
    /// von Mojang (wie bei signierten Chatnachrichten): Mojang bestätigt, dass der Schlüssel zu dieser UUID gehört,
    /// und der Launcher signiert damit eine Nachricht. Das Minecraft-Token geht dabei nur an Mojang.
    /// </summary>
    public static async Task<string?> RegisterAsync(HttpClient http, MSession session, string apiUrl)
    {
        // 1. Spieler-Zertifikat bei Mojang abholen
        // Leerer Inhalt, aber als JSON gekennzeichnet (sonst antwortet Mojang mit 415)
        var emptyJson = new ByteArrayContent([]);
        emptyJson.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        using var certRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.minecraftservices.com/player/certificates")
        {
            Content = emptyJson
        };
        certRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        using var certResponse = await http.SendAsync(certRequest);
        if (!certResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Mojang hat kein Spieler-Zertifikat ausgestellt (Status {(int)certResponse.StatusCode}).");
        using var cert = JsonDocument.Parse(await certResponse.Content.ReadAsStringAsync());
        var root = cert.RootElement;
        var keyPair = root.GetProperty("keyPair");

        // Mojang beschriftet die Schlüssel als "RSA ... KEY", der Inhalt ist aber PKCS#8 bzw. X.509 (SPKI)
        var publicKey = PemBody(keyPair.GetProperty("publicKey").GetString()!);
        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(PemBody(keyPair.GetProperty("privateKey").GetString()!), out _);
        var expiresAt = DateTimeOffset.Parse(root.GetProperty("expiresAt").GetString()!, CultureInfo.InvariantCulture)
            .ToUnixTimeMilliseconds();

        // 2. Nachricht mit Zeitstempel signieren, damit sie nicht später wiederverwendet werden kann
        var uuid = session.UUID!.Replace("-", "").ToLowerInvariant();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var signature = rsa.SignData(Encoding.UTF8.GetBytes($"mclauncher-badge:register:{uuid}:{timestamp}"),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // 3. Der Dienst prüft beide Signaturen und trägt den Spieler ein
        using var register = await http.PostAsJsonAsync(apiUrl.Trim().TrimEnd('/') + "/register", new
        {
            uuid,
            name = session.Username,
            publicKey = Convert.ToBase64String(publicKey),
            expiresAt,
            keySignature = root.GetProperty("publicKeySignatureV2").GetString(),
            timestamp,
            signature = Convert.ToBase64String(signature)
        });
        if (!register.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Der Dienst hat die Anmeldung abgelehnt: {await register.Content.ReadAsStringAsync()}");

        // Token für Freunde und Status (ältere Dienst-Versionen liefern keins)
        using var answer = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        return answer.RootElement.TryGetProperty("token", out var token) ? token.GetString() : null;
    }

    /// <summary>Inhalt eines PEM-Blocks (ohne "-----BEGIN/END ...-----") als Bytes.</summary>
    private static byte[] PemBody(string pem) =>
        Convert.FromBase64String(string.Concat(pem.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("-----"))));
}
