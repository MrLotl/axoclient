package de.mclauncher.badge.hud;

/**
 * Ein Ausrüstungsplatz der Rüstungsanzeige. Die Reihenfolge ist auch die Reihenfolge im Bild;
 * {@code id} steht so in der Einstellungsdatei und darf sich nicht mehr ändern.
 */
public enum HudSlot {
	HELMET("helm", "Helm", true),
	CHEST("brust", "Brust", true),
	LEGS("hose", "Hose", true),
	BOOTS("schuhe", "Schuhe", true),
	MAINHAND("haupthand", "Hand", true),
	/** Standardmäßig aus, damit sich für alle bisherigen Einstellungen nichts ändert. */
	OFFHAND("nebenhand", "2.Hand", false);

	public final String id;
	/** Kurze Beschriftung im Auswahlmenü (der Platz dort ist knapp). */
	public final String label;
	public final boolean defaultEnabled;

	HudSlot(String id, String label, boolean defaultEnabled) {
		this.id = id;
		this.label = label;
		this.defaultEnabled = defaultEnabled;
	}

	public static final int COUNT = values().length;
}
