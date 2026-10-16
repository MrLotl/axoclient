using System.Globalization;
using System.Text.RegularExpressions;

namespace AxoClient.Core.Common;

/// <summary>Vorlagen für den Akzent-Verlauf und die Kontrastprüfung bei eigenen Farben.</summary>
public static partial class Appearance
{
    public static readonly IReadOnlyDictionary<string, AccentGradient> Presets = new Dictionary<string, AccentGradient>
    {
        ["axo"] = new("#db2777", "#7c3aed", 135),
        ["mint"] = new("#0d9488", "#2563eb", 135),
        ["sunset"] = new("#ea580c", "#db2777", 135),
        ["midnight"] = new("#4f46e5", "#1e3a8a", 135)
    };

    public static bool IsColor(string? value) => value != null && HexColor().IsMatch(value);

    /// <summary>Kleinster Kontrast zwischen weißem Button-Text und den beiden Verlaufsfarben (WCAG).</summary>
    public static double MinContrastWithWhite(AccentGradient accent) =>
        Math.Min(ContrastWithWhite(accent.From), ContrastWithWhite(accent.To));

    /// <summary>Weißer Text braucht mindestens 4,5 : 1.</summary>
    public static bool HasEnoughContrast(AccentGradient accent) => MinContrastWithWhite(accent) >= 4.5;

    public static double ContrastWithWhite(string hex)
    {
        var l = RelativeLuminance(hex);
        return 1.05 / (l + 0.05);
    }

    private static double RelativeLuminance(string hex)
    {
        var value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber);
        double Channel(int c)
        {
            var s = c / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel((value >> 16) & 0xFF) + 0.7152 * Channel((value >> 8) & 0xFF) + 0.0722 * Channel(value & 0xFF);
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}
