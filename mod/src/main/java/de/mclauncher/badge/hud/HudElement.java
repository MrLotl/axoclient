package de.mclauncher.badge.hud;

/**
 * Ein einzeln platzierbares Stück im Bild: entweder eine ganze Anzeige oder – wenn die
 * Rüstungsanzeige auf "einzeln verschieben" steht – ein einzelner Ausrüstungsplatz.
 *
 * @param slot Nummer des Ausrüstungsplatzes oder {@link #WHOLE} für die ganze Anzeige
 */
public record HudElement(HudModule module, int slot) {
	/** Steht für die ganze Anzeige statt für einen einzelnen Ausrüstungsplatz. */
	public static final int WHOLE = -1;

	public static HudElement of(HudModule module) {
		return new HudElement(module, WHOLE);
	}

	public boolean isSlot() {
		return slot != WHOLE;
	}
}
