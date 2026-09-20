package de.mclauncher.badge.hud;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Ordnet die Anzeigen an und zeichnet sie. Hier liegt die ganze Anordnung – auch die der
 * Ausrüstungsplätze –, damit sie für alle Minecraft-Versionen dieselbe ist.
 */
public final class HudOverlay {
	/**
	 * Höhe einer Textzeile. Genau so hoch sind die Buchstaben – nicht 12 wie der Zeilenabstand,
	 * sonst stünde der Text in seiner Hintergrundfläche oben statt in der Mitte, und beim
	 * Vergrößern würde die Lücke darunter mitwachsen.
	 */
	public static final int LINE = 8;

	/** Kantenlänge eines Ausrüstungsbildes und der Platz, den eines samt Abstand belegt. */
	public static final int ICON = 16;
	public static final int SLOT = 20;
	/** Breitester Haltbarkeitswert – damit die Anzeige beim Abnutzen nicht springt. */
	private static final String WIDEST_PERCENT = "100%";
	/** Rand der Hintergrundfläche um den Inhalt, bei Größe 100 %. */
	public static final int BACKGROUND_PAD = 3;

	private HudOverlay() {}

	public static void draw(HudConfig config, HudPainter painter, Map<HudModule, String> texts,
							int screenWidth, int screenHeight) {
		for (HudElement element : elements(config, painter, texts, false))
			draw(config, element, painter, texts, box(config, element, painter, texts, screenWidth, screenHeight));
	}

	/**
	 * Alle Stücke, die gerade im Bild stehen.
	 *
	 * @param editor im Auswahlmenü werden auch leere Stücke aufgeführt, damit man sie greifen kann
	 */
	public static List<HudElement> elements(HudConfig config, HudPainter painter,
											Map<HudModule, String> texts, boolean editor) {
		List<HudElement> elements = new ArrayList<>();
		for (HudModule module : HudModule.values()) {
			if (!config.isEnabled(module))
				continue;
			HudConfig.Entry entry = config.get(module);
			if (module.equipment && entry.split) {
				for (HudSlot slot : HudSlot.values()) {
					if (!entry.slots[slot.ordinal()])
						continue;
					if (!editor && !painter.hasEquipment(slot.ordinal()))
						continue;
					elements.add(new HudElement(module, slot.ordinal()));
				}
				continue;
			}
			HudElement whole = HudElement.of(module);
			if (!editor && width(config, whole, painter, texts) <= 0)
				continue; // z.B. Ping im Einzelspieler: nichts anzuzeigen
			elements.add(whole);
		}
		return elements;
	}

	/** Zeichnet ein Stück samt Hintergrund an der Stelle, die {@link #box} geliefert hat. */
	public static void draw(HudConfig config, HudElement element, HudPainter painter,
							Map<HudModule, String> texts, int[] box) {
		HudConfig.Entry entry = config.get(element.module());
		if (entry.background) {
			int pad = pad(entry);
			int backX = box[0] - pad;
			int backY = box[1] - pad;
			int backWidth = box[2] + 2 * pad;
			int backHeight = box[3] + 2 * pad;
			int corner = HudConfig.clampCorner(entry.corner);
			HudSkin.rounded(painter, backX, backY, backWidth, backHeight, corner, entry.backgroundArgb());
			if (entry.border)
				// Rahmen in der Textfarbe, etwas zurückgenommen
				HudSkin.outline(painter, backX, backY, backWidth, backHeight, corner,
					0x99000000 | (entry.textArgb() & 0xFFFFFF));
		}
		painter.push(box[0], box[1], entry.factor());
		content(entry, element, painter, texts, box[0], box[1]);
		painter.pop();
	}

	/** Der Inhalt in Originalgröße; die Vergrößerung erledigt der Aufrufer. */
	private static void content(HudConfig.Entry entry, HudElement element, HudPainter painter,
								Map<HudModule, String> texts, int x, int y) {
		int color = entry.textArgb();
		if (!element.module().equipment) {
			String text = texts.get(element.module());
			if (text != null && !text.isEmpty())
				painter.text(text, x, y, color, entry.shadow);
			return;
		}
		if (element.isSlot()) {
			drawSlot(entry, painter, element.slot(), x, y, color, false);
			return;
		}
		int index = 0;
		for (HudSlot slot : HudSlot.values()) {
			if (!entry.slots[slot.ordinal()] || !painter.hasEquipment(slot.ordinal()))
				continue;
			int slotX = entry.vertical ? x : x + index * SLOT;
			int slotY = entry.vertical ? y + index * SLOT : y;
			index++;
			drawSlot(entry, painter, slot.ordinal(), slotX, slotY, color, !entry.vertical);
		}
	}

	/**
	 * Ein Ausrüstungsstück.
	 *
	 * @param below Haltbarkeit mittig darunter statt rechts daneben
	 */
	private static void drawSlot(HudConfig.Entry entry, HudPainter painter, int slot, int x, int y,
								 int color, boolean below) {
		painter.equipment(slot, x, y);
		if (!entry.percent)
			return;
		String value = painter.durability(slot);
		if (value == null)
			return;
		int valueX = below ? x + (ICON - painter.textWidth(value)) / 2 : x + SLOT;
		int valueY = below ? y + ICON + 2 : y + (ICON - 8) / 2;
		painter.text(value, valueX, valueY, color, entry.shadow);
	}

	/**
	 * Platz eines Stücks auf dem Bildschirm als {x, y, Breite, Höhe}; die eingestellte Größe ist
	 * schon eingerechnet und das Stück bleibt im Bild, auch nach einer Fenstergrößenänderung.
	 */
	public static int[] box(HudConfig config, HudElement element, HudPainter painter,
							Map<HudModule, String> texts, int screenWidth, int screenHeight) {
		HudConfig.Entry entry = config.get(element.module());
		float factor = entry.factor();
		int width = Math.round(width(config, element, painter, texts) * factor);
		int height = Math.round(height(config, element, painter) * factor);
		// Die Hintergrundfläche steht über den Inhalt hinaus und soll auch im Bild bleiben
		int pad = entry.background ? pad(entry) : 0;
		return new int[] {
			pad + clamp(position(entry, element, true) - pad, width + 2 * pad, screenWidth),
			pad + clamp(position(entry, element, false) - pad, height + 2 * pad, screenHeight),
			width,
			height
		};
	}

	/** Rand der Hintergrundfläche; wächst mit der eingestellten Größe mit. */
	public static int pad(HudConfig.Entry entry) {
		return Math.max(1, Math.round(BACKGROUND_PAD * entry.factor()));
	}

	/** Die gespeicherte Position eines Stücks (die der Anzeige oder die des einzelnen Stücks). */
	public static int position(HudConfig.Entry entry, HudElement element, boolean horizontal) {
		if (!element.isSlot())
			return horizontal ? entry.x : entry.y;
		return horizontal ? entry.slotX[element.slot()] : entry.slotY[element.slot()];
	}

	/** Verschiebt ein Stück an eine neue Stelle. */
	public static void move(HudConfig.Entry entry, HudElement element, int x, int y) {
		if (element.isSlot()) {
			entry.slotX[element.slot()] = x;
			entry.slotY[element.slot()] = y;
			return;
		}
		entry.x = x;
		entry.y = y;
	}

	/** Breite ohne die eingestellte Größe. */
	public static int width(HudConfig config, HudElement element, HudPainter painter,
							Map<HudModule, String> texts) {
		HudConfig.Entry entry = config.get(element.module());
		if (!element.module().equipment) {
			String text = texts.get(element.module());
			return text == null || text.isEmpty() ? 0 : painter.textWidth(text);
		}
		int value = entry.percent ? SLOT - ICON + painter.textWidth(WIDEST_PERCENT) : 0;
		if (element.isSlot())
			// Einzeln: nur Platz lassen, wenn das Stück überhaupt eine Haltbarkeit hat
			return ICON + (painter.durability(element.slot()) == null ? 0 : value);
		if (entry.vertical)
			return ICON + value;
		// Nebeneinander steht die Haltbarkeit mittig unter dem Bild und ist breiter als dieses;
		// der Abstand hinter dem letzten Stück bleibt dann stehen.
		return Math.max(1, occupied(entry, painter)) * SLOT - (entry.percent ? 0 : SLOT - ICON);
	}

	/** Höhe ohne die eingestellte Größe. */
	public static int height(HudConfig config, HudElement element, HudPainter painter) {
		HudConfig.Entry entry = config.get(element.module());
		if (!element.module().equipment)
			return LINE;
		if (element.isSlot())
			return ICON;
		if (entry.vertical)
			return Math.max(1, occupied(entry, painter)) * SLOT - (SLOT - ICON);
		return entry.percent ? ICON + 10 : ICON;
	}

	/** Wie viele Ausrüstungsplätze gezeigt werden; leere lassen wir aus, damit keine Lücken bleiben. */
	private static int occupied(HudConfig.Entry entry, HudPainter painter) {
		int count = 0;
		for (HudSlot slot : HudSlot.values())
			if (entry.slots[slot.ordinal()] && painter.hasEquipment(slot.ordinal()))
				count++;
		return count;
	}

	/** Hält die Anzeige im Bild, auch wenn sich die Fenstergröße geändert hat. */
	public static int clamp(int value, int size, int available) {
		return Math.max(0, Math.min(value, available - size));
	}
}
