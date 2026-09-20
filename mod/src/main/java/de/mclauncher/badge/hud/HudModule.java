package de.mclauncher.badge.hud;

/** Eine Anzeige im Bild (HUD). Reihenfolge = Reihenfolge im Auswahlmenü. */
public enum HudModule {
	FPS("fps", "Bilder pro Sekunde", 4, 4, true, false),
	PING("ping", "Ping", 4, 16, true, false),
	COORDS("koordinaten", "Koordinaten", 4, 28, true, false),
	CHUNK("chunk", "Chunk", 4, 100, false, false),
	DIRECTION("richtung", "Blickrichtung", 4, 40, false, false),
	ANGLE("blickwinkel", "Blickwinkel", 4, 112, false, false),
	SPEED("geschwindigkeit", "Geschwindigkeit", 4, 124, false, false),
	BIOME("biom", "Biom", 4, 136, false, false),
	TIME("uhrzeit", "Uhrzeit", 4, 52, false, false),
	WORLD_TIME("spielzeit", "Spielzeit", 4, 148, false, false),
	MEMORY("speicher", "Arbeitsspeicher", 4, 160, false, false),
	ARMOR("ruestung", "Ausrüstung", 4, 68, true, true);

	/** Name in der Einstellungsdatei (darf sich nicht mehr ändern). */
	public final String id;
	/** Beschriftung im Auswahlmenü. */
	public final String label;
	public final int defaultX;
	public final int defaultY;
	public final boolean defaultEnabled;
	/**
	 * Ob die Anzeige Ausrüstungsplätze zeigt. Nur dann gibt es Ausrichtung, Haltbarkeit,
	 * die Auswahl der Plätze und "einzeln verschieben".
	 */
	public final boolean equipment;

	HudModule(String id, String label, int defaultX, int defaultY, boolean defaultEnabled, boolean equipment) {
		this.id = id;
		this.label = label;
		this.defaultX = defaultX;
		this.defaultY = defaultY;
		this.defaultEnabled = defaultEnabled;
		this.equipment = equipment;
	}
}
