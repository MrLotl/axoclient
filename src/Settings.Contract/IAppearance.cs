using AxoClient.DataModel.Settings;

namespace AxoClient.Settings.Contract
{
    public interface IAppearance
    {
        /// <summary>Mitgelieferte Akzent-Verläufe.</summary>
        IReadOnlyDictionary<string, AccentGradient> Presets { get; }

        bool IsColor(string? value);

        /// <summary>
        /// Bereinigt die Darstellungs-Einstellungen: ungültige eigene Verläufe fliegen raus, der aktive Verlauf
        /// passt immer zur gewählten Vorlage. Das alte Format (<c>custom</c> ohne Id) wird zu einem eigenen Verlauf.
        /// </summary>
        void Validate(AppearanceSettings a);

        /// <summary>Kleinster Kontrast zwischen weißem Button-Text und den beiden Verlaufsfarben (WCAG).</summary>
        double MinContrastWithWhite(AccentGradient accent);

        /// <summary>Weißer Text braucht mindestens 4,5 : 1.</summary>
        bool HasEnoughContrast(AccentGradient accent);

        double ContrastWithWhite(string hex);
    }
}
