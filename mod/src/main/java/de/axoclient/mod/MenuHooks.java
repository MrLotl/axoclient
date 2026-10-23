package de.axoclient.mod;

import java.util.function.Consumer;

/**
 * Verbindung zum neuen Axo-Menü, das es nur in Versionen ab 26.2 gibt (Quelle src/emote26). Gemeinsamer Code fragt nur
 * diese Felder ab; sind sie leer, bleibt alles beim alten Anzeigen-Fenster.
 */
public final class MenuHooks {
	/** Öffnet das Axo-Menü (z. B. aus dem Pausenmenü); null = Version ohne neues Menü. */
	public static volatile Runnable openMenu;

	/**
	 * Ob die Version eigene Minecraft-Tastenbelegungen hat (Optionen → Steuerung → AxoClient). Dann gelten die alten
	 * Tasten aus der Anzeigen-Einstellung (Menü, Emotes) nicht mehr.
	 */
	public static volatile boolean ownKeys;

	/** Zeichnet zusätzlich etwas über den Anzeigen (Einblendungen); bekommt den Grafik-Kontext der Version. */
	public static volatile Consumer<Object> overlay;

	/**
	 * Anzeigen in einem feineren Raster zeichnen (wie das Axo-Menü, bei 1080p zwei Bildschirmpunkte je Einheit statt der
	 * groben GUI-Skalierung); die Minecraft-Schrift bleibt dabei pixelgenau scharf. Positionen gelten in diesem Raster.
	 */
	public static volatile boolean fineHud;

	/** Solange true, zeichnet das Spiel die Anzeigen nicht selbst (der Bearbeiten-Modus zeichnet sie). */
	public static volatile boolean hideHud;

	private MenuHooks() {}
}
