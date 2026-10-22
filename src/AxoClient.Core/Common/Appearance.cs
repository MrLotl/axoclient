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

    public const int MaxCustomGradients = 12;
    public const string CustomPrefix = "custom:";

    public static bool IsColor(string? value) => value != null && HexColor().IsMatch(value);

    /// <summary>
    /// Bereinigt die Darstellungs-Einstellungen: ungültige eigene Verläufe fliegen raus, der aktive Verlauf
    /// passt immer zur gewählten Vorlage. Das alte Format (<c>custom</c> ohne Id) wird zu einem eigenen Verlauf.
    /// </summary>
    public static void Validate(AppearanceSettings a)
    {
        a.CustomGradients = (a.CustomGradients ?? [])
            .Where(c => c != null && !string.IsNullOrWhiteSpace(c.Id) && c.Id.Length <= 40 && IsColor(c.From) && IsColor(c.To))
            .DistinctBy(c => c.Id)
            .Take(MaxCustomGradients)
            .Select(c => c with { Angle = Math.Clamp(c.Angle, 0, 360) })
            .ToList();

        var accent = a.Accent;
        if (accent == null || !IsColor(accent.From) || !IsColor(accent.To))
            accent = Presets["axo"];
        accent = accent with { Angle = Math.Clamp(accent.Angle, 0, 360) };

        if (a.Preset == "custom" && a.CustomGradients.Count < MaxCustomGradients)
        {
            var id = Guid.NewGuid().ToString("N")[..8];
            a.CustomGradients.Add(new CustomGradient(id, accent.From, accent.To, accent.Angle));
            a.Preset = CustomPrefix + id;
        }

        if (a.Preset?.StartsWith(CustomPrefix, StringComparison.Ordinal) == true)
        {
            var custom = a.CustomGradients.FirstOrDefault(c => CustomPrefix + c.Id == a.Preset);
            if (custom != null)
            {
                a.Accent = new AccentGradient(custom.From, custom.To, custom.Angle);
                return;
            }
        }
        else if (a.Preset != null && Presets.TryGetValue(a.Preset, out var preset))
        {
            a.Accent = preset;
            return;
        }

        a.Preset = "axo";
        a.Accent = Presets["axo"];
    }

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
