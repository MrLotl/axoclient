using AxoClient.Social.Contract;

namespace AxoClient.Social
{
    /// <summary>
    /// options.txt von Freunden: nur bekannte, harmlose Schlüssel (Grafik, Steuerung, Sound). Unbekannte Schlüssel werden
    /// verworfen, z. B. Ressourcepack-Listen oder Pfade.
    /// </summary>
    public class OptionsFile : IOptionsFile
    {
        private readonly string[] _allowedPrefixes =
        [
            "key_", "soundCategory_", "modelPart_", "mouseSensitivity", "fov", "gamma", "renderDistance", "simulationDistance",
            "guiScale", "particles", "maxFps", "graphicsMode", "ao", "biomeBlendRadius", "entityDistanceScaling", "renderClouds",
            "chatOpacity", "chatScale", "chatWidth", "chatHeightFocused", "chatHeightUnfocused", "autoJump", "toggleCrouch", "toggleSprint",
            "invertYMouse", "bobView", "enableVsync", "fullscreen", "showSubtitles", "narrator", "lang", "darkMojangStudiosBackground",
            "entityShadows", "fovEffectScale", "screenEffectScale", "damageTiltStrength", "mainHand", "attackIndicator", "chatColors",
            "chatLinks", "reducedDebugInfo", "mipmapLevels", "cloudRange", "menuBackgroundBlurriness"
        ];

        public string Filter(string text) =>
            string.Join('\n', text.Split('\n').Select(l => l.TrimEnd('\r'))
                .Where(l => l.IndexOf(':') is > 0 and var colon && _allowedPrefixes.Any(p => l[..colon].StartsWith(p, StringComparison.Ordinal))
                            && l.Length < 200 && !l.Contains('\\') && !l.Contains('/'))
                .Take(400));
    }
}
