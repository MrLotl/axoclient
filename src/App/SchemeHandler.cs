using System.Reflection;
using AxoClient.Common;
using AxoClient.Game.Contract;
using AxoClient.Instances.Contract;
using AxoClient.Servers.Contract;
using AxoClient.Skins.Contract;

namespace AxoClient.App
{
    /// <summary>
    /// Inhalte für den <see cref="UiServer"/>: die mitgelieferte Oberfläche (eingebettet, nie fremde Seiten),
    /// <c>/axo/…</c> (gleicher Ursprung) liefert Bilder aus dem Datenordner über IDs (Instanzbilder, Köpfe, Skins, Screenshots …).
    /// </summary>
    public sealed class SchemeHandler
    {
        private readonly IImageFormats _imageFormats;
        private readonly IInstanceManager _instances;
        private readonly IWorldManager _worlds;
        private readonly IScreenshotManager _screenshots;
        private readonly IServerListManager _serverLists;
        private readonly ILocalServerManager _localServers;
        private readonly ISkinManager _skins;
        private readonly ICapeManager _capes;
        private readonly IPanoramaService _panoramas;
        private readonly Assembly _assembly = typeof(SchemeHandler).Assembly;

        private readonly Dictionary<string, string> _mime = new(StringComparer.OrdinalIgnoreCase)
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

        public SchemeHandler(IImageFormats imageFormats, IInstanceManager instances, IWorldManager worlds, IScreenshotManager screenshots,
            IServerListManager serverLists, ILocalServerManager localServers, ISkinManager skins, ICapeManager capes, IPanoramaService panoramas)
        {
            _imageFormats = imageFormats;
            _instances = instances;
            _worlds = worlds;
            _screenshots = screenshots;
            _serverLists = serverLists;
            _localServers = localServers;
            _skins = skins;
            _capes = capes;
            _panoramas = panoramas;
        }

        public Stream? App(string url, out string contentType)
        {
            var path = new Uri(url).AbsolutePath.TrimStart('/');
            // Lokale Bilder laufen über denselben Ursprung wie die Oberfläche (nötig für die 3D-Skin-Ansicht)
            if (path.StartsWith("axo/", StringComparison.Ordinal))
            {
                return Axo(url, out contentType);
            }

            if (path.Length == 0 || !path.Contains('.'))
            {
                path = "index.html";
            }

            var stream = _assembly.GetManifestResourceStream("wwwroot/" + path);
            if (stream == null && !path.StartsWith("assets/"))
            {
                stream = _assembly.GetManifestResourceStream("wwwroot/index.html");
            }

            contentType = _mime.GetValueOrDefault(Path.GetExtension(path), "application/octet-stream");
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
                {
                    throw new UriFormatException();
                }

                var kind = segments[1];
                var parts = segments[2..];
                byte[]? bytes = kind switch
                {
                    "instance-icon" when parts.Length == 1 => readFile(_instances.ImagePath(parts[0], banner: false)),
                    "instance-banner" when parts.Length == 1 => readFile(_instances.ImagePath(parts[0], banner: true)),
                    "world-icon" when parts.Length == 2 => readFile(_worlds.IconPath(parts[0], parts[1])),
                    "screenshot" when parts.Length == 2 => readFile(_screenshots.PathOf(parts[0], parts[1])),
                    "server-icon" when parts.Length == 2 && int.TryParse(parts[1], out var index) => _serverLists.Icon(parts[0], index),
                    "server-icon-local" when parts.Length == 1 => readFile(_localServers.IconPath(parts[0])),
                    "server-banner-local" when parts.Length == 1 => readFile(_localServers.BannerPath(parts[0])),
                    "skin-file" when parts.Length == 1 => readFile(_skins.FilePath(parts[0])),
                    "cape-file" when parts.Length == 1 => readFile(_capes.FilePath(parts[0])),
                    // Titelbild-Panorama einer Minecraft-Version, direkt aus deren Spieldatei
                    // (noch nie gestartete Versionen: nur das Panorama-Bild bei Mojang nachladen)
                    "panorama" when parts.Length == 1 => _panoramas.Get(parts[0]) ?? runSync(() => _panoramas.Fetch(parts[0])),
                    // Weitere Blickrichtungen: /axo/panorama/<Version>/<0–3>
                    "panorama" when parts.Length == 2 && int.TryParse(parts[1], out var face) && face is >= 0 and <= 3 =>
                        _panoramas.Get(parts[0], face) ?? runSync(() => _panoramas.Fetch(parts[0], face)),
                    // Skin eines Spielers (für Köpfe): erst Cache, sonst Mojang
                    "head" when parts.Length == 1 => runSync(() => _skins.PlayerSkin(parts[0], CancellationToken.None)),
                    _ => null
                };
                if (bytes == null)
                {
                    contentType = "text/plain";
                    return new MemoryStream();
                }
                contentType = _imageFormats.MimeOf(bytes);
                return new MemoryStream(bytes);
            }
            catch (Exception exception) when (exception is UriFormatException or IOException or AxoException or UnauthorizedAccessException)
            {
                contentType = "text/plain";
                return new MemoryStream();
            }
        }

        private byte[]? readFile(string? path) => path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;

        /// <summary>Der UI-Server ruft das synchron auf einem Hintergrund-Thread auf.</summary>
        private T? runSync<T>(Func<Task<T?>> work)
        {
#pragma warning disable VSTHRD002 // läuft auf einem Thread des UI-Servers, nie auf dem UI-Thread
            return Task.Run(work).GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
        }
    }
}
