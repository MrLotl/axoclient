package de.mclauncher.badge.hud;

/** Eine Anzeige im Bild (HUD). Reihenfolge = Reihenfolge im Auswahlmenü. */
public enum HudModule {
	FPS("fps", "Bilder pro Sekunde", "FPS", 4, 4, true, false, null,
		new Metric(360, 5, new int[] { 0, 30, 60 }, new int[] { Metric.RED, Metric.YELLOW, Metric.GREEN }), "FPS"),
	PING("ping", "Ping", "Ping", 4, 16, true, false, null,
		new Metric(500, 10, new int[] { 0, 80, 150 }, new int[] { Metric.GREEN, Metric.YELLOW, Metric.RED }), "ms"),
	COORDS("koordinaten", "Koordinaten", "Pos", 4, 28, true, false, null, null, null),
	CHUNK("chunk", "Chunk", "Chunk", 4, 100, false, false,
		new String[] { "Chunk und Position", "Nur Chunk", "Nur Position im Chunk" }, null, null),
	DIRECTION("richtung", "Blickrichtung", "Richtung", 4, 40, false, false,
		new String[] { "Mit Achse", "Kurz (N, O, S, W)", "Nur Name" }, null, null),
	ANGLE("blickwinkel", "Blickwinkel", "Winkel", 4, 112, false, false, null, null, null),
	SPEED("geschwindigkeit", "Geschwindigkeit", "Tempo", 4, 124, false, false,
		new String[] { "Blöcke pro Sekunde", "km/h" },
		new Metric(40, 1, new int[] { 0, 5, 12 }, new int[] { Metric.WHITE, Metric.GREEN, Metric.YELLOW }), "B/s"),
	BIOME("biom", "Biom", "Biom", 4, 136, false, false, null, null, null),
	TIME("uhrzeit", "Uhrzeit", "Zeit", 4, 52, false, false,
		new String[] { "24 Stunden", "Mit Sekunden", "12 Stunden" }, null, null),
	WORLD_TIME("spielzeit", "Spielzeit", "Welt", 4, 148, false, false,
		new String[] { "Uhrzeit und Tag", "Nur Uhrzeit", "Nur Tag" }, null, null),
	MEMORY("speicher", "Arbeitsspeicher", "RAM", 4, 160, false, false,
		new String[] { "Belegt / Maximal", "Nur belegt", "Prozent" },
		new Metric(100, 5, new int[] { 0, 70, 90 }, new int[] { Metric.GREEN, Metric.YELLOW, Metric.RED }), "GB"),
	ARMOR("ruestung", "Ausrüstung", "", 4, 68, true, true, null,
		new Metric(100, 5, new int[] { 0, 25, 60 }, new int[] { Metric.RED, Metric.YELLOW, Metric.GREEN }), null);

	/**
	 * Was sich für Farbregeln und "nur bei Bedarf" messen lässt: ein Zahlenwert mit Bereich und
	 * sinnvollen Standardregeln.
	 *
	 * @param max größter Wert der Regler (Prozent bei Speicher und Haltbarkeit)
	 */
	public record Metric(int max, int step, int[] defaultAt, int[] defaultRgb) {
		public static final int RED = 0xEB5757;
		public static final int YELLOW = 0xF2C94C;
		public static final int GREEN = 0x6FCF97;
		public static final int WHITE = 0xFFFFFF;
	}

	/** Name in der Einstellungsdatei (darf sich nicht mehr ändern). */
	public final String id;
	/** Beschriftung im Auswahlmenü. */
	public final String label;
	/** Kurzer Name, der auf Wunsch vor dem Wert steht. */
	public final String shortLabel;
	public final int defaultX;
	public final int defaultY;
	public final boolean defaultEnabled;
	/**
	 * Ob die Anzeige Ausrüstungsplätze zeigt. Nur dann gibt es Ausrichtung, Haltbarkeit,
	 * die Auswahl der Plätze und "einzeln verschieben".
	 */
	public final boolean equipment;
	/** Namen der Darstellungen zum Durchklicken; null, wenn es nur eine gibt. */
	public final String[] variants;
	/** Zahlenwert für Farbregeln; null bei Anzeigen ohne. */
	public final Metric metric;
	/** Einheit hinter dem Wert; null, wenn es keine gibt. */
	public final String unit;

	HudModule(String id, String label, String shortLabel, int defaultX, int defaultY, boolean defaultEnabled,
			  boolean equipment, String[] variants, Metric metric, String unit) {
		this.id = id;
		this.label = label;
		this.shortLabel = shortLabel;
		this.defaultX = defaultX;
		this.defaultY = defaultY;
		this.defaultEnabled = defaultEnabled;
		this.equipment = equipment;
		this.variants = variants;
		this.metric = metric;
		this.unit = unit;
	}
}
