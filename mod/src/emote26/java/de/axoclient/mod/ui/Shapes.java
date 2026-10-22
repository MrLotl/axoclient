package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;

import java.util.HashMap;
import java.util.Map;

/**
 * Kleine weiße Masken für abgerundete Ecken, Rahmen, Verläufe und Schatten, je Bildschirmgröße einmal gerastert. Eine
 * runde Fläche besteht damit aus wenigen eingefärbten Bildern statt aus hunderten einzelnen Pixelflächen – das hält
 * das Menü schnell.
 */
final class Shapes {
	/** Welche Ecke: 0 oben links, 1 oben rechts, 2 unten links, 3 unten rechts. */
	enum Kind { FILL, RING, MASK }

	static final int RAMP = 256;

	private static final Map<String, Identifier> TEXTURES = new HashMap<>();

	private Shapes() {}

	/** Ecke mit Radius r Bildschirmpunkten (Fläche, 1-Punkt-Rahmen oder Maske für außerhalb). */
	static Identifier corner(Kind kind, int corner, int r) {
		String key = "corner_" + kind.name().toLowerCase() + "_" + corner + "_" + r;
		Identifier known = TEXTURES.get(key);
		if (known != null)
			return known;
		NativeImage image = new NativeImage(r, r, true);
		for (int j = 0; j < r; j++)
			for (int i = 0; i < r; i++) {
				// Abstand zum Kreismittelpunkt (für die Ecke oben links liegt er unten rechts)
				float dx = r - (i + 0.5F), dy = r - (j + 0.5F);
				float d = (float) Math.sqrt(dx * dx + dy * dy);
				float inside = clamp(r - d + 0.5F);
				float a = switch (kind) {
					case FILL -> inside;
					case RING -> clamp(r - d + 0.5F) - clamp(r - 1 - d + 0.5F);
					case MASK -> 1 - inside;
				};
				int x = corner == 1 || corner == 3 ? r - 1 - i : i;
				int y = corner == 2 || corner == 3 ? r - 1 - j : j;
				image.setPixel(x, y, (Math.round(a * 255) << 24) | 0xFFFFFF);
			}
		return register(key, image);
	}

	/** Fertige Verlaufsflächen (Farben eingebacken), zuletzt benutzte zuerst; zu viele werden wieder freigegeben. */
	private static final Map<String, Identifier> GRADIENTS = new java.util.LinkedHashMap<>(64, 0.75F, true) {
		@Override
		protected boolean removeEldestEntry(Map.Entry<String, Identifier> eldest) {
			if (size() <= 160)
				return false;
			Minecraft.getInstance().getTextureManager().release(eldest.getValue());
			return true;
		}
	};

	/**
	 * Abgerundete Fläche w × h Punkte (Radius r) mit Verlauf von from (oben links) nach to (unten rechts) wie
	 * "linear-gradient(135deg, …)", Kanten geglättet. Wird einmal gerastert und danach nur noch gezeichnet.
	 */
	static Identifier gradient(int w, int h, int r, int from, int to) {
		String key = w + "x" + h + "_" + r + "_" + Integer.toHexString(from) + "_" + Integer.toHexString(to);
		Identifier known = GRADIENTS.get(key);
		if (known != null)
			return known;
		NativeImage image = new NativeImage(w, h, true);
		float span = Math.max(1, (w - 1) + (h - 1));
		for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++) {
				int c = Theme.mix(from, to, (x + y) / span);
				float cover = coverage(x, y, w, h, r);
				int a = Math.round(((c >>> 24) & 0xFF) * cover);
				image.setPixel(x, y, (a << 24) | (c & 0xFFFFFF));
			}
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/gradient/" + Integer.toHexString(key.hashCode()) + "_" + w + "x" + h);
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient Verlauf", image));
		GRADIENTS.put(key, id);
		return id;
	}

	/** Wie viel des Punktes (x, y) in einem abgerundeten Rechteck w × h mit Radius r liegt. */
	private static float coverage(int x, int y, int w, int h, int r) {
		if (r <= 0)
			return 1;
		float cx = x + 0.5F, cy = y + 0.5F;
		float dx = cx < r ? r - cx : cx > w - r ? cx - (w - r) : 0;
		float dy = cy < r ? r - cy : cy > h - r ? cy - (h - r) : 0;
		if (dx == 0 || dy == 0)
			return 1;
		return clamp(r - (float) Math.sqrt(dx * dx + dy * dy) + 0.5F);
	}

	/** Waagerechter Verlauf der Deckkraft (0 → 1, oder 1 → 0 bei down), RAMP × 1 Punkte. */
	static Identifier ramp(boolean down) {
		String key = down ? "ramp_down" : "ramp_up";
		Identifier known = TEXTURES.get(key);
		if (known != null)
			return known;
		NativeImage image = new NativeImage(RAMP, 1, true);
		for (int i = 0; i < RAMP; i++) {
			float t = i / (float) (RAMP - 1);
			image.setPixel(i, 0, (Math.round((down ? 1 - t : t) * 255) << 24) | 0xFFFFFF);
		}
		return register(key, image);
	}

	/**
	 * Schatten-Ecke: innen (Radius r) voll, nach außen über spread Punkte weich auslaufend; Kantenlänge r + spread.
	 */
	static Identifier shadowCorner(int corner, int r, int spread) {
		String key = "shadow_" + corner + "_" + r + "_" + spread;
		Identifier known = TEXTURES.get(key);
		if (known != null)
			return known;
		int size = r + spread;
		NativeImage image = new NativeImage(size, size, true);
		for (int j = 0; j < size; j++)
			for (int i = 0; i < size; i++) {
				float dx = size - (i + 0.5F), dy = size - (j + 0.5F);
				float d = (float) Math.sqrt(dx * dx + dy * dy);
				int x = corner == 1 || corner == 3 ? size - 1 - i : i;
				int y = corner == 2 || corner == 3 ? size - 1 - j : j;
				image.setPixel(x, y, (Math.round(falloff(d - r, spread) * 255) << 24) | 0xFFFFFF);
			}
		return register(key, image);
	}

	/**
	 * Schatten-Kante über spread Punkte von voll (innen) nach aus (außen). side: 0 oben, 1 rechts, 2 unten, 3 links
	 * (= wohin "außen" zeigt). Oben/unten 1 × spread, links/rechts spread × 1.
	 */
	static Identifier shadowEdge(int side, int spread) {
		String key = "shadow_edge_" + side + "_" + spread;
		Identifier known = TEXTURES.get(key);
		if (known != null)
			return known;
		boolean vertical = side == 0 || side == 2;
		NativeImage image = new NativeImage(vertical ? 1 : spread, vertical ? spread : 1, true);
		for (int i = 0; i < spread; i++) {
			int a = Math.round(falloff(i + 0.5F, spread) * 255) << 24 | 0xFFFFFF;
			int at = side == 0 || side == 3 ? spread - 1 - i : i;
			if (vertical)
				image.setPixel(0, at, a);
			else
				image.setPixel(at, 0, a);
		}
		return register(key, image);
	}

	/** Weicher Auslauf: 1 innen, 0 nach spread Punkten. */
	private static float falloff(float outside, int spread) {
		if (outside <= 0)
			return 1;
		float t = clamp(1 - outside / Math.max(1, spread));
		return t * t * (3 - 2 * t) * t;
	}

	private static Identifier register(String key, NativeImage image) {
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/shape/" + key);
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient " + key, image));
		TEXTURES.put(key, id);
		return id;
	}

	private static float clamp(float v) {
		return Math.max(0, Math.min(1, v));
	}
}
