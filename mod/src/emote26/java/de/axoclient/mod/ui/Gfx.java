package de.axoclient.mod.ui;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import org.joml.Matrix3x2fStack;

/**
 * Zeichnen in "Design-Pixeln": dieselben Maße wie im Mockup (1280 × 800), damit das Menü immer gleich viel Platz
 * einnimmt. Gezeichnet wird aber in echten Bildschirmpunkten: jede Kante wird auf ganze Punkte gerundet, Ecken und
 * Symbole liegen 1:1 auf dem Raster, und die Schrift ({@link TextRaster}) wird genau in der Größe gerastert, in der sie
 * erscheint. So bleibt alles scharf, auch wenn ein Design-Pixel z. B. 1,35 Punkte groß ist.
 * <p>
 * Flächen, Rahmen und Schatten kommen aus wenigen Neun-Teile-Bildern ({@link Shapes}); gleiche Bilder hintereinander
 * fasst Minecraft zu einem Zeichenaufruf zusammen. Jede Textzeile ist ein einziges Viereck.
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

	// ---------- Grundbausteine ----------

	/** Bild 1:1 (Bildschirmpunkte). */
	private void blit(Identifier id, int x, int y, int w, int h, int texW, int texH, int argb) {
		g.blit(RenderPipelines.GUI_TEXTURED, id, x, y, 0, 0, w, h, texW, texH, texW, texH, argb);
	}

	/**
	 * Ausschnitt (u, v, rw × rh) eines Bildes der Größe tex × tex nach (x, y, w × h) – 1:1 oder gestreckt. Gestreckt wird
	 * nur ein gleichförmiger Bereich, deshalb bleibt das Ergebnis bei jedem Texturfilter sauber.
	 */
	private void piece(Identifier id, int x, int y, int w, int h, int u, int v, int rw, int rh, int tex, int argb) {
		if (w <= 0 || h <= 0)
			return;
		if (w == rw && h == rh) {
			g.blit(RenderPipelines.GUI_TEXTURED, id, x, y, u, v, rw, rh, rw, rh, tex, tex, argb);
			return;
		}
		pose().pushMatrix();
		pose().translate(x, y);
		pose().scale(w / (float) rw, h / (float) rh);
		g.blit(RenderPipelines.GUI_TEXTURED, id, 0, 0, u, v, rw, rh, rw, rh, tex, tex, argb);
		pose().popMatrix();
	}

	/**
	 * Neun-Teile-Bild mit Eckgröße c auf das Rechteck (x0, y0, w × h) Bildschirmpunkte. edges/center: ob Kanten und Mitte
	 * gezeichnet werden (bei Rahmen ist die Mitte leer, bei Masken auch die Kanten).
	 */
	private void nine(Identifier id, int c, int x0, int y0, int w, int h, int argb, boolean edges, boolean center) {
		int tex = Shapes.size(c);
		int far = c + Shapes.MID;
		int mid = c + 1;
		int x1 = x0 + w, y1 = y0 + h;
		piece(id, x0, y0, c, c, 0, 0, c, c, tex, argb);
		piece(id, x1 - c, y0, c, c, far, 0, c, c, tex, argb);
		piece(id, x0, y1 - c, c, c, 0, far, c, c, tex, argb);
		piece(id, x1 - c, y1 - c, c, c, far, far, c, c, tex, argb);
		if (edges) {
			piece(id, x0 + c, y0, w - 2 * c, c, mid, 0, 1, c, tex, argb);
			piece(id, x0 + c, y1 - c, w - 2 * c, c, mid, far, 1, c, tex, argb);
			piece(id, x0, y0 + c, c, h - 2 * c, 0, mid, c, 1, tex, argb);
			piece(id, x1 - c, y0 + c, c, h - 2 * c, far, mid, c, 1, tex, argb);
		}
		if (center)
			piece(id, x0 + c, y0 + c, w - 2 * c, h - 2 * c, mid, mid, 1, 1, tex, argb);
	}

	// ---------- Flächen ----------

	public void fill(int x, int y, int w, int h, int argb) {
		if (g != null && w > 0 && h > 0)
			g.fill(X(x), Y(y), X(x + w), Y(y + h), argb);
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
		else
			nine(Shapes.slice(Shapes.Kind.FILL, r), r, x0, y0, wp, hp, argb, true, true);
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
		if (r == 0) {
			g.fill(x0, y0, x1, y0 + 1, argb);
			g.fill(x0, y1 - 1, x1, y1, argb);
			g.fill(x0, y0 + 1, x0 + 1, y1 - 1, argb);
			g.fill(x1 - 1, y0 + 1, x1, y1 - 1, argb);
		} else
			nine(Shapes.slice(Shapes.Kind.RING, r), r, x0, y0, wp, hp, argb, true, false);
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

	/**
	 * Verlauf von from (links oben) nach to (rechts unten). Gezeichnet in schmalen senkrechten Streifen aus der
	 * Flächen-Maske, jeder in seiner Farbe: kein eigenes Bild je Größe und Farbe, also auch kein Ruckeln, wenn sich
	 * Größe oder Farbe ändern (Hover, Zoom, Fenstergröße).
	 */
	public void gradient(int x, int y, int w, int h, int radius, int from, int to) {
		if (g == null || w <= 0 || h <= 0)
			return;
		int x0 = X(x), y0 = Y(y), x1 = X(x + w), y1 = Y(y + h);
		int wp = x1 - x0, hp = y1 - y0;
		if (wp <= 0 || hp <= 0)
			return;
		int r = radiusPx(radius, wp, hp);
		int step = Math.max(2, (wp + 55) / 56);
		// Farbe je Streifen wie entlang der Mittellinie eines 135°-Verlaufs
		float span = wp + hp;
		if (r == 0) {
			for (int a = x0; a < x1; a += step) {
				int b = Math.min(x1, a + step);
				g.fill(a, y0, b, y1, Theme.mix(from, to, ((a + b) / 2.0F - x0 + hp / 2.0F) / span));
			}
			return;
		}
		Identifier id = Shapes.slice(Shapes.Kind.FILL, r);
		int tex = Shapes.size(r);
		int far = r + Shapes.MID;
		int mid = r + 1;
		for (int a = x0; a < x1; ) {
			// Streifen enden an den Eckgrenzen, damit jeder ganz in einer Zone liegt
			int limit = a < x0 + r ? x0 + r : a < x1 - r ? x1 - r : x1;
			int b = Math.min(limit, a + step);
			int sw = b - a;
			int c = Theme.mix(from, to, ((a + b) / 2.0F - x0 + hp / 2.0F) / span);
			if (a < x0 + r) {
				int u = a - x0;
				piece(id, a, y0, sw, r, u, 0, sw, r, tex, c);
				piece(id, a, y0 + r, sw, hp - 2 * r, u, mid, sw, 1, tex, c);
				piece(id, a, y1 - r, sw, r, u, far, sw, r, tex, c);
			} else if (a < x1 - r) {
				piece(id, a, y0, sw, r, mid, 0, 1, r, tex, c);
				piece(id, a, y0 + r, sw, hp - 2 * r, mid, mid, 1, 1, tex, c);
				piece(id, a, y1 - r, sw, r, mid, far, 1, r, tex, c);
			} else {
				int u = far + (a - (x1 - r));
				piece(id, a, y0, sw, r, u, 0, sw, r, tex, c);
				piece(id, a, y0 + r, sw, hp - 2 * r, u, mid, sw, 1, tex, c);
				piece(id, a, y1 - r, sw, r, u, far, sw, r, tex, c);
			}
			a = b;
		}
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
		nine(Shapes.shadow(r, s), r + s, x0 - s, y0 - s, wp + 2 * s, hp + 2 * s, c, true, true);
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

	/** Ersatz, falls die eigene Schrift einen Text nicht kann: Minecraft-Schrift, Großbuchstaben so hoch wie bei Inter. */
	private static float fallbackZoom(float em) {
		return em * 0.7275F / 7.0F;
	}

	/** Breite in Design-Pixeln. */
	public float width(String text, float size, int weight) {
		return width(text, size, weight, 0);
	}

	/** Breite in Design-Pixeln, mit Sperrung tracking (Design-Pixel je Zeichen). */
	public float width(String text, float size, int weight, float tracking) {
		if (text == null || text.isEmpty())
			return 0;
		float em = size * px;
		float w = TextRaster.width(text, weight, em, tracking * px);
		if (w < 0)
			w = font().width(Component.literal(text)) * fallbackZoom(em);
		return w / px;
	}

	/** Text mit der Oberkante seines Schriftgrads bei y. */
	public void text(String text, float x, float y, float size, int weight, int argb) {
		textMid(text, x, y + size * 0.47F, size, weight, argb);
	}

	/** Text senkrecht mittig auf cy (wie align-items: center). */
	public void textMid(String text, float x, float cy, float size, int weight, int argb) {
		draw(text, x, cy, size, weight, argb, 0, false, 0, 0);
	}

	/** Wie textMid, aber gesperrt (letter-spacing in Design-Pixeln). */
	public void textMid(String text, float x, float cy, float size, int weight, int argb, float tracking) {
		draw(text, x, cy, size, weight, argb, tracking, false, 0, 0);
	}

	public void textCentered(String text, float cx, float cy, float size, int weight, int argb) {
		textMid(text, cx - width(text, size, weight) / 2, cy, size, weight, argb);
	}

	public void textRight(String text, float right, float cy, float size, int weight, int argb) {
		textMid(text, right - width(text, size, weight), cy, size, weight, argb);
	}

	/** Text im Akzent-Verlauf (wie background-clip: text); gibt die Breite zurück. */
	public float gradientText(String text, float x, float cy, float size, int weight) {
		draw(text, x, cy, size, weight, 0xFFFFFFFF, 0, true, Theme.TEXT_FROM, Theme.TEXT_TO);
		return width(text, size, weight);
	}

	private void draw(String text, float x, float cy, float size, int weight, int argb, float tracking, boolean gradient, int from, int to) {
		if (g == null || text == null || text.isEmpty() || (argb >>> 24) == 0)
			return;
		float em = size * px;
		float center = oy + cy * px;
		TextRaster.Sprite s = TextRaster.sprite(text, weight, em, tracking * px, gradient, from, to);
		if (s != null) {
			if (s.id() == null)
				return; // nur Leerzeichen
			int baseline = Math.round(center + TextRaster.baselineFromCenter(weight, em));
			blit(s.id(), X(x) + s.dx(), baseline + s.dy(), s.w(), s.h(), s.w(), s.h(), argb);
			return;
		}
		// Ersatz: Minecraft-Schrift (z. B. für Zeichen, die Inter nicht hat)
		float k = fallbackZoom(em);
		pose().pushMatrix();
		pose().translate(X(x), Math.round(center - 3.5F * k));
		pose().scale(k, k);
		g.text(font(), Component.literal(text), 0, 0, gradient ? Theme.mix(from, to, 0.5F) : argb, false);
		pose().popMatrix();
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
			nine(Shapes.slice(Shapes.Kind.MASK, r), r, x0, y0, wp, hp, bg, false, false);
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
