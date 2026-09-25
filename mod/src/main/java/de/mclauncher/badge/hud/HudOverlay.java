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
	/** Abstand von einer Textzeile zur nächsten, wenn ein Text mehrere Zeilen hat. */
	public static final int LINE_STEP = 10;

	/** Kantenlänge eines Ausrüstungsbildes und der Platz, den eines samt Abstand belegt. */
	public static final int ICON = 16;
	public static final int SLOT = 20;
	/** Breitester Haltbarkeitswert – damit die Anzeige beim Abnutzen nicht springt. */
	private static final String WIDEST_PERCENT = "100%";
	/** Rand der Hintergrundfläche um den Inhalt, bei Größe 100 %. */
	public static final int BACKGROUND_PAD = 3;

	/** Gesetzt, solange das Bearbeiten-Fenster offen ist: dann bekommen auch leere Anzeigen (Scoreboard) eine Größe. */
	public static boolean sampleMode;
	private static final int SAMPLE_WIDTH = 84;
	private static final int SAMPLE_HEIGHT = 45;

	private HudOverlay() {}

	public static void draw(HudConfig config, HudPainter painter, Map<HudModule, String> texts,
							int screenWidth, int screenHeight) {
		sampleMode = false;
		HudProfiles.applyPending(config);
		List<HudElement> shown = elements(config, painter, texts, false);

		// Als privat markierte Anzeigen wandern aus dem Spielbild heraus und werden stattdessen vom
		// Launcher gezeichnet (siehe HudPrivacy). Einen eigenen Schalter für alle gibt es nicht mehr.
		List<HudElement> secret = new ArrayList<>();
		for (HudElement element : new ArrayList<>(shown)) {
			if (!config.isPrivate(element.module()) || !HudPrivacy.canHide(config, element.module()))
				continue;
			shown.remove(element);
			secret.add(element);
		}

		drawGroups(config, shown, painter, texts, screenWidth, screenHeight);
		for (HudElement element : shown)
			draw(config, element, painter, texts, box(config, element, painter, texts, screenWidth, screenHeight));

		if (HudPrivacy.editorOpen()) {
			// Das Menü zeichnet gerade alles selbst: Launcher-Fenster leeren, sonst stünde es doppelt da
			HudPrivacy.send(config, List.of(), painter, texts, screenWidth, screenHeight);
			return;
		}
		HudPrivacy.send(config, secret, painter, texts, screenWidth, screenHeight);
		// Ohne Launcher zeichnet niemand die privaten Anzeigen. Das darf nicht stillschweigend
		// passieren, sonst sucht man im Spiel vergeblich nach seinen Koordinaten.
		if (!secret.isEmpty() && !HudPrivacy.available()) {
			int[] box = box(config, secret.get(0), painter, texts, screenWidth, screenHeight);
			painter.text("Privat: kein Launcher verbunden", box[0], box[1], 0xFFE36D6F, true);
		}
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
			if (!editor && !entry.visibleFor(metricOf(module, painter, entry)))
				continue; // "nur bei Bedarf": gerade nicht nötig
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

	/** Die gemeinsamen Hintergründe aller Gruppen; gehört hinter alle Anzeigen. */
	public static void drawGroups(HudConfig config, List<HudElement> elements, HudPainter painter,
								  Map<HudModule, String> texts, int screenWidth, int screenHeight) {
		for (HudConfig.Group group : config.groups) {
			int left = Integer.MAX_VALUE;
			int top = Integer.MAX_VALUE;
			int right = Integer.MIN_VALUE;
			int bottom = Integer.MIN_VALUE;
			for (HudElement element : elements) {
				if (!group.members.contains(element.module()))
					continue;
				int[] box = box(config, element, painter, texts, screenWidth, screenHeight);
				left = Math.min(left, box[0]);
				top = Math.min(top, box[1]);
				right = Math.max(right, box[0] + box[2]);
				bottom = Math.max(bottom, box[1] + box[3]);
			}
			if (left > right)
				continue;
			int corner = HudConfig.clampCorner(group.corner);
			HudSkin.rounded(painter, left - BACKGROUND_PAD, top - BACKGROUND_PAD, right - left + 2 * BACKGROUND_PAD,
				bottom - top + 2 * BACKGROUND_PAD, corner, group.argb());
			if (group.border)
				HudSkin.outline(painter, left - BACKGROUND_PAD, top - BACKGROUND_PAD, right - left + 2 * BACKGROUND_PAD,
					bottom - top + 2 * BACKGROUND_PAD, corner, 0x99FFFFFF);
		}
	}

	/** Zeichnet ein Stück samt Hintergrund an der Stelle, die {@link #box} geliefert hat. */
	public static void draw(HudConfig config, HudElement element, HudPainter painter,
							Map<HudModule, String> texts, int[] box) {
		HudConfig.Entry entry = config.get(element.module());
		if (entry.background && config.groupOf(element.module()) == null) {
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
		if (element.module() == HudModule.EFFECTS && entry.variant == 0) {
			// Die Symbole zeichnet Minecraft selbst (verschoben); im Editor nur Platzhalter
			if (painter.effectsSize() == null)
				for (int i = 0; i < 2; i++)
					HudSkin.rounded(painter, x + i * 25, y, 24, 24, 3, 0x66000000);
			return;
		}
		if (element.module() == HudModule.SCOREBOARD) {
			if (painter.scoreboardSize() != null) {
				painter.scoreboard(x, y);
				return;
			}
			painter.text("Scoreboard", x + (SAMPLE_WIDTH - painter.textWidth("Scoreboard")) / 2, y + 1, color, false);
			for (int i = 1; i < 5; i++)
				painter.text("Beispiel", x, y + i * (LINE + 1) + 1, color, false);
			return;
		}
		if (element.module().keys) {
			drawKeys(entry, painter, x, y);
			return;
		}
		if (!element.module().equipment) {
			String text = texts.get(element.module());
			if (text == null || text.isEmpty())
				return;
			color = entry.colorFor(HudText.metric(element.module()), color);
			String[] lines = text.split("\n");
			int widest = 0;
			for (String line : lines)
				widest = Math.max(widest, painter.textWidth(HudText.plain(line)));
			for (int i = 0; i < lines.length; i++) {
				int spare = widest - painter.textWidth(HudText.plain(lines[i]));
				int offset = entry.align == 1 ? spare / 2 : entry.align == 2 ? spare : 0;
				drawMarked(entry, painter, lines[i], x + offset, y + i * LINE_STEP, color);
			}
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
	 * Eine Zeile, deren Farbmarken (siehe {@link HudText#COLOUR_MARK}) die Farbe wechseln, z.B. X, Y
	 * und Z in eigenen Farben. Ohne Marken einfach die ganze Zeile in {@code color}.
	 */
	private static void drawMarked(HudConfig.Entry entry, HudPainter painter, String line, int x, int y, int color) {
		if (line.indexOf(HudText.COLOUR_MARK) < 0) {
			painter.text(line, x, y, color, entry.shadow);
			return;
		}
		int current = color;
		StringBuilder run = new StringBuilder();
		for (int i = 0; i < line.length(); i++) {
			char c = line.charAt(i);
			if (c != HudText.COLOUR_MARK || i + 1 >= line.length()) {
				run.append(c);
				continue;
			}
			if (run.length() > 0) {
				painter.text(run.toString(), x, y, current, entry.shadow);
				x += painter.textWidth(run.toString());
				run.setLength(0);
			}
			int axis = line.charAt(++i) - '0';
			current = axis >= 0 && axis < entry.axisRgb.length
				? (color & 0xFF000000) | entry.axisRgb[axis] : color;
		}
		if (run.length() > 0)
			painter.text(run.toString(), x, y, current, entry.shadow);
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
		painter.text(value, valueX, valueY, entry.colorFor(percentOf(value), color), entry.shadow);
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
		int height = Math.round(height(config, element, painter, texts) * factor);
		// Die Hintergrundfläche steht über den Inhalt hinaus und soll auch im Bild bleiben
		int pad = entry.background ? pad(entry) : 0;
		return new int[] {
			pad + clamp(toLeft(entry.anchorX, position(entry, element, true), width + 2 * pad, screenWidth, pad),
				width + 2 * pad, screenWidth),
			pad + clamp(toLeft(entry.anchorY, position(entry, element, false), height + 2 * pad, screenHeight, pad),
				height + 2 * pad, screenHeight),
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

	/**
	 * Linke bzw. obere Kante der Hintergrundfläche aus der gespeicherten Position. Anker 0: ab dem Bildrand, 1: ab
	 * der Mitte, 2: ab dem gegenüberliegenden Rand.
	 */
	public static int toLeft(int anchor, int stored, int size, int screen, int pad) {
		return switch (anchor) {
			case 1 -> (screen - size) / 2 + stored;
			case 2 -> screen - size - stored;
			default -> stored - pad;
		};
	}

	/** Umkehrung von {@link #toLeft}. */
	public static int toStored(int anchor, int left, int size, int screen, int pad) {
		return switch (anchor) {
			case 1 -> left - (screen - size) / 2;
			case 2 -> screen - size - left;
			default -> left + pad;
		};
	}

	/** Drittel des Bildes, in dem die Mitte des Stücks liegt: 0, 1 oder 2. */
	private static int zone(int center, int screen) {
		return center < screen / 3 ? 0 : center > screen * 2 / 3 ? 2 : 1;
	}

	/**
	 * Verschiebt ein Stück an eine neue Stelle (x, y = linke obere Ecke des Inhalts auf dem Bildschirm). Mit
	 * automatischem Anker wird dabei der nächstgelegene gewählt.
	 */
	public static void move(HudConfig.Entry entry, HudElement element, int x, int y, int boxWidth, int boxHeight,
							int screenWidth, int screenHeight) {
		if (entry.autoAnchor) {
			entry.anchorX = zone(x + boxWidth / 2, screenWidth);
			entry.anchorY = zone(y + boxHeight / 2, screenHeight);
		}
		int pad = entry.background ? pad(entry) : 0;
		int storedX = toStored(entry.anchorX, x - pad, boxWidth + 2 * pad, screenWidth, pad);
		int storedY = toStored(entry.anchorY, y - pad, boxHeight + 2 * pad, screenHeight, pad);
		if (element.isSlot()) {
			entry.slotX[element.slot()] = storedX;
			entry.slotY[element.slot()] = storedY;
			return;
		}
		entry.x = storedX;
		entry.y = storedY;
	}

	/** Breite ohne die eingestellte Größe. */
	public static int width(HudConfig config, HudElement element, HudPainter painter,
							Map<HudModule, String> texts) {
		HudConfig.Entry entry = config.get(element.module());
		if (element.module() == HudModule.EFFECTS && entry.variant == 0) {
			int[] size = painter.effectsSize();
			return size != null ? size[0] : sampleMode ? 49 : 0;
		}
		if (element.module() == HudModule.SCOREBOARD) {
			int[] size = painter.scoreboardSize();
			return size != null ? size[0] : sampleMode ? SAMPLE_WIDTH : 0;
		}
		if (element.module().keys)
			return KEYS_WIDTH;
		if (!element.module().equipment) {
			String text = texts.get(element.module());
			if (text == null || text.isEmpty())
				return 0;
			int widest = 0;
			for (String line : text.split("\n"))
				widest = Math.max(widest, painter.textWidth(HudText.plain(line)));
			return widest;
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
	public static int height(HudConfig config, HudElement element, HudPainter painter,
							Map<HudModule, String> texts) {
		HudConfig.Entry entry = config.get(element.module());
		if (element.module() == HudModule.EFFECTS && entry.variant == 0) {
			int[] size = painter.effectsSize();
			return size != null ? size[1] : 24;
		}
		if (element.module() == HudModule.SCOREBOARD) {
			int[] size = painter.scoreboardSize();
			return size != null ? size[1] : SAMPLE_HEIGHT;
		}
		if (element.module().keys)
			return keysHeight(entry);
		if (!element.module().equipment) {
			String text = texts.get(element.module());
			int lines = text == null || text.isEmpty() ? 1 : text.split("\n").length;
			return LINE + (lines - 1) * LINE_STEP;
		}
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

	// ---------- Tastenanzeige ----------

	private static final int KEY = 16;
	private static final int KEY_GAP = 2;
	private static final int KEYS_WIDTH = 3 * KEY + 2 * KEY_GAP;
	private static final int SPACE_HEIGHT = 8;

	/** Höhe der Tastenanzeige: WASD, dazu je nach Darstellung Maus und Leertaste. */
	private static int keysHeight(HudConfig.Entry entry) {
		int height = 2 * KEY + KEY_GAP;
		if (entry.variant == 2)
			height += KEY_GAP + SPACE_HEIGHT;
		if (entry.variant != 1)
			height += KEY_GAP + KEY;
		return height;
	}

	private static void drawKeys(HudConfig.Entry entry, HudPainter painter, int x, int y) {
		int color = entry.textArgb();
		drawKey(entry, painter, "W", HudInput.isDown(HudInput.FORWARD), x + KEY + KEY_GAP, y, KEY, KEY, color);
		int row = y + KEY + KEY_GAP;
		drawKey(entry, painter, "A", HudInput.isDown(HudInput.LEFT), x, row, KEY, KEY, color);
		drawKey(entry, painter, "S", HudInput.isDown(HudInput.BACK), x + KEY + KEY_GAP, row, KEY, KEY, color);
		drawKey(entry, painter, "D", HudInput.isDown(HudInput.RIGHT), x + 2 * (KEY + KEY_GAP), row, KEY, KEY, color);
		row += KEY + KEY_GAP;
		if (entry.variant == 2) {
			drawKey(entry, painter, "", HudInput.isDown(HudInput.JUMP), x, row, KEYS_WIDTH, SPACE_HEIGHT, color);
			row += SPACE_HEIGHT + KEY_GAP;
		}
		if (entry.variant != 1) {
			int half = (KEYS_WIDTH - KEY_GAP) / 2;
			drawKey(entry, painter, HudInput.cps(HudInput.ATTACK) + "", HudInput.isDown(HudInput.ATTACK), x, row, half, KEY, color);
			drawKey(entry, painter, HudInput.cps(HudInput.USE) + "", HudInput.isDown(HudInput.USE), x + half + KEY_GAP, row,
				KEYS_WIDTH - half - KEY_GAP, KEY, color);
		}
	}

	/** Schriftfarbe auf der gedrückten Taste: dunkel auf hellem, hell auf dunklem Grund. */
	private static int pressedText(int rgb) {
		int luminance = (((rgb >> 16) & 255) * 30 + ((rgb >> 8) & 255) * 59 + (rgb & 255) * 11) / 100;
		return luminance > 140 ? 0xFF000000 : 0xFFFFFFFF;
	}

	/** Eine Taste: gedrückt heller, mit Beschriftung mittig. */
	private static void drawKey(HudConfig.Entry entry, HudPainter painter, String label, boolean pressed, int x, int y,
								int width, int height, int color) {
		HudSkin.rounded(painter, x, y, width, height, 2, pressed ? 0xAA000000 | (entry.pressRgb & 0xFFFFFF) : 0x66000000);
		if (!label.isEmpty())
			painter.text(label, x + (width - painter.textWidth(label)) / 2, y + (height - LINE) / 2 + 1,
				pressed ? pressedText(entry.pressRgb) : color, false);
	}

	/** Der Zahlenwert einer Anzeige für Farbregeln und "nur bei Bedarf"; bei der Ausrüstung der schlechteste Platz. */
	public static Double metricOf(HudModule module, HudPainter painter, HudConfig.Entry entry) {
		if (!module.equipment)
			return HudText.metric(module);
		Double worst = null;
		for (HudSlot slot : HudSlot.values()) {
			if (!entry.slots[slot.ordinal()])
				continue;
			Double percent = percentOf(painter.durability(slot.ordinal()));
			if (percent != null && (worst == null || percent < worst))
				worst = percent;
		}
		return worst;
	}

	/** "87%" als Zahl, sonst null. */
	private static Double percentOf(String value) {
		if (value == null || !value.endsWith("%"))
			return null;
		try {
			return Double.parseDouble(value.substring(0, value.length() - 1).trim());
		} catch (NumberFormatException e) {
			return null;
		}
	}

	/** Hält die Anzeige im Bild, auch wenn sich die Fenstergröße geändert hat. */
	public static int clamp(int value, int size, int available) {
		return Math.max(0, Math.min(value, available - size));
	}
}
