package de.axoclient.mod.ui;

/** Farben des Ingame-Menüs, genau wie im Mockup (dunkles Design, Akzent-Verlauf Pink → Lila). */
public final class Theme {
	// Verlauf der Hauptknöpfe und Abzeichen
	public static final int ACCENT_FROM = 0xFFDB2777;
	public static final int ACCENT_TO = 0xFF7C3AED;
	// Heller Verlauf für Schrift ("Axo", Werte im Overlay)
	public static final int TEXT_FROM = 0xFFF472B6;
	public static final int TEXT_TO = 0xFFA78BFA;
	/** Schrift auf zartem Akzent-Hintergrund (gewählter Tab, "Eingeladen"). */
	public static final int ACCENT_TEXT = 0xFFF5D0FB;
	public static final int ACCENT_LINE = 0xFFECC6F2;

	public static final int TEXT_STRONG = 0xFFF2F2F2;
	public static final int TEXT = 0xFFE6E6E6;
	public static final int TEXT_SOFT = 0xFFD9D9D9;
	public static final int TEXT_DIM = 0xFFBDBDBD;
	public static final int MUTED = 0xFFA3A3A3;
	public static final int MUTED_2 = 0xFF8A8A8A;
	public static final int MUTED_3 = 0xFF7A7A7A;
	public static final int FAINT = 0xFF6B6B6B;
	public static final int WHITE = 0xFFFFFFFF;

	/** Fenster und Flächen. */
	public static final int WINDOW = 0xF7262626;
	public static final int WINDOW_LINE = 0xFF3A3A3A;
	public static final int DIVIDER = 0xFF333333;
	public static final int ROW = 0xFF303030;
	public static final int CARD = 0xFF2C2C2C;
	public static final int PANEL = 0xFF2A2A2A;
	public static final int INSET = 0xFF242424;
	public static final int WELL = 0xFF1F1F1F;
	public static final int TRACK = 0xFF252525;
	public static final int BUTTON = 0xFF353535;
	public static final int BUTTON_HOVER = 0xFF3A3A3A;
	public static final int BUTTON_2 = 0xFF383838;
	public static final int CHIP = 0xFF2E2E2E;
	public static final int KBD = 0xFF333333;
	public static final int OUTLINE = 0xFF444444;

	/** Status. */
	public static final int GREEN = 0xFF3FB950;
	public static final int GREEN_BG = 0xFF263B2C;
	public static final int GREEN_BG_HOVER = 0xFF2F4D37;
	public static final int BLUE = 0xFF4C8DFF;
	public static final int YELLOW = 0xFFE3B341;
	public static final int YELLOW_TEXT = 0xFFF0C860;
	public static final int OFFLINE = 0xFF6B6B6B;

	/** Schleier hinter dem Menü (rgba(10,10,14,0.55)). */
	public static final int SCRIM = 0x8C0A0A0E;

	private Theme() {}

	/** Gleiche Farbe mit anderer Deckkraft (0..1). */
	public static int alpha(int argb, float alpha) {
		int a = Math.round(Math.max(0, Math.min(1, alpha)) * 255);
		return (a << 24) | (argb & 0xFFFFFF);
	}

	/** Mischung zweier Farben samt Deckkraft; at 0 = erste, 1 = zweite. */
	public static int mix(int from, int to, float at) {
		int result = 0;
		for (int shift = 0; shift <= 24; shift += 8) {
			int a = (from >>> shift) & 0xFF;
			int b = (to >>> shift) & 0xFF;
			result |= Math.round(a + (b - a) * at) << shift;
		}
		return result;
	}

	/** Zartes Akzent-Gelb/Lila über einer Grundfarbe, wie "linear-gradient(rgba(236,72,153,a), rgba(139,92,246,a)), base". */
	public static int tint(int base, float alpha, float at) {
		int accent = mix(0xFFEC4899, 0xFF8B5CF6, at);
		return mix(base, accent | 0xFF000000, alpha);
	}

	/** "#rrggbb" vom Launcher in ARGB; ungültig = fallback. */
	public static int parse(String hex, int fallback) {
		if (hex == null || !hex.matches("#[0-9a-fA-F]{6}"))
			return fallback;
		return 0xFF000000 | Integer.parseInt(hex.substring(1), 16);
	}
}
