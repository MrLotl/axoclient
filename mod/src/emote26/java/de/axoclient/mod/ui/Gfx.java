package de.axoclient.mod.ui;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.FontDescription;
import net.minecraft.resources.Identifier;
import org.joml.Matrix3x2fStack;

/**
 * Zeichnen in "Design-Pixeln": dieselben Maße wie im Mockup (1280 × 800), damit das Menü immer gleich viel Platz
 * einnimmt. Gezeichnet wird aber in echten Bildschirmpunkten: jede Kante wird auf ganze Punkte gerundet, Ecken und
 * Symbole liegen 1:1 auf dem Raster, und die Schrift (Inter in vier Stärken) wird in Viertelstufen genau in der Größe
 * gerastert, in der sie erscheint. So bleibt alles scharf, auch wenn ein Design-Pixel z. B. 1,35 Punkte groß ist.
 */
public final class Gfx {
	public static final float REF_WIDTH = 1280;
	public static final float REF_HEIGHT = 800;

	public static final int REGULAR = 400;
	public static final int MEDIUM = 500;
	public static final int SEMIBOLD = 600;
	public static final int BOLD = 700;

	/** Rasterstufen der Schrift: Faktor q / 4 für q = 4 … 20 (assets/axoclient/font/ui_&lt;Stärke&gt;_&lt;q&gt;.json). */
	private static final int MIN_Q = 4;
	private static final int MAX_Q = 20;
	private static final FontDescription[][] FONTS = new FontDescription[4][MAX_Q + 1];

	static {
		int[] weights = { 400, 500, 600, 700 };
		for (int w = 0; w < weights.length; w++)
			for (int q = MIN_Q; q <= MAX_Q; q++)
				FONTS[w][q] = new FontDescription.Resource(Identifier.fromNamespaceAndPath("axoclient", "ui_" + weights[w] + "_" + q));
	}

	public final GuiGraphicsExtractor g;
	/** GUI-Einheiten je Design-Pixel (für Mauspositionen). */
	public final float scale;
	/** Bildschirmpunkte je Design-Pixel. */
	public final float px;
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

	// ---------- Flächen ----------

	public void fill(int x, int y, int w, int h, int argb) {
		if (g != null && w > 0 && h > 0)
			g.fill(X(x), Y(y), X(x + w), Y(y + h), argb);
	}

	private void blit(Identifier id, int x, int y, int w, int h, int texW, int texH, int argb) {
		g.blit(RenderPipelines.GUI_TEXTURED, id, x, y, 0, 0, w, h, texW, texH, texW, texH, argb);
	}

	/** Eckenradius in Punkten, höchstens halb so groß wie die kürzere Seite. */
	private int radiusPx(int radius, int wp, int hp) {
		return Math.max(0, Math.min(Math.round(radius * px), Math.min(wp, hp) / 2));
	}

	public void rect(int x, int y, int w, int h, int radius, int argb) {
		if (g == null || w <= 0 || h <= 0 || (argb >>> 24) == 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		int wp = x1 - x0, hp = y1 - y0;
		if (wp <= 0 || hp <= 0)
			return;
		int r = radiusPx(radius, wp, hp);
		if (r == 0)
			g.fill(x0, y0, x1, y1, argb);
		else {
			g.fill(x0, y0 + r, x1, y1 - r, argb);
			g.fill(x0 + r, y0, x1 - r, y0 + r, argb);
			g.fill(x0 + r, y1 - r, x1 - r, y1, argb);
			corners(Shapes.Kind.FILL, x0, y0, wp, hp, r, argb, argb);
		}
	}

	private void corners(Shapes.Kind kind, int x0, int y0, int wp, int hp, int r, int left, int right) {
		blit(Shapes.corner(kind, 0, r), x0, y0, r, r, r, r, left);
		blit(Shapes.corner(kind, 1, r), x0 + wp - r, y0, r, r, r, r, right);
		blit(Shapes.corner(kind, 2, r), x0, y0 + hp - r, r, r, r, r, left);
		blit(Shapes.corner(kind, 3, r), x0 + wp - r, y0 + hp - r, r, r, r, r, right);
	}

	/** Rahmen, genau einen Bildschirmpunkt dick, innen an der Kante. */
	public void outline(int x, int y, int w, int h, int radius, int argb) {
		if (g == null || w <= 0 || h <= 0 || (argb >>> 24) == 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		int wp = x1 - x0, hp = y1 - y0;
		if (wp <= 1 || hp <= 1)
			return;
		int r = radiusPx(radius, wp, hp);
		g.fill(x0 + r, y0, x1 - r, y0 + 1, argb);
		g.fill(x0 + r, y1 - 1, x1 - r, y1, argb);
		g.fill(x0, y0 + Math.max(r, 1), x0 + 1, y1 - Math.max(r, 1), argb);
		g.fill(x1 - 1, y0 + Math.max(r, 1), x1, y1 - Math.max(r, 1), argb);
		if (r > 0)
			corners(Shapes.Kind.RING, x0, y0, wp, hp, r, argb, argb);
	}

	/** Fläche mit Rahmen nach innen (wie box-shadow: inset 0 0 0 1px). */
	public void box(int x, int y, int w, int h, int radius, int fill, int line) {
		rect(x, y, w, h, radius, fill);
		outline(x, y, w, h, radius, line);
	}

	/** Akzent-Verlauf (Pink → Lila) wie die Hauptknöpfe. */
	public void accent(int x, int y, int w, int h, int radius) {
		gradient(x, y, w, h, radius, Theme.ACCENT_FROM, Theme.ACCENT_TO);
	}

	/** Waagerechter Verlauf von from nach to: ein Verlaufsbild statt einer Fläche je Spalte. */
	public void gradient(int x, int y, int w, int h, int radius, int from, int to) {
		if (g == null || w <= 0 || h <= 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		int wp = x1 - x0, hp = y1 - y0;
		if (wp <= 0 || hp <= 0)
			return;
		int r = radiusPx(radius, wp, hp);
		if (hp <= 6 && r <= 3) {
			// Dünne Balken (z. B. Ablaufbalken, der jedes Bild kürzer wird): Abschnitte statt eigenem Bild
			int steps = Math.max(1, Math.min(48, wp / 6));
			for (int i = 0; i < steps; i++) {
				int a = x0 + wp * i / steps, b = x0 + wp * (i + 1) / steps;
				g.fill(a, y0, b, y1, Theme.mix(from, to, (i + 0.5F) / steps));
			}
			return;
		}
		if (wp * hp > 600_000) {
			// Riesige Flächen (kommt kaum vor): einfache Fläche in der Mischfarbe
			rect(x, y, w, h, radius, Theme.mix(from, to, 0.5F));
			return;
		}
		// Fertiges Bild mit eingebackenem 135°-Verlauf und geglätteten Ecken, 1:1 auf den Bildschirmpunkten
		blit(Shapes.gradient(wp, hp, r, from, to), x0, y0, wp, hp, wp, hp, 0xFFFFFFFF);
	}

	/** Weicher Schatten unter einer Fläche (wie box-shadow 0 offsetY spread). */
	public void shadow(int x, int y, int w, int h, int radius, int spread, int offsetY, float strength) {
		if (g == null || w <= 0 || h <= 0 || spread <= 0)
			return;
		int s = Math.max(1, Math.round(spread * px));
		int x0 = X(x), y0 = Y(y + offsetY), x1 = X(x + w), y1 = Y(y + offsetY + h);
		int wp = x1 - x0, hp = y1 - y0;
		if (wp <= 0 || hp <= 0)
			return;
		int r = radiusPx(radius, wp, hp);
		int c = Theme.alpha(0xFF000000, Math.min(1, strength * 0.9F));
		int ax = x0 - s, ay = y0 - s;
		int full = r + s;
		int tw = wp + 2 * s, th = hp + 2 * s;
		blit(Shapes.shadowCorner(0, r, s), ax, ay, full, full, full, full, c);
		blit(Shapes.shadowCorner(1, r, s), ax + tw - full, ay, full, full, full, full, c);
		blit(Shapes.shadowCorner(2, r, s), ax, ay + th - full, full, full, full, full, c);
		blit(Shapes.shadowCorner(3, r, s), ax + tw - full, ay + th - full, full, full, full, full, c);
		// Kanten (gestreckt) und die Mitte
		edge(Shapes.shadowEdge(0, s), ax + full, ay, tw - 2 * full, s, 1, s, c);
		edge(Shapes.shadowEdge(2, s), ax + full, ay + th - s, tw - 2 * full, s, 1, s, c);
		edge(Shapes.shadowEdge(3, s), ax, ay + full, s, th - 2 * full, s, 1, c);
		edge(Shapes.shadowEdge(1, s), ax + tw - s, ay + full, s, th - 2 * full, s, 1, c);
		g.fill(ax + s, ay + full, ax + tw - s, ay + th - full, c);
		g.fill(ax + full, ay + s, ax + tw - full, ay + full, c);
		g.fill(ax + full, ay + th - full, ax + tw - full, ay + th - s, c);
	}

	private void edge(Identifier id, int x, int y, int w, int h, int texW, int texH, int argb) {
		if (w <= 0 || h <= 0)
			return;
		pose().pushMatrix();
		pose().translate(x, y);
		pose().scale(w / (float) texW, h / (float) texH);
		g.blit(RenderPipelines.GUI_TEXTURED, id, 0, 0, 0, 0, texW, texH, texW, texH, texW, texH, argb);
		pose().popMatrix();
	}

	/** Waagerechte Linie, genau einen Bildschirmpunkt dick. */
	public void hline(int x, int y, int w, int argb) {
		if (g != null)
			g.fill(X(x), Y(y), X(x + w), Y(y) + 1, argb);
	}

	public void vline(int x, int y, int h, int argb) {
		if (g != null)
			g.fill(X(x), Y(y), X(x) + 1, Y(y + h), argb);
	}

	/** Runder Punkt (Status). */
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

	// ---------- Schrift ----------

	private static Font font() {
		return Minecraft.getInstance().font;
	}

	/** Rasterstufe (q / 4 Punkte je Schrifteinheit) für Schriftgröße size. */
	private int step(float size) {
		return Math.max(MIN_Q, Math.min(MAX_Q, Math.round(size * px / 9.0F * 4)));
	}

	private static Component styled(String text, int q, int weight) {
		int w = weight >= 700 ? 3 : weight >= 600 ? 2 : weight >= 500 ? 1 : 0;
		FontDescription font = FONTS[w][q];
		return Component.literal(text).withStyle(style -> style.withFont(font));
	}

	public float width(String text, float size, int weight) {
		if (text == null || text.isEmpty())
			return 0;
		int q = step(size);
		return font().width(styled(text, q, weight)) * (q / 4.0F) / px;
	}

	/** Text mit der Oberkante seines Schriftgrads bei y. */
	public void text(String text, float x, float y, float size, int weight, int argb) {
		if (g == null || text == null || text.isEmpty())
			return;
		int q = step(size);
		float k = q / 4.0F;
		// Mitte der Zeile wie gewünscht, die Schrift selbst ist genau 9 · k Punkte groß gerastert
		float center = oy + (y + size * 0.47F) * px;
		int top = Math.round(center - 0.47F * 9 * k);
		pose().pushMatrix();
		pose().translate(X(x), top);
		pose().scale(k, k);
		// Minecrafts Schrift-Engine rastert ohne Hinting; helle Schrift auf Dunkel wirkt dadurch weich. Zweimal an
		// dieselbe Stelle gezeichnet steigt die Deckung der Kantenpunkte (wie die Gamma-Anhebung im Browser).
		Component styled = styled(text, q, weight);
		g.text(font(), styled, 0, 0, argb, false);
		g.text(font(), styled, 0, 0, argb, false);
		pose().popMatrix();
	}

	/** Text senkrecht mittig auf cy (wie align-items: center). */
	public void textMid(String text, float x, float cy, float size, int weight, int argb) {
		text(text, x, cy - size * 0.47F, size, weight, argb);
	}

	public void textCentered(String text, float cx, float cy, float size, int weight, int argb) {
		textMid(text, cx - width(text, size, weight) / 2, cy, size, weight, argb);
	}

	public void textRight(String text, float right, float cy, float size, int weight, int argb) {
		textMid(text, right - width(text, size, weight), cy, size, weight, argb);
	}

	/** Text im Akzent-Verlauf (wie background-clip: text), Zeichen für Zeichen eingefärbt. */
	public float gradientText(String text, float x, float cy, float size, int weight) {
		float total = width(text, size, weight);
		float at = x;
		for (int i = 0; i < text.length(); i++) {
			String c = String.valueOf(text.charAt(i));
			float w = width(c, size, weight);
			float t = total <= 0 ? 0 : (at - x + w / 2) / total;
			textMid(c, at, cy, size, weight, Theme.mix(Theme.TEXT_FROM, Theme.TEXT_TO, t));
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
		String cut = text;
		while (cut.length() > 1 && width(cut + "…", size, weight) > maxWidth)
			cut = cut.substring(0, cut.length() - 1);
		return cut + "…";
	}

	// ---------- Bilder ----------

	/** Symbol in size × size Design-Pixeln, eingefärbt, 1:1 auf dem Bildschirmraster. */
	public void icon(Icons.Icon icon, float x, float y, float size, int argb) {
		if (g == null)
			return;
		int pixels = Math.max(4, Math.round(size * px));
		Identifier id = Icons.texture(icon, pixels);
		// Mittig an die gewünschte Stelle, auf ganze Punkte
		int x0 = Math.round(ox + (x + size / 2) * px - pixels / 2.0F);
		int y0 = Math.round(oy + (y + size / 2) * px - pixels / 2.0F);
		g.blit(RenderPipelines.GUI_TEXTURED, id, x0, y0, 0, 0, pixels, pixels, pixels, pixels, pixels, pixels, argb);
	}

	/** Ausschnitt einer Textur in ein Rechteck (Design-Pixel), z. B. Gesicht aus dem Skin. */
	public void image(Identifier id, float x, float y, float w, float h, float u, float v, int regionW, int regionH, int texW, int texH,
					  int argb) {
		if (g == null || id == null)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		if (x1 <= x0 || y1 <= y0)
			return;
		pose().pushMatrix();
		pose().translate(x0, y0);
		pose().scale((x1 - x0) / (float) regionW, (y1 - y0) / (float) regionH);
		g.blit(RenderPipelines.GUI_TEXTURED, id, 0, 0, u, v, regionW, regionH, regionW, regionH, texW, texH, argb);
		pose().popMatrix();
	}

	/**
	 * Kopf eines Spielers aus seinem Skin (Gesicht und Hut), mit abgerundeten Ecken auf der Hintergrundfarbe bg. Ohne
	 * Skin ein neutrales Feld.
	 */
	public void head(Identifier skin, int x, int y, int size, int radius, int bg, boolean dim) {
		if (g == null)
			return;
		if (skin == null) {
			rect(x, y, size, size, radius, 0xFF3A3A3A);
			return;
		}
		int tint = dim ? 0xB38C8C8C : 0xFFFFFFFF;
		image(skin, x, y, size, size, 8, 8, 8, 8, 64, 64, tint);
		image(skin, x, y, size, size, 40, 8, 8, 8, 64, 64, tint);
		cornerMask(x, y, size, size, radius, bg);
	}

	/** Deckt die Ecken eines Rechtecks mit der Hintergrundfarbe ab, damit es abgerundet aussieht. */
	public void cornerMask(int x, int y, int w, int h, int radius, int bg) {
		if (g == null || radius <= 0)
			return;
		int x0 = X(x), y0 = Y(y), wp = X(x + w) - x0, hp = Y(y + h) - y0;
		int r = radiusPx(radius, wp, hp);
		if (r > 0)
			corners(Shapes.Kind.MASK, x0, y0, wp, hp, r, bg, bg);
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
