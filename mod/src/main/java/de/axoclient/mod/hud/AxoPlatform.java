package de.axoclient.mod.hud;

import java.nio.file.Path;

/**
 * Was der Bereich "Skins & Capes" von der Minecraft-Version braucht. Versionen ohne diese Anbindung setzen
 * {@link #current} nicht; dann fehlt der Bereich im Menü einfach.
 */
public interface AxoPlatform {
	/** Die Anbindung der laufenden Version oder null. */
	final class Holder {
		public static volatile AxoPlatform current;

		private Holder() {}
	}

	static AxoPlatform current() {
		return Holder.current;
	}

	/** Spielername der Sitzung. */
	String playerName();

	/** UUID der Sitzung ohne Bindestriche. */
	String playerId();

	/** Zugangsschlüssel der Sitzung für die Minecraft-Dienste (Skin hochladen, Umhang wählen). */
	String accessToken();

	/** Ordner des Launchers (dort liegt die Skin-Bibliothek) oder null, wenn das Spiel nicht vom Launcher kommt. */
	Path launcherHome();

	/** Adresse des AxoClient-Dienstes oder null. */
	String serviceUrl();

	/** Anmeldung beim AxoClient-Dienst (vom Launcher) oder null. */
	String serviceToken();

	/** Spielordner (dort liest die Mod den eigenen AxoClient-Umhang sofort). */
	Path gameDir();

	/**
	 * Textur aus einem PNG; beim ersten Aufruf wird sie angemeldet. Nur auf dem Render-Thread aufrufen.
	 *
	 * @return Griff für {@link HudPainter#image} oder null, wenn das Bild nicht lesbar ist
	 */
	Object texture(String key, byte[] png);

	/** Zeigt den eigenen Skin sofort im Spiel (nur bei dir; andere sehen ihn nach dem Neuladen). */
	void showOwnSkin(byte[] png, boolean slim);

	/** Zeigt den eigenen Minecraft-Umhang sofort; null = keiner. */
	void showOwnCape(byte[] png);

	/** Führt etwas auf dem Render-Thread aus. */
	void runOnMain(Runnable task);

	/** Das Axolotl-Logo (60x46) als Textur oder null. */
	default Object logo() {
		return null;
	}
}
