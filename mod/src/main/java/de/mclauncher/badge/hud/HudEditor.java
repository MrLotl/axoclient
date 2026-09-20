package de.mclauncher.badge.hud;

import java.util.ArrayList;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;

/**
 * Das Menü der Anzeigen. Es steht fest in der Bildmitte: links die Liste der Anzeigen, rechts
 * alles zur ausgewählten Anzeige. Verschoben wird nichts davon – dafür gibt es die Schaltfläche
 * "Overlay bearbeiten", die das Menü durch eine schmale Leiste ersetzt und alle Anzeigen zum
 * Ziehen freigibt.
 *
 * Die Bedienung ist für alle Minecraft-Versionen gleich; die jeweilige Version liefert nur das
 * Fenster und das Zeichnen.
 */
public final class HudEditor {
	private static final int WIDTH = 304;
	private static final int PAD = 8;
	private static final int COL_GAP = 8;
	private static final int LEFT_WIDTH = 128;
	private static final int RIGHT_WIDTH = WIDTH - 2 * PAD - LEFT_WIDTH - COL_GAP;
	private static final int HEADER = 19;
	private static final int ROW = 12;
	private static final int BUTTON = 14;
	private static final int GAP = 5;
	/** Die Ausrüstungsplätze stehen in zwei Reihen zu dritt. */
	private static final int SLOT_COLUMNS = 3;

	/** Die Leiste im Bearbeiten-Modus. */
	private static final int BAR_WIDTH = 300;
	private static final int BAR_HEIGHT = 28;

	/** Der Farbwähler schwebt über dem Menü, damit dieses nicht höher wird. */
	private static final int PICKER_WIDTH = 136;
	private static final int PICKER_PAD = 6;
	private static final int PICKER_LABEL = 16;
	private static final int PICKER_VALUE = 22;
	private static final int TAB_WIDTH = 40;
	private static final int TAB_GAP = 2;
	private static final int FIELD_HEIGHT = 14;
	private static final int SWATCH = 10;
	private static final int SWATCH_STEP = 10;

	/**
	 * Reihenfolge der Umschaltflächen im Farbwähler; die Nummer steht in der Einstellungsdatei.
	 * RGB ist die 0 und damit der Standardfall.
	 */
	private static final String[] COLOUR_MODES = { "RGB", "HSL", "Hex" };
	private static final int MODE_HSL = 1;
	private static final int MODE_HEX = 2;

	/** Was gerade mit der Maus gezogen wird. */
	private enum Drag { NONE, ELEMENT, SLIDER }

	/** Welcher Regler gezogen wird. */
	private enum Slider { SIZE, ALPHA, CORNER, GRID, RED, GREEN, BLUE, HUE, SATURATION, LIGHTNESS }

	/** Für welche Farbe der Wähler gerade offen ist. */
	private enum Colour { NONE, TEXT, BACKGROUND }

	private final HudConfig config;
	private HudModule selected = HudModule.values()[0];
	/** Im Bearbeiten-Modus verschwindet das Menü und alles im Bild lässt sich ziehen. */
	private boolean editing;
	private Colour picker = Colour.NONE;
	/**
	 * Farbton, Sättigung und Helligkeit gehören dem Wähler, solange er offen ist. Würde jeder
	 * Reglerzug aus der Farbe zurückgerechnet, verlöre der Farbton bei Grau seinen Wert.
	 */
	private final int[] hsl = new int[3];
	/** Was gerade ins Hex-Feld getippt wurde, ohne das Doppelkreuz. */
	private String hexInput = "";

	private Drag drag = Drag.NONE;
	private Slider slider = Slider.SIZE;
	/** Schiene des Reglers, der gerade gezogen wird. */
	private int trackX;
	private int trackWidth;

	private HudElement dragged;
	private int grabX;
	private int grabY;
	private int guideX = -1;
	private int guideY = -1;

	public HudEditor(HudConfig config) {
		this.config = config;
	}

	// ---------- Zeichnen ----------

	public void render(HudPainter painter, Map<HudModule, String> texts, int width, int height,
					   int mouseX, int mouseY) {
		Map<HudModule, String> shown = withPlaceholders(texts);
		painter.fill(0, 0, width, height, HudSkin.SCRIM);
		if (editing && drag == Drag.ELEMENT)
			HudSnap.drawGrid(config, painter, width, height);

		for (HudElement element : HudOverlay.elements(config, painter, shown, true)) {
			int[] box = HudOverlay.box(config, element, painter, shown, width, height);
			if (editing)
				renderFrame(painter, element, box, mouseX, mouseY);
			HudOverlay.draw(config, element, painter, shown, box);
		}

		if (editing) {
			if (drag == Drag.ELEMENT)
				HudSnap.drawGuides(painter, guideX, guideY, width, height);
			renderBar(painter, bar(width, height), mouseX, mouseY);
			return;
		}

		Layout layout = layout(width, height);
		closePickerIfGone(layout);
		renderMenu(painter, layout, mouseX, mouseY);
		if (picker != Colour.NONE)
			renderPicker(painter, pick(layout, height), mouseX, mouseY);
	}

	/** Rahmen um ein Stück, solange man es anfassen kann. */
	private void renderFrame(HudPainter painter, HudElement element, int[] box, int mouseX, int mouseY) {
		boolean hovered = drag == Drag.NONE && inside(mouseX, mouseY, box, 3);
		boolean active = element.equals(dragged) || element.module() == selected;
		if (!hovered && !active)
			return;
		HudConfig.Entry entry = config.get(element.module());
		int pad = (entry.background ? HudOverlay.pad(entry) : 0) + 3;
		int corner = HudConfig.clampCorner(entry.corner) + 1;
		HudSkin.rounded(painter, box[0] - pad, box[1] - pad, box[2] + 2 * pad, box[3] + 2 * pad, corner,
			hovered ? HudSkin.ACCENT_FAINT : HudSkin.GLASS_SOFT);
		HudSkin.outline(painter, box[0] - pad, box[1] - pad, box[2] + 2 * pad, box[3] + 2 * pad, corner,
			active ? HudSkin.ACCENT : HudSkin.BORDER);
	}

	private void renderMenu(HudPainter painter, Layout layout, int mouseX, int mouseY) {
		int x = layout.x;
		HudSkin.card(painter, x, layout.y, WIDTH, layout.height);
		painter.text("AxoClient", x + PAD, layout.y + 6, HudSkin.ACCENT);
		HudSkin.right(painter, "Anzeigen im Bild", x + WIDTH - PAD, layout.y + 6, HudSkin.MUTED);
		HudSkin.separator(painter, x + 1, layout.y + HEADER, WIDTH - 2);
		painter.fill(x + PAD + LEFT_WIDTH + COL_GAP / 2, layout.y + HEADER + 5, 1,
			layout.actionsY - layout.y - HEADER - 9, HudSkin.LINE);

		renderModules(painter, layout, mouseX, mouseY);
		HudSkin.separator(painter, layout.leftX, layout.copyLineY, LEFT_WIDTH);
		HudSkin.button(painter, layout.leftX, layout.copyY, LEFT_WIDTH, BUTTON, "Hintergrund für alle",
			inside(mouseX, mouseY, layout.leftX, layout.copyY, LEFT_WIDTH, BUTTON), false);
		renderSettings(painter, layout, mouseX, mouseY);

		HudSkin.separator(painter, x + 1, layout.actionsY, WIDTH - 2);
		HudSkin.button(painter, layout.standardX, layout.buttonY, layout.smallWidth, BUTTON, "Standard",
			inside(mouseX, mouseY, layout.standardX, layout.buttonY, layout.smallWidth, BUTTON), false);
		HudSkin.button(painter, layout.editX, layout.buttonY, layout.editWidth, BUTTON, "Overlay bearbeiten",
			inside(mouseX, mouseY, layout.editX, layout.buttonY, layout.editWidth, BUTTON), false);
		HudSkin.button(painter, layout.doneX, layout.buttonY, layout.smallWidth, BUTTON, "Fertig",
			inside(mouseX, mouseY, layout.doneX, layout.buttonY, layout.smallWidth, BUTTON), true);
	}

	private void renderModules(HudPainter painter, Layout layout, int mouseX, int mouseY) {
		HudModule[] modules = HudModule.values();
		for (int i = 0; i < modules.length; i++) {
			HudModule module = modules[i];
			int rowY = layout.rowsY + i * ROW;
			boolean hovered = inside(mouseX, mouseY, layout.leftX, rowY, LEFT_WIDTH, ROW);
			if (module == selected) {
				HudSkin.rounded(painter, layout.leftX, rowY, LEFT_WIDTH, ROW, 2, HudSkin.ACCENT_FAINT);
				painter.fill(layout.leftX, rowY + 2, 2, ROW - 4, HudSkin.ACCENT);
			}
			HudSkin.toggleRow(painter, layout.leftX + 3, rowY, LEFT_WIDTH - 3, ROW, module.label,
				config.isEnabled(module), hovered);
		}
	}

	private void renderSettings(HudPainter painter, Layout layout, int mouseX, int mouseY) {
		HudConfig.Entry entry = config.get(selected);
		int x = layout.rightX;
		painter.text(selected.label, x + 2, layout.titleY + 2, HudSkin.TEXT);

		HudSkin.sliderRow(painter, x, layout.sizeY, RIGHT_WIDTH, ROW, "Größe",
			HudConfig.clampScale(entry.scale) + " %",
			progress(entry.scale, HudConfig.MIN_SCALE, HudConfig.MAX_SCALE),
			sliderHovered(mouseX, mouseY, x, layout.sizeY, Slider.SIZE));

		HudSkin.toggleRow(painter, x, layout.backgroundY, RIGHT_WIDTH, ROW, "Hintergrund", entry.background,
			inside(mouseX, mouseY, x, layout.backgroundY, RIGHT_WIDTH, ROW));
		if (layout.alphaY >= 0) {
			HudSkin.sliderRow(painter, x, layout.alphaY, RIGHT_WIDTH, ROW, "Deckkraft",
				entry.backgroundAlpha + " %", entry.backgroundAlpha / 100.0F,
				sliderHovered(mouseX, mouseY, x, layout.alphaY, Slider.ALPHA));
			HudSkin.cycleRow(painter, x, layout.backColorY, RIGHT_WIDTH, ROW, "Farbe",
				HudSkin.hex(entry.backgroundRgb), 0xFF000000 | entry.backgroundRgb,
				picker == Colour.BACKGROUND
					|| inside(mouseX, mouseY, x, layout.backColorY, RIGHT_WIDTH, ROW));
			HudSkin.sliderRow(painter, x, layout.cornerY, RIGHT_WIDTH, ROW, "Ecken",
				HudConfig.clampCorner(entry.corner) + " px",
				progress(entry.corner, HudConfig.MIN_CORNER, HudConfig.MAX_CORNER),
				sliderHovered(mouseX, mouseY, x, layout.cornerY, Slider.CORNER));
			HudSkin.toggleRow(painter, x, layout.borderY, RIGHT_WIDTH, ROW, "Rahmen", entry.border,
				inside(mouseX, mouseY, x, layout.borderY, RIGHT_WIDTH, ROW));
		}

		HudSkin.cycleRow(painter, x, layout.textColorY, RIGHT_WIDTH, ROW, "Textfarbe",
			HudSkin.hex(entry.textRgb), entry.textArgb(),
			picker == Colour.TEXT || inside(mouseX, mouseY, x, layout.textColorY, RIGHT_WIDTH, ROW));
		HudSkin.toggleRow(painter, x, layout.shadowY, RIGHT_WIDTH, ROW, "Schatten", entry.shadow,
			inside(mouseX, mouseY, x, layout.shadowY, RIGHT_WIDTH, ROW));

		if (layout.verticalY < 0)
			return;
		HudSkin.toggleRow(painter, x, layout.verticalY, RIGHT_WIDTH, ROW, "Untereinander", entry.vertical,
			inside(mouseX, mouseY, x, layout.verticalY, RIGHT_WIDTH, ROW));
		HudSkin.toggleRow(painter, x, layout.percentY, RIGHT_WIDTH, ROW, "Haltbarkeit", entry.percent,
			inside(mouseX, mouseY, x, layout.percentY, RIGHT_WIDTH, ROW));
		HudSkin.toggleRow(painter, x, layout.splitY, RIGHT_WIDTH, ROW, "Einzeln verschieben", entry.split,
			inside(mouseX, mouseY, x, layout.splitY, RIGHT_WIDTH, ROW));
		HudSkin.heading(painter, "Plätze", x + 2, layout.slotsY);
		for (HudSlot slot : HudSlot.values()) {
			int[] cell = slotCell(layout, slot.ordinal());
			HudSkin.toggleRow(painter, cell[0], cell[1], cell[2], ROW, slot.label,
				entry.slots[slot.ordinal()], inside(mouseX, mouseY, cell[0], cell[1], cell[2], ROW));
		}
	}

	private void renderPicker(HudPainter painter, Pick pick, int mouseX, int mouseY) {
		HudSkin.card(painter, pick.x, pick.y, PICKER_WIDTH, pick.height);
		int rgb = colour();
		for (int i = 0; i < COLOUR_MODES.length; i++) {
			int tabX = pick.innerX + i * (TAB_WIDTH + TAB_GAP);
			HudSkin.tab(painter, tabX, pick.tabsY, TAB_WIDTH, ROW, COLOUR_MODES[i],
				config.colorMode == i, inside(mouseX, mouseY, tabX, pick.tabsY, TAB_WIDTH, ROW));
		}

		switch (config.colorMode) {
			case MODE_HSL -> {
				value(painter, pick, pick.firstY, "H", hsl[0], 360, "°", mouseX, mouseY, Slider.HUE);
				value(painter, pick, pick.secondY, "S", hsl[1], 100, "%", mouseX, mouseY, Slider.SATURATION);
				value(painter, pick, pick.thirdY, "L", hsl[2], 100, "%", mouseX, mouseY, Slider.LIGHTNESS);
			}
			case MODE_HEX -> {
				HudSkin.field(painter, pick.innerX, pick.firstY, pick.innerWidth, FIELD_HEIGHT,
					"#" + hexInput, true);
			}
			default -> {
				value(painter, pick, pick.firstY, "R", HudSkin.component(rgb, 16), 255, "",
					mouseX, mouseY, Slider.RED);
				value(painter, pick, pick.secondY, "G", HudSkin.component(rgb, 8), 255, "",
					mouseX, mouseY, Slider.GREEN);
				value(painter, pick, pick.thirdY, "B", HudSkin.component(rgb, 0), 255, "",
					mouseX, mouseY, Slider.BLUE);
			}
		}

		for (int i = 0; i < HudSkin.PRESETS.length; i++) {
			int swatchX = pick.innerX + i * SWATCH_STEP;
			HudSkin.rounded(painter, swatchX, pick.presetsY, SWATCH - 1, SWATCH, 2,
				0xFF000000 | HudSkin.PRESETS[i]);
			boolean chosen = HudSkin.PRESETS[i] == (rgb & 0xFFFFFF);
			boolean hovered = inside(mouseX, mouseY, swatchX, pick.presetsY, SWATCH, SWATCH);
			HudSkin.outline(painter, swatchX, pick.presetsY, SWATCH - 1, SWATCH, 2,
				chosen ? HudSkin.ACCENT : (hovered ? HudSkin.TEXT : HudSkin.BORDER));
		}
	}

	private void value(HudPainter painter, Pick pick, int y, String label, int value, int max,
					   String unit, int mouseX, int mouseY, Slider which) {
		boolean hovered = (drag == Drag.SLIDER && slider == which)
			|| onTrack(mouseX, mouseY, pick.innerX, y, pick.innerWidth, PICKER_LABEL, PICKER_VALUE);
		HudSkin.sliderRow(painter, pick.innerX, y, pick.innerWidth, ROW, label, value + unit,
			value / (float) max, hovered, PICKER_LABEL, PICKER_VALUE);
	}

	private void renderBar(HudPainter painter, Bar bar, int mouseX, int mouseY) {
		HudSkin.centered(painter, "Anzeigen mit der Maus ziehen · rechte Maustaste stellt eine zurück",
			bar.x + BAR_WIDTH / 2, bar.y - 13, HudSkin.MUTED);
		HudSkin.card(painter, bar.x, bar.y, BAR_WIDTH, BAR_HEIGHT);

		HudSkin.toggleRow(painter, bar.gridX, bar.rowY, 52, ROW, "Raster", config.grid,
			inside(mouseX, mouseY, bar.gridX, bar.rowY, 52, ROW));
		HudSkin.slider(painter, bar.trackX, bar.rowY + 3, bar.trackWidth, HudSkin.TRACK_HEIGHT,
			progress(config.gridSize, HudConfig.MIN_GRID, HudConfig.MAX_GRID),
			drag == Drag.SLIDER && slider == Slider.GRID
				|| inside(mouseX, mouseY, bar.trackX, bar.rowY, bar.trackWidth, ROW));
		HudSkin.right(painter, HudConfig.clampGrid(config.gridSize) + " px", bar.trackX + bar.trackWidth + 26,
			bar.rowY + 2, HudSkin.TEXT_DIM);
		HudSkin.toggleRow(painter, bar.snapX, bar.rowY, 72, ROW, "Einrasten", config.snap,
			inside(mouseX, mouseY, bar.snapX, bar.rowY, 72, ROW));
		HudSkin.button(painter, bar.doneX, bar.y + (BAR_HEIGHT - BUTTON) / 2, 58, BUTTON, "Fertig",
			inside(mouseX, mouseY, bar.doneX, bar.y + (BAR_HEIGHT - BUTTON) / 2, 58, BUTTON), true);
	}

	// ---------- Maus ----------

	/** @return true, wenn das ganze Fenster geschlossen werden soll. */
	public boolean mouseDown(HudPainter painter, Map<HudModule, String> texts, int width, int height,
							 int mouseX, int mouseY) {
		if (editing) {
			Bar bar = bar(width, height);
			if (inside(mouseX, mouseY, bar.x, bar.y, BAR_WIDTH, BAR_HEIGHT)) {
				clickBar(bar, mouseX, mouseY);
				return false;
			}
			grabElement(painter, texts, width, height, mouseX, mouseY);
			return false;
		}

		Layout layout = layout(width, height);
		closePickerIfGone(layout);
		if (picker != Colour.NONE) {
			Pick pick = pick(layout, height);
			if (inside(mouseX, mouseY, pick.x, pick.y, PICKER_WIDTH, pick.height)) {
				clickPicker(pick, mouseX, mouseY);
				return false;
			}
			picker = Colour.NONE; // Klick daneben schließt den Farbwähler
			return false;
		}
		if (!inside(mouseX, mouseY, layout.x, layout.y, WIDTH, layout.height))
			return false; // außerhalb des Menüs passiert nichts; zum Verschieben gibt es den Modus
		return clickMenu(layout, mouseX, mouseY);
	}

	/** Rechte Maustaste im Bearbeiten-Modus: das Stück darunter auf seinen Standardplatz. */
	public void rightClick(HudPainter painter, Map<HudModule, String> texts, int width, int height,
						   int mouseX, int mouseY) {
		if (!editing)
			return;
		Map<HudModule, String> shown = withPlaceholders(texts);
		List<HudElement> elements = HudOverlay.elements(config, painter, shown, true);
		for (int i = elements.size() - 1; i >= 0; i--) {
			HudElement element = elements.get(i);
			if (!inside(mouseX, mouseY, HudOverlay.box(config, element, painter, shown, width, height), 3))
				continue;
			HudModule module = element.module();
			HudOverlay.move(config.get(module), element, module.defaultX,
				module.defaultY + (element.isSlot() ? element.slot() * 18 : 0));
			config.save();
			return;
		}
	}

	/** @return true, wenn das ganze Fenster geschlossen werden soll. */
	private boolean clickMenu(Layout layout, int mouseX, int mouseY) {
		HudModule[] modules = HudModule.values();
		for (int i = 0; i < modules.length; i++) {
			if (!inside(mouseX, mouseY, layout.leftX, layout.rowsY + i * ROW, LEFT_WIDTH, ROW))
				continue;
			selected = modules[i];
			// Klick aufs Kästchen schaltet ein und aus, sonst nur auswählen
			if (inside(mouseX, mouseY, layout.leftX, layout.rowsY + i * ROW, 17, ROW)) {
				HudConfig.Entry entry = config.get(modules[i]);
				entry.enabled = !entry.enabled;
				config.save();
			}
			return false;
		}
		if (inside(mouseX, mouseY, layout.leftX, layout.copyY, LEFT_WIDTH, BUTTON))
			return changed(() -> config.copyBackgroundToAll(selected));

		HudConfig.Entry entry = config.get(selected);
		if (startSlider(mouseX, mouseY, layout.rightX, layout.sizeY, Slider.SIZE))
			return false;
		if (inside(mouseX, mouseY, layout.rightX, layout.backgroundY, RIGHT_WIDTH, ROW))
			return changed(() -> entry.background = !entry.background);
		if (layout.alphaY >= 0) {
			if (startSlider(mouseX, mouseY, layout.rightX, layout.alphaY, Slider.ALPHA))
				return false;
			if (inside(mouseX, mouseY, layout.rightX, layout.backColorY, RIGHT_WIDTH, ROW)) {
				startPicker(Colour.BACKGROUND);
				return false;
			}
			if (startSlider(mouseX, mouseY, layout.rightX, layout.cornerY, Slider.CORNER))
				return false;
			if (inside(mouseX, mouseY, layout.rightX, layout.borderY, RIGHT_WIDTH, ROW))
				return changed(() -> entry.border = !entry.border);
		}
		if (inside(mouseX, mouseY, layout.rightX, layout.textColorY, RIGHT_WIDTH, ROW)) {
			startPicker(Colour.TEXT);
			return false;
		}
		if (inside(mouseX, mouseY, layout.rightX, layout.shadowY, RIGHT_WIDTH, ROW))
			return changed(() -> entry.shadow = !entry.shadow);

		if (layout.verticalY >= 0) {
			if (inside(mouseX, mouseY, layout.rightX, layout.verticalY, RIGHT_WIDTH, ROW))
				return changed(() -> entry.vertical = !entry.vertical);
			if (inside(mouseX, mouseY, layout.rightX, layout.percentY, RIGHT_WIDTH, ROW))
				return changed(() -> entry.percent = !entry.percent);
			if (inside(mouseX, mouseY, layout.rightX, layout.splitY, RIGHT_WIDTH, ROW))
				return changed(() -> entry.split = !entry.split);
			for (HudSlot slot : HudSlot.values()) {
				int[] cell = slotCell(layout, slot.ordinal());
				if (inside(mouseX, mouseY, cell[0], cell[1], cell[2], ROW))
					return changed(() -> entry.slots[slot.ordinal()] = !entry.slots[slot.ordinal()]);
			}
		}

		if (inside(mouseX, mouseY, layout.standardX, layout.buttonY, layout.smallWidth, BUTTON))
			return changed(config::reset);
		if (inside(mouseX, mouseY, layout.editX, layout.buttonY, layout.editWidth, BUTTON)) {
			editing = true;
			return false;
		}
		if (inside(mouseX, mouseY, layout.doneX, layout.buttonY, layout.smallWidth, BUTTON)) {
			config.save();
			return true;
		}
		return false; // Klick daneben im Menü: nichts tun
	}

	private void clickPicker(Pick pick, int mouseX, int mouseY) {
		for (int i = 0; i < COLOUR_MODES.length; i++) {
			int tabX = pick.innerX + i * (TAB_WIDTH + TAB_GAP);
			if (!inside(mouseX, mouseY, tabX, pick.tabsY, TAB_WIDTH, ROW))
				continue;
			config.colorMode = i;
			startPicker(picker);
			config.save();
			return;
		}

		if (config.colorMode == MODE_HEX) {
			if (inside(mouseX, mouseY, pick.innerX, pick.firstY, pick.innerWidth, FIELD_HEIGHT))
				hexInput = ""; // Klick ins Feld beginnt von vorn
		} else if (startComponent(pick, mouseX, mouseY, pick.firstY, first())
			|| startComponent(pick, mouseX, mouseY, pick.secondY, second())
			|| startComponent(pick, mouseX, mouseY, pick.thirdY, third())) {
			return;
		}

		if (!inside(mouseX, mouseY, pick.innerX, pick.presetsY,
				HudSkin.PRESETS.length * SWATCH_STEP, SWATCH))
			return;
		setColour(HudSkin.PRESETS[Math.min(HudSkin.PRESETS.length - 1,
			(mouseX - pick.innerX) / SWATCH_STEP)]);
		syncFromColour();
		config.save();
	}

	private Slider first() {
		return config.colorMode == MODE_HSL ? Slider.HUE : Slider.RED;
	}

	private Slider second() {
		return config.colorMode == MODE_HSL ? Slider.SATURATION : Slider.GREEN;
	}

	private Slider third() {
		return config.colorMode == MODE_HSL ? Slider.LIGHTNESS : Slider.BLUE;
	}

	/**
	 * Tasteneingabe im Fenster: Esc geht eine Stufe zurück, im Hex-Modus wird getippt.
	 *
	 * @return true, wenn die Taste verbraucht wurde
	 */
	public boolean keyPressed(int key) {
		if (key == 256) { // Esc geht erst eine Stufe zurück, statt gleich das Fenster zu schließen
			if (picker != Colour.NONE) {
				picker = Colour.NONE;
				return true;
			}
			if (editing) {
				editing = false;
				config.save();
				return true;
			}
			return false;
		}
		if (editing || picker == Colour.NONE)
			return false;
		if (config.colorMode != MODE_HEX)
			return false;
		if (key == 259) { // Rücktaste
			if (!hexInput.isEmpty())
				hexInput = hexInput.substring(0, hexInput.length() - 1);
			return true;
		}
		char typed = hexDigit(key);
		if (typed == 0 || hexInput.length() >= 6)
			return typed != 0;
		hexInput += typed;
		if (hexInput.length() == 6) {
			setColour(Integer.parseInt(hexInput, 16));
			syncFromColour();
			config.save();
		}
		return true;
	}

	/** GLFW-Nummern von 0-9 und A-F sind ihre Zeichen; alles andere zählt nicht. */
	private static char hexDigit(int key) {
		if (key >= '0' && key <= '9')
			return (char) key;
		if (key >= 'A' && key <= 'F')
			return (char) key;
		return 0;
	}

	private void clickBar(Bar bar, int mouseX, int mouseY) {
		if (inside(mouseX, mouseY, bar.gridX, bar.rowY, 52, ROW)) {
			config.grid = !config.grid;
			config.save();
			return;
		}
		if (inside(mouseX, mouseY, bar.trackX, bar.rowY, bar.trackWidth, ROW)) {
			startTrack(Slider.GRID, bar.trackX, bar.trackWidth, mouseX);
			return;
		}
		if (inside(mouseX, mouseY, bar.snapX, bar.rowY, 72, ROW)) {
			config.snap = !config.snap;
			config.save();
			return;
		}
		if (inside(mouseX, mouseY, bar.doneX, bar.y + (BAR_HEIGHT - BUTTON) / 2, 58, BUTTON)) {
			editing = false;
			config.save();
		}
	}

	public void mouseDrag(HudPainter painter, Map<HudModule, String> texts, int width, int height,
						  int mouseX, int mouseY) {
		switch (drag) {
			case SLIDER -> applySlider(mouseX);
			case ELEMENT -> dragElement(painter, texts, width, height, mouseX, mouseY);
			default -> {
			}
		}
	}

	public void mouseUp() {
		if (drag == Drag.NONE)
			return;
		drag = Drag.NONE;
		dragged = null;
		guideX = -1;
		guideY = -1;
		config.save();
	}

	/** Beim Schließen mit Esc sichern. */
	public void close() {
		config.save();
	}

	// ---------- Verschieben ----------

	/** Greift das oberste Stück unter dem Mauszeiger. */
	private void grabElement(HudPainter painter, Map<HudModule, String> texts, int width, int height,
							 int mouseX, int mouseY) {
		Map<HudModule, String> shown = withPlaceholders(texts);
		List<HudElement> elements = HudOverlay.elements(config, painter, shown, true);
		for (int i = elements.size() - 1; i >= 0; i--) {
			HudElement element = elements.get(i);
			int[] box = HudOverlay.box(config, element, painter, shown, width, height);
			if (!inside(mouseX, mouseY, box, 3))
				continue;
			selected = element.module();
			dragged = element;
			drag = Drag.ELEMENT;
			grabX = mouseX - box[0];
			grabY = mouseY - box[1];
			return;
		}
	}

	private void dragElement(HudPainter painter, Map<HudModule, String> texts, int width, int height,
							 int mouseX, int mouseY) {
		if (dragged == null)
			return;
		Map<HudModule, String> shown = withPlaceholders(texts);
		int[] box = HudOverlay.box(config, dragged, painter, shown, width, height);
		List<int[]> others = new ArrayList<>();
		for (HudElement element : HudOverlay.elements(config, painter, shown, true))
			if (!element.equals(dragged))
				others.add(HudOverlay.box(config, element, painter, shown, width, height));

		HudSnap.Result snapped = HudSnap.apply(config, mouseX - grabX, mouseY - grabY, box[2], box[3],
			others, width, height);
		guideX = snapped.guideX;
		guideY = snapped.guideY;
		HudOverlay.move(config.get(dragged.module()), dragged, snapped.x, snapped.y);
	}

	// ---------- Regler ----------

	private boolean startSlider(int mouseX, int mouseY, int x, int y, Slider which) {
		if (!onTrack(mouseX, mouseY, x, y, RIGHT_WIDTH, HudSkin.LABEL_WIDTH, HudSkin.VALUE_WIDTH))
			return false;
		int[] track = HudSkin.track(x, y, RIGHT_WIDTH, ROW);
		startTrack(which, track[0], track[2], mouseX);
		return true;
	}

	private boolean startComponent(Pick pick, int mouseX, int mouseY, int y, Slider which) {
		if (!onTrack(mouseX, mouseY, pick.innerX, y, pick.innerWidth, PICKER_LABEL, PICKER_VALUE))
			return false;
		int[] track = HudSkin.track(pick.innerX, y, pick.innerWidth, ROW, PICKER_LABEL, PICKER_VALUE);
		startTrack(which, track[0], track[2], mouseX);
		return true;
	}

	private void startTrack(Slider which, int x, int width, int mouseX) {
		drag = Drag.SLIDER;
		slider = which;
		trackX = x;
		trackWidth = width;
		applySlider(mouseX);
	}

	private boolean sliderHovered(int mouseX, int mouseY, int x, int y, Slider which) {
		return (drag == Drag.SLIDER && slider == which)
			|| onTrack(mouseX, mouseY, x, y, RIGHT_WIDTH, HudSkin.LABEL_WIDTH, HudSkin.VALUE_WIDTH);
	}

	/** Nur die Schiene zählt – sonst würde ein Klick auf die Beschriftung den Wert verstellen. */
	private static boolean onTrack(int mouseX, int mouseY, int x, int y, int width, int labelWidth,
								   int valueWidth) {
		int[] track = HudSkin.track(x, y, width, ROW, labelWidth, valueWidth);
		return inside(mouseX, mouseY, track[0] - 2, y, track[2] + 4, ROW);
	}

	private void applySlider(int mouseX) {
		float progress = Math.max(0.0F, Math.min(1.0F,
			(mouseX - (trackX + 1)) / (float) Math.max(1, trackWidth - 2)));
		HudConfig.Entry entry = config.get(selected);
		switch (slider) {
			case SIZE -> entry.scale = HudConfig.clampScale(
				step(progress, HudConfig.MIN_SCALE, HudConfig.MAX_SCALE, HudConfig.SCALE_STEP));
			case ALPHA -> entry.backgroundAlpha = step(progress, 0, 100, 5);
			case CORNER -> entry.corner = HudConfig.clampCorner(
				step(progress, HudConfig.MIN_CORNER, HudConfig.MAX_CORNER, 1));
			case GRID -> config.gridSize = HudConfig.clampGrid(
				step(progress, HudConfig.MIN_GRID, HudConfig.MAX_GRID, HudConfig.GRID_STEP));
			case RED -> setRgb(HudSkin.withComponent(colour(), 16, step(progress, 0, 255, 1)));
			case GREEN -> setRgb(HudSkin.withComponent(colour(), 8, step(progress, 0, 255, 1)));
			case BLUE -> setRgb(HudSkin.withComponent(colour(), 0, step(progress, 0, 255, 1)));
			case HUE -> setHsl(0, step(progress, 0, 360, 1));
			case SATURATION -> setHsl(1, step(progress, 0, 100, 1));
			case LIGHTNESS -> setHsl(2, step(progress, 0, 100, 1));
		}
	}

	/** Farbe aus den Reglern übernehmen und Farbton/Sättigung/Helligkeit nachziehen. */
	private void setRgb(int rgb) {
		setColour(rgb);
		syncFromColour();
	}

	private void setHsl(int index, int value) {
		hsl[index] = value;
		setColour(HudSkin.fromHsl(hsl[0], hsl[1], hsl[2]));
		hexInput = HudSkin.hex(colour()).substring(1);
	}

	private static float progress(int value, int min, int max) {
		return Math.max(0.0F, Math.min(1.0F, (value - min) / (float) (max - min)));
	}

	private static int step(float progress, int min, int max, int stepSize) {
		return Math.round((min + progress * (max - min)) / (float) stepSize) * stepSize;
	}

	// ---------- Farbe ----------

	private int colour() {
		HudConfig.Entry entry = config.get(selected);
		return picker == Colour.BACKGROUND ? entry.backgroundRgb : entry.textRgb;
	}

	private void setColour(int rgb) {
		HudConfig.Entry entry = config.get(selected);
		if (picker == Colour.BACKGROUND)
			entry.backgroundRgb = rgb & 0xFFFFFF;
		else
			entry.textRgb = rgb & 0xFFFFFF;
	}

	/** Öffnet den Wähler für eine Farbe und stellt alle drei Darstellungen darauf ein. */
	private void startPicker(Colour which) {
		picker = which;
		syncFromColour();
	}

	private void syncFromColour() {
		int[] converted = HudSkin.toHsl(colour());
		System.arraycopy(converted, 0, hsl, 0, hsl.length);
		hexInput = HudSkin.hex(colour()).substring(1);
	}

	/** Schließt den Farbwähler, wenn seine Zeile gerade nicht mehr da ist. */
	private void closePickerIfGone(Layout layout) {
		if (picker == Colour.BACKGROUND && layout.alphaY < 0)
			picker = Colour.NONE;
	}

	// ---------- Hilfsmittel ----------

	/** Einstellung ändern und gleich sichern; das Fenster bleibt offen. */
	private boolean changed(Runnable change) {
		change.run();
		config.save();
		return false;
	}

	/** Fläche eines Ausrüstungsplatzes im Menü als {x, y, Breite}. */
	private static int[] slotCell(Layout layout, int index) {
		int cellWidth = RIGHT_WIDTH / SLOT_COLUMNS;
		return new int[] {
			layout.rightX + (index % SLOT_COLUMNS) * cellWidth,
			layout.slotBoxesY + (index / SLOT_COLUMNS) * ROW,
			cellWidth
		};
	}

	/**
	 * Ohne Wert (z.B. Ping im Einzelspieler) wird der Name angezeigt, damit man die Anzeige
	 * trotzdem greifen kann.
	 */
	private static Map<HudModule, String> withPlaceholders(Map<HudModule, String> texts) {
		Map<HudModule, String> shown = new EnumMap<>(HudModule.class);
		for (HudModule module : HudModule.values()) {
			String text = texts.get(module);
			shown.put(module, text == null || text.isEmpty() ? module.label : text);
		}
		return shown;
	}

	private static boolean inside(int mouseX, int mouseY, int x, int y, int width, int height) {
		return HudSkin.inside(mouseX, mouseY, x, y, width, height);
	}

	/** Trefferprüfung auf einen Kasten {x, y, Breite, Höhe} mit etwas Luft ringsum. */
	private static boolean inside(int mouseX, int mouseY, int[] box, int margin) {
		return HudSkin.inside(mouseX, mouseY, box[0] - margin, box[1] - margin,
			box[2] + 2 * margin, box[3] + 2 * margin);
	}

	// ---------- Aufbau ----------

	/** Der schwebende Farbwähler. */
	private static final class Pick {
		int x;
		int y;
		int height;
		int innerX;
		int innerWidth;
		int tabsY;
		int firstY;
		int secondY;
		int thirdY;
		int presetsY;
	}

	private Pick pick(Layout layout, int screenHeight) {
		Pick pick = new Pick();
		int content = config.colorMode == MODE_HEX ? FIELD_HEIGHT : 3 * ROW;
		pick.height = 2 * PICKER_PAD + ROW + 4 + content + 4 + SWATCH;
		pick.x = layout.rightX + RIGHT_WIDTH - PICKER_WIDTH;
		int rowY = picker == Colour.BACKGROUND ? layout.backColorY : layout.textColorY;
		// Unter der Zeile, wenn dort Platz ist, sonst darüber
		pick.y = rowY + ROW + 2 + pick.height <= screenHeight
			? rowY + ROW + 2
			: Math.max(0, rowY - pick.height - 2);
		pick.innerX = pick.x + PICKER_PAD;
		pick.innerWidth = PICKER_WIDTH - 2 * PICKER_PAD;
		pick.tabsY = pick.y + PICKER_PAD;
		pick.firstY = pick.tabsY + ROW + 4;
		pick.secondY = pick.firstY + ROW;
		pick.thirdY = pick.secondY + ROW;
		pick.presetsY = pick.firstY + content + 4;
		return pick;
	}

	/** Die Leiste im Bearbeiten-Modus. */
	private static final class Bar {
		int x;
		int y;
		int rowY;
		int gridX;
		int trackX;
		int trackWidth;
		int snapX;
		int doneX;
	}

	private static Bar bar(int screenWidth, int screenHeight) {
		Bar bar = new Bar();
		bar.x = (screenWidth - BAR_WIDTH) / 2;
		bar.y = Math.max(0, screenHeight - BAR_HEIGHT - 8);
		bar.rowY = bar.y + (BAR_HEIGHT - ROW) / 2;
		bar.gridX = bar.x + PAD;
		bar.trackX = bar.gridX + 56;
		bar.trackWidth = 56;
		bar.snapX = bar.trackX + bar.trackWidth + 34;
		bar.doneX = bar.x + BAR_WIDTH - PAD - 58;
		return bar;
	}

	/** Alle Maße des Menüs an einer Stelle, damit Zeichnen und Klicken nicht auseinanderlaufen. */
	private static final class Layout {
		int x;
		int y;
		int height;
		int leftX;
		int rightX;

		int rowsY;
		int copyLineY;
		int copyY;

		int titleY;
		int sizeY;
		int backgroundY;
		/** -1, wenn der Hintergrund aus ist; dann gibt es auch keine Deckkraft, Farbe, Ecken, Rahmen. */
		int alphaY;
		int backColorY;
		int cornerY;
		int borderY;
		int textColorY;
		int shadowY;
		/** -1, wenn die Anzeige keine Ausrüstung zeigt. */
		int verticalY;
		int percentY;
		int splitY;
		int slotsY;
		int slotBoxesY;

		int actionsY;
		int buttonY;
		int smallWidth;
		int editWidth;
		int standardX;
		int editX;
		int doneX;
	}

	private Layout layout(int screenWidth, int screenHeight) {
		Layout layout = new Layout();
		int top = HEADER + 1 + 4;

		int left = top;
		layout.rowsY = left;
		left += HudModule.values().length * ROW + GAP;
		layout.copyLineY = left;
		left += 1 + GAP;
		layout.copyY = left;
		left += BUTTON;

		int right = top;
		layout.titleY = right;
		right += ROW;
		layout.sizeY = right;
		right += ROW;
		layout.backgroundY = right;
		right += ROW;
		HudConfig.Entry entry = config.get(selected);
		if (entry.background) {
			layout.alphaY = right;
			right += ROW;
			layout.backColorY = right;
			right += ROW;
			layout.cornerY = right;
			right += ROW;
			layout.borderY = right;
			right += ROW;
		} else {
			layout.alphaY = -1;
		}
		layout.textColorY = right;
		right += ROW;
		layout.shadowY = right;
		right += ROW;
		if (selected.equipment) {
			layout.verticalY = right;
			right += ROW;
			layout.percentY = right;
			right += ROW;
			layout.splitY = right;
			right += ROW + 3;
			layout.slotsY = right;
			right += 10;
			layout.slotBoxesY = right;
			right += ROW * ((HudSlot.COUNT + SLOT_COLUMNS - 1) / SLOT_COLUMNS);
		} else {
			layout.verticalY = -1;
		}

		layout.actionsY = Math.max(left, right) + GAP;
		layout.buttonY = layout.actionsY + 1 + GAP;
		layout.height = layout.buttonY + BUTTON + PAD;

		layout.x = (screenWidth - WIDTH) / 2;
		layout.y = Math.max(0, (screenHeight - layout.height) / 2);
		layout.leftX = layout.x + PAD;
		layout.rightX = layout.x + PAD + LEFT_WIDTH + COL_GAP;
		layout.smallWidth = 76;
		layout.editWidth = 118;
		layout.standardX = layout.x + PAD + 4;
		layout.editX = layout.x + (WIDTH - layout.editWidth) / 2;
		layout.doneX = layout.x + WIDTH - PAD - 4 - layout.smallWidth;
		offset(layout);
		return layout;
	}

	/** Die Maße oben sind relativ zur Karte; hier wandern sie auf die tatsächliche Position. */
	private static void offset(Layout layout) {
		int y = layout.y;
		layout.rowsY += y;
		layout.copyLineY += y;
		layout.copyY += y;
		layout.titleY += y;
		layout.sizeY += y;
		layout.backgroundY += y;
		if (layout.alphaY >= 0) {
			layout.alphaY += y;
			layout.backColorY += y;
			layout.cornerY += y;
			layout.borderY += y;
		}
		layout.textColorY += y;
		layout.shadowY += y;
		if (layout.verticalY >= 0) {
			layout.verticalY += y;
			layout.percentY += y;
			layout.splitY += y;
			layout.slotsY += y;
			layout.slotBoxesY += y;
		}
		layout.actionsY += y;
		layout.buttonY += y;
	}
}
