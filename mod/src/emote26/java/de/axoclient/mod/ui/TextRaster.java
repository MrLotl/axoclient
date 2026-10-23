package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;
import org.lwjgl.BufferUtils;
import org.lwjgl.stb.STBTTFontinfo;
import org.lwjgl.stb.STBTruetype;
import org.lwjgl.system.MemoryStack;

import java.io.InputStream;
import java.nio.ByteBuffer;
import java.nio.IntBuffer;
import java.util.HashMap;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Eigene Schrift für das Menü. Minecrafts Schrift-Engine rastert jede Größe in festen Stufen und zeichnet Buchstabe für
 * Buchstabe ohne Kerning; das wirkt weich und unruhig. Hier wird Inter mit stb_truetype genau in der Pixelgröße
 * gerastert, in der sie erscheint:
 * <ul>
 * <li>Grundlinie auf ganzen Punkten, x-Höhe leicht auf ganze Punkte gezogen (wie "leichtes Hinting"): waagerechte
 * Kanten liegen scharf auf dem Raster.</li>
 * <li>Jeder Buchstabe in Viertelpunkten genau an seiner Stelle, mit Kerning – Abstände wie im Browser.</li>
 * <li>Gamma-Anhebung der Kantendeckung, damit helle Schrift auf Dunkel nicht dünn und grau wirkt.</li>
 * </ul>
 * Jede Zeile wird einmal zu einem Bild und danach nur noch als ein einziges Viereck gezeichnet; ändert sich der Text,
 * entsteht ein neues Bild, alte fallen nach einer Weile wieder heraus.
 */
final class TextRaster {
	/** Gamma für die Kantendeckung (1 = unverändert; größer = kräftiger). */
	private static final float GAMMA = 1.45F;
	private static final int[] LUT = new int[256];

	/** Unterpixel-Stufen je Punkt (Lage der Buchstaben). */
	private static final int PHASES = 4;

	/** Höchstens so viele Zeilenbilder bzw. Bildpunkte gleichzeitig. */
	private static final int MAX_SPRITES = 900;
	private static final long MAX_PIXELS = 6_000_000;

	static {
		for (int i = 0; i < 256; i++)
			LUT[i] = Math.round((float) Math.pow(i / 255.0, 1 / GAMMA) * 255);
	}

	/** Ein fertig gerastertes Zeilenbild; dx/dy = Lage der linken oberen Ecke relativ zu Anfang und Grundlinie. */
	record Sprite(Identifier id, int dx, int dy, int w, int h) {}

	private static final Face[] FACES = new Face[4];
	private static boolean loaded;
	private static boolean failed;

	private static final Map<String, Sprite> SPRITES = new LinkedHashMap<>(256, 0.75F, true);
	private static long pixels;
	private static int counter;

	private static final Map<String, Float> WIDTHS = new LinkedHashMap<>(512, 0.75F, true) {
		@Override
		protected boolean removeEldestEntry(Map.Entry<String, Float> eldest) {
			return size() > 4096;
		}
	};

	private TextRaster() {}

	private static int index(int weight) {
		return weight >= 700 ? 3 : weight >= 600 ? 2 : weight >= 500 ? 1 : 0;
	}

	/** Schrift für die Stärke, oder null, wenn die eigene Schrift nicht verfügbar ist. */
	private static Face face(int weight) {
		if (!loaded) {
			loaded = true;
			try {
				FACES[0] = Face.load("/assets/axoclient/font/inter-400.ttf");
				FACES[1] = Face.load("/assets/axoclient/font/inter-medium.ttf");
				FACES[2] = Face.load("/assets/axoclient/font/inter-600.ttf");
				FACES[3] = Face.load("/assets/axoclient/font/inter-700.ttf");
			} catch (Throwable t) {
				failed = true;
				System.err.println("[AxoClient] Eigene Menü-Schrift nicht verfügbar, nutze Minecraft-Schrift: " + t);
			}
		}
		if (failed)
			return null;
		Face f = FACES[index(weight)];
		return f != null ? f : FACES[0];
	}

	/** Größenschlüssel: Schriftgröße (em) in Viertelpunkten. */
	private static int sizeKey(float em) {
		return Math.max(8, Math.round(em * 4));
	}

	/**
	 * Breite in Punkten für Schriftgröße em (Punkte) und Sperrung tracking (Punkte je Zeichen); -1, wenn die eigene Schrift
	 * den Text nicht darstellen kann.
	 */
	static float width(String text, int weight, float em, float tracking) {
		Face f = face(weight);
		if (f == null)
			return -1;
		int key = sizeKey(em);
		int tq = Math.round(tracking * 4);
		String cacheKey = index(weight) + "|" + key + "|" + tq + "|" + text;
		Float known = WIDTHS.get(cacheKey);
		if (known != null)
			return known;
		float scale = f.scale(key);
		float pen = 0;
		int prev = -1;
		int count = 0;
		for (int i = 0; i < text.length(); ) {
			int cp = text.codePointAt(i);
			i += Character.charCount(cp);
			int glyph = f.glyph(cp);
			if (glyph == 0 && !isSpace(cp)) {
				WIDTHS.put(cacheKey, -1F);
				return -1;
			}
			if (prev >= 0)
				pen += f.kern(prev, glyph) * scale + tq / 4.0F;
			pen += f.advance(glyph) * scale;
			prev = glyph;
			count++;
		}
		float w = count == 0 ? 0 : pen;
		WIDTHS.put(cacheKey, w);
		return w;
	}

	/** Abstand von der Zeilenmitte zur Grundlinie (Punkte), so dass Großbuchstaben genau mittig sitzen (wie CSS). */
	static float baselineFromCenter(int weight, float em) {
		Face f = face(weight);
		if (f == null)
			return 0;
		return (f.ascent + f.descent) * 0.5F * f.scale(sizeKey(em));
	}

	/**
	 * Zeilenbild für den Text, oder null, wenn die eigene Schrift ihn nicht darstellen kann. Bei gradient wird der Verlauf
	 * from → to (links nach rechts) eingebacken; sonst ist das Bild weiß und wird beim Zeichnen eingefärbt.
	 */
	static Sprite sprite(String text, int weight, float em, float tracking, boolean gradient, int from, int to) {
		Face f = face(weight);
		if (f == null)
			return null;
		int key = sizeKey(em);
		int tq = Math.round(tracking * 4);
		String cacheKey = index(weight) + "|" + key + "|" + tq + "|" + (gradient ? Integer.toHexString(from) + ">" + Integer.toHexString(to) : "")
			+ "|" + text;
		Sprite known = SPRITES.get(cacheKey);
		if (known != null)
			return known;
		if (SPRITES.containsKey(cacheKey))
			return null; // schon einmal als "nicht darstellbar" erkannt
		Sprite made = build(f, text, key, tq / 4.0F, gradient, from, to);
		SPRITES.put(cacheKey, made);
		if (made != null) {
			pixels += (long) made.w() * made.h();
			trim();
		}
		return made;
	}

	private static void trim() {
		Iterator<Map.Entry<String, Sprite>> it = SPRITES.entrySet().iterator();
		while ((SPRITES.size() > MAX_SPRITES || pixels > MAX_PIXELS) && it.hasNext()) {
			Sprite s = it.next().getValue();
			it.remove();
			if (s != null && s.id() != null) {
				pixels -= (long) s.w() * s.h();
				Minecraft.getInstance().getTextureManager().release(s.id());
			}
		}
	}

	private static boolean isSpace(int cp) {
		return cp == ' ' || cp == 0xA0 || cp == 0x202F || cp == 0x2009;
	}

	private static Sprite build(Face f, String text, int key, float tracking, boolean gradient, int from, int to) {
		float scale = f.scale(key);
		// Erst alle Buchstaben setzen und die Gesamtfläche bestimmen
		int n = text.codePointCount(0, text.length());
		Glyph[] glyphs = new Glyph[n];
		int[] gx = new int[n];
		int minX = Integer.MAX_VALUE, minY = Integer.MAX_VALUE, maxX = Integer.MIN_VALUE, maxY = Integer.MIN_VALUE;
		float pen = 0;
		int prev = -1;
		int at = 0;
		for (int i = 0; i < text.length(); ) {
			int cp = text.codePointAt(i);
			i += Character.charCount(cp);
			int glyph = f.glyph(cp);
			if (glyph == 0 && !isSpace(cp))
				return null;
			if (prev >= 0)
				pen += f.kern(prev, glyph) * scale + tracking;
			int ix = (int) Math.floor(pen);
			int phase = Math.round((pen - ix) * PHASES);
			if (phase == PHASES) {
				ix++;
				phase = 0;
			}
			Glyph g = f.bitmap(glyph, key, scale, phase);
			if (g.w > 0 && g.h > 0) {
				glyphs[at] = g;
				gx[at] = ix;
				minX = Math.min(minX, ix + g.x0);
				maxX = Math.max(maxX, ix + g.x0 + g.w);
				minY = Math.min(minY, g.y0);
				maxY = Math.max(maxY, g.y0 + g.h);
			}
			pen += f.advance(glyph) * scale;
			prev = glyph;
			at++;
		}
		if (minX == Integer.MAX_VALUE)
			return new Sprite(null, 0, 0, 0, 0); // nur Leerzeichen
		int w = maxX - minX, h = maxY - minY;
		int[] cover = new int[w * h];
		for (int k = 0; k < n; k++) {
			Glyph g = glyphs[k];
			if (g == null)
				continue;
			int ox = gx[k] + g.x0 - minX, oy = g.y0 - minY;
			for (int y = 0; y < g.h; y++) {
				int row = (oy + y) * w + ox;
				int src = y * g.w;
				for (int x = 0; x < g.w; x++) {
					int a = g.alpha[src + x] & 0xFF;
					if (a != 0)
						cover[row + x] = Math.min(255, cover[row + x] + a);
				}
			}
		}
		NativeImage image = new NativeImage(w, h, true);
		for (int x = 0; x < w; x++) {
			int rgb = gradient ? Theme.mix(from, to, (x + 0.5F) / w) & 0xFFFFFF : 0xFFFFFF;
			for (int y = 0; y < h; y++)
				image.setPixel(x, y, (LUT[cover[y * w + x]] << 24) | rgb);
		}
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/text/" + Integer.toString(counter++, 36));
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient Schrift", image));
		return new Sprite(id, minX, minY, w, h);
	}

	/** Ein gerasterter Buchstabe (Deckung 0..255), Lage relativ zu Stift und Grundlinie. */
	private record Glyph(int x0, int y0, int w, int h, byte[] alpha) {}

	private static final class Face {
		final STBTTFontinfo info;
		/** Muss leben bleiben, solange info benutzt wird. */
		final ByteBuffer data;
		final float emScale;
		final int ascent;
		final int descent;
		final float xHeight;
		final Map<Integer, Integer> glyphs = new HashMap<>();
		final Map<Integer, Integer> advances = new HashMap<>();
		final Map<Long, Integer> kerning = new HashMap<>();
		final Map<Integer, Float> scales = new HashMap<>();
		final Map<Long, Glyph> bitmaps = new HashMap<>();
		private ByteBuffer scratch = BufferUtils.createByteBuffer(64 * 64);

		private Face(STBTTFontinfo info, ByteBuffer data) {
			this.info = info;
			this.data = data;
			this.emScale = STBTruetype.stbtt_ScaleForMappingEmToPixels(info, 1);
			try (MemoryStack stack = MemoryStack.stackPush()) {
				IntBuffer a = stack.mallocInt(1), d = stack.mallocInt(1), l = stack.mallocInt(1);
				STBTruetype.stbtt_GetFontVMetrics(info, a, d, l);
				ascent = a.get(0);
				descent = d.get(0);
				IntBuffer x0 = stack.callocInt(1), y0 = stack.callocInt(1), x1 = stack.callocInt(1), y1 = stack.callocInt(1);
				STBTruetype.stbtt_GetCodepointBox(info, 'x', x0, y0, x1, y1);
				xHeight = y1.get(0) > 0 ? y1.get(0) : (ascent - descent) * 0.45F;
			}
		}

		static Face load(String path) throws Exception {
			byte[] bytes;
			try (InputStream in = TextRaster.class.getResourceAsStream(path)) {
				if (in == null)
					throw new IllegalStateException("fehlt: " + path);
				bytes = in.readAllBytes();
			}
			ByteBuffer data = BufferUtils.createByteBuffer(bytes.length);
			data.put(bytes).flip();
			STBTTFontinfo info = STBTTFontinfo.create();
			STBTruetype.stbtt_InitFont(info, data);
			Face face = new Face(info, data);
			if (!(face.emScale > 0) || face.glyph('a') == 0)
				throw new IllegalStateException("ungültig: " + path);
			return face;
		}

		int glyph(int cp) {
			Integer known = glyphs.get(cp);
			if (known != null)
				return known;
			int g = STBTruetype.stbtt_FindGlyphIndex(info, cp);
			glyphs.put(cp, g);
			return g;
		}

		int advance(int glyph) {
			Integer known = advances.get(glyph);
			if (known != null)
				return known;
			int adv;
			try (MemoryStack stack = MemoryStack.stackPush()) {
				IntBuffer a = stack.mallocInt(1), l = stack.mallocInt(1);
				STBTruetype.stbtt_GetGlyphHMetrics(info, glyph, a, l);
				adv = a.get(0);
			}
			advances.put(glyph, adv);
			return adv;
		}

		int kern(int left, int right) {
			long key = ((long) left << 32) | (right & 0xFFFFFFFFL);
			Integer known = kerning.get(key);
			if (known != null)
				return known;
			int k = STBTruetype.stbtt_GetGlyphKernAdvance(info, left, right);
			kerning.put(key, k);
			return k;
		}

		/** Maßstab (Punkte je Schrifteinheit) für Größe key/4; leicht angepasst, damit die x-Höhe ganze Punkte hoch ist. */
		float scale(int key) {
			Float known = scales.get(key);
			if (known != null)
				return known;
			float s = emScale * (key / 4.0F);
			float xh = xHeight * s;
			float snapped = Math.round(xh);
			if (snapped >= 4 && Math.abs(snapped / xh - 1) <= 0.07F)
				s *= snapped / xh;
			scales.put(key, s);
			return s;
		}

		Glyph bitmap(int glyph, int key, float scale, int phase) {
			long cacheKey = ((long) key << 40) | ((long) phase << 32) | (glyph & 0xFFFFFFFFL);
			Glyph known = bitmaps.get(cacheKey);
			if (known != null)
				return known;
			if (bitmaps.size() > 12_000)
				bitmaps.clear();
			float shift = phase / (float) PHASES;
			Glyph made;
			try (MemoryStack stack = MemoryStack.stackPush()) {
				IntBuffer x0 = stack.mallocInt(1), y0 = stack.mallocInt(1), x1 = stack.mallocInt(1), y1 = stack.mallocInt(1);
				STBTruetype.stbtt_GetGlyphBitmapBoxSubpixel(info, glyph, scale, scale, shift, 0, x0, y0, x1, y1);
				int w = x1.get(0) - x0.get(0), h = y1.get(0) - y0.get(0);
				if (w <= 0 || h <= 0)
					made = new Glyph(0, 0, 0, 0, new byte[0]);
				else {
					if (scratch.capacity() < w * h)
						scratch = BufferUtils.createByteBuffer(Math.max(w * h, scratch.capacity() * 2));
					scratch.clear();
					STBTruetype.stbtt_MakeGlyphBitmapSubpixel(info, scratch, w, h, w, scale, scale, shift, 0, glyph);
					byte[] alpha = new byte[w * h];
					scratch.get(0, alpha, 0, w * h);
					made = new Glyph(x0.get(0), y0.get(0), w, h, alpha);
				}
			}
			bitmaps.put(cacheKey, made);
			return made;
		}
	}
}
