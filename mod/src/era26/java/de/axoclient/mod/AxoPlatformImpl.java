package de.axoclient.mod;

import com.mojang.blaze3d.platform.NativeImage;
import de.axoclient.mod.hud.AxoPlatform;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.PlayerModelType;

import java.io.ByteArrayInputStream;
import java.nio.file.Path;
import java.util.Arrays;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;

/** Anbindung von "Skins & Capes" an Minecraft 26.x. */
public final class AxoPlatformImpl implements AxoPlatform {
	/** Erfolglos gelesene Bilder: nicht bei jedem Bild erneut versuchen. */
	private static final Object FAILED = new Object();

	private final Map<String, Object> textures = new HashMap<>();

	/** Eigener Skin bzw. Minecraft-Umhang, den der Spieler gerade im Menü gewechselt hat (sonst null). */
	public static volatile ClientAsset.Texture ownSkin;
	public static volatile PlayerModelType ownModel;
	public static volatile boolean ownCapeChanged;
	public static volatile ClientAsset.Texture ownCape;

	static void install() {
		AxoPlatform.Holder.current = new AxoPlatformImpl();
	}

	/** Ob die UUID die des angemeldeten Spielers ist. */
	public static boolean isSelf(UUID id) {
		return id != null && id.equals(Minecraft.getInstance().getUser().getProfileId());
	}

	@Override
	public String playerName() {
		return Minecraft.getInstance().getUser().getName();
	}

	@Override
	public String playerId() {
		return Minecraft.getInstance().getUser().getProfileId().toString().replace("-", "");
	}

	@Override
	public String accessToken() {
		return Minecraft.getInstance().getUser().getAccessToken();
	}

	@Override
	public Path launcherHome() {
		String home = System.getProperty("axoclient.home");
		return home == null || home.isBlank() ? null : Path.of(home);
	}

	@Override
	public String serviceUrl() {
		return BadgeService.apiBase();
	}

	@Override
	public String serviceToken() {
		// Die Mod bekommt nie das Token des Online-Service (-Daxoclient.token ist nur für die Launcher-Verbindung)
		return null;
	}

	@Override
	public Path gameDir() {
		return Minecraft.getInstance().gameDirectory.toPath();
	}

	@Override
	public Object texture(String key, byte[] png) {
		if (png == null || png.length == 0)
			return null;
		Object byIdentity = known.get(png);
		if (byIdentity != null)
			return byIdentity == FAILED ? null : byIdentity;
		Object result = register(key, png);
		known.put(png, result == null ? FAILED : result);
		return result;
	}

	/** Schneller Weg für dasselbe Bild in jedem Bild: nach dem Array selbst, nicht nach seinem Inhalt. */
	private final Map<byte[], Object> known = new java.util.IdentityHashMap<>();

	private Object register(String key, byte[] png) {
		String name = key.toLowerCase(Locale.ROOT).replaceAll("[^a-z0-9/._-]", "_") + "_"
			+ Integer.toHexString(Arrays.hashCode(png));
		Object known = textures.get(name);
		if (known != null)
			return known == FAILED ? null : known;
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/" + name);
		try {
			NativeImage image = NativeImage.read(new ByteArrayInputStream(png));
			Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient " + name, image));
			textures.put(name, id);
			return id;
		} catch (Exception e) {
			textures.put(name, FAILED);
			return null;
		}
	}

	@Override
	public void showOwnSkin(byte[] png, boolean slim) {
		Object id = texture("own/skin", png);
		if (id instanceof Identifier texture) {
			ownSkin = new ClientAsset.ResourceTexture(texture, texture);
			ownModel = slim ? PlayerModelType.SLIM : PlayerModelType.WIDE;
		}
	}

	@Override
	public void showOwnCape(byte[] png) {
		Object id = png == null ? null : texture("own/cape", png);
		ownCape = id instanceof Identifier texture ? new ClientAsset.ResourceTexture(texture, texture) : null;
		ownCapeChanged = true;
	}

	@Override
	public void runOnMain(Runnable task) {
		Minecraft.getInstance().execute(task);
	}

	@Override
	public Object logo() {
		return BadgeTexture.get();
	}
}
