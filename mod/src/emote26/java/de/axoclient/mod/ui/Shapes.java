package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Weiße Masken für abgerundete Flächen, Rahmen und Schatten, je Radius (in Bildschirmpunkten) einmal gerastert.
 * <p>
 * Jede Maske ist ein "Neun-Teile-Bild": vier Ecken (je {@code c × c}), dazwischen ein 3 Punkte breiter, gleichförmiger
 * Streifen. Gfx zeichnet daraus die Ecken 1:1 und zieht den mittleren Punkt der Streifen auf Kanten und Mitte. Weil
 * alle Teile einer Fläche – und alle Flächen mit gleichem Radius – aus demselben Bild kommen, fasst Minecraft sie zu
 * einem einzigen Zeichenaufruf zusammen (vorher: drei Flächen und vier verschiedene Eckbilder je Rechteck).
 */
final class Shapes {
	enum Kind { FILL, RING, MASK }

	/** Breite des gleichförmigen Mittelstreifens; gezeichnet wird nur sein mittlerer Punkt (sicher bei jedem Filter). */
	static final int MID = 3;

	/** Kantenglättung: Abtastpunkte je Achse für Bildpunkte, die auf der Kante liegen. */
	private static final int SAMPLES = 4;

	private static final Map<String, Identifier> CACHE = new LinkedHashMap<>(64, 0.75F, true) {
		@Override
		protected boolean removeEldestEntry(Map.Entry<String, Identifier> eldest) {
			if (size() <= 220)
				return false;
			Minecraft.getInstance().getTextureManager().release(eldest.getValue());
			return true;
		}
	};

	private Shapes() {}

	/** Kantenlänge des Bildes für Eckgröße c. */
	static int size(int c) {
		return 2 * c + MID;
	}

	/** Neun-Teile-Maske einer abgerundeten Fläche (FILL), eines 1-Punkt-Rahmens (RING) oder des Außenbereichs (MASK). */
	static Identifier slice(Kind kind, int r) {
		String key = kind.name().toLowerCase() + "_" + r;
		Identifier known = CACHE.get(key);
		if (known != null)
			return known;
		int n = size(r);
		// Eine Ecke (oben links) rechnen, dann spiegeln; Streifen und Mitte sind gleichförmig
		float[] corner = new float[r * r];
		for (int j = 0; j < r; j++)
			for (int i = 0; i < r; i++) {
				float outer = coverage(i, j, r);
				float a = switch (kind) {
					case FILL -> outer;
					case RING -> outer - (r > 1 ? coverage(i - 1, j - 1, r - 1) : (i >= 1 && j >= 1 ? 1 : 0));
					case MASK -> 1 - outer;
				};
				corner[j * r + i] = Math.max(0, Math.min(1, a));
			}
		NativeImage image = new NativeImage(n, n, true);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++) {
				int cx = x < r ? x : x >= r + MID ? n - 1 - x : -1;
				int cy = y < r ? y : y >= r + MID ? n - 1 - y : -1;
				float a;
				if (cx >= 0 && cy >= 0)
					a = corner[cy * r + cx];
				else {
					// Auf einer geraden Kante oder in der Mitte: Abstand zur nächsten Außenkante
					int edge = cx >= 0 ? cx : cy >= 0 ? cy : Integer.MAX_VALUE;
					a = switch (kind) {
						case FILL -> 1;
						case RING -> edge == 0 ? 1 : 0;
						case MASK -> 0;
					};
				}
				image.setPixel(x, y, (Math.round(a * 255) << 24) | 0xFFFFFF);
			}
		return register(key, image);
	}

	/**
	 * Deckung des Bildpunkts (i, j) in der Ecke oben links eines abgerundeten Rechtecks mit Radius r (Kreismittelpunkt
	 * bei (r, r)). Auf der Kante wird mehrfach abgetastet, sonst reicht der Mittelpunkt.
	 */
	private static float coverage(int i, int j, int r) {
		if (i < 0 || j < 0)
			return 0;
		float dx = r - (i + 0.5F), dy = r - (j + 0.5F);
		float d = (float) Math.sqrt(dx * dx + dy * dy) - r;
		if (d <= -0.75F)
			return 1;
		if (d >= 0.75F)
			return 0;
		int inside = 0;
		for (int sy = 0; sy < SAMPLES; sy++)
			for (int sx = 0; sx < SAMPLES; sx++) {
				float px = r - (i + (sx + 0.5F) / SAMPLES), py = r - (j + (sy + 0.5F) / SAMPLES);
				if (px <= 0 || py <= 0 || px * px + py * py <= r * (float) r)
					inside++;
			}
		return inside / (float) (SAMPLES * SAMPLES);
	}

	/**
	 * Schatten als Neun-Teile-Bild: innen (Rechteck mit Radius r) voll, nach außen über spread Punkte weich auslaufend.
	 * Eckgröße r + spread.
	 */
	static Identifier shadow(int r, int spread) {
		String key = "shadow_" + r + "_" + spread;
		Identifier known = CACHE.get(key);
		if (known != null)
			return known;
		int c = r + spread;
		int n = size(c);
		NativeImage image = new NativeImage(n, n, true);
		for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++) {
				// Abstand zum inneren Rechteck [spread, n - spread] mit Radius r
				float px = x + 0.5F, py = y + 0.5F;
				float qx = Math.max(Math.abs(px - n / 2.0F) - (n / 2.0F - spread - r), 0);
				float qy = Math.max(Math.abs(py - n / 2.0F) - (n / 2.0F - spread - r), 0);
				float d = (float) Math.sqrt(qx * qx + qy * qy) - r;
				image.setPixel(x, y, (Math.round(falloff(d, spread) * 255) << 24) | 0xFFFFFF);
			}
		return register(key, image);
	}

	/** Weicher Auslauf: 1 innen, 0 nach spread Punkten. */
	private static float falloff(float outside, int spread) {
		if (outside <= 0)
			return 1;
		float t = Math.max(0, Math.min(1, 1 - outside / Math.max(1, spread)));
		return t * t * (3 - 2 * t) * t;
	}

	private static Identifier register(String key, NativeImage image) {
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/shape/" + key);
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient " + key, image));
		CACHE.put(key, id);
		return id;
	}
}
