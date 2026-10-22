package de.axoclient.mod.hud;

/**
 * Das Aussehen von Menü und Anzeigen: dieselben Farben wie im Launcher, durchscheinende Flächen
 * mit echt abgerundeten Ecken. Den verschwommenen Hintergrund liefert Minecraft selbst, sobald
 * ein Fenster offen ist – die Flächen hier lassen ihn bewusst durchscheinen.
 */
public final class HudSkin {
	/** Rosa des Launchers; Hauptknöpfe laufen von hier nach {@link #ACCENT_END} (Lila). */
	public static final int ACCENT = 0xFFEC4899;
	public static final int ACCENT_END = 0xFF8B5CF6;
	public static final int ACCENT_SOFT = 0x55EC4899;
	public static final int ACCENT_FAINT = 0x22EC4899;
	/** Helles Rosa für Schrift auf dunklem Rosa. */
	public static final int ACCENT_TEXT = 0xFFF4A5CF;

	public static final int TEXT = 0xFFF2F0F5;
	public static final int TEXT_DIM = 0xFFC9C4D1;
	public static final int MUTED = 0xFF8E889A;
	public static final int ON_ACCENT = 0xFFFFFFFF;

	/** Karten des Menüs: dunkel, aber durchscheinend. */
	public static final int GLASS = 0xF218171C;
	public static final int GLASS_SOFT = 0xB018171C;
	public static final int BORDER = 0xFF2E2B35;
	public static final int HIGHLIGHT = 0x10FFFFFF;
	public static final int HOVER = 0x14FFFFFF;
	public static final int LINE = 0xFF2A2730;
	public static final int TRACK = 0xFF2A2830;

	/** Leichter Schleier über dem Spiel; der Rest der Tiefe kommt vom Weichzeichner. */
	public static final int SCRIM = 0x9E0A080E;
	/** Rasterlinien und Hilfslinien beim Verschieben. */
	public static final int GRID_LINE = 0x18FFFFFF;
	public static final int GUIDE_LINE = 0xCCEC4899;

	/** Eckenradius der Menükarten. */
	public static final int CARD_RADIUS = 7;

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
			vline(painter, x + 5 + painter.textWidth(text), textY - 1, 9, ACCENT);
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
		float p = painter.pixelScale();
		if (p > 1.01F) {
			// Fein gezeichnet: Mitte und Ränder als Flächen, die Ecken je Bildschirmpixel mit weicher Kante
			painter.fill(x, y + r, width, height - 2 * r, argb);
			painter.fill(x + r, y, width - 2 * r, r, argb);
			painter.fill(x + r, y + height - r, width - 2 * r, r, argb);
			corner(painter, x, y, r, p, argb, false, false);
			corner(painter, x + width - r, y, r, p, argb, true, false);
			corner(painter, x, y + height - r, r, p, argb, false, true);
			corner(painter, x + width - r, y + height - r, r, p, argb, true, true);
			return;
		}
		painter.fill(x, y + r, width, height - 2 * r, argb);
		for (int row = 0; row < r; row++) {
			int inset = inset(r, row);
			painter.fill(x + inset, y + row, width - 2 * inset, 1, argb);
			painter.fill(x + inset, y + height - 1 - row, width - 2 * inset, 1, argb);
		}
	}

	/**
	 * Gefüllter Viertelkreis mit Radius r (Einheiten) im Quadrat ab (cx, cy), gezeichnet je Bildschirmpixel: Randpixel
	 * bekommen nur so viel Deckkraft, wie der Kreis sie bedeckt.
	 */
	private static void corner(HudPainter painter, int cx, int cy, int r, float p, int argb, boolean right, boolean bottom) {
		float radius = r * p;
		int size = (int) Math.ceil(radius);
		painter.push(cx, cy, 1.0F / p);
		for (int j = 0; j < size; j++) {
			float dy = Math.max(0.0F, radius - (j + 0.5F));
			float start = radius - (float) Math.sqrt(Math.max(0.0F, radius * radius - dy * dy));
			int solid = Math.min(size, (int) Math.ceil(start));
			int row = cy + (bottom ? size - 1 - j : j);
			if (solid < size)
				painter.fill(right ? cx : cx + solid, row, size - solid, 1, argb);
			float coverage = solid - start;
			if (solid > 0 && coverage > 0.03F)
				painter.fill(right ? cx + size - solid : cx + solid - 1, row, 1, 1, alpha(argb, coverage));
		}
		painter.pop();
	}

	/** Rahmen als feine Linie von einem Bildschirmpixel, die Ecken als geglätteter Bogen. */
	private static void fineOutline(HudPainter painter, int x, int y, int width, int height, int r, float p, int argb) {
		int w = Math.round(width * p);
		int h = Math.round(height * p);
		float radius = r * p;
		int size = Math.min((int) Math.ceil(radius), Math.min(w, h) / 2);
		painter.push(x, y, 1.0F / p);
		if (w > 2 * size) {
			painter.fill(x + size, y, w - 2 * size, 1, argb);
			painter.fill(x + size, y + h - 1, w - 2 * size, 1, argb);
		}
		if (h > 2 * size) {
			painter.fill(x, y + size, 1, h - 2 * size, argb);
			painter.fill(x + w - 1, y + size, 1, h - 2 * size, argb);
		}
		for (int j = 0; j < size; j++) {
			for (int i = 0; i < size; i++) {
				float dx = radius - (i + 0.5F);
				float dy = radius - (j + 0.5F);
				float d = (float) Math.sqrt(dx * dx + dy * dy);
				float coverage = clamp01(radius - d + 0.5F) - clamp01(radius - 1.0F - d + 0.5F);
				if (coverage <= 0.03F)
					continue;
				int color = alpha(argb, coverage);
				painter.fill(x + i, y + j, 1, 1, color);
				painter.fill(x + w - 1 - i, y + j, 1, 1, color);
				painter.fill(x + i, y + h - 1 - j, 1, 1, color);
				painter.fill(x + w - 1 - i, y + h - 1 - j, 1, 1, color);
			}
		}
		painter.pop();
	}

	private static float clamp01(float value) {
		return Math.max(0.0F, Math.min(1.0F, value));
	}

	/** Farbe mit anteiliger Deckkraft. */
	private static int alpha(int argb, float factor) {
		int a = Math.round(((argb >>> 24) & 0xFF) * Math.max(0.0F, Math.min(1.0F, factor)));
		return (a << 24) | (argb & 0xFFFFFF);
	}

	/** Rahmen mit frei wählbarem Eckenradius. */
	public static void outline(HudPainter painter, int x, int y, int width, int height, int radius, int argb) {
		if (width <= 0 || height <= 0)
			return;
		float p = painter.pixelScale();
		if (p > 1.01F) {
			fineOutline(painter, x, y, width, height, radius(radius, width, height), p, argb);
			return;
		}
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
		hairline(painter, x + CARD_RADIUS, y, width - 2 * CARD_RADIUS, 1, HIGHLIGHT);
	}

	/** Trennstrich innerhalb einer Karte. */
	public static void separator(HudPainter painter, int x, int y, int width) {
		hairline(painter, x, y, width, 0, LINE);
	}

	/**
	 * Waagerechte Linie, nur einen Bildschirmpixel dick (wie im Launcher), {@code offset} Bildschirmpixel unter y. Auf
	 * grobem Raster ein Menüpixel.
	 */
	public static void hairline(HudPainter painter, int x, int y, int width, int offset, int argb) {
		float p = painter.pixelScale();
		if (p <= 1.01F) {
			painter.fill(x, y + offset, width, 1, argb);
			return;
		}
		painter.push(x, y, 1.0F / p);
		painter.fill(x, y + offset, Math.round(width * p), 1, argb);
		painter.pop();
	}

	/** Senkrechte Linie, einen Bildschirmpixel breit. */
	public static void vline(HudPainter painter, int x, int y, int height, int argb) {
		float p = painter.pixelScale();
		if (p <= 1.01F) {
			painter.fill(x, y, 1, height, argb);
			return;
		}
		painter.push(x, y, 1.0F / p);
		painter.fill(x, y, 1, Math.round(height * p), argb);
		painter.pop();
	}

	/** Feiner Winkelpfeil wie im Launcher, mittig um (cx, cy), {@code size} hoch; zeigt nach links oder rechts. */
	public static void chevron(HudPainter painter, float cx, float cy, float size, boolean left, int argb) {
		if (painter.pixelScale() <= 1.01F) {
			centered(painter, left ? "<" : ">", Math.round(cx), Math.round(cy) - 4, argb);
			return;
		}
		float half = size / 2.0F;
		float depth = half * 0.55F;
		float tip = left ? cx - depth / 2.0F : cx + depth / 2.0F;
		float back = left ? cx + depth / 2.0F : cx - depth / 2.0F;
		stroke(painter, Math.max(0.6F, size * 0.13F), argb, back, cy - half, tip, cy, back, cy + half);
	}

	/**
	 * Geglätteter Linienzug durch die Punkte (Menüeinheiten, abwechselnd x und y), {@code thickness} in Menüeinheiten;
	 * gezeichnet je Bildschirmpixel mit weicher Kante.
	 */
	private static void stroke(HudPainter painter, float thickness, int argb, float... points) {
		float p = painter.pixelScale();
		float minX = Float.MAX_VALUE, minY = Float.MAX_VALUE, maxX = -Float.MAX_VALUE, maxY = -Float.MAX_VALUE;
		for (int i = 0; i < points.length; i += 2) {
			minX = Math.min(minX, points[i]);
			maxX = Math.max(maxX, points[i]);
			minY = Math.min(minY, points[i + 1]);
			maxY = Math.max(maxY, points[i + 1]);
		}
		int bx = (int) Math.floor(minX - thickness);
		int by = (int) Math.floor(minY - thickness);
		int w = (int) Math.ceil((maxX + thickness - bx) * p);
		int h = (int) Math.ceil((maxY + thickness - by) * p);
		float half = thickness * p / 2.0F;
		painter.push(bx, by, 1.0F / p);
		for (int j = 0; j < h; j++) {
			for (int i = 0; i < w; i++) {
				float px = i + 0.5F, py = j + 0.5F;
				float best = Float.MAX_VALUE;
				for (int k = 0; k + 3 < points.length; k += 2) {
					float ax = (points[k] - bx) * p, ay = (points[k + 1] - by) * p;
					float dx = (points[k + 2] - bx) * p - ax, dy = (points[k + 3] - by) * p - ay;
					float t = clamp01(((px - ax) * dx + (py - ay) * dy) / Math.max(1e-6F, dx * dx + dy * dy));
					float ex = px - (ax + t * dx), ey = py - (ay + t * dy);
					best = Math.min(best, (float) Math.sqrt(ex * ex + ey * ey));
				}
				float coverage = clamp01(half - best + 0.5F);
				if (coverage > 0.03F)
					painter.fill(bx + i, by + j, 1, 1, alpha(argb, coverage));
			}
		}
		painter.pop();
	}

	/** Überschrift eines Abschnitts. */
	public static void heading(HudPainter painter, String text, int x, int y) {
		painter.text(text, x, y, MUTED);
	}

	/** Schaltfläche; {@code primary} bekommt den Verlauf Rosa → Lila wie die Hauptknöpfe im Launcher. */
	public static void button(HudPainter painter, int x, int y, int width, int height, String label,
							  boolean hovered, boolean primary) {
		if (primary) {
			gradient(painter, x, y, width, height, 4, hovered ? lighten(ACCENT) : ACCENT,
				hovered ? lighten(ACCENT_END) : ACCENT_END);
			centered(painter, label, x + width / 2, y + (height - 8) / 2, ON_ACCENT);
			return;
		}
		rounded(painter, x, y, width, height, 4, hovered ? 0xFF35323D : TRACK);
		outline(painter, x, y, width, height, 4, hovered ? 0xFF4A4552 : 0xFF3A3642);
		centered(painter, label, x + width / 2, y + (height - 8) / 2, hovered ? TEXT : TEXT_DIM);
	}

	/** Abgerundete Fläche mit waagerechtem Farbverlauf von {@code from} nach {@code to}. */
	public static void gradient(HudPainter painter, int x, int y, int width, int height, int radius, int from, int to) {
		if (width <= 0 || height <= 0)
			return;
		int r = radius(radius, width, height);
		float p = painter.pixelScale();
		if (p > 1.01F && r > 0) {
			painter.fill(x, y + r, r, height - 2 * r, from);
			painter.fill(x + width - r, y + r, r, height - 2 * r, to);
			for (int column = r; column < width - r; column++)
				painter.fill(x + column, y, 1, height, mix(from, to, column / (float) Math.max(1, width - 1)));
			corner(painter, x, y, r, p, from, false, false);
			corner(painter, x, y + height - r, r, p, from, false, true);
			corner(painter, x + width - r, y, r, p, to, true, false);
			corner(painter, x + width - r, y + height - r, r, p, to, true, true);
			return;
		}
		for (int column = 0; column < width; column++) {
			int edge = Math.min(column, width - 1 - column);
			int inset = edge < r ? inset(r, edge) : 0;
			painter.fill(x + column, y + inset, 1, height - 2 * inset, mix(from, to, width == 1 ? 0 : column / (float) (width - 1)));
		}
	}

	/** Mischt zwei Farben (samt Deckkraft); {@code at} 0 = erste, 1 = zweite. */
	public static int mix(int from, int to, float at) {
		int result = 0;
		for (int shift = 0; shift <= 24; shift += 8) {
			int a = (from >>> shift) & 0xFF;
			int b = (to >>> shift) & 0xFF;
			result |= Math.round(a + (b - a) * at) << shift;
		}
		return result;
	}

	private static int lighten(int argb) {
		return mix(argb, 0xFFFFFFFF, 0.12F);
	}

	/** Kästchen zum Ein- und Ausschalten. */
	public static void checkbox(HudPainter painter, int x, int y, int size, boolean checked, boolean hovered) {
		rounded(painter, x, y, size, size, 2, checked ? ACCENT : TRACK);
		outline(painter, x, y, size, size, 2, checked ? ACCENT : (hovered ? BORDER : LINE));
		if (!checked)
			return;
		if (painter.pixelScale() > 1.01F) {
			// Haken als geglätteter Strich
			stroke(painter, size * 0.16F, ON_ACCENT, x + size * 0.25F, y + size * 0.52F, x + size * 0.43F,
				y + size * 0.7F, x + size * 0.76F, y + size * 0.32F);
			return;
		}
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
		// Wie im Launcher: runde Schiene, gefüllter Teil im Verlauf, runder weißer Knopf
		int r = height / 2;
		rounded(painter, x, y, width, height, r, hovered ? 0xFF35323D : TRACK);
		int filled = Math.round(width * Math.max(0.0F, Math.min(1.0F, progress)));
		if (filled > 0)
			gradient(painter, x, y, Math.max(height, filled), height, r, ACCENT, hovered ? lighten(ACCENT_END) : ACCENT_END);
		int knob = height + 4;
		int knobX = Math.max(x - 1, Math.min(x + width - knob + 1, x + filled - knob / 2));
		rounded(painter, knobX, y - 2, knob, knob, knob / 2, TEXT);
	}

	// ---------- Bausteine des großen Menüs (Seitenleiste, Kacheln, Einstellungskarten) ----------

	/** Seitenleiste: etwas dunkler als die Karte, wie die Navigation im Launcher. */
	public static final int SIDEBAR = 0xFF121116;
	/** Hintergrund einer Kachel oder Einstellungszeile. */
	public static final int ROW_BACKGROUND = 0xFF222027;
	public static final int ROW_BORDER = 0xFF2E2B35;
	/** Gewählter Eintrag der Seitenleiste (wie im Launcher: Grün, stark zurückgenommen). */
	public static final int NAV_ACTIVE = 0xFF2A1D2C;

	/** Eintrag der Seitenleiste; {@code info} steht rechtsbündig (z.B. "3/5"), darf null sein. */
	public static void nav(HudPainter painter, int x, int y, int width, int height, String label, String info,
						   boolean active, boolean hovered) {
		if (active) {
			rounded(painter, x, y, width, height, 4, NAV_ACTIVE);
			rounded(painter, x, y + 3, 2, height - 6, 1, ACCENT);
		} else if (hovered) {
			rounded(painter, x, y, width, height, 4, 0xFF221F28);
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
		centered(painter, letter, x + size / 2, y + (size - 8) / 2 + 1, TEXT);
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
			vline(painter, x + 6 + painter.textWidth(text), textY - 1, 9, ACCENT);
	}

	/** Auswahlfeld zum Durchklicken: Wert links, Pfeil rechts. */
	public static void dropdown(HudPainter painter, int x, int y, int width, int height, String value,
								boolean hovered) {
		rounded(painter, x, y, width, height, 3, 0x55000000);
		outline(painter, x, y, width, height, 3, hovered ? ACCENT : BORDER);
		int textY = y + (height - 8) / 2;
		painter.text(value, x + 5, textY, TEXT_DIM);
		chevron(painter, x + width - 8, y + height / 2.0F, 6, false, hovered ? TEXT : MUTED);
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
			hairline(painter, lineX, y + 4, x + width - lineX, 0, BORDER);
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
