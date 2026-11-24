using System.Globalization;
using System.Text.RegularExpressions;
using AxoClient.DataModel.Settings;
using AxoClient.Settings.Contract;

namespace AxoClient.Settings
{
    /// <summary>Vorlagen für den Akzent-Verlauf und die Kontrastprüfung bei eigenen Farben.</summary>
    public partial class Appearance : IAppearance
    {
        /// <summary>Mitgelieferte Akzent-Verläufe.</summary>
        public IReadOnlyDictionary<string, AccentGradient> Presets { get; } = new Dictionary<string, AccentGradient>
        {
            ["axo"] = new("#db2777", "#7c3aed", 135),
            ["mint"] = new("#0d9488", "#2563eb", 135),
            ["sunset"] = new("#ea580c", "#db2777", 135),
            ["midnight"] = new("#4f46e5", "#1e3a8a", 135)
        };

        public const int MaxCustomGradients = 12;
        public const string CustomPrefix = "custom:";

        public bool IsColor(string? value) => value != null && hexColor().IsMatch(value);

        /// <summary>
        /// Bereinigt die Darstellungs-Einstellungen: ungültige eigene Verläufe fliegen raus, der aktive Verlauf
        /// passt immer zur gewählten Vorlage. Das alte Format (<c>custom</c> ohne Id) wird zu einem eigenen Verlauf.
        /// </summary>
        public void Validate(AppearanceSettings appearanceSettings)
        {
            appearanceSettings.CustomGradients = (appearanceSettings.CustomGradients ?? [])
                .Where(customGradient => customGradient != null && !string.IsNullOrWhiteSpace(customGradient.Id) && customGradient.Id.Length <= 40 && IsColor(customGradient.From) && IsColor(customGradient.To))
                .DistinctBy(customGradient => customGradient.Id)
                .Take(MaxCustomGradients)
                .Select(customGradient => customGradient with { Angle = Math.Clamp(customGradient.Angle, 0, 360) })
                .ToList();

            var accent = appearanceSettings.Accent;
            if (accent == null || !IsColor(accent.From) || !IsColor(accent.To))
            {
                accent = Presets["axo"];
            }

            accent = accent with { Angle = Math.Clamp(accent.Angle, 0, 360) };

            if (appearanceSettings.Preset == "custom" && appearanceSettings.CustomGradients.Count < MaxCustomGradients)
            {
                var id = Guid.NewGuid().ToString("N")[..8];
                appearanceSettings.CustomGradients.Add(new CustomGradient(id, accent.From, accent.To, accent.Angle));
                appearanceSettings.Preset = CustomPrefix + id;
            }

            if (appearanceSettings.Preset?.StartsWith(CustomPrefix, StringComparison.Ordinal) == true)
            {
                var custom = appearanceSettings.CustomGradients.FirstOrDefault(customGradient => CustomPrefix + customGradient.Id == appearanceSettings.Preset);
                if (custom != null)
                {
                    appearanceSettings.Accent = new AccentGradient(custom.From, custom.To, custom.Angle);
                    return;
                }
            }
            else if (appearanceSettings.Preset != null && Presets.TryGetValue(appearanceSettings.Preset, out var preset))
            {
                appearanceSettings.Accent = preset;
                return;
            }

            appearanceSettings.Preset = "axo";
            appearanceSettings.Accent = Presets["axo"];
        }

        /// <summary>Kleinster Kontrast zwischen weißem Button-Text und den beiden Verlaufsfarben (WCAG).</summary>
        public double MinContrastWithWhite(AccentGradient accent) =>
            Math.Min(ContrastWithWhite(accent.From), ContrastWithWhite(accent.To));

        /// <summary>Weißer Text braucht mindestens 4,5 : 1.</summary>
        public bool HasEnoughContrast(AccentGradient accent) => MinContrastWithWhite(accent) >= 4.5;

        public double ContrastWithWhite(string hex)
        {
            var luminance = relativeLuminance(hex);
            return 1.05 / (luminance + 0.05);
        }

        private double relativeLuminance(string hex)
        {
            var value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber);
            double Channel(int channelValue)
            {
                var scaled = channelValue / 255.0;
                return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel((value >> 16) & 0xFF) + 0.7152 * Channel((value >> 8) & 0xFF) + 0.0722 * Channel(value & 0xFF);
        }

        [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
        private partial Regex hexColor();
    }
}
