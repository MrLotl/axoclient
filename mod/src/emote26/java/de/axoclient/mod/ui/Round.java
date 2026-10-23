package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;

import java.util.LinkedHashMap;
import java.util.Map;

/**
 * Glatte Kreise für alles Runde (Punkte, Schalter, Regler, Emote-Rad): weiße Masken mit weicher Kante, je Durchmesser
 * in Bildschirmpunkten einmal gerastert und beim Zeichnen eingefärbt. Pillen setzen sich aus den beiden Hälften und
 * einer Fläche dazwischen zusammen.
 */
final class Round {
	/** Abtastpunkte je Achse auf der Kante. */
	private static final int SAMPLES = 4;

	private static final Map<String, Identifier> CACHE = new LinkedHashMap<>(32, 0.75F, true) {
		@Override
		protected boolean removeEldestEntry(Map.Entry<String, Identifier> eldest) {
			if (size() <= 96)
				return false;
			Minecraft.getInstance().getTextureManager().release(eldest.getValue());
			return true;
		}
	};

	private Round() {}

	/** Gefüllter Kreis mit Durchmesser d. */
	static Identifier disc(int d) {
		return mask("disc_" + d, d, 0);
	}

	/** Kreisring mit Durchmesser d und Stärke t (nach innen). */
	static Identifier ring(int d, int t) {
		return mask("ring_" + d + "_" + t, d, t);
	}

	private static Identifier mask(String key, int d, int t) {
		Identifier known = CACHE.get(key);
		if (known != null)
			return known;
		NativeImage image = new NativeImage(d, d, true);
		float r = d / 2.0F;
		for (int y = 0; y < d; y++)
			for (int x = 0; x < d; x++) {
				float a = coverage(x, y, r, r) - (t > 0 ? coverage(x, y, r, r - t) : 0);
				image.setPixel(x, y, (Math.round(Math.max(0, Math.min(1, a)) * 255) << 24) | 0xFFFFFF);
			}
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/round/" + key);
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient " + key, image));
		CACHE.put(key, id);
		return id;
	}

	/** Wie viel des Punktes (x, y) im Kreis mit Mittelpunkt (c, c) und Radius r liegt. */
	private static float coverage(int x, int y, float c, float r) {
		if (r <= 0)
			return 0;
		float dx = x + 0.5F - c, dy = y + 0.5F - c;
		float d = (float) Math.sqrt(dx * dx + dy * dy) - r;
		if (d <= -0.75F)
			return 1;
		if (d >= 0.75F)
			return 0;
		int inside = 0;
		for (int sy = 0; sy < SAMPLES; sy++)
			for (int sx = 0; sx < SAMPLES; sx++) {
				float px = x + (sx + 0.5F) / SAMPLES - c, py = y + (sy + 0.5F) / SAMPLES - c;
				if (px * px + py * py <= r * r)
					inside++;
			}
		return inside / (float) (SAMPLES * SAMPLES);
	}
}
