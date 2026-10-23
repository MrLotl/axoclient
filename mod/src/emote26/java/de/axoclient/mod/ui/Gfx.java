package de.axoclient.mod.ui;

import de.axoclient.mod.hud.HudSkin;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import org.joml.Matrix3x2fStack;

/**
 * Zeichnen in "Design-Pixeln": dieselben Maße wie im Mockup (1280 × 800), damit das Menü immer gleich viel Platz
 * einnimmt. Das Aussehen ist Minecraft: Schrift, Symbole, Ecken und Rahmen bestehen aus "Minecraft-Pixeln"
 * ({@link #mc} Bildschirmpunkte groß, immer eine ganze Zahl). So bleibt alles pixelgenau scharf – die Schrift wird nur
 * in ganzen Vielfachen vergrößert, Ecken sind Pixel-Treppen statt weicher Bögen, Rahmen und Linien einen
 * Minecraft-Pixel dick.
 * <p>
 * Flächen, Rahmen, Verläufe und Symbole sind schlichte Rechtecke derselben Art; Minecraft fasst sie zu wenigen
 * Zeichenaufrufen zusammen.
 */
public final class Gfx {
	public static final float REF_WIDTH = 1280;
	public static final float REF_HEIGHT = 800;

	public static final int REGULAR = 400;
	public static final int MEDIUM = 500;
	public static final int SEMIBOLD = 600;
	public static final int BOLD = 700;

	public final GuiGraphicsExtractor g;
	/** GUI-Einheiten je Design-Pixel (für Mauspositionen). */
	public final float scale;
	/** Bildschirmpunkte je Design-Pixel. */
	public final float px;
	/** Bildschirmpunkte je Minecraft-Pixel (Schrift, Symbole, Ecken, Rahmen); immer ganzzahlig. */
	public final int mc;
	/** Lage des Design-Ursprungs in Bildschirmpunkten. */
	private final float ox;
	private final float oy;
	/** Sichtbare Fläche in Design-Pixeln. */
	public final int width;
	public final int height;

	private Gfx(GuiGraphicsExtractor g, float scale, float px, float ox, float oy, int width, int height) {
		this.g = g;
		this.scale = scale;
		this.px = px;
		this.mc = Math.max(1, Math.round(px * 1.5F));
		this.ox = ox;
		this.oy = oy;
		this.width = width;
		this.height = height;
	}

	private static float guiScale() {
		return (float) Minecraft.getInstance().getWindow().getGuiScale();
	}

	/** Maßstab für eine Fläche von guiWidth × guiHeight GUI-Einheiten. */
	public static float scaleFor(int guiWidth, int guiHeight) {
		return Math.min(guiWidth / REF_WIDTH, guiHeight / REF_HEIGHT);
	}

	/** Beginnt das Zeichnen (die Pose rechnet ab hier in Bildschirmpunkten); am Ende {@link #end()}. */
	public static Gfx begin(GuiGraphicsExtractor g, int guiWidth, int guiHeight) {
		float scale = scaleFor(guiWidth, guiHeight);
		float gui = guiScale();
		g.pose().pushMatrix();
		g.pose().scale(1.0F / gui, 1.0F / gui);
		return new Gfx(g, scale, gui * scale, 0, 0, Math.round(guiWidth / scale), Math.round(guiHeight / scale));
	}

	/** Nur zum Messen (Klicks): kein Zeichnen. */
	public static Gfx measure(int guiWidth, int guiHeight) {
		float scale = scaleFor(guiWidth, guiHeight);
		return new Gfx(null, scale, guiScale() * scale, 0, 0, Math.round(guiWidth / scale), Math.round(guiHeight / scale));
	}

	/** Dasselbe Zeichnen, aber ab (x, y) und um zoom vergrößert (z. B. Einblendungen in ihrer eingestellten Größe). */
	public Gfx at(float x, float y, float zoom) {
		return new Gfx(g, scale * zoom, px * zoom, X(x), Y(y), width, height);
	}

	public void end() {
		if (g != null)
			g.pose().popMatrix();
	}

	/** Mausposition (GUI-Einheiten) in Design-Pixeln. */
	public int mx(double guiX) {
		return (int) Math.floor(guiX / scale);
	}

	public int my(double guiY) {
		return (int) Math.floor(guiY / scale);
	}

	private Matrix3x2fStack pose() {
		return g.pose();
	}

	/** Design-Koordinate → ganzer Bildschirmpunkt. */
	private int X(float x) {
		return Math.round(ox + x * px);
	}

	private int Y(float y) {
		return Math.round(oy + y * px);
	}

	// ---------- Pixel-Ecken (alles in Bildschirmpunkten) ----------

	/**
	 * Eckenradius in Minecraft-Pixeln für ein Rechteck von wp × hp Punkten. Kreise und Pillen (Radius ≥ halbe Seite)
	 * werden ganz rund; sonst wird der Radius aus dem Mockup auf wenige Pixelstufen verkleinert, wie bei Minecraft.
	 */
	private int steps(int radius, int w, int h, int wp, int hp) {
		if (radius <= 0)
			return 0;
		int full = Math.min(wp, hp) / (2 * mc);
		if (radius * 2 >= Math.min(w, h))
			return full;
		return Math.min(full, Math.max(1, Math.round(radius * px / (2.0F * mc))));
	}

	private void rectPx(int x0, int y0, int x1, int y1, int r, int argb) {
		if (r <= 0) {
			g.fill(x0, y0, x1, y1, argb);
			return;
		}
		int u = mc;
		g.fill(x0, y0 + r * u, x1, y1 - r * u, argb);
		for (int row = 0; row < r; row++) {
			int in = HudSkin.inset(r, row) * u;
			g.fill(x0 + in, y0 + row * u, x1 - in, y0 + (row + 1) * u, argb);
			g.fill(x0 + in, y1 - (row + 1) * u, x1 - in, y1 - row * u, argb);
		}
	}

	/** Rahmen einen Minecraft-Pixel dick; kein Punkt doppelt (wichtig bei durchscheinenden Farben). */
	private void outlinePx(int x0, int y0, int x1, int y1, int r, int argb) {
		int u = mc;
		if (x1 - x0 < 2 * u || y1 - y0 < 2 * u) {
			g.fill(x0, y0, x1, y1, argb);
			return;
		}
		if (r <= 0) {
			g.fill(x0, y0, x1, y0 + u, argb);
			g.fill(x0, y1 - u, x1, y1, argb);
			g.fill(x0, y0 + u, x0 + u, y1 - u, argb);
			g.fill(x1 - u, y0 + u, x1, y1 - u, argb);
			return;
		}
		for (int row = 0; row < r; row++) {
			int in = HudSkin.inset(r, row);
			if (row == 0) {
				g.fill(x0 + in * u, y0, x1 - in * u, y0 + u, argb);
				g.fill(x0 + in * u, y1 - u, x1 - in * u, y1, argb);
				continue;
			}
			int run = Math.max(1, HudSkin.inset(r, row - 1) - in);
			int top = y0 + row * u, bottom = y1 - (row + 1) * u;
			g.fill(x0 + in * u, top, x0 + (in + run) * u, top + u, argb);
			g.fill(x1 - (in + run) * u, top, x1 - in * u, top + u, argb);
			g.fill(x0 + in * u, bottom, x0 + (in + run) * u, bottom + u, argb);
			g.fill(x1 - (in + run) * u, bottom, x1 - in * u, bottom + u, argb);
		}
		g.fill(x0, y0 + r * u, x0 + u, y1 - r * u, argb);
		g.fill(x1 - u, y0 + r * u, x1, y1 - r * u, argb);
	}

	/** Deckt außerhalb der Pixel-Ecken mit bg ab (für Bilder, die abgerundet aussehen sollen). */
	private void maskPx(int x0, int y0, int x1, int y1, int r, int bg) {
		int u = mc;
		for (int row = 0; row < r; row++) {
			int in = HudSkin.inset(r, row) * u;
			if (in == 0)
				continue;
			g.fill(x0, y0 + row * u, x0 + in, y0 + (row + 1) * u, bg);
			g.fill(x1 - in, y0 + row * u, x1, y0 + (row + 1) * u, bg);
			g.fill(x0, y1 - (row + 1) * u, x0 + in, y1 - row * u, bg);
			g.fill(x1 - in, y1 - (row + 1) * u, x1, y1 - row * u, bg);
		}
	}

	/** Wie viele Pixelzeilen oben in Spalte column (von außen gezählt) einer Ecke mit Radius r fehlen. */
	private static int cutRows(int r, int column) {
		int rows = 0;
		while (rows < r && HudSkin.inset(r, rows) > column)
			rows++;
		return rows;
	}

	// ---------- Flächen ----------

	public void fill(int x, int y, int w, int h, int argb) {
		if (g != null && w > 0 && h > 0)
			g.fill(X(x), Y(y), X(x + w), Y(y + h), argb);
	}

	public void rect(int x, int y, int w, int h, int radius, int argb) {
		if (g == null || w <= 0 || h <= 0 || (argb >>> 24) == 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		if (x1 <= x0 || y1 <= y0)
			return;
		rectPx(x0, y0, x1, y1, steps(radius, w, h, x1 - x0, y1 - y0), argb);
	}

	/** Rahmen, einen Minecraft-Pixel dick, innen an der Kante. */
	public void outline(int x, int y, int w, int h, int radius, int argb) {
		if (g == null || w <= 0 || h <= 0 || (argb >>> 24) == 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		if (x1 <= x0 || y1 <= y0)
			return;
		outlinePx(x0, y0, x1, y1, steps(radius, w, h, x1 - x0, y1 - y0), argb);
	}

	/** Fläche mit Rahmen nach innen. */
	public void box(int x, int y, int w, int h, int radius, int fill, int line) {
		rect(x, y, w, h, radius, fill);
		outline(x, y, w, h, radius, line);
	}

	/** Akzent-Verlauf (Pink → Lila) wie die Hauptknöpfe. */
	public void accent(int x, int y, int w, int h, int radius) {
		gradient(x, y, w, h, radius, Theme.ACCENT_FROM, Theme.ACCENT_TO);
	}

	/** Waagerechter Verlauf von from nach to, in Streifen von einem Minecraft-Pixel; Ecken als Pixel-Treppen. */
	public void gradient(int x, int y, int w, int h, int radius, int from, int to) {
		if (g == null || w <= 0 || h <= 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		int wp = x1 - x0, hp = y1 - y0;
		if (wp <= 0 || hp <= 0)
			return;
		int r = steps(radius, w, h, wp, hp);
		int u = mc;
		int corner = r * u;
		int step = Math.max(u, (wp + 47) / 48 / u * u);
		for (int a = x0; a < x1; ) {
			int fromLeft = a - x0, fromRight = x1 - a;
			int b;
			int cut;
			if (fromLeft < corner) {
				b = Math.min(x0 + corner, a + u);
				cut = cutRows(r, fromLeft / u);
			} else if (a >= x1 - corner) {
				b = Math.min(x1, a + u);
				cut = cutRows(r, (fromRight - 1) / u);
			} else {
				b = Math.min(x1 - corner, a + step);
				cut = 0;
			}
			int c = Theme.mix(from, to, ((a + b) / 2.0F - x0) / wp);
			g.fill(a, y0 + cut * u, b, y1 - cut * u, c);
			a = b;
		}
	}

	/** Harter Schatten wie in Minecraft: dieselbe Form, um einen Minecraft-Pixel nach rechts unten versetzt. */
	public void shadow(int x, int y, int w, int h, int radius, int spread, int offsetY, float strength) {
		if (g == null || w <= 0 || h <= 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		if (x1 <= x0 || y1 <= y0)
			return;
		int d = offsetY >= 12 ? 2 * mc : mc;
		int c = Theme.alpha(0xFF000000, Math.min(0.55F, strength * 0.8F));
		rectPx(x0 + mc, y0 + d, x1 + mc, y1 + d, steps(radius, w, h, x1 - x0, y1 - y0), c);
	}

	/** Waagerechte Linie, einen Minecraft-Pixel dick. */
	public void hline(int x, int y, int w, int argb) {
		if (g != null)
			g.fill(X(x), Y(y), X(x + w), Y(y) + mc, argb);
	}

	public void vline(int x, int y, int h, int argb) {
		if (g != null)
			g.fill(X(x), Y(y), X(x) + mc, Y(y + h), argb);
	}

	/** Runder Punkt (Status), als kleiner Pixel-Kreis. */
	public void dot(int cx, int cy, int diameter, int argb) {
		rect(cx - diameter / 2, cy - diameter / 2, diameter, diameter, diameter, argb);
	}

	public void clip(int x, int y, int w, int h) {
		if (g != null)
			g.enableScissor(X(x), Y(y), X(x + w), Y(y + h));
	}

	public void unclip() {
		if (g != null)
			g.disableScissor();
	}

	// ---------- Schrift (Minecraft) ----------

	private static Font font() {
		return Minecraft.getInstance().font;
	}

	/** Vergrößerung der Minecraft-Schrift (ganzzahlig): normal ein Minecraft-Pixel, Titel etwas größer. */
	private int zoom(float size) {
		return size >= 19 ? Math.max(mc + 1, Math.round(mc * 1.5F)) : mc;
	}

	private static Component styled(String text, int weight) {
		Component plain = Component.literal(text);
		return weight >= BOLD ? Component.literal(text).withStyle(style -> style.withBold(true)) : plain;
	}

	/** Breite in Design-Pixeln. */
	public float width(String text, float size, int weight) {
		if (text == null || text.isEmpty())
			return 0;
		return font().width(styled(text, weight)) * zoom(size) / px;
	}

	/** Breite in Design-Pixeln; Sperrung gibt es in der Pixelschrift nicht. */
	public float width(String text, float size, int weight, float tracking) {
		return width(text, size, weight);
	}

	/** Text mit der Oberkante seines Schriftgrads bei y. */
	public void text(String text, float x, float y, float size, int weight, int argb) {
		textMid(text, x, y + size * 0.47F, size, weight, argb);
	}

	/** Text senkrecht mittig auf cy (Großbuchstaben mittig). */
	public void textMid(String text, float x, float cy, float size, int weight, int argb) {
		if (g == null || text == null || text.isEmpty() || (argb >>> 24) == 0)
			return;
		draw(styled(text, weight), X(x), cy, zoom(size), argb);
	}

	/** Wie textMid; die Sperrung entfällt in der Pixelschrift (sie ist schon weit genug). */
	public void textMid(String text, float x, float cy, float size, int weight, int argb, float tracking) {
		textMid(text, x, cy, size, weight, argb);
	}

	public void textCentered(String text, float cx, float cy, float size, int weight, int argb) {
		textMid(text, cx - width(text, size, weight) / 2, cy, size, weight, argb);
	}

	public void textRight(String text, float right, float cy, float size, int weight, int argb) {
		textMid(text, right - width(text, size, weight), cy, size, weight, argb);
	}

	/** Zeichnet die Zeile bei left (Bildschirmpunkte), Großbuchstaben (7 Pixel hoch) mittig auf cy, mit Schatten. */
	private void draw(Component text, int left, float cy, int k, int argb) {
		int top = Math.round(oy + cy * px - 3.5F * k);
		pose().pushMatrix();
		pose().translate(left, top);
		pose().scale(k, k);
		g.text(font(), text, 0, 0, argb, true);
		pose().popMatrix();
	}

	/** Text im Akzent-Verlauf, Zeichen für Zeichen eingefärbt; gibt die Breite zurück. */
	public float gradientText(String text, float x, float cy, float size, int weight) {
		float total = width(text, size, weight);
		if (g == null || text == null || text.isEmpty())
			return total;
		int k = zoom(size);
		int left = X(x);
		int at = 0;
		int all = Math.max(1, font().width(styled(text, weight)));
		for (int i = 0; i < text.length(); ) {
			int cp = text.codePointAt(i);
			String c = new String(Character.toChars(cp));
			i += Character.charCount(cp);
			int w = font().width(styled(c, weight));
			int color = Theme.mix(Theme.TEXT_FROM, Theme.TEXT_TO, (at + w / 2.0F) / all);
			draw(styled(c, weight), left + at * k, cy, k, color);
			at += w;
		}
		return total;
	}

	/** Kürzt den Text mit "…", bis er in maxWidth passt. */
	public String ellipsize(String text, float size, int weight, float maxWidth) {
		if (text == null)
			return "";
		if (width(text, size, weight) <= maxWidth)
			return text;
		// Längsten passenden Anfang suchen (in ganzen Zeichen; die Breite wächst mit der Länge)
		int lo = 1, hi = text.codePointCount(0, text.length()) - 1;
		while (lo < hi) {
			int m = (lo + hi + 1) >>> 1;
			if (width(text.substring(0, text.offsetByCodePoints(0, m)) + "…", size, weight) <= maxWidth)
				lo = m;
			else
				hi = m - 1;
		}
		String cut = text.substring(0, text.offsetByCodePoints(0, Math.max(1, lo))).stripTrailing();
		return (cut.isEmpty() ? text.substring(0, text.offsetByCodePoints(0, 1)) : cut) + "…";
	}

	// ---------- Bilder ----------

	/** Pixel-Symbol, mittig in size × size Design-Pixeln, in ganzen Vielfachen vergrößert. */
	public void icon(Icons.Icon icon, float x, float y, float size, int argb) {
		if (g == null)
			return;
		int n = Icons.grid(icon);
		int s = Math.max(1, Math.round(size * px / n - 0.1F));
		int total = n * s;
		int x0 = Math.round(ox + (x + size / 2) * px - total / 2.0F);
		int y0 = Math.round(oy + (y + size / 2) * px - total / 2.0F);
		boolean[] on = Icons.pixels(icon);
		for (int row = 0; row < n; row++) {
			int start = -1;
			for (int col = 0; col <= n; col++) {
				boolean set = col < n && on[row * n + col];
				if (set && start < 0)
					start = col;
				else if (!set && start >= 0) {
					g.fill(x0 + start * s, y0 + row * s, x0 + col * s, y0 + (row + 1) * s, argb);
					start = -1;
				}
			}
		}
	}

	/** Ausschnitt einer Textur in ein Rechteck (Design-Pixel), z. B. Gesicht aus dem Skin. */
	public void image(Identifier id, float x, float y, float w, float h, float u, float v, int regionW, int regionH, int texW, int texH,
					  int argb) {
		if (g == null || id == null)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		if (x1 <= x0 || y1 <= y0)
			return;
		blitScaled(id, x0, y0, (x1 - x0) / (float) regionW, (y1 - y0) / (float) regionH, u, v, regionW, regionH, texW, texH, argb);
	}

	private void blitScaled(Identifier id, int x0, int y0, float sx, float sy, float u, float v, int regionW, int regionH, int texW,
							int texH, int argb) {
		pose().pushMatrix();
		pose().translate(x0, y0);
		pose().scale(sx, sy);
		g.blit(RenderPipelines.GUI_TEXTURED, id, 0, 0, u, v, regionW, regionH, regionW, regionH, texW, texH, argb);
		pose().popMatrix();
	}

	/**
	 * Kopf eines Spielers aus seinem Skin (Gesicht und Hut), jedes Skin-Pixel gleich groß (ganzzahlig vergrößert), mit
	 * Pixel-Ecken auf der Hintergrundfarbe bg. Ohne Skin ein neutrales Feld.
	 */
	public void head(Identifier skin, int x, int y, int size, int radius, int bg, boolean dim) {
		if (g == null)
			return;
		int t = Math.max(1, Math.round(size * px / 8));
		int side = 8 * t;
		int x0 = Math.round(ox + (x + size / 2.0F) * px - side / 2.0F);
		int y0 = Math.round(oy + (y + size / 2.0F) * px - side / 2.0F);
		int r = steps(radius, size, size, side, side);
		if (skin == null) {
			rectPx(x0, y0, x0 + side, y0 + side, r, 0xFF3A3A3A);
			return;
		}
		int tint = dim ? 0xB38C8C8C : 0xFFFFFFFF;
		blitScaled(skin, x0, y0, t, t, 8, 8, 8, 8, 64, 64, tint);
		blitScaled(skin, x0, y0, t, t, 40, 8, 8, 8, 64, 64, tint);
		maskPx(x0, y0, x0 + side, y0 + side, r, bg);
	}

	/** Deckt die Ecken eines Rechtecks mit der Hintergrundfarbe ab, damit es (pixelig) abgerundet aussieht. */
	public void cornerMask(int x, int y, int w, int h, int radius, int bg) {
		if (g == null || radius <= 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		maskPx(x0, y0, x1, y1, steps(radius, w, h, x1 - x0, y1 - y0), bg);
	}

	// ---------- Bausteine ----------

	/** Taste wie &lt;kbd&gt;: dunkles Feld mit Text. Gibt die Breite zurück. */
	public int kbd(String label, int x, int cy, int height, float size, int bg, int fg) {
		int w = Math.max(height, Math.round(width(label, size, SEMIBOLD)) + 12);
		rect(x, cy - height / 2, w, height, 5, bg);
		textCentered(label, x + w / 2.0F, cy, size, SEMIBOLD, fg);
		return w;
	}

	/** Schalter (36 × 20) wie im Mockup. */
	public void toggle(int x, int y, boolean on) {
		if (on)
			accent(x, y, 36, 20, 10);
		else
			rect(x, y, 36, 20, 10, 0xFF4A4A4A);
		int knob = on ? x + 18 : x + 2;
		rect(knob, y + 2, 16, 16, 8, Theme.WHITE);
	}

	/** Segment-Knopf (gewählt = zarter Akzent mit Rahmen). */
	public void segment(String label, Icons.Icon icon, int x, int y, int w, int h, int radius, boolean on, float size) {
		if (on) {
			gradient(x, y, w, h, radius, Theme.alpha(0xFFEC4899, 0.22F), Theme.alpha(0xFF8B5CF6, 0.22F));
			outline(x, y, w, h, radius, Theme.alpha(0xFFD68CE6, 0.30F));
		}
		int color = on ? Theme.ACCENT_TEXT : Theme.MUTED_2;
		int weight = on ? SEMIBOLD : MEDIUM;
		float tw = width(label, size, weight);
		float iw = icon == null ? 0 : 14 + 6;
		float start = x + (w - tw - iw) / 2.0F;
		if (icon != null)
			icon(icon, start, y + (h - 14) / 2.0F, 14, color);
		textMid(label, start + iw, y + h / 2.0F, size, weight, color);
	}

	public static boolean inside(int mx, int my, int x, int y, int w, int h) {
		return mx >= x && my >= y && mx < x + w && my < y + h;
	}
}
