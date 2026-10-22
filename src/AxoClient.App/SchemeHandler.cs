using System.Reflection;
using AxoClient.Core.Common;
using AxoClient.Core.Instances;

namespace AxoClient.App;

/// <summary>
/// Inhalte für den <see cref="UiServer"/>: die mitgelieferte Oberfläche (eingebettet, nie fremde Seiten),
/// <c>/axo/…</c> (gleicher Ursprung) liefert Bilder aus dem Datenordner über IDs (Instanzbilder, Köpfe, Skins, Screenshots …).
/// </summary>
internal sealed class SchemeHandler(AppHost host)
{
    private static readonly Assembly Assembly = typeof(SchemeHandler).Assembly;

    private static readonly Dictionary<string, string> Mime = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".ico"] = "image/x-icon"
    };

    public Stream? App(string url, out string contentType)
    {
        var path = new Uri(url).AbsolutePath.TrimStart('/');
        // Lokale Bilder laufen über denselben Ursprung wie die Oberfläche (nötig für die 3D-Skin-Ansicht)
        if (path.StartsWith("axo/", StringComparison.Ordinal))
            return Axo(url, out contentType);
        if (path.Length == 0 || !path.Contains('.'))
            path = "index.html";
        var stream = Assembly.GetManifestResourceStream("wwwroot/" + path);
        if (stream == null && !path.StartsWith("assets/"))
            stream = Assembly.GetManifestResourceStream("wwwroot/index.html");
        contentType = Mime.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
        if (stream == null)
        {
            contentType = "text/plain";
            return new MemoryStream("UI fehlt. Bitte mit 'npm run build' im Ordner ui bauen."u8.ToArray());
        }
        return stream;
    }

    public Stream? Axo(string url, out string contentType)
    {
        contentType = "image/png";
        try
        {
            var uri = new Uri(url);
            var segments = uri.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
            if (segments.Length < 2 || segments[0] != "axo")
                throw new UriFormatException();
            var kind = segments[1];
            var parts = segments[2..];
            byte[]? bytes = kind switch
            {
                "instance-icon" when parts.Length == 1 => ReadFile(host.Instances.ImagePath(parts[0], banner: false)),
                "instance-banner" when parts.Length == 1 => ReadFile(host.Instances.ImagePath(parts[0], banner: true)),
                "world-icon" when parts.Length == 2 => ReadFile(host.Worlds.IconPath(parts[0], parts[1])),
                "screenshot" when parts.Length == 2 => ReadFile(host.Screenshots.PathOf(parts[0], parts[1])),
                "server-icon" when parts.Length == 2 && int.TryParse(parts[1], out var index) => host.ServerLists.Icon(parts[0], index),
                "server-icon-local" when parts.Length == 1 => ReadFile(host.LocalServers.IconPath(parts[0])),
                "server-banner-local" when parts.Length == 1 => ReadFile(host.LocalServers.BannerPath(parts[0])),
                "skin-file" when parts.Length == 1 => ReadFile(host.Skins.FilePath(parts[0])),
                "cape-file" when parts.Length == 1 => ReadFile(host.Capes.FilePath(parts[0])),
                // Skin eines Spielers (für Köpfe): erst Cache, sonst Mojang
                "head" when parts.Length == 1 => RunSync(() => host.Skins.PlayerSkinAsync(parts[0], CancellationToken.None)),
                _ => null
            };
            if (bytes == null)
            {
                contentType = "text/plain";
                return new MemoryStream();
            }
            contentType = ImageFormats.MimeOf(bytes);
            return new MemoryStream(bytes);
        }
        catch (Exception ex) when (ex is UriFormatException or IOException or AxoException or UnauthorizedAccessException)
        {
            contentType = "text/plain";
            return new MemoryStream();
        }
    }

    private static byte[]? ReadFile(string? path) => path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;

    /// <summary>Der UI-Server ruft das synchron auf einem Hintergrund-Thread auf.</summary>
    private static T? RunSync<T>(Func<Task<T?>> work)
    {
#pragma warning disable VSTHRD002 // läuft auf einem Thread des UI-Servers, nie auf dem UI-Thread
        return Task.Run(work).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
    }
}
