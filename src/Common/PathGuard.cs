namespace AxoClient.Common
{
    /// <summary>
    /// Prüft jeden Pfad aus fremden Quellen (Archive, .mrpack, Pakete von Freunden) gegen den Zielordner.
    /// <c>../</c>, absolute Pfade, Laufwerksbuchstaben und reservierte Namen werden abgelehnt.
    /// </summary>
    public class PathGuard : IPathGuard
    {
        private readonly ITextHelper _textHelper;
        private readonly HashSet<string> _reserved = new(StringComparer.OrdinalIgnoreCase)
        {
            "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        };

        public PathGuard(ITextHelper textHelper)
        {
            _textHelper = textHelper;
        }

        /// <summary>Gibt den vollen Pfad zurück oder <c>null</c>, wenn <paramref name="relative"/> nicht sicher ist.</summary>
        public string? SafeCombine(string baseDirectory, string? relative)
        {
            if (string.IsNullOrWhiteSpace(relative) || relative.Length > 260)
            {
                return null;
            }

            var normalized = relative.Replace('\\', '/');
            if (normalized.StartsWith('/') || normalized.Contains(':') || normalized.Contains('\0'))
            {
                return null;
            }

            var invalid = Path.GetInvalidFileNameChars();
            foreach (var segment in normalized.Split('/'))
            {
                if (segment.Length == 0 || segment is "." or ".." || segment.IndexOfAny(invalid) >= 0
                    || segment.EndsWith(' ') || segment.EndsWith('.')
                    || _reserved.Contains(Path.GetFileNameWithoutExtension(segment)))
                {
                    return null;
                }
            }
            return ResolveInside(baseDirectory, normalized);
        }

        /// <summary>Wie <see cref="SafeCombine"/>, wirft aber einen übersetzbaren Fehler.</summary>
        public string Combine(string baseDirectory, string? relative) =>
            SafeCombine(baseDirectory, relative) ?? throw new AxoException("errors.unsafePath",
                new() { ["path"] = _textHelper.Clean(relative, 120) });

        public string? ResolveInside(string parent, string relative)
        {
            try
            {
                var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        public bool IsInside(string parent, string path)
        {
            try
            {
                var root = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>Ein einzelner, harmloser Dateiname (keine Ordner).</summary>
        public bool IsSafeFileName(string? name) =>
            !string.IsNullOrWhiteSpace(name) && name.Length <= 200 && !name.Contains('/') && !name.Contains('\\')
            && SafeCombine(Path.GetTempPath(), name) != null;
    }
}
