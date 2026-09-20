package de.mclauncher.badge;

import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.texture.NativeImageBackedTexture;
import net.minecraft.util.Identifier;

import java.io.InputStream;

/**
 * Das Axolotl-Bild als Textur. Ohne Fabric API lädt Minecraft keine Bilder aus Mods, daher liest die Mod
 * das Bild selbst aus ihrer jar und meldet es beim ersten Gebrauch beim TextureManager an.
 */
public final class BadgeTexture {
	private static final Identifier ID = Identifier.of("mclauncher_badge", "dynamic/axolotl");
	private static final String PATH = "/assets/mclauncher_badge/textures/gui/axolotl.png";

	private static boolean loaded;
	private static boolean failed;

	private BadgeTexture() {}

	/** Muss auf dem Render-Thread aufgerufen werden; liefert null, falls das Bild nicht geladen werden konnte. */
	public static Identifier get() {
		if (!loaded && !failed) {
			try (InputStream in = BadgeTexture.class.getResourceAsStream(PATH)) {
				if (in == null)
					throw new IllegalStateException("Bild fehlt in der Mod: " + PATH);
				NativeImage image = NativeImage.read(in);
				MinecraftClient.getInstance().getTextureManager().registerTexture(ID, new NativeImageBackedTexture(() -> "AxoClient Badge", image));
				loaded = true;
			} catch (Exception e) {
				failed = true; // nicht bei jedem Bild erneut versuchen
			}
		}
		return loaded ? ID : null;
	}
}
