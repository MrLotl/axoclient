package de.mclauncher.badge;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;

import java.io.InputStream;

/**
 * Das Axolotl-Bild als Textur. Ohne Fabric API lädt Minecraft keine Bilder aus Mods, daher liest die Mod
 * das Bild selbst aus ihrer jar und meldet es beim ersten Gebrauch beim TextureManager an.
 */
public final class BadgeTexture {
	private static final Identifier ID = Identifier.fromNamespaceAndPath("mclauncher_badge", "dynamic/axolotl");
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
				Minecraft.getInstance().getTextureManager().register(ID, new DynamicTexture(() -> "AxoClient Badge", image));
				loaded = true;
			} catch (Exception e) {
				failed = true; // nicht bei jedem Bild erneut versuchen
			}
		}
		return loaded ? ID : null;
	}
}
