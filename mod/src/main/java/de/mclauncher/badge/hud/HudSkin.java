package de.mclauncher.badge.hud;

/**
 * Das Aussehen von Menü und Anzeigen: dieselben Farben wie im Launcher, durchscheinende Flächen
 * mit echt abgerundeten Ecken. Den verschwommenen Hintergrund liefert Minecraft selbst, sobald
 * ein Fenster offen ist – die Flächen hier lassen ihn bewusst durchscheinen.
 */
public final class HudSkin {
	/** Grün des Launchers. */
	public static final int ACCENT = 0xFF3BA55C;
	public static final int ACCENT_SOFT = 0x553BA55C;
	public static final int ACCENT_FAINT = 0x223BA55C;

	public static final int TEXT = 0xFFFFFFFF;
	public static final int TEXT_DIM = 0xFFD0D4D9;
	public static final int MUTED = 0xFF9AA0A6;
	public static final int ON_ACCENT = 0xFF14161A;

	/** Karten des Menüs: dunkel, aber durchscheinend. */
	public static final int GLASS = 0xD01E1F22;
	public static final int GLASS_SOFT = 0x991E1F22;
	public static final int BORDER = 0x33FFFFFF;
	public static final int HIGHLIGHT = 0x22FFFFFF;
	public static final int HOVER = 0x14FFFFFF;
	public static final int LINE = 0x14FFFFFF;
	public static final int TRACK = 0x552B2D31;

	/** Leichter Schleier über dem Spiel; der Rest der Tiefe kommt vom Weichzeichner. */
	public static final int SCRIM = 0x55000000;
	/** Rasterlinien und Hilfslinien beim Verschieben. */
	public static final int GRID_LINE = 0x18FFFFFF;
	public static final int GUIDE_LINE = 0xCC3BA55C;

	/** Eckenradius der Menükarten. */
	public static final int CARD_RADIUS = 4;

	/** Platz für Beschriftung und Wert einer Reglerzeile, dazu die Höhe der Schiene. */
	public static final int LABEL_WIDTH = 56;
	public static final int VALUE_WIDTH = 34;
	public static final int TRACK_HEIGHT = 6;

	/** Schnellzugriff im Farbwähler; frei wählbar bleibt trotzdem jede Farbe. */
	public static final int[] PRESETS = {
		0xFFFFFF, 0x000000, 0xD0D4D9, 0x9AA0A6, 0x3BA55C, 0x4CC06D,
		0xE8B84A, 0xE36D6F, 0xD83C3E, 0x6FA8DC, 0xB07CD8, 0xE87CB0
	};

	/** Nur zum Einlesen älterer Einstellungsdateien: dort stand statt der Farbe ihre Nummer. */
	public static final int[] LEGACY_TEXT_COLORS = {
		0xFFFFFF, 0x3BA55C, 0xE8B84A, 0xE36D6F, 0x6FA8DC, 0x9AA0A6
	};
	public static final int[] LEGACY_BACKGROUND_COLORS = {
		0x000000, 0x1E1F22, 0x3BA55C, 0x6FA8DC, 0xFFFFFF
	};

	private HudSkin() {}

	/** Farbe als "#RRGGBB" zum Anzeigen im Menü. */
	public static String hex(int rgb) {
		return String.format("#%06X", rgb & 0xFFFFFF);
	}

	/** Ein Farbanteil (0 bis 255); {@code shift} ist 16 für Rot, 8 für Grün, 0 für Blau. */
	public static int component(int rgb, int shift) {
		return (rgb >> shift) & 0xFF;
	}

	public static int withComponent(int rgb, int shift, int value) {
		return (rgb & ~(0xFF << shift) & 0xFFFFFF) | ((Math.max(0, Math.min(255, value))) << shift);
	}

	/** Farbe als Farbton (0-360), Sättigung und Helligkeit (je 0-100). */
	public static int[] toHsl(int rgb) {
		float red = component(rgb, 16) / 255.0F;
		float green = component(rgb, 8) / 255.0F;
		float blue = component(rgb, 0) / 255.0F;
		float max = Math.max(red, Math.max(green, blue));
		float min = Math.min(red, Math.min(green, blue));
		float lightness = (max + min) / 2.0F;
		float span = max - min;
		if (span < 0.0001F)
			return new int[] { 0, 0, Math.round(lightness * 100.0F) };
		float saturation = lightness > 0.5F ? span / (2.0F - max - min) : span / (max + min);
		float hue;
		if (max == red)
			hue = (green - blue) / span + (green < blue ? 6.0F : 0.0F);
		else if (max == green)
			hue = (blue - red) / span + 2.0F;
		else
			hue = (red - green) / span + 4.0F;
		return new int[] {
			Math.round(hue / 6.0F * 360.0F) % 360,
			Math.round(saturation * 100.0F),
			Math.round(lightness * 100.0F)
		};
	}

	/** Umgekehrt: aus Farbton, Sättigung und Helligkeit wieder 0xRRGGBB. */
	public static int fromHsl(int hueDegrees, int saturationPercent, int lightnessPercent) {
		float hue = (((hueDegrees % 360) + 360) % 360) / 360.0F;
		float saturation = Math.max(0, Math.min(100, saturationPercent)) / 100.0F;
		float lightness = Math.max(0, Math.min(100, lightnessPercent)) / 100.0F;
		if (saturation < 0.0001F) {
			int grey = Math.round(lightness * 255.0F);
			return (grey << 16) | (grey << 8) | grey;
		}
		float upper = lightness < 0.5F ? lightness * (1.0F + saturation)
			: lightness + saturation - lightness * saturation;
		float lower = 2.0F * lightness - upper;
		return (channel(lower, upper, hue + 1.0F / 3.0F) << 16)
			| (channel(lower, upper, hue) << 8)
			| channel(lower, upper, hue - 1.0F / 3.0F);
	}

	private static int channel(float lower, float upper, float position) {
		float at = position < 0.0F ? position + 1.0F : (position > 1.0F ? position - 1.0F : position);
		float value;
		if (at < 1.0F / 6.0F)
			value = lower + (upper - lower) * 6.0F * at;
		else if (at < 0.5F)
			value = upper;
		else if (at < 2.0F / 3.0F)
			value = lower + (upper - lower) * (2.0F / 3.0F - at) * 6.0F;
		else
			value = lower;
		return Math.max(0, Math.min(255, Math.round(value * 255.0F)));
	}

	/** Kleine Umschaltfläche, z.B. RGB / HSL / Hex im Farbwähler. */
	public static void tab(HudPainter painter, int x, int y, int width, int height, String label,
						   boolean active, boolean hovered) {
		rounded(painter, x, y, width, height, 2, active ? ACCENT_SOFT : (hovered ? HOVER : TRACK));
		outline(painter, x, y, width, height, 2, active ? ACCENT : BORDER);
		centered(painter, label, x + width / 2, y + (height - 8) / 2, active ? TEXT : MUTED);
	}

	/** Eingabefeld, hier nur für den Hex-Wert. */
	public static void field(HudPainter painter, int x, int y, int width, int height, String text,
							 boolean caret) {
		rounded(painter, x, y, width, height, 2, TRACK);
		outline(painter, x, y, width, height, 2, ACCENT);
		int textY = y + (height - 8) / 2;
		painter.text(text, x + 4, textY, TEXT);
		if (caret)
			painter.fill(x + 5 + painter.textWidth(text), textY - 1, 1, 9, ACCENT);
	}

	public static int wrap(int index, int length) {
		return ((index % length) + length) % length;
	}

	// ---------- Abgerundete Flächen ----------

	/**
	 * Wie weit die Fläche in dieser Zeile der Ecke eingerückt ist. Die Ecke folgt einem Kreis,
	 * damit die Rundung bei jedem Radius sauber aussieht.
	 */
	private static int inset(int radius, int row) {
		double distance = radius - row - 0.5;
		return radius - (int) Math.round(Math.sqrt(Math.max(0.0, radius * radius - distance * distance)));
	}

	private static int radius(int radius, int width, int height) {
		return Math.max(0, Math.min(radius, Math.min(width, height) / 2));
	}

	/** Gefüllte Fläche mit frei wählbarem Eckenradius. */
	public static void rounded(HudPainter painter, int x, int y, int width, int height, int radius, int argb) {
		if (width <= 0 || height <= 0)
			return;
		int r = radius(radius, width, height);
		if (r == 0) {
			painter.fill(x, y, width, height, argb);
			return;
		}
		painter.fill(x, y + r, width, height - 2 * r, argb);
		for (int row = 0; row < r; row++) {
			int inset = inset(r, row);
			painter.fill(x + inset, y + row, width - 2 * inset, 1, argb);
			painter.fill(x + inset, y + height - 1 - row, width - 2 * inset, 1, argb);
		}
	}

	/** Rahmen mit frei wählbarem Eckenradius. */
	public static void outline(HudPainter painter, int x, int y, int width, int height, int radius, int argb) {
		if (width <= 0 || height <= 0)
			return;
		int r = radius(radius, width, height);
		painter.fill(x + r, y, width - 2 * r, 1, argb);
		painter.fill(x + r, y + height - 1, width - 2 * r, 1, argb);
		painter.fill(x, y + r, 1, height - 2 * r, argb);
		painter.fill(x + width - 1, y + r, 1, height - 2 * r, argb);
		for (int row = 0; row < r; row++) {
			// Von der vorigen Zeile bis zu dieser durchziehen, damit der Bogen keine Löcher hat
			int inset = inset(r, row);
			int run = Math.max(1, (row == 0 ? r : inset(r, row - 1)) - inset + 1);
			painter.fill(x + inset, y + row, run, 1, argb);
			painter.fill(x + width - inset - run, y + row, run, 1, argb);
			painter.fill(x + inset, y + height - 1 - row, run, 1, argb);
			painter.fill(x + width - inset - run, y + height - 1 - row, run, 1, argb);
		}
	}

	// ---------- Bausteine des Menüs ----------

	/** Glaskarte: durchscheinende Fläche, feiner Rahmen, oben ein Lichtstreifen. */
	public static void card(HudPainter painter, int x, int y, int width, int height) {
		rounded(painter, x, y, width, height, CARD_RADIUS, GLASS);
		outline(painter, x, y, width, height, CARD_RADIUS, BORDER);
		painter.fill(x + CARD_RADIUS, y + 1, width - 2 * CARD_RADIUS, 1, HIGHLIGHT);
	}

	/** Trennstrich innerhalb einer Karte. */
	public static void separator(HudPainter painter, int x, int y, int width) {
		painter.fill(x, y, width, 1, LINE);
	}

	/** Überschrift eines Abschnitts. */
	public static void heading(HudPainter painter, String text, int x, int y) {
		painter.text(text, x, y, MUTED);
	}

	/** Schaltfläche; {@code primary} färbt sie im Launcher-Grün. */
	public static void button(HudPainter painter, int x, int y, int width, int height, String label,
							  boolean hovered, boolean primary) {
		int background = primary ? (hovered ? ACCENT : ACCENT_SOFT) : (hovered ? HIGHLIGHT : TRACK);
		rounded(painter, x, y, width, height, 3, background);
		outline(painter, x, y, width, height, 3, primary ? ACCENT : BORDER);
		centered(painter, label, x + width / 2, y + (height - 8) / 2, primary && hovered ? ON_ACCENT : TEXT_DIM);
	}

	/** Kästchen zum Ein- und Ausschalten. */
	public static void checkbox(HudPainter painter, int x, int y, int size, boolean checked, boolean hovered) {
		rounded(painter, x, y, size, size, 2, checked ? ACCENT : TRACK);
		outline(painter, x, y, size, size, 2, checked ? ACCENT : (hovered ? BORDER : LINE));
		if (!checked)
			return;
		// kleiner Haken aus zwei Strichen
		int base = x + size / 2 - 2;
		painter.fill(base, y + size - 5, 2, 2, ON_ACCENT);
		painter.fill(base + 1, y + size - 4, 2, 1, ON_ACCENT);
		painter.fill(base + 2, y + size - 6, 2, 2, ON_ACCENT);
		painter.fill(base + 3, y + size - 7, 2, 2, ON_ACCENT);
	}

	/** Zeile mit Kästchen und Beschriftung. */
	public static void toggleRow(HudPainter painter, int x, int y, int width, int height, String label,
								 boolean checked, boolean hovered) {
		if (hovered)
			rounded(painter, x, y, width, height, 2, HOVER);
		checkbox(painter, x + 1, y + (height - 9) / 2, 9, checked, hovered);
		painter.text(label, x + 14, y + (height - 8) / 2, checked ? TEXT : MUTED);
	}

	/** Zeile zum Durchklicken einer Auswahl, rechts der Wert und ein Farbtupfer. */
	public static void cycleRow(HudPainter painter, int x, int y, int width, int height, String label,
								String value, int swatch, boolean hovered) {
		if (hovered)
			rounded(painter, x, y, width, height, 2, HOVER);
		int textY = y + (height - 8) / 2;
		painter.text(label, x + 2, textY, MUTED);
		int right = x + width - 2;
		if (swatch != 0) {
			int size = 8;
			int swatchY = y + (height - size) / 2;
			rounded(painter, right - size, swatchY, size, size, 2, swatch);
			outline(painter, right - size, swatchY, size, size, 2, BORDER);
			right -= size + 4;
		}
		right(painter, value, right, textY, TEXT_DIM);
	}

	/**
	 * Zeile mit Beschriftung, Regler und Wert – alles nebeneinander, damit das Menü kurz bleibt.
	 *
	 * @param progress 0 bis 1
	 */
	public static void sliderRow(HudPainter painter, int x, int y, int width, int height, String label,
								 String value, float progress, boolean hovered) {
		sliderRow(painter, x, y, width, height, label, value, progress, hovered, LABEL_WIDTH, VALUE_WIDTH);
	}

	/** Wie oben, aber mit eigenen Spaltenbreiten (im Farbwähler ist es enger). */
	public static void sliderRow(HudPainter painter, int x, int y, int width, int height, String label,
								 String value, float progress, boolean hovered, int labelWidth,
								 int valueWidth) {
		int textY = y + (height - 8) / 2;
		painter.text(label, x + 2, textY, MUTED);
		right(painter, value, x + width - 2, textY, TEXT_DIM);
		int[] track = track(x, y, width, height, labelWidth, valueWidth);
		slider(painter, track[0], track[1], track[2], track[3], progress, hovered);
	}

	/** Die Lage des Reglers innerhalb einer {@link #sliderRow} als {x, y, Breite, Höhe}. */
	public static int[] track(int x, int y, int width, int height) {
		return track(x, y, width, height, LABEL_WIDTH, VALUE_WIDTH);
	}

	public static int[] track(int x, int y, int width, int height, int labelWidth, int valueWidth) {
		int left = x + labelWidth;
		int right = x + width - valueWidth;
		return new int[] { left, y + (height - TRACK_HEIGHT) / 2, Math.max(8, right - left), TRACK_HEIGHT };
	}

	/** Nackter Schieberegler ohne Beschriftung. */
	public static void slider(HudPainter painter, int x, int y, int width, int height, float progress,
							   boolean hovered) {
		rounded(painter, x, y, width, height, 2, TRACK);
		outline(painter, x, y, width, height, 2, hovered ? BORDER : LINE);
		int filled = Math.round((width - 2) * Math.max(0.0F, Math.min(1.0F, progress)));
		if (filled > 0)
			painter.fill(x + 1, y + 1, filled, height - 2, hovered ? ACCENT : ACCENT_SOFT);
		int knob = x + 1 + Math.max(0, Math.min(width - 4, filled - 1));
		painter.fill(knob, y - 1, 2, height + 2, TEXT);
	}

	// ---------- Bausteine des großen Menüs (Seitenleiste, Kacheln, Einstellungskarten) ----------

	/** Seitenleiste: etwas dunkler als die Karte, wie die Navigation im Launcher. */
	public static final int SIDEBAR = 0x661A1B1E;
	/** Hintergrund einer Kachel oder Einstellungszeile. */
	public static final int ROW_BACKGROUND = 0x40000000;
	public static final int ROW_BORDER = 0x1AFFFFFF;
	/** Gewählter Eintrag der Seitenleiste (wie im Launcher: Grün, stark zurückgenommen). */
	public static final int NAV_ACTIVE = 0x333BA55C;

	/** Eintrag der Seitenleiste; {@code info} steht rechtsbündig (z.B. "3/5"), darf null sein. */
	public static void nav(HudPainter painter, int x, int y, int width, int height, String label, String info,
						   boolean active, boolean hovered) {
		if (active) {
			rounded(painter, x, y, width, height, 3, NAV_ACTIVE);
			painter.fill(x, y + 3, 2, height - 6, ACCENT);
		} else if (hovered) {
			rounded(painter, x, y, width, height, 3, HOVER);
		}
		int textY = y + (height - 8) / 2;
		painter.text(label, x + 7, textY, active ? TEXT : TEXT_DIM);
		if (info != null)
			right(painter, info, x + width - 5, textY, MUTED);
	}

	/** Kachel bzw. Einstellungszeile: dunkle Fläche mit feinem Rahmen, grün umrandet wenn gewählt. */
	public static void rowCard(HudPainter painter, int x, int y, int width, int height, boolean hovered,
							   boolean selected) {
		rounded(painter, x, y, width, height, 3, ROW_BACKGROUND);
		if (hovered)
			rounded(painter, x, y, width, height, 3, HOVER);
		outline(painter, x, y, width, height, 3, selected ? ACCENT : (hovered ? BORDER : ROW_BORDER));
	}

	/** Schalter wie im Launcher: Pille mit Knopf, grün und rechts wenn an. Immer 18 x 10. */
	public static void toggleSwitch(HudPainter painter, int x, int y, boolean on, boolean hovered) {
		rounded(painter, x, y, 18, 10, 5, on ? ACCENT : 0x33FFFFFF);
		outline(painter, x, y, 18, 10, 5, on ? ACCENT : (hovered ? ACCENT : 0x55FFFFFF));
		rounded(painter, on ? x + 10 : x + 2, y + 2, 6, 6, 3, on ? TEXT : 0xDDFFFFFF);
	}

	/** Kleines Symbol einer Anzeige: Anfangsbuchstabe auf der Farbe ihrer Kategorie. */
	public static void badge(HudPainter painter, int x, int y, int size, String letter, int rgb) {
		rounded(painter, x, y, size, size, 3, 0x55000000 | (rgb & 0xFFFFFF));
		outline(painter, x, y, size, size, 3, 0xAA000000 | (rgb & 0xFFFFFF));
		centered(painter, letter, x + size / 2 + 1, y + (size - 8) / 2 + 1, TEXT);
	}

	/** Suchfeld mit Platzhalter; {@code focus} zeigt den Rahmen in Grün und die Schreibmarke. */
	public static void search(HudPainter painter, int x, int y, int width, int height, String text,
							  boolean focus) {
		rounded(painter, x, y, width, height, 3, 0x66000000);
		outline(painter, x, y, width, height, 3, focus ? ACCENT : BORDER);
		int textY = y + (height - 8) / 2;
		if (text.isEmpty() && !focus) {
			painter.text("Suchen...", x + 5, textY, MUTED);
			return;
		}
		painter.text(text, x + 5, textY, TEXT);
		if (focus)
			painter.fill(x + 6 + painter.textWidth(text), textY - 1, 1, 9, ACCENT);
	}

	/** Auswahlfeld zum Durchklicken: Wert links, Pfeil rechts. */
	public static void dropdown(HudPainter painter, int x, int y, int width, int height, String value,
								boolean hovered) {
		rounded(painter, x, y, width, height, 3, 0x55000000);
		outline(painter, x, y, width, height, 3, hovered ? ACCENT : BORDER);
		int textY = y + (height - 8) / 2;
		painter.text(value, x + 5, textY, TEXT_DIM);
		right(painter, ">", x + width - 5, textY, hovered ? TEXT : MUTED);
	}

	/** Farbfeld mit Rahmen. */
	public static void swatch(HudPainter painter, int x, int y, int width, int height, int argb, boolean hovered) {
		rounded(painter, x, y, width, height, 2, argb);
		outline(painter, x, y, width, height, 2, hovered ? TEXT : BORDER);
	}

	/** Abschnittsüberschrift mit Linie bis zum rechten Rand. */
	public static void section(HudPainter painter, String text, int x, int y, int width) {
		painter.text(text, x, y, TEXT);
		int lineX = x + painter.textWidth(text) + 6;
		if (x + width > lineX)
			painter.fill(lineX, y + 4, x + width - lineX, 1, BORDER);
	}

	public static void centered(HudPainter painter, String text, int centerX, int y, int argb) {
		painter.text(text, centerX - painter.textWidth(text) / 2, y, argb);
	}

	/** Text rechtsbündig an {@code rightX}. */
	public static void right(HudPainter painter, String text, int rightX, int y, int argb) {
		painter.text(text, rightX - painter.textWidth(text), y, argb);
	}

	public static boolean inside(int mouseX, int mouseY, int x, int y, int width, int height) {
		return mouseX >= x && mouseX < x + width && mouseY >= y && mouseY < y + height;
	}
}
