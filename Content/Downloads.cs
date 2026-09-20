using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace McLauncher;

/// <summary>Streaming-Downloads in Dateien, mit Prüfsummen. Gedacht für große Dateien (Modpacks, Mods).</summary>
public static class Downloads
{
    /// <summary>
    /// Server, von denen Dateien eines Modpacks geladen werden dürfen. Das schreibt die Modrinth-Spezifikation
    /// (.mrpack) vor; alles andere ist ein Zeichen für ein manipuliertes Pack.
    /// </summary>
    private static readonly string[] ModpackHosts =
        ["cdn.modrinth.com", "github.com", "raw.githubusercontent.com", "gitlab.com"];

    public static bool IsAllowedModpackUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && ModpackHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Lädt <paramref name="url"/> nach <paramref name="destination"/>. Geschrieben wird erst in eine ".part"-Datei,
    /// damit nie eine halbe Datei unter dem endgültigen Namen liegt. Liefert SHA-1 und SHA-512 (klein geschriebenes Hex).
    /// </summary>
    /// <param name="bytesReceived">Wird mit der Menge der jeweils neu geladenen Bytes aufgerufen (kann aus mehreren Threads kommen).</param>
    public static async Task<(string Sha1, string Sha512)> DownloadToFileAsync(HttpClient http, string url,
        string destination, Action<long>? bytesReceived, CancellationToken cancel)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".part";
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("AxoClient/1.0");
            // ResponseHeadersRead: der Inhalt wird gestreamt, das Zeitlimit von HttpClient gilt nur bis zu den Kopfzeilen
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            await using (var source = await response.Content.ReadAsStreamAsync(cancel).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None,
                             bufferSize: 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancel).ConfigureAwait(false)) > 0)
                {
                    sha1.AppendData(buffer, 0, read);
                    sha512.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel).ConfigureAwait(false);
                    bytesReceived?.Invoke(read);
                }
            }

            File.Move(partial, destination, overwrite: true);
            return (Convert.ToHexString(sha1.GetHashAndReset()).ToLowerInvariant(),
                Convert.ToHexString(sha512.GetHashAndReset()).ToLowerInvariant());
        }
        finally
        {
            if (File.Exists(partial))
                File.Delete(partial); // Abbruch oder Fehler: keine Reste liegen lassen
        }
    }

    /// <summary>
    /// Prüft einen Dateipfad aus einer fremden Quelle (Modpack, geteilte Datei) und liefert den vollständigen Pfad
    /// innerhalb von <paramref name="baseDir"/> – oder null, wenn er hinausführen würde (".." , Laufwerk, Datenstrom).
    /// </summary>
    public static string? SafeCombine(string baseDir, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || relativePath.Length > 240)
            return null;
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('\0'))
            return null;

        var invalid = Path.GetInvalidFileNameChars();
        foreach (var segment in normalized.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny(invalid) >= 0
                || segment.EndsWith(' ') || segment.EndsWith('.'))
                return null;
        }

        var root = Path.GetFullPath(baseDir).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, normalized));
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
    }
}
