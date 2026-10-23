package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.NativeImage;
import de.axoclient.mod.emote.Emotes;
import de.axoclient.mod.emote.SkinRender;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;

import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.HashMap;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

/**
 * Vorschaubilder der Emotes: der eigene Skin in der markantesten Pose ({@link SkinRender}), genau in der Pixelgröße,
 * in der sie im Menü erscheinen. Gerechnet wird im Hintergrund; bis ein Bild fertig ist (oder ohne Skin), zeigt das
 * Menü das Pixel-Symbol.
 */
final class EmotePreviews {
	private static final ExecutorService WORKER = Executors.newSingleThreadExecutor(r -> {
		Thread thread = new Thread(r, "AxoClient Emote-Vorschau");
		thread.setDaemon(true);
		return thread;
	});

	private static final Map<String, Future<int[]>> PENDING = new HashMap<>();
	private static final Map<String, Identifier> READY = new HashMap<>();
	private static final Set<String> FAILED = new HashSet<>();
	/** Für welchen Skin die Bilder gelten (ändert er sich, werden sie neu gerechnet). */
	private static byte[] skinShown;
	private static String builtin;
	private static int counter;

	private EmotePreviews() {}

	/** Vorschau für das Emote in pixels × pixels Bildschirmpunkten, oder null (noch nicht fertig / kein Skin). */
	static Identifier get(String emoteId, int pixels) {
		byte[] png = ownSkin();
		if (png == null || emoteId == null || pixels < 8)
			return null;
		if (png != skinShown) {
			clear();
			skinShown = png;
		}
		String key = emoteId + "|" + pixels;
		Identifier known = READY.get(key);
		if (known != null || FAILED.contains(key))
			return known;
		Future<int[]> job = PENDING.get(key);
		if (job == null) {
			String[] source = source(emoteId);
			if (source == null)
				return null; // eigenes Emote: Daten kommen noch vom Launcher
			PENDING.put(key, WORKER.submit(() -> {
				int[][] skin = SkinRender.decodePng(png);
				return skin == null ? null : SkinRender.render(skin, SkinRender.pose(source[0], source[1]), pixels);
			}));
			return null;
		}
		if (!job.isDone())
			return null;
		PENDING.remove(key);
		int[] image;
		try {
			image = job.get();
		} catch (Exception e) {
			image = null;
		}
		if (image == null) {
			FAILED.add(key);
			return null;
		}
		NativeImage texture = new NativeImage(pixels, pixels, true);
		for (int y = 0; y < pixels; y++)
			for (int x = 0; x < pixels; x++)
				texture.setPixel(x, y, image[y * pixels + x]);
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/emote_preview/" + Integer.toString(counter++, 36));
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient Emote-Vorschau", texture));
		READY.put(key, id);
		return id;
	}

	/**
	 * Der eigene Skin: unter der UUID des angemeldeten Kontos (ohne Bindestriche, so legt das Menü die Skins ab), zur
	 * Not unter der UUID, die der Launcher als "ich" meldet.
	 */
	private static byte[] ownSkin() {
		try {
			byte[] png = MenuData.skinPng(Minecraft.getInstance().getUser().getProfileId().toString().replace("-", ""));
			if (png != null)
				return png;
		} catch (RuntimeException ignored) {
			// kein Konto
		}
		String me = MenuData.meUuid;
		return me == null ? null : MenuData.skinPng(me.replace("-", "").toLowerCase(java.util.Locale.ROOT));
	}

	private static void clear() {
		for (Identifier id : READY.values())
			Minecraft.getInstance().getTextureManager().release(id);
		READY.clear();
		FAILED.clear();
		PENDING.clear();
	}

	/** {JSON, Animationsname}: eingebaute Emotes aus der Mod, eigene vom Launcher. */
	private static String[] source(String id) {
		Emotes.Emote emote = Emotes.find(id);
		if (emote != null && !emote.imported()) {
			if (builtin == null) {
				try (InputStream in = EmotePreviews.class.getResourceAsStream("/assets/axoclient/player_animations/emotes.json")) {
					builtin = in == null ? "" : new String(in.readAllBytes(), StandardCharsets.UTF_8);
				} catch (Exception e) {
					builtin = "";
				}
			}
			return builtin.isEmpty() ? null : new String[] { builtin, id };
		}
		return Emotes.customSource(id);
	}
}
