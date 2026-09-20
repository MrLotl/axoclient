package de.mclauncher.badge.hud;

/**
 * Zeichnen im Bild – die eine Stelle, an der sich die Minecraft-Versionen unterscheiden.
 * Bewusst klein gehalten: jede Version liefert nur die Grundbausteine (Text, Fläche, ein
 * Ausrüstungsstück), die Anordnung der Anzeigen ist für alle Versionen gemeinsam.
 */
public interface HudPainter {
	int textWidth(String text);

	void text(String text, int x, int y, int argb, boolean shadow);

	/** Text mit Schatten. */
	default void text(String text, int x, int y, int argb) {
		text(text, x, y, argb, true);
	}

	void fill(int x, int y, int width, int height, int argb);

	/**
	 * Vergrößert alles Folgende bis zum passenden {@link #pop()}. Der Punkt (x, y) bleibt
	 * dabei stehen, die Anzeige wächst also nach rechts unten.
	 */
	void push(int x, int y, float scale);

	void pop();

	/** Ob auf dem Ausrüstungsplatz ({@link HudSlot}-Nummer) etwas liegt. */
	boolean hasEquipment(int slot);

	/** Zeichnet das Ausrüstungsstück (16x16) samt Anzahl und Haltbarkeitsbalken. */
	void equipment(int slot, int x, int y);

	/** Haltbarkeit als "87%" oder null, wenn der Gegenstand keine hat. */
	String durability(int slot);
}
