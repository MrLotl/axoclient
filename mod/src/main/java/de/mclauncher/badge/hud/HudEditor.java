package de.mclauncher.badge.hud;

import java.util.ArrayList;
import java.util.EnumMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.function.Consumer;

/**
 * Das Menü der Anzeigen, aufgebaut wie ein Einstellungsfenster: links die Kategorien, rechts alle
 * Anzeigen als Kacheln mit Schalter und Suche. Ein Klick auf eine Kachel öffnet ihre Einstellungen
 * mit Vorschau, alles in einer scrollbaren Liste mit Abschnitten. "Overlay bearbeiten" ersetzt das
 * Fenster durch eine schmale Leiste und gibt alle Anzeigen zum Ziehen frei.
 *
 * Die Bedienung ist für alle Minecraft-Versionen gleich; die jeweilige Version liefert nur das
 * Fenster und das Zeichnen.
 */
public final class HudEditor {
	// ---------- Maße des Fensters ----------
	private static final int MAX_WIDTH = 500;
	private static final int MAX_HEIGHT = 320;
	/** Mindestabstand des Fensters zum Bildrand. */
	private static final int MARGIN = 12;
	private static final int PAD = 8;
	private static final int SIDE_WIDTH = 100;
	private static final int TOP_HEIGHT = 16;
	private static final int NAV_HEIGHT = 14;
	private static final int NAV_STEP = 16;
	private static final int TILE_HEIGHT = 20;
	private static final int TILE_GAP = 4;
	/** Rechter Teil einer Kachel mit dem Schalter. */
	private static final int SWITCH_CELL = 26;
	private static final int PREVIEW_HEIGHT = 44;
	private static final int SCROLLBAR = 3;
	private static final int SCROLL_STEP = 20;
	private static final int EDIT_WIDTH = 104;
	private static final int BACK_WIDTH = 44;

	// ---------- Einstellungszeilen ----------
	private static final int CARD = 18;
	/** Zeile mit Beschreibung unter der Beschriftung. */
	private static final int CARD_DESC = 26;
	private static final int CARD_GAP = 3;
	private static final int SWITCH_WIDTH = 18;
	private static final int VALUE_WIDTH = 34;
	private static final int SWATCH_WIDTH = 22;
	private static final int RULE_SWATCH = 14;

	// ---------- Leiste im Bearbeiten-Modus und Farbwähler ----------
	private static final int ROW = 12;
	private static final int BUTTON = 14;
	private static final int BAR_WIDTH = 300;
	private static final int BAR_HEIGHT = 28;

	/** Der Farbwähler schwebt über dem Menü. */
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

	private static final String[] CATEGORY_NAMES = { "Leistung", "Position", "Zeit", "Kampf", "Bildschirm" };
	/** Farbe der Symbole je Kategorie (aus der Farbpalette des Launchers). */
	private static final int[] CATEGORY_RGB = { 0x3BA55C, 0x6FA8DC, 0xE8B84A, 0xE36D6F, 0xB07CD8 };
	private static final HudModule[][] CATEGORIES = {
		{ HudModule.FPS, HudModule.PING, HudModule.CPU, HudModule.GPU, HudModule.MEMORY },
		{ HudModule.COORDS, HudModule.CHUNK, HudModule.DIRECTION, HudModule.SPEED, HudModule.BIOME, HudModule.LIGHT },
		{ HudModule.TIME, HudModule.WORLD_TIME },
		{ HudModule.ARMOR, HudModule.CPS, HudModule.KEYS },
		{ HudModule.EFFECTS, HudModule.SCOREBOARD }
	};
	/** Eintrag "Profile" in der Seitenleiste (nach den Kategorien). */
	private static final int NAV_PROFILES = CATEGORIES.length;

	/** Was gerade mit der Maus gezogen wird. */
	private enum Drag { NONE, ELEMENT, SLIDER, BAND, GROUP }

	/** Welcher Regler gezogen wird; die Regler der Einstellungen laufen alle über WIDGET. */
	private enum Slider { GROUP_ALPHA, GRID, RED, GREEN, BLUE, HUE, SATURATION, LIGHTNESS, WIDGET }

	/** Was rechts im Fenster zu sehen ist. */
	private enum View { OVERVIEW, DETAIL, PROFILES }

	/** Für welche Farbe der Wähler gerade offen ist. */
	private enum Colour { NONE, TEXT, BACKGROUND, RULE, GROUP, PRESS, AXIS }

	private final HudConfig config;
	private HudModule selected = HudModule.values()[0];
	/** Im Bearbeiten-Modus verschwindet das Menü und alles im Bild lässt sich ziehen. */
	private boolean editing;
	private View view = View.OVERVIEW;
	/** Gewählte Kategorie in der Seitenleiste; -1 = alle. */
	private int category = -1;
	private String search = "";
	private boolean searchFocus;
	/** Wie weit jede Ansicht nach unten gescrollt ist. */
	private final int[] scroll = new int[View.values().length];

	private Colour picker = Colour.NONE;
	/** Welche Farbregel (Colour.RULE) bzw. welche Achse (Colour.AXIS) der Wähler gerade bearbeitet. */
	private int pickerRule = -1;
	/** Name, der im Profil-Feld getippt wird, und ob das Feld gerade Eingaben bekommt. */
	private String profileName = "";
	private boolean nameFocus;
	/** Ausgewähltes Profil in der Liste; leer = keins. */
	private String profilePick = "";
	private boolean confirmDelete;
	private Widget activeWidget;
	/** Zuletzt gesehene Umgebung (zum Umrechnen der Position beim Anker-Wechsel). */
	private HudPainter ctxPainter;
	private Map<HudModule, String> ctxTexts;
	private int ctxWidth;
	private int ctxHeight;
	/** Zuletzt gelesene Profilnamen (nicht bei jedem Bild neu von der Platte). */
	private List<String> profiles;
	/** Rückmeldung auf der Profilseite ("Gespeichert" usw.). */
	private String status = "";
	/**
	 * Farbton, Sättigung und Helligkeit gehören dem Wähler, solange er offen ist. Würde jeder
	 * Reglerzug aus der Farbe zurückgerechnet, verlöre der Farbton bei Grau seinen Wert.
	 */
	private final int[] hsl = new int[3];
	/** Was gerade ins Hex-Feld getippt wurde, ohne das Doppelkreuz. */
	private String hexInput = "";

	private Drag drag = Drag.NONE;
	private Slider slider = Slider.WIDGET;
	/** Schiene des Reglers, der gerade gezogen wird. */
	private int trackX;
	private int trackWidth;

	private HudElement dragged;
	private HudConfig.Group groupToEdit;
	/** Mehrere gewählte Stücke (Auswahlrahmen aufziehen), die zusammen verschoben werden. */
	private final java.util.Set<HudElement> multi = new java.util.HashSet<>();
	private final Map<HudElement, int[]> groupStart = new java.util.HashMap<>();
	private int bandX0;
	private int bandY0;
	private int bandX1;
	private int bandY1;
	private int groupGrabX;
	private int groupGrabY;
	private HudElement groupLeader;
	private int grabX;
	private int grabY;
	private int guideX = -1;
	private int guideY = -1;

	public HudEditor(HudConfig config) {
		this.config = config;
		HudOverlay.sampleMode = true;
	}

	// ---------- Zeichnen ----------

	public void render(HudPainter painter, Map<HudModule, String> texts, int width, int height,
					   int mouseX, int mouseY) {
		ctxWidth = width;
		ctxHeight = height;
		HudOverlay.sampleMode = true;
		HudPrivacy.editorShown();
		Map<HudModule, String> shown = withPlaceholders(texts);
		// Launcher-Fenster leeren, auch falls Minecraft das normale Overlay unter dem Menü nicht zeichnet
		HudPrivacy.send(config, List.of(), painter, shown, width, height);
		painter.fill(0, 0, width, height, HudSkin.SCRIM);
		if (editing && drag == Drag.ELEMENT)
			HudSnap.drawGrid(config, painter, width, height);

		List<HudElement> all = HudOverlay.elements(config, painter, shown, true);
		HudOverlay.drawGroups(config, all, painter, shown, width, height);
		for (HudElement element : all) {
			int[] box = HudOverlay.box(config, element, painter, shown, width, height);
			if (editing)
				renderFrame(painter, element, box, mouseX, mouseY);
			HudOverlay.draw(config, element, painter, shown, box);
		}

		if (editing) {
			if (drag == Drag.ELEMENT)
				HudSnap.drawGuides(painter, guideX, guideY, width, height);
			if (drag == Drag.BAND) {
				int x = Math.min(bandX0, bandX1);
				int y = Math.min(bandY0, bandY1);
				int w = Math.abs(bandX1 - bandX0);
				int h = Math.abs(bandY1 - bandY0);
				painter.fill(x, y, w, h, HudSkin.ACCENT_FAINT);
				HudSkin.outline(painter, x, y, Math.max(1, w), Math.max(1, h), 0, HudSkin.ACCENT);
			}
			renderGroupPanel(painter, bar(width, height), mouseX, mouseY);
			renderBar(painter, bar(width, height), mouseX, mouseY);
			if (picker == Colour.GROUP && (currentGroup() == null || currentGroup() != groupToEdit))
				picker = Colour.NONE;
			if (picker == Colour.GROUP)
				renderPicker(painter, groupPick(), mouseX, mouseY);
			return;
		}

		Frame frame = frame(width, height);
		clampScroll(frame);
		closePickerIfGone(frame);
		renderWindow(painter, frame, shown, mouseX, mouseY);
		if (picker != Colour.NONE)
			renderPicker(painter, pick(frame), mouseX, mouseY);
	}

	/** Rahmen um ein Stück, solange man es anfassen kann. */
	private void renderFrame(HudPainter painter, HudElement element, int[] box, int mouseX, int mouseY) {
		boolean hovered = drag == Drag.NONE && inside(mouseX, mouseY, box, 3);
		boolean active = element.equals(dragged) || multi.contains(element)
			|| (multi.isEmpty() && element.module() == selected);
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

	private void renderWindow(HudPainter painter, Frame f, Map<HudModule, String> shown, int mouseX, int mouseY) {
		HudSkin.card(painter, f.x, f.y, f.width, f.height);
		renderSide(painter, f, mouseX, mouseY);
		renderTop(painter, f, mouseX, mouseY);
		switch (view) {
			case OVERVIEW -> renderTiles(painter, f, mouseX, mouseY);
			case DETAIL -> {
				renderPreview(painter, f, shown);
				renderRows(painter, f, mouseX, mouseY);
			}
			case PROFILES -> renderRows(painter, f, mouseX, mouseY);
		}
		renderScrollbar(painter, f);
	}

	/** Links: Name, Kategorien, Profile, unten Standard und Fertig. */
	private void renderSide(HudPainter painter, Frame f, int mouseX, int mouseY) {
		HudSkin.rounded(painter, f.sideX, f.sideY, f.sideWidth, f.sideHeight, 3, HudSkin.SIDEBAR);
		painter.text("AxoClient", f.sideX + 8, f.sideY + 8, HudSkin.ACCENT);
		painter.text("Overlay", f.sideX + 8, f.sideY + 18, HudSkin.MUTED);

		for (int entry = -1; entry <= NAV_PROFILES; entry++) {
			int y = navY(f, entry);
			boolean hovered = inside(mouseX, mouseY, f.sideX + 4, y, f.sideWidth - 8, NAV_HEIGHT);
			if (entry == NAV_PROFILES) {
				painter.fill(f.sideX + 8, y - 5, f.sideWidth - 16, 1, HudSkin.LINE);
				HudSkin.nav(painter, f.sideX + 4, y, f.sideWidth - 8, NAV_HEIGHT, "Profile", null,
					view == View.PROFILES, hovered);
				continue;
			}
			HudModule[] members = entry < 0 ? HudModule.values() : CATEGORIES[entry];
			int on = 0;
			for (HudModule member : members)
				if (config.isEnabled(member))
					on++;
			HudSkin.nav(painter, f.sideX + 4, y, f.sideWidth - 8, NAV_HEIGHT,
				entry < 0 ? "Alle" : CATEGORY_NAMES[entry], on + "/" + members.length,
				view != View.PROFILES && category == entry, hovered);
		}

		int buttonX = f.sideX + 6;
		int buttonWidth = f.sideWidth - 12;
		if (fullbrightFits(f))
			HudSkin.toggleRow(painter, buttonX, f.fullbrightY, buttonWidth, ROW, "Fullbright (G)", config.fullbright,
				inside(mouseX, mouseY, buttonX, f.fullbrightY, buttonWidth, ROW));
		HudSkin.button(painter, buttonX, f.standardY, buttonWidth, BUTTON, "Standard",
			inside(mouseX, mouseY, buttonX, f.standardY, buttonWidth, BUTTON), false);
		HudSkin.button(painter, buttonX, f.doneY, buttonWidth, BUTTON, "Fertig",
			inside(mouseX, mouseY, buttonX, f.doneY, buttonWidth, BUTTON), true);
	}

	/** Oben: Suche (Übersicht), Zurück mit Name und Schalter (Anzeige) oder Titel (Profile); rechts "Overlay bearbeiten". */
	private void renderTop(HudPainter painter, Frame f, int mouseX, int mouseY) {
		int editX = f.mainX + f.mainWidth - EDIT_WIDTH;
		HudSkin.button(painter, editX, f.topY, EDIT_WIDTH, TOP_HEIGHT, "Overlay bearbeiten",
			inside(mouseX, mouseY, editX, f.topY, EDIT_WIDTH, TOP_HEIGHT), true);
		int textY = f.topY + (TOP_HEIGHT - 8) / 2;
		switch (view) {
			case OVERVIEW -> HudSkin.search(painter, f.mainX, f.topY, editX - 6 - f.mainX, TOP_HEIGHT, search,
				searchFocus);
			case PROFILES -> painter.text("Profile", f.mainX + 2, textY, HudSkin.TEXT);
			case DETAIL -> {
				HudSkin.button(painter, f.mainX, f.topY, BACK_WIDTH, TOP_HEIGHT, "< Alle",
					inside(mouseX, mouseY, f.mainX, f.topY, BACK_WIDTH, TOP_HEIGHT), false);
				int x = f.mainX + BACK_WIDTH + 8;
				HudSkin.badge(painter, x, f.topY + 2, 12, selected.label.substring(0, 1),
					CATEGORY_RGB[categoryOf(selected)]);
				x += 18;
				String title = fit(painter, selected.label, editX - x - 36);
				painter.text(title, x, textY, HudSkin.TEXT);
				int switchX = x + painter.textWidth(title) + 8;
				HudSkin.toggleSwitch(painter, switchX, f.topY + 3, config.isEnabled(selected),
					inside(mouseX, mouseY, switchX - 2, f.topY, SWITCH_WIDTH + 4, TOP_HEIGHT));
			}
		}
	}

	/** Übersicht: je Kategorie eine Überschrift und die Anzeigen als Kacheln in zwei Spalten. */
	private void renderTiles(HudPainter painter, Frame f, int mouseX, int mouseY) {
		List<Tile> tiles = tiles(f);
		if (tiles.isEmpty()) {
			painter.text("Keine Anzeige gefunden.", f.mainX + 2, f.listY + 4, HudSkin.MUTED);
			return;
		}
		for (Tile tile : tiles) {
			int y = f.listY - scroll() + tile.y;
			if (!visible(f, y, tile.height))
				continue;
			if (tile.module == null) {
				HudSkin.section(painter, CATEGORY_NAMES[tile.category], tile.x, y + 3, f.listWidth);
				continue;
			}
			HudModule module = tile.module;
			boolean on = config.isEnabled(module);
			boolean overSwitch = inside(mouseX, mouseY, tile.x + tile.width - SWITCH_CELL, y, SWITCH_CELL, tile.height);
			boolean hovered = inside(mouseX, mouseY, tile.x, y, tile.width, tile.height);
			HudSkin.rowCard(painter, tile.x, y, tile.width, tile.height, hovered && !overSwitch, module == selected);
			HudSkin.badge(painter, tile.x + 4, y + 4, 12, module.label.substring(0, 1), CATEGORY_RGB[tile.category]);
			int textY = y + (tile.height - 8) / 2;
			painter.text(fit(painter, module.label, tile.width - SWITCH_CELL - 34), tile.x + 21, textY,
				on ? HudSkin.TEXT : HudSkin.TEXT_DIM);
			painter.text(">", tile.x + tile.width - SWITCH_CELL - 9, textY, hovered && !overSwitch ? HudSkin.TEXT
				: HudSkin.MUTED);
			painter.fill(tile.x + tile.width - SWITCH_CELL, y + 3, 1, tile.height - 6, HudSkin.ROW_BORDER);
			HudSkin.toggleSwitch(painter, tile.x + tile.width - SWITCH_CELL + 4, y + (tile.height - 10) / 2, on,
				overSwitch);
		}
	}

	/** Vorschau der gewählten Anzeige, so wie sie gerade eingestellt ist. */
	private void renderPreview(HudPainter painter, Frame f, Map<HudModule, String> shown) {
		int x = f.mainX;
		int y = f.contentY;
		int width = f.mainWidth;
		HudSkin.rounded(painter, x, y, width, PREVIEW_HEIGHT, 3, 0x66000000);
		HudSkin.outline(painter, x, y, width, PREVIEW_HEIGHT, 3, HudSkin.ROW_BORDER);
		painter.text("Vorschau", x + 6, y + 5, HudSkin.MUTED);

		HudElement element = HudElement.of(selected);
		HudConfig.Entry entry = config.get(selected);
		float factor = entry.factor();
		int boxWidth = Math.round(HudOverlay.width(config, element, painter, shown) * factor);
		int boxHeight = Math.round(HudOverlay.height(config, element, painter, shown) * factor);
		int pad = entry.background ? HudOverlay.pad(entry) : 0;
		if (boxWidth <= 0 || boxHeight <= 0) {
			HudSkin.centered(painter, "Wird im Bild angezeigt", x + width / 2, y + (PREVIEW_HEIGHT - 8) / 2,
				HudSkin.MUTED);
			return;
		}
		// Passt die Anzeige nicht in den Streifen, wird sie verkleinert gezeigt
		int roomWidth = width - 70;
		int roomHeight = PREVIEW_HEIGHT - 8;
		float fit = Math.min(1.0F, Math.min(roomWidth / (float) (boxWidth + 2 * pad),
			roomHeight / (float) (boxHeight + 2 * pad)));
		int left = x + 60 + (roomWidth - Math.round((boxWidth + 2 * pad) * fit)) / 2;
		int top = y + 4 + (roomHeight - Math.round((boxHeight + 2 * pad) * fit)) / 2;
		painter.push(left, top, fit);
		HudOverlay.draw(config, element, painter, shown, new int[] { left + pad, top + pad, boxWidth, boxHeight });
		painter.pop();
		if (!config.isEnabled(selected))
			HudSkin.right(painter, "ausgeschaltet", x + width - 6, y + 5, HudSkin.MUTED);
	}

	/** Die Einstellungszeilen (Anzeige oder Profile). */
	private void renderRows(HudPainter painter, Frame f, int mouseX, int mouseY) {
		for (Widget widget : rows()) {
			int[] cell = cell(f, widget);
			if (!visible(f, cell[1], cell[3]))
				continue;
			renderRow(painter, widget, cell[0], cell[1], cell[2], cell[3], mouseX, mouseY);
		}
	}

	private void renderRow(HudPainter painter, Widget widget, int x, int y, int width, int height,
						   int mouseX, int mouseY) {
		boolean hovered = inside(mouseX, mouseY, x, y, width, height);
		switch (widget.kind) {
			case HEADING -> {
				HudSkin.section(painter, widget.label, x, y + 5, width);
				return;
			}
			case INFO -> {
				painter.text(widget.label, x + 2, y + 1, HudSkin.TEXT_DIM);
				return;
			}
			case BUTTON -> {
				HudSkin.button(painter, x, y, width, height, widget.label, hovered, widget.primary);
				return;
			}
			case FIELD -> {
				HudSkin.field(painter, x, y, width, height, widget.value, nameFocus);
				return;
			}
			default -> {
			}
		}

		boolean slides = widget.kind == Widget.Kind.SLIDER || widget.kind == Widget.Kind.RULE;
		HudSkin.rowCard(painter, x, y, width, height, hovered && !slides, false);
		if (widget.desc != null) {
			painter.text(widget.label, x + 6, y + 5, HudSkin.TEXT);
			painter.text(widget.desc, x + 6, y + 15, HudSkin.MUTED);
		} else {
			painter.text(widget.label, x + 6, y + (height - 8) / 2, widget.kind == Widget.Kind.RULE
				? HudSkin.MUTED : HudSkin.TEXT);
		}

		int right = x + width - 6;
		int textY = y + (height - 8) / 2;
		switch (widget.kind) {
			case TOGGLE -> HudSkin.toggleSwitch(painter, right - SWITCH_WIDTH, y + (height - 10) / 2, widget.on, hovered);
			case CYCLE -> {
				int boxWidth = dropdownWidth(width);
				HudSkin.dropdown(painter, right - boxWidth, y + (height - 14) / 2, boxWidth, 14,
					fit(painter, widget.value, boxWidth - 16), hovered);
			}
			case COLOUR -> {
				boolean open = picker == widget.colour && picker != Colour.NONE
					&& (picker != Colour.AXIS || pickerRule == widget.rule);
				HudSkin.swatch(painter, right - SWATCH_WIDTH, y + (height - 10) / 2, SWATCH_WIDTH, 10, widget.swatch,
					hovered || open);
				HudSkin.right(painter, HudSkin.hex(widget.swatch), right - SWATCH_WIDTH - 5, textY, HudSkin.TEXT_DIM);
			}
			case SLIDER -> {
				int[] track = sliderTrack(x, y, width, height);
				HudSkin.right(painter, widget.value, right, textY, HudSkin.TEXT_DIM);
				HudSkin.slider(painter, track[0], track[1], track[2], track[3], widget.progress,
					(drag == Drag.SLIDER && activeWidget == widget) || onTrack(mouseX, mouseY, track, y, height));
			}
			case RULE -> {
				int[] track = ruleTrack(x, y, width, height);
				HudSkin.right(painter, widget.value, right - RULE_SWATCH - 5, textY, HudSkin.TEXT_DIM);
				HudSkin.slider(painter, track[0], track[1], track[2], track[3], widget.progress,
					(drag == Drag.SLIDER && activeWidget == widget) || onTrack(mouseX, mouseY, track, y, height));
				boolean over = (picker == Colour.RULE && pickerRule == widget.rule)
					|| inside(mouseX, mouseY, right - RULE_SWATCH, y, RULE_SWATCH, height);
				HudSkin.swatch(painter, right - RULE_SWATCH, y + (height - 10) / 2, RULE_SWATCH, 10, widget.swatch, over);
			}
			default -> {
			}
		}
	}

	/** Schmale Leiste rechts, wenn die Liste länger ist als das Fenster. */
	private void renderScrollbar(HudPainter painter, Frame f) {
		int visibleHeight = f.bottom - f.listY;
		int content = contentHeight(f);
		if (content <= visibleHeight)
			return;
		int x = f.mainX + f.mainWidth - SCROLLBAR;
		painter.fill(x, f.listY, SCROLLBAR, visibleHeight, 0x33000000);
		int thumb = Math.max(16, visibleHeight * visibleHeight / content);
		int max = content - visibleHeight;
		int thumbY = f.listY + Math.round((visibleHeight - thumb) * (scroll() / (float) max));
		painter.fill(x, thumbY, SCROLLBAR, thumb, 0x88FFFFFF);
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
			|| onPickerTrack(mouseX, mouseY, pick.innerX, y, pick.innerWidth);
		HudSkin.sliderRow(painter, pick.innerX, y, pick.innerWidth, ROW, label, value + unit,
			value / (float) max, hovered, PICKER_LABEL, PICKER_VALUE);
	}

	// ---------- Gruppen ----------

	private static final int PANEL_GAP = 18;

	/** Die Gruppe, die gerade gewählt ist (alle gewählten Anzeigen gehören ihr), sonst null. */
	private HudConfig.Group currentGroup() {
		HudConfig.Group group = null;
		for (HudElement element : multi) {
			HudConfig.Group of = config.groupOf(element.module());
			if (of == null || (group != null && of != group))
				return null;
			group = of;
		}
		return group;
	}

	private java.util.Set<HudModule> multiModules() {
		java.util.Set<HudModule> modules = new java.util.HashSet<>();
		for (HudElement element : multi)
			modules.add(element.module());
		return modules;
	}

	private boolean panelVisible() {
		return editing && multiModules().size() > 1;
	}

	private int panelY(Bar bar) {
		return bar.y - PANEL_GAP - BAR_HEIGHT;
	}

	private void renderGroupPanel(HudPainter painter, Bar bar, int mouseX, int mouseY) {
		if (!panelVisible())
			return;
		int y = panelY(bar);
		int rowY = y + (BAR_HEIGHT - ROW) / 2;
		HudSkin.card(painter, bar.x, y, BAR_WIDTH, BAR_HEIGHT);
		HudConfig.Group group = currentGroup();
		int buttonY = y + (BAR_HEIGHT - BUTTON) / 2;
		int px = bar.x + PAD;
		HudSkin.button(painter, px, buttonY, 82, BUTTON, group == null ? "Gruppe bilden" : "Auflösen",
			inside(mouseX, mouseY, px, buttonY, 82, BUTTON), group == null);
		if (group == null)
			return;
		int trackX = px + 90;
		HudSkin.slider(painter, trackX, rowY + 3, 60, HudSkin.TRACK_HEIGHT, group.alpha / 100.0F,
			drag == Drag.SLIDER && slider == Slider.GROUP_ALPHA || inside(mouseX, mouseY, trackX, rowY, 60, ROW));
		HudSkin.right(painter, group.alpha + " %", trackX + 60 + 26, rowY + 2, HudSkin.TEXT_DIM);
		int swatchX = trackX + 60 + 34;
		HudSkin.rounded(painter, swatchX, rowY + 1, 16, ROW - 2, 2, 0xFF000000 | group.rgb);
		HudSkin.outline(painter, swatchX, rowY + 1, 16, ROW - 2, 2, HudSkin.BORDER);
		HudSkin.toggleRow(painter, swatchX + 22, rowY, 56, ROW, "Rahmen", group.border,
			inside(mouseX, mouseY, swatchX + 22, rowY, 56, ROW));
	}

	/** Klick im Gruppenfeld. @return true, wenn getroffen */
	private boolean clickGroupPanel(Bar bar, int mouseX, int mouseY) {
		if (!panelVisible())
			return false;
		int y = panelY(bar);
		if (!inside(mouseX, mouseY, bar.x, y, BAR_WIDTH, BAR_HEIGHT))
			return false;
		int rowY = y + (BAR_HEIGHT - ROW) / 2;
		int buttonY = y + (BAR_HEIGHT - BUTTON) / 2;
		int px = bar.x + PAD;
		HudConfig.Group group = currentGroup();
		if (inside(mouseX, mouseY, px, buttonY, 82, BUTTON)) {
			if (group == null)
				config.makeGroup(multiModules());
			else
				config.groups.remove(group);
			config.save();
			return true;
		}
		if (group == null)
			return true;
		int trackX = px + 90;
		if (inside(mouseX, mouseY, trackX - 2, rowY, 64, ROW)) {
			groupToEdit = group;
			startTrack(Slider.GROUP_ALPHA, trackX, 60, mouseX);
			return true;
		}
		int swatchX = trackX + 60 + 34;
		if (inside(mouseX, mouseY, swatchX, rowY, 18, ROW)) {
			groupToEdit = group;
			if (picker == Colour.GROUP)
				picker = Colour.NONE;
			else
				startPicker(Colour.GROUP);
		} else if (inside(mouseX, mouseY, swatchX + 22, rowY, 56, ROW)) {
			group.border = !group.border;
			config.save();
		}
		return true;
	}

	private void renderBar(HudPainter painter, Bar bar, int mouseX, int mouseY) {
		HudSkin.centered(painter, "Ziehen · leere Fläche aufziehen wählt mehrere (dann Gruppe bilden) · Rechtsklick setzt zurück",
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
		ctxPainter = painter;
		ctxTexts = texts;
		ctxWidth = width;
		ctxHeight = height;
		if (editing) {
			Bar bar = bar(width, height);
			if (picker == Colour.GROUP) {
				Pick pick = groupPick();
				if (inside(mouseX, mouseY, pick.x, pick.y, PICKER_WIDTH, pick.height)) {
					clickPicker(pick, mouseX, mouseY);
					return false;
				}
				picker = Colour.NONE;
			}
			if (clickGroupPanel(bar, mouseX, mouseY))
				return false;
			if (inside(mouseX, mouseY, bar.x, bar.y, BAR_WIDTH, BAR_HEIGHT)) {
				clickBar(bar, mouseX, mouseY);
				return false;
			}
			grabElement(painter, texts, width, height, mouseX, mouseY);
			return false;
		}

		Frame f = frame(width, height);
		clampScroll(f);
		closePickerIfGone(f);
		if (picker != Colour.NONE) {
			Pick pick = pick(f);
			if (inside(mouseX, mouseY, pick.x, pick.y, PICKER_WIDTH, pick.height)) {
				clickPicker(pick, mouseX, mouseY);
				return false;
			}
			picker = Colour.NONE; // Klick daneben schließt den Farbwähler
			return false;
		}
		searchFocus = false;
		if (!inside(mouseX, mouseY, f.x, f.y, f.width, f.height))
			return false; // außerhalb des Fensters passiert nichts; zum Verschieben gibt es den Modus

		// Seitenleiste
		int buttonX = f.sideX + 6;
		int buttonWidth = f.sideWidth - 12;
		if (inside(mouseX, mouseY, buttonX, f.doneY, buttonWidth, BUTTON)) {
			config.save();
			return true;
		}
		if (inside(mouseX, mouseY, buttonX, f.standardY, buttonWidth, BUTTON))
			return changed(config::reset);
		if (fullbrightFits(f) && inside(mouseX, mouseY, buttonX, f.fullbrightY, buttonWidth, ROW)) {
			config.fullbright = !config.fullbright;
			config.save();
			return false;
		}
		for (int entry = -1; entry <= NAV_PROFILES; entry++) {
			if (!inside(mouseX, mouseY, f.sideX + 4, navY(f, entry), f.sideWidth - 8, NAV_HEIGHT))
				continue;
			nameFocus = false;
			if (entry == NAV_PROFILES) {
				view = View.PROFILES;
				profiles = null;
				status = "";
				confirmDelete = false;
			} else {
				view = View.OVERVIEW;
				category = entry;
				search = "";
			}
			scroll[view.ordinal()] = 0;
			return false;
		}

		// Oben
		int editX = f.mainX + f.mainWidth - EDIT_WIDTH;
		if (inside(mouseX, mouseY, editX, f.topY, EDIT_WIDTH, TOP_HEIGHT)) {
			editing = true;
			multi.clear();
			nameFocus = false;
			return false;
		}
		if (view == View.OVERVIEW && inside(mouseX, mouseY, f.mainX, f.topY, editX - 6 - f.mainX, TOP_HEIGHT)) {
			searchFocus = true;
			return false;
		}
		if (view == View.DETAIL) {
			if (inside(mouseX, mouseY, f.mainX, f.topY, BACK_WIDTH, TOP_HEIGHT)) {
				view = View.OVERVIEW;
				return false;
			}
			if (inside(mouseX, mouseY, f.mainX + BACK_WIDTH, f.topY, editX - f.mainX - BACK_WIDTH, TOP_HEIGHT))
				return changed(() -> toggle(selected)); // Name oder Schalter: ein- und ausschalten
		}

		// Liste
		if (mouseY < f.listY || mouseY >= f.bottom)
			return false;
		if (view == View.OVERVIEW)
			clickTiles(f, mouseX, mouseY);
		else if (!clickRows(f, mouseX, mouseY))
			nameFocus = false;
		return false;
	}

	private void clickTiles(Frame f, int mouseX, int mouseY) {
		for (Tile tile : tiles(f)) {
			int y = f.listY - scroll() + tile.y;
			if (tile.module == null || !visible(f, y, tile.height)
					|| !inside(mouseX, mouseY, tile.x, y, tile.width, tile.height))
				continue;
			if (inside(mouseX, mouseY, tile.x + tile.width - SWITCH_CELL, y, SWITCH_CELL, tile.height)) {
				changed(() -> toggle(tile.module));
				return;
			}
			selected = tile.module;
			view = View.DETAIL;
			scroll[View.DETAIL.ordinal()] = 0;
			return;
		}
	}

	/** @return true, wenn eine Zeile getroffen wurde */
	private boolean clickRows(Frame f, int mouseX, int mouseY) {
		for (Widget widget : rows()) {
			int[] cell = cell(f, widget);
			int x = cell[0];
			int y = cell[1];
			int width = cell[2];
			int height = cell[3];
			if (widget.kind == Widget.Kind.HEADING || widget.kind == Widget.Kind.INFO || !visible(f, y, height))
				continue;
			if (widget.kind == Widget.Kind.SLIDER) {
				int[] track = sliderTrack(x, y, width, height);
				if (!onTrack(mouseX, mouseY, track, y, height))
					continue;
				activeWidget = widget;
				startTrack(Slider.WIDGET, track[0], track[2], mouseX);
				return true;
			}
			if (widget.kind == Widget.Kind.RULE) {
				if (inside(mouseX, mouseY, x + width - 6 - RULE_SWATCH, y, RULE_SWATCH + 6, height)) {
					widget.swatchClick.run();
					return true;
				}
				int[] track = ruleTrack(x, y, width, height);
				if (!onTrack(mouseX, mouseY, track, y, height))
					continue;
				activeWidget = widget;
				startTrack(Slider.WIDGET, track[0], track[2], mouseX);
				return true;
			}
			if (!inside(mouseX, mouseY, x, y, width, height))
				continue;
			widget.click.run();
			config.save();
			return true;
		}
		return false;
	}

	/** Mausrad: die Liste rechts scrollen. */
	public void mouseScrolled(int width, int height, int mouseX, int mouseY, double amount) {
		if (editing || picker != Colour.NONE)
			return;
		Frame f = frame(width, height);
		if (!inside(mouseX, mouseY, f.mainX, f.listY, f.mainWidth, f.bottom - f.listY))
			return;
		scroll[view.ordinal()] -= (int) Math.round(amount * SCROLL_STEP);
		clampScroll(f);
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
			int[] box = HudOverlay.box(config, element, painter, shown, width, height);
			if (!inside(mouseX, mouseY, box, 3))
				continue;
			HudModule module = element.module();
			HudConfig.Entry entry = config.get(module);
			entry.anchorX = 0; // zurück an den Standardplatz: oben links
			entry.anchorY = 0;
			HudOverlay.move(entry, element, module.defaultX,
				module.defaultY + (element.isSlot() ? element.slot() * 18 : 0), box[2], box[3], width, height);
			config.save();
			return;
		}
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
	 * Tasteneingabe im Fenster: Esc geht eine Stufe zurück, in Such-, Namens- und Hex-Feld wird getippt.
	 *
	 * @return true, wenn die Taste verbraucht wurde
	 */
	public boolean keyPressed(int key) {
		if (key == 256) { // Esc geht erst eine Stufe zurück, statt gleich das Fenster zu schließen
			if (picker != Colour.NONE) {
				picker = Colour.NONE;
				return true;
			}
			if (nameFocus) {
				nameFocus = false;
				return true;
			}
			if (searchFocus || !search.isEmpty()) {
				searchFocus = false;
				search = "";
				return true;
			}
			if (editing && !multi.isEmpty()) {
				multi.clear();
				return true;
			}
			if (editing) {
				editing = false;
				config.save();
				return true;
			}
			if (view != View.OVERVIEW) {
				view = View.OVERVIEW;
				return true;
			}
			return false;
		}
		if (!editing && view == View.PROFILES && nameFocus) {
			if (key == 259 && !profileName.isEmpty())
				profileName = profileName.substring(0, profileName.length() - 1);
			if (key == 257 || key == 335)
				nameFocus = false;
			return key == 259 || key == 257 || key == 335;
		}
		if (!editing && searchFocus) {
			if (key == 259 && !search.isEmpty()) {
				search = search.substring(0, search.length() - 1);
				scroll[View.OVERVIEW.ordinal()] = 0;
			}
			if (key == 257 || key == 335)
				searchFocus = false;
			return key == 259 || key == 257 || key == 335;
		}
		if (picker == Colour.NONE || config.colorMode != MODE_HEX)
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

	/** Zeichen für das Namensfeld der Profile und die Suche. */
	public boolean charTyped(int codepoint) {
		if (editing)
			return false;
		if (view == View.PROFILES && nameFocus) {
			if (!HudProfiles.allowed(codepoint))
				return false;
			if (profileName.length() < HudProfiles.MAX_NAME)
				profileName += (char) codepoint;
			return true;
		}
		if (view == View.OVERVIEW && searchFocus) {
			if (codepoint < 32 || search.length() >= 24)
				return true;
			search += new String(Character.toChars(codepoint));
			scroll[View.OVERVIEW.ordinal()] = 0;
			return true;
		}
		return false;
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
			case BAND -> {
				bandX1 = mouseX;
				bandY1 = mouseY;
				selectBand(painter, texts, width, height);
			}
			case GROUP -> dragGroup(width, height, mouseX, mouseY);
			default -> {
			}
		}
	}

	public void mouseUp() {
		if (drag == Drag.NONE)
			return;
		drag = Drag.NONE;
		dragged = null;
		activeWidget = null;
		groupLeader = null;
		groupStart.clear();
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
			HudConfig.Group own = config.groupOf(element.module());
			if (own != null && !multi.contains(element)) {
				// Zu einer Gruppe gehörig: die ganze Gruppe wird gegriffen
				multi.clear();
				for (HudElement member : elements)
					if (own.members.contains(member.module()))
						multi.add(member);
			}
			if (multi.size() > 1 && multi.contains(element)) {
				// Ein gewähltes Stück gegriffen: alle gewählten wandern mit
				groupStart.clear();
				for (HudElement member : multi)
					groupStart.put(member, HudOverlay.box(config, member, painter, shown, width, height));
				groupLeader = element;
				groupGrabX = mouseX;
				groupGrabY = mouseY;
				drag = Drag.GROUP;
				return;
			}
			multi.clear();
			dragged = element;
			drag = Drag.ELEMENT;
			grabX = mouseX - box[0];
			grabY = mouseY - box[1];
			return;
		}
		// Leere Fläche: Auswahlrahmen aufziehen
		multi.clear();
		drag = Drag.BAND;
		bandX0 = mouseX;
		bandY0 = mouseY;
		bandX1 = mouseX;
		bandY1 = mouseY;
	}

	/** Wählt alle Stücke, die der Auswahlrahmen berührt. */
	private void selectBand(HudPainter painter, Map<HudModule, String> texts, int width, int height) {
		Map<HudModule, String> shown = withPlaceholders(texts);
		int left = Math.min(bandX0, bandX1);
		int top = Math.min(bandY0, bandY1);
		int right = Math.max(bandX0, bandX1);
		int bottom = Math.max(bandY0, bandY1);
		multi.clear();
		for (HudElement element : HudOverlay.elements(config, painter, shown, true)) {
			int[] box = HudOverlay.box(config, element, painter, shown, width, height);
			if (box[0] <= right && box[0] + box[2] >= left && box[1] <= bottom && box[1] + box[3] >= top) {
				multi.add(element);
				selected = element.module();
			}
		}
	}

	/** Verschiebt alle gewählten Stücke um dieselbe Strecke; das gegriffene rastet am Raster ein. */
	private void dragGroup(int width, int height, int mouseX, int mouseY) {
		if (groupLeader == null)
			return;
		int dx = mouseX - groupGrabX;
		int dy = mouseY - groupGrabY;
		int[] lead = groupStart.get(groupLeader);
		if (lead != null && config.grid) {
			int size = HudConfig.clampGrid(config.gridSize);
			dx = Math.round((lead[0] + dx) / (float) size) * size - lead[0];
			dy = Math.round((lead[1] + dy) / (float) size) * size - lead[1];
		}
		// Nicht über den Bildrand hinaus: die Gruppe als Ganzes begrenzen
		for (int[] box : groupStart.values()) {
			dx = Math.max(-box[0], Math.min(dx, width - box[2] - box[0]));
			dy = Math.max(-box[1], Math.min(dy, height - box[3] - box[1]));
		}
		for (Map.Entry<HudElement, int[]> member : groupStart.entrySet()) {
			int[] box = member.getValue();
			HudOverlay.move(config.get(member.getKey().module()), member.getKey(), box[0] + dx, box[1] + dy, box[2],
				box[3], width, height);
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
		HudOverlay.move(config.get(dragged.module()), dragged, snapped.x, snapped.y, box[2], box[3], width, height);
	}

	// ---------- Regler ----------

	/** Schiene eines Reglers in einer Einstellungszeile als {x, y, Breite, Höhe}: rechts, vor dem Wert. */
	private static int[] sliderTrack(int x, int y, int width, int height) {
		int trackWidth = Math.max(30, Math.min(120, width / 2 - 24));
		int right = x + width - 6 - VALUE_WIDTH;
		return new int[] { right - trackWidth, y + (height - HudSkin.TRACK_HEIGHT) / 2, trackWidth,
			HudSkin.TRACK_HEIGHT };
	}

	/** Schiene einer Farbregel: zwischen "ab" und Wert samt Farbfeld. */
	private static int[] ruleTrack(int x, int y, int width, int height) {
		int left = x + 24;
		int right = x + width - 6 - RULE_SWATCH - 5 - VALUE_WIDTH;
		return new int[] { left, y + (height - HudSkin.TRACK_HEIGHT) / 2, Math.max(20, right - left),
			HudSkin.TRACK_HEIGHT };
	}

	/** Nur die Schiene zählt (mit etwas Luft) – sonst würde ein Klick auf die Beschriftung den Wert verstellen. */
	private static boolean onTrack(int mouseX, int mouseY, int[] track, int y, int height) {
		return inside(mouseX, mouseY, track[0] - 3, y, track[2] + 6, height);
	}

	private static boolean onPickerTrack(int mouseX, int mouseY, int x, int y, int width) {
		int[] track = HudSkin.track(x, y, width, ROW, PICKER_LABEL, PICKER_VALUE);
		return inside(mouseX, mouseY, track[0] - 2, y, track[2] + 4, ROW);
	}

	private boolean startComponent(Pick pick, int mouseX, int mouseY, int y, Slider which) {
		if (!onPickerTrack(mouseX, mouseY, pick.innerX, y, pick.innerWidth))
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

	private void applySlider(int mouseX) {
		float progress = Math.max(0.0F, Math.min(1.0F,
			(mouseX - (trackX + 1)) / (float) Math.max(1, trackWidth - 2)));
		switch (slider) {
			case GROUP_ALPHA -> {
				if (groupToEdit != null)
					groupToEdit.alpha = step(progress, 0, 100, 5);
			}
			case GRID -> config.gridSize = HudConfig.clampGrid(
				step(progress, HudConfig.MIN_GRID, HudConfig.MAX_GRID, HudConfig.GRID_STEP));
			case RED -> setRgb(HudSkin.withComponent(colour(), 16, step(progress, 0, 255, 1)));
			case GREEN -> setRgb(HudSkin.withComponent(colour(), 8, step(progress, 0, 255, 1)));
			case BLUE -> setRgb(HudSkin.withComponent(colour(), 0, step(progress, 0, 255, 1)));
			case HUE -> setHsl(0, step(progress, 0, 360, 1));
			case SATURATION -> setHsl(1, step(progress, 0, 100, 1));
			case LIGHTNESS -> setHsl(2, step(progress, 0, 100, 1));
			case WIDGET -> {
				if (activeWidget != null && activeWidget.slide != null)
					activeWidget.slide.accept(progress);
			}
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
		if (picker == Colour.GROUP)
			return groupToEdit == null ? 0 : groupToEdit.rgb;
		if (picker == Colour.PRESS)
			return config.get(selected).pressRgb;
		HudConfig.Entry entry = config.get(selected);
		if (picker == Colour.AXIS)
			return entry.axisRgb[Math.max(0, Math.min(entry.axisRgb.length - 1, pickerRule))];
		if (picker == Colour.RULE)
			return entry.ruleRgb[Math.max(0, Math.min(HudConfig.MAX_RULES - 1, pickerRule))];
		return picker == Colour.BACKGROUND ? entry.backgroundRgb : entry.textRgb;
	}

	private void setColour(int rgb) {
		if (picker == Colour.GROUP) {
			if (groupToEdit != null)
				groupToEdit.rgb = rgb & 0xFFFFFF;
			return;
		}
		if (picker == Colour.PRESS) {
			config.get(selected).pressRgb = rgb & 0xFFFFFF;
			return;
		}
		HudConfig.Entry entry = config.get(selected);
		if (picker == Colour.AXIS)
			entry.axisRgb[Math.max(0, Math.min(entry.axisRgb.length - 1, pickerRule))] = rgb & 0xFFFFFF;
		else if (picker == Colour.RULE)
			entry.ruleRgb[Math.max(0, Math.min(HudConfig.MAX_RULES - 1, pickerRule))] = rgb & 0xFFFFFF;
		else if (picker == Colour.BACKGROUND)
			entry.backgroundRgb = rgb & 0xFFFFFF;
		else
			entry.textRgb = rgb & 0xFFFFFF;
	}

	/** Öffnet den Wähler für eine Farbe und stellt alle drei Darstellungen darauf ein. */
	private void startPicker(Colour which) {
		picker = which;
		syncFromColour();
	}

	/** Zweiter Klick auf dieselbe Farbe schließt den Wähler wieder. */
	private void togglePicker(Colour which) {
		if (picker == which)
			picker = Colour.NONE;
		else
			startPicker(which);
	}

	private void startRulePicker(int rule) {
		if (picker == Colour.RULE && pickerRule == rule) {
			picker = Colour.NONE;
			return;
		}
		pickerRule = rule;
		startPicker(Colour.RULE);
	}

	private void syncFromColour() {
		int[] converted = HudSkin.toHsl(colour());
		System.arraycopy(converted, 0, hsl, 0, hsl.length);
		hexInput = HudSkin.hex(colour()).substring(1);
	}

	/** Schließt den Farbwähler, wenn seine Zeile gerade nicht (mehr) zu sehen ist. */
	private void closePickerIfGone(Frame f) {
		if (picker == Colour.NONE)
			return;
		if (picker == Colour.GROUP || pickerAnchor(f) == null)
			picker = Colour.NONE;
	}

	/** Die sichtbare Zeile, zu der der offene Farbwähler gehört, als {x, y, Breite, Höhe}; sonst null. */
	private int[] pickerAnchor(Frame f) {
		if (view == View.OVERVIEW)
			return null;
		for (Widget widget : rows()) {
			if (widget.colour != picker || ((picker == Colour.RULE || picker == Colour.AXIS) && widget.rule != pickerRule))
				continue;
			int[] cell = cell(f, widget);
			return visible(f, cell[1], cell[3]) ? cell : null;
		}
		return null;
	}

	// ---------- Zeilen der Einstellungen ----------

	/** Eine Zeile (oder Zelle einer Zeile) der Einstellungen. */
	private static final class Widget {
		enum Kind { HEADING, TOGGLE, CYCLE, SLIDER, COLOUR, BUTTON, INFO, FIELD, RULE }

		final Kind kind;
		final String label;
		/** Zweite, graue Zeile unter der Beschriftung; null = keine. */
		String desc;
		String value = "";
		boolean on;
		boolean primary;
		float progress;
		int swatch;
		Runnable click;
		Runnable swatchClick;
		Consumer<Float> slide;
		/** Zu welcher Farbe die Zeile gehört (für die Lage des Farbwählers). */
		Colour colour = Colour.NONE;
		int col;
		int cols = 1;
		int y;
		int height = CARD;
		/** Abstand nach der Zeile. */
		int after = CARD_GAP;
		int rule = -1;

		Widget(Kind kind, String label) {
			this.kind = kind;
			this.label = label;
		}
	}

	/** Sammelt Zeilen von oben nach unten; Zellen einer Zeile teilen sich das y. */
	private static final class Rows {
		final List<Widget> list = new ArrayList<>();
		int y;

		Widget add(Widget widget) {
			widget.y = y;
			list.add(widget);
			if (widget.col == widget.cols - 1)
				y += widget.height + widget.after;
			return widget;
		}

		void gap(int height) {
			y += height;
		}

		void info(String text) {
			Widget widget = new Widget(Widget.Kind.INFO, text);
			widget.height = 10;
			widget.after = 1;
			add(widget);
		}

		void heading(String text) {
			if (!list.isEmpty())
				y += 6;
			Widget widget = new Widget(Widget.Kind.HEADING, text);
			widget.height = 16;
			widget.after = 2;
			add(widget);
		}

		private static Widget card(Widget.Kind kind, String label, String desc) {
			Widget widget = new Widget(kind, label);
			widget.desc = desc;
			widget.height = desc == null ? CARD : CARD_DESC;
			return widget;
		}

		Widget toggle(String label, String desc, boolean on, Runnable click) {
			Widget widget = card(Widget.Kind.TOGGLE, label, desc);
			widget.on = on;
			widget.click = click;
			return add(widget);
		}

		/** Schalter als Zelle einer Zeile mit {@code cols} Zellen. */
		Widget toggleCell(String label, boolean on, Runnable click, int col, int cols) {
			Widget widget = card(Widget.Kind.TOGGLE, label, null);
			widget.on = on;
			widget.click = click;
			widget.col = col;
			widget.cols = cols;
			return add(widget);
		}

		Widget cycle(String label, String desc, String value, Runnable click) {
			Widget widget = card(Widget.Kind.CYCLE, label, desc);
			widget.value = value;
			widget.click = click;
			return add(widget);
		}

		Widget slider(String label, String desc, String value, float progress, Consumer<Float> slide) {
			Widget widget = card(Widget.Kind.SLIDER, label, desc);
			widget.value = value;
			widget.progress = progress;
			widget.slide = slide;
			return add(widget);
		}

		Widget colour(String label, String desc, int rgb, Colour which, Runnable click) {
			Widget widget = card(Widget.Kind.COLOUR, label, desc);
			widget.swatch = 0xFF000000 | rgb;
			widget.colour = which;
			widget.click = click;
			return add(widget);
		}

		/** Farbfeld als Zelle einer Zeile mit {@code cols} Zellen. */
		Widget colourCell(String label, int rgb, Colour which, Runnable click, int col, int cols) {
			Widget widget = card(Widget.Kind.COLOUR, label, null);
			widget.swatch = 0xFF000000 | rgb;
			widget.colour = which;
			widget.click = click;
			widget.col = col;
			widget.cols = cols;
			return add(widget);
		}

		Widget button(String label, boolean primary, Runnable click) {
			return button(label, primary, click, 0, 1);
		}

		/** Schaltfläche; mehrere nebeneinander über col und cols. */
		Widget button(String label, boolean primary, Runnable click, int col, int cols) {
			Widget widget = new Widget(Widget.Kind.BUTTON, label);
			widget.primary = primary;
			widget.click = click;
			widget.height = BUTTON + 2;
			widget.col = col;
			widget.cols = cols;
			return add(widget);
		}
	}

	/** Die Zeilen der aktuellen Ansicht (leer in der Übersicht). */
	private List<Widget> rows() {
		Rows rows = new Rows();
		if (view == View.DETAIL)
			detailRows(rows, config.get(selected));
		else if (view == View.PROFILES)
			profileRows(rows);
		return rows.list;
	}

	/** Alle Einstellungen einer Anzeige, nach Abschnitten. */
	private void detailRows(Rows rows, HudConfig.Entry entry) {
		rows.heading("Darstellung");
		rows.slider("Größe", "Wie groß die Anzeige im Bild ist.", HudConfig.clampScale(entry.scale) + " %",
			progress(entry.scale, HudConfig.MIN_SCALE, HudConfig.MAX_SCALE),
			p -> entry.scale = HudConfig.clampScale(step(p, HudConfig.MIN_SCALE, HudConfig.MAX_SCALE,
				HudConfig.SCALE_STEP)));
		rows.colour("Textfarbe", "Grundfarbe von Schrift und Werten.", entry.textRgb, Colour.TEXT,
			() -> togglePicker(Colour.TEXT));
		rows.toggle("Schatten", "Dunkler Schatten unter der Schrift.", entry.shadow, () -> entry.shadow = !entry.shadow);

		formatRows(rows, entry);
		if (selected.equipment)
			equipmentRows(rows, entry);
		backgroundRows(rows, entry);
		ruleRows(rows, entry);
		placeRows(rows, entry);
		privacyRows(rows, entry);
	}

	/** Wie der Wert aussieht: Darstellung, Beschriftung, Einheit, Stellen, Koordinaten. */
	private void formatRows(Rows rows, HudConfig.Entry entry) {
		if (selected.equipment || selected == HudModule.SCOREBOARD)
			return;
		rows.heading(selected.keys ? "Tasten" : selected == HudModule.EFFECTS ? "Effekte" : "Text");
		if (selected.variants != null) {
			String[] names = selected.variants;
			rows.cycle("Format", "Wie der Wert dargestellt wird.", names[Math.min(entry.variant, names.length - 1)],
				() -> entry.variant = (entry.variant + 1) % names.length);
		}
		if (selected.keys) {
			rows.colour("Farbe beim Klick", "So leuchtet eine Taste beim Drücken auf.", entry.pressRgb,
				Colour.PRESS, () -> togglePicker(Colour.PRESS));
			return;
		}
		if (selected == HudModule.EFFECTS) {
			rows.info(entry.variant == 0 ? "Wie in Minecraft; die Größe stellst du oben ein."
				: "Zeigt Stufe und Restzeit als Text.");
			return;
		}
		if (!selected.shortLabel.isEmpty())
			rows.toggle("Beschriftung davor", "z.B. \"" + selected.shortLabel + "\" vor dem Wert.", entry.labels,
				() -> entry.labels = !entry.labels);
		if (selected.unit != null)
			rows.toggle("Einheit dahinter", "z.B. \"" + selected.unit + "\" hinter dem Wert.", entry.units,
				() -> entry.units = !entry.units);

		boolean hasDecimals = selected == HudModule.COORDS
			|| selected == HudModule.SPEED || selected == HudModule.MEMORY;
		if (hasDecimals) {
			int minimum = selected == HudModule.MEMORY ? 1 : 0;
			int shown = Math.max(minimum, entry.decimals);
			rows.slider("Nachkommastellen", null, Integer.toString(shown), shown / (float) HudConfig.MAX_DECIMALS,
				progress -> entry.decimals = Math.max(minimum, step(progress, 0, HudConfig.MAX_DECIMALS, 1)));
		}

		if (selected != HudModule.COORDS)
			return;
		rows.heading("Koordinaten");
		String[] axes = { "X", "Y", "Z" };
		for (int i = 0; i < axes.length; i++) {
			int axis = i;
			rows.toggleCell(axes[i], entry.axes[i], () -> {
				// Mindestens eine Achse bleibt an
				boolean others = false;
				for (int j = 0; j < entry.axes.length; j++)
					others |= j != axis && entry.axes[j];
				if (others || !entry.axes[axis])
					entry.axes[axis] = !entry.axes[axis];
			}, i, axes.length);
		}
		rows.cycle("Achsen einfärben", "Eigene Farbe für X, Y und Z.",
			HudConfig.AXIS_COLOUR_NAMES[entry.axisColours],
			() -> entry.axisColours = (entry.axisColours + 1) % HudConfig.AXIS_COLOUR_NAMES.length);
		if (entry.axisColours != 0) {
			for (int i = 0; i < axes.length; i++) {
				int axis = i;
				Widget colour = rows.colourCell(axes[i], entry.axisRgb[i], Colour.AXIS, () -> {
					if (picker == Colour.AXIS && pickerRule == axis) {
						picker = Colour.NONE;
						return;
					}
					pickerRule = axis;
					startPicker(Colour.AXIS);
				}, i, axes.length);
				colour.rule = i;
			}
			if (entry.axisColours == 1 && !entry.labels)
				rows.info("Buchstaben sind aus (\"Beschriftung davor\" oben).");
		}
		rows.toggle("Untereinander", "Jede Achse in einer eigenen Zeile.", entry.stacked,
			() -> entry.stacked = !entry.stacked);
		if (entry.stacked)
			rows.cycle("Ausrichtung", null, HudConfig.ALIGN_NAMES[entry.align],
				() -> entry.align = (entry.align + 1) % HudConfig.ALIGN_NAMES.length);
		else
			rows.cycle("Trenner", "Zeichen zwischen den Achsen.", HudConfig.SEPARATOR_NAMES[entry.separator],
				() -> entry.separator = (entry.separator + 1) % HudConfig.SEPARATORS.length);
		rows.toggle("Himmelsrichtung", "Zeigt N, O, S oder W dazu.", entry.facing, () -> entry.facing = !entry.facing);
	}

	/** Ausrüstung: Ausrichtung, Haltbarkeit, einzeln verschieben und welche Plätze. */
	private void equipmentRows(Rows rows, HudConfig.Entry entry) {
		rows.heading("Ausrüstung");
		rows.toggle("Untereinander", "Teile senkrecht statt nebeneinander.", entry.vertical,
			() -> entry.vertical = !entry.vertical);
		rows.toggle("Haltbarkeit", "Prozent bei jedem Teil.", entry.percent, () -> entry.percent = !entry.percent);
		rows.toggle("Einzeln verschieben", "Jedes Teil lässt sich im Bild für sich ziehen.", entry.split,
			() -> entry.split = !entry.split);
		HudSlot[] slots = HudSlot.values();
		int columns = 3;
		for (HudSlot slot : slots) {
			int index = slot.ordinal();
			rows.toggleCell(slot.label, entry.slots[index], () -> entry.slots[index] = !entry.slots[index],
				index % columns, columns);
		}
		if (slots.length % columns != 0)
			rows.gap(CARD + CARD_GAP); // angebrochene letzte Zeile
	}

	private void backgroundRows(Rows rows, HudConfig.Entry entry) {
		rows.heading("Hintergrund");
		rows.toggle("Hintergrund", "Fläche hinter der Anzeige.", entry.background,
			() -> entry.background = !entry.background);
		if (entry.background) {
			rows.slider("Deckkraft", null, entry.backgroundAlpha + " %", entry.backgroundAlpha / 100.0F,
				p -> entry.backgroundAlpha = step(p, 0, 100, 5));
			rows.colour("Farbe", null, entry.backgroundRgb, Colour.BACKGROUND, () -> togglePicker(Colour.BACKGROUND));
			rows.slider("Ecken", "Rundung der Fläche.", HudConfig.clampCorner(entry.corner) + " px",
				progress(entry.corner, HudConfig.MIN_CORNER, HudConfig.MAX_CORNER),
				p -> entry.corner = HudConfig.clampCorner(step(p, HudConfig.MIN_CORNER, HudConfig.MAX_CORNER, 1)));
			rows.toggle("Rahmen", "Feine Linie in der Textfarbe.", entry.border, () -> entry.border = !entry.border);
		}
		rows.button("Hintergrund auf alle Anzeigen übertragen", false, () -> config.copyBackgroundToAll(selected));
	}

	/** Farbe nach Wert (grün, gelb, rot) und "nur bei Bedarf zeigen". */
	private void ruleRows(Rows rows, HudConfig.Entry entry) {
		HudModule.Metric metric = selected.metric;
		if (metric == null)
			return;
		String percent = selected == HudModule.MEMORY || selected == HudModule.ARMOR ? "%" : "";
		rows.heading("Farbe nach Wert");
		rows.toggle("Farbe nach Wert", "Färbt den Wert je nach Höhe ein.", entry.rulesOn, () -> {
			entry.rulesOn = !entry.rulesOn;
			if (entry.rulesOn && entry.ruleCount == 0)
				entry.seedRules(selected);
		});
		if (entry.rulesOn) {
			rows.info("Ab diesem Wert gilt die Farbe:");
			for (int i = 0; i < entry.ruleCount; i++) {
				int index = i;
				Widget rule = Rows.card(Widget.Kind.RULE, "ab", null);
				rule.rule = i;
				rule.colour = Colour.RULE;
				rule.value = entry.ruleAt[i] + percent;
				rule.progress = entry.ruleAt[i] / (float) metric.max();
				rule.swatch = 0xFF000000 | entry.ruleRgb[i];
				rule.slide = progress -> entry.ruleAt[index] = step(progress, 0, metric.max(), metric.step());
				rule.swatchClick = () -> startRulePicker(index);
				rows.add(rule);
			}
			rows.button("+ Regel", false, () -> {
				if (entry.ruleCount >= HudConfig.MAX_RULES)
					return;
				int last = entry.ruleCount - 1;
				entry.ruleAt[entry.ruleCount] = last < 0 ? 0
					: Math.min(metric.max(), entry.ruleAt[last] + metric.max() / 5);
				entry.ruleRgb[entry.ruleCount] = last < 0 ? 0xFFFFFF : entry.ruleRgb[last];
				entry.ruleCount++;
			}, 0, 3);
			rows.button("- Regel", false, () -> {
				if (entry.ruleCount > 1)
					entry.ruleCount--;
			}, 1, 3);
			rows.button("Standardregeln", false, () -> entry.seedRules(selected), 2, 3);
			rows.info(selected == HudModule.SPEED ? "Wert = Blöcke pro Sekunde."
				: selected == HudModule.ARMOR ? "Wert = Haltbarkeit, je Teil."
				: selected == HudModule.MEMORY ? "Wert = belegter Speicher."
				: "Darunter gilt die normale Textfarbe.");
		}
		String[] modes = { "Immer", "Unter der Grenze", "Ab der Grenze" };
		rows.cycle("Sichtbar", "Nur bei Bedarf zeigen, z.B. wenig FPS.", modes[entry.showMode],
			() -> entry.showMode = (entry.showMode + 1) % modes.length);
		if (entry.showMode != 0) {
			int limit = Math.max(0, Math.min(metric.max(), entry.showLimit));
			rows.slider("Grenze", null, limit + percent, limit / (float) metric.max(),
				progress -> entry.showLimit = step(progress, 0, metric.max(), metric.step()));
		}
	}

	/** Anker und Abstand: wo die Anzeige am Bildschirm hängt. */
	private void placeRows(Rows rows, HudConfig.Entry entry) {
		String[] horizontal = { "Links", "Mitte", "Rechts" };
		String[] vertical = { "Oben", "Mitte", "Unten" };
		rows.heading("Position");
		rows.cycle("Waagerecht", "Rand, von dem aus gemessen wird.", horizontal[entry.anchorX],
			() -> reanchor(selected, (entry.anchorX + 1) % 3, entry.anchorY));
		rows.cycle("Senkrecht", null, vertical[entry.anchorY],
			() -> reanchor(selected, entry.anchorX, (entry.anchorY + 1) % 3));
		rows.toggle("Automatisch beim Verschieben", "Der Anker folgt dem nächsten Rand.", entry.autoAnchor,
			() -> entry.autoAnchor = !entry.autoAnchor);
		rows.info("Abstand: " + entry.x + " / " + entry.y + " px. Der Anker hält ihn, auch wenn sich");
		rows.info("die Fenstergröße ändert. Verschieben: \"Overlay bearbeiten\".");
	}

	/**
	 * Privatsphäre: was hier markiert ist, zeichnet nicht das Spiel, sondern der Launcher in einem
	 * Fenster, das Windows aus jeder Bildschirmaufnahme heraushält.
	 */
	private void privacyRows(Rows rows, HudConfig.Entry entry) {
		rows.heading("Privatsphäre");
		String reason = HudPrivacy.reasonAgainst(config, selected);
		if (reason != null) {
			rows.info("Nicht möglich: " + reason + ".");
		} else {
			HudConfig.Group group = config.groupOf(selected);
			rows.toggle(group == null ? "Diese Anzeige privat" : "Ganze Gruppe privat",
				"Taucht in Aufnahmen (Discord, OBS) nicht auf.", group == null ? entry.privat : group.privat,
				() -> {
					if (group == null)
						entry.privat = !entry.privat;
					else
						group.privat = !group.privat;
				});
		}
		rows.info("Privates zeichnet der Launcher in einem eigenen Fenster:");
		rows.info("auf dem Bildschirm sichtbar, in Aufnahmen nicht.");
		if (config.isPrivate(selected))
			rows.info("Ohne laufenden Launcher bleibt die Anzeige aus.");
	}

	private void profileRows(Rows rows) {
		if (profiles == null)
			profiles = HudProfiles.list(config);
		rows.info("Aktiv: " + (config.activeProfile.isEmpty() ? HudProfiles.DEFAULT : config.activeProfile)
			+ ". Änderungen gelten für das aktive Profil.");

		// Server: nur für Profile außer "Standard" (das gilt überall sonst)
		if (!HudProfiles.DEFAULT.equals(config.activeProfile) && !config.activeProfile.isEmpty()) {
			rows.heading("Automatisch auf diesen Servern");
			if (config.servers.isEmpty())
				rows.info("Keiner - das Profil wechselt nur von Hand.");
			for (String server : config.servers)
				rows.info(server.length() > 50 ? server.substring(0, 49) + "..." : server);
			String current = HudProfiles.currentServer();
			rows.button(current.isEmpty() ? "Nicht auf einem Server" : "Diesen Server hinzufügen", false, () -> {
				if (!current.isEmpty() && config.servers.size() < 8 && config.servers.stream()
						.noneMatch(s -> HudProfiles.normalize(s).equals(HudProfiles.normalize(current))))
					config.servers.add(current);
			}, 0, 2);
			rows.button("Leeren", false, config.servers::clear, 1, 2);
		}

		rows.heading("Neues Profil aus dem Aktuellen");
		Widget field = new Widget(Widget.Kind.FIELD, "");
		field.value = profileName.isEmpty() && !nameFocus ? "Name eingeben..." : profileName;
		field.height = FIELD_HEIGHT + 2;
		field.click = () -> nameFocus = true;
		rows.add(field);
		rows.button("Anlegen", true, this::saveProfile);

		rows.heading("Profile");
		for (String name : profiles) {
			rows.toggle(name, name.equals(config.activeProfile) ? "Gerade aktiv" : null, name.equals(profilePick),
				() -> {
					profilePick = name;
					confirmDelete = false;
				});
		}
		if (!profilePick.isEmpty() && profiles.contains(profilePick)) {
			boolean fixed = HudProfiles.DEFAULT.equals(profilePick);
			rows.button("\"" + profilePick + "\" laden", true, this::loadProfile, 0, fixed ? 1 : 2);
			if (!fixed)
				rows.button(confirmDelete ? "Wirklich löschen?" : "Löschen", false, this::deleteProfile, 1, 2);
		}
		if (!status.isEmpty())
			rows.info(status);
	}

	/** Anker wechseln, ohne dass die Anzeige im Bild springt. */
	private void reanchor(HudModule module, int anchorX, int anchorY) {
		HudConfig.Entry entry = config.get(module);
		if (ctxPainter == null) {
			entry.anchorX = anchorX;
			entry.anchorY = anchorY;
			return;
		}
		Map<HudModule, String> shown = withPlaceholders(ctxTexts);
		List<HudElement> parts = new ArrayList<>();
		parts.add(HudElement.of(module));
		if (module.equipment)
			for (HudSlot slot : HudSlot.values())
				parts.add(new HudElement(module, slot.ordinal()));
		int[][] boxes = new int[parts.size()][];
		for (int i = 0; i < boxes.length; i++)
			boxes[i] = HudOverlay.box(config, parts.get(i), ctxPainter, shown, ctxWidth, ctxHeight);
		entry.anchorX = anchorX;
		entry.anchorY = anchorY;
		boolean auto = entry.autoAnchor;
		entry.autoAnchor = false;
		for (int i = 0; i < boxes.length; i++)
			HudOverlay.move(entry, parts.get(i), boxes[i][0], boxes[i][1], boxes[i][2], boxes[i][3], ctxWidth, ctxHeight);
		entry.autoAnchor = auto;
	}

	private void saveProfile() {
		String name = HudProfiles.clean(profileName);
		if (name.isEmpty())
			name = HudProfiles.freeName(config);
		status = HudProfiles.save(config, name) ? "Gespeichert." : "Speichern nicht möglich.";
		profilePick = name;
		profileName = name;
		profiles = null;
	}

	private void loadProfile() {
		status = HudProfiles.load(config, profilePick) ? "Geladen." : "Laden nicht möglich.";
		profiles = null;
	}

	private void deleteProfile() {
		if (!confirmDelete) {
			confirmDelete = true;
			return;
		}
		status = HudProfiles.delete(config, profilePick) ? "Gelöscht." : "Löschen nicht möglich.";
		profilePick = "";
		confirmDelete = false;
		profiles = null;
	}

	// ---------- Übersicht ----------

	/** Eine Kachel der Übersicht; ohne Anzeige ist es die Überschrift einer Kategorie. y ist relativ zur Liste. */
	private static final class Tile {
		HudModule module;
		int category;
		int x;
		int y;
		int width;
		int height;
	}

	/** Kacheln der Übersicht: gewählte Kategorie (oder alle), bei einer Suche alle passenden. */
	private List<Tile> tiles(Frame f) {
		List<Tile> tiles = new ArrayList<>();
		String query = search.trim().toLowerCase(Locale.ROOT);
		int columnWidth = (f.listWidth - TILE_GAP) / 2;
		int y = 0;
		for (int c = 0; c < CATEGORIES.length; c++) {
			if (query.isEmpty() && category >= 0 && category != c)
				continue;
			List<HudModule> members = new ArrayList<>();
			for (HudModule module : CATEGORIES[c])
				if (query.isEmpty() || module.label.toLowerCase(Locale.ROOT).contains(query)
						|| module.shortLabel.toLowerCase(Locale.ROOT).contains(query))
					members.add(module);
			if (members.isEmpty())
				continue;
			Tile heading = new Tile();
			heading.category = c;
			heading.x = f.mainX;
			heading.y = y;
			heading.width = f.listWidth;
			heading.height = 14;
			tiles.add(heading);
			y += heading.height;
			for (int i = 0; i < members.size(); i++) {
				Tile tile = new Tile();
				tile.module = members.get(i);
				tile.category = c;
				tile.x = f.mainX + (i % 2) * (columnWidth + TILE_GAP);
				tile.y = y + (i / 2) * (TILE_HEIGHT + TILE_GAP);
				tile.width = columnWidth;
				tile.height = TILE_HEIGHT;
				tiles.add(tile);
			}
			y += ((members.size() + 1) / 2) * (TILE_HEIGHT + TILE_GAP) + 6;
		}
		return tiles;
	}

	private static int categoryOf(HudModule module) {
		for (int i = 0; i < CATEGORIES.length; i++)
			for (HudModule member : CATEGORIES[i])
				if (member == module)
					return i;
		return 0;
	}

	private void toggle(HudModule module) {
		HudConfig.Entry entry = config.get(module);
		entry.enabled = !entry.enabled;
	}

	// ---------- Scrollen ----------

	private int scroll() {
		return scroll[view.ordinal()];
	}

	/** Höhe des ganzen Inhalts der Liste, unabhängig vom Scrollen. */
	private int contentHeight(Frame f) {
		int height = 0;
		if (view == View.OVERVIEW) {
			for (Tile tile : tiles(f))
				height = Math.max(height, tile.y + tile.height);
		} else {
			for (Widget widget : rows())
				height = Math.max(height, widget.y + widget.height);
		}
		return height;
	}

	private void clampScroll(Frame f) {
		int max = Math.max(0, contentHeight(f) - (f.bottom - f.listY));
		scroll[view.ordinal()] = Math.max(0, Math.min(max, scroll()));
	}

	/** Gezeichnet (und anklickbar) wird nur, was ganz in den sichtbaren Teil der Liste passt. */
	private static boolean visible(Frame f, int y, int height) {
		return y >= f.listY && y + height <= f.bottom;
	}

	/** Lage einer Zeile als {x, y, Breite, Höhe}; Zellen teilen sich die Breite. */
	private int[] cell(Frame f, Widget widget) {
		int step = (f.listWidth + CARD_GAP) / widget.cols;
		int x = f.mainX + widget.col * step;
		int width = widget.cols == 1 ? f.listWidth : step - CARD_GAP;
		return new int[] { x, f.listY - scroll() + widget.y, width, widget.height };
	}

	private static int dropdownWidth(int rowWidth) {
		return Math.max(50, Math.min(116, rowWidth / 2 - 10));
	}

	// ---------- Hilfsmittel ----------

	/** Einstellung ändern und gleich sichern; das Fenster bleibt offen. */
	private boolean changed(Runnable change) {
		change.run();
		config.save();
		return false;
	}

	/** Kürzt einen Text mit "..." auf die Breite. */
	private static String fit(HudPainter painter, String text, int width) {
		if (painter.textWidth(text) <= width)
			return text;
		String cut = text;
		while (cut.length() > 1 && painter.textWidth(cut + "...") > width)
			cut = cut.substring(0, cut.length() - 1);
		return cut + "...";
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

	/** Alle Maße des Fensters an einer Stelle, damit Zeichnen und Klicken nicht auseinanderlaufen. */
	private static final class Frame {
		int x;
		int y;
		int width;
		int height;
		int sideX;
		int sideY;
		int sideWidth;
		int sideHeight;
		int standardY;
		int doneY;
		int fullbrightY;
		int mainX;
		int mainWidth;
		int topY;
		/** Oberkante unter der oberen Leiste. */
		int contentY;
		/** Oberkante der scrollbaren Liste (in der Anzeige unter der Vorschau). */
		int listY;
		/** Breite der Liste ohne Scrollleiste. */
		int listWidth;
		int bottom;
	}

	private Frame frame(int screenWidth, int screenHeight) {
		Frame f = new Frame();
		f.width = Math.max(200, Math.min(MAX_WIDTH, screenWidth - 2 * MARGIN));
		f.height = Math.max(160, Math.min(MAX_HEIGHT, screenHeight - 2 * MARGIN));
		f.x = (screenWidth - f.width) / 2;
		f.y = Math.max(0, (screenHeight - f.height) / 2);
		f.sideX = f.x + 6;
		f.sideY = f.y + 6;
		f.sideWidth = SIDE_WIDTH;
		f.sideHeight = f.height - 12;
		f.doneY = f.sideY + f.sideHeight - 6 - BUTTON;
		f.standardY = f.doneY - BUTTON - 4;
		f.fullbrightY = f.standardY - ROW - 6;
		f.mainX = f.sideX + f.sideWidth + PAD;
		f.mainWidth = f.x + f.width - PAD - f.mainX;
		f.topY = f.y + PAD;
		f.contentY = f.topY + TOP_HEIGHT + 8;
		f.listY = view == View.DETAIL ? f.contentY + PREVIEW_HEIGHT + 6 : f.contentY;
		f.listWidth = f.mainWidth - SCROLLBAR - 4;
		f.bottom = f.y + f.height - PAD;
		return f;
	}

	/** Oberkante eines Eintrags der Seitenleiste; -1 = "Alle", danach die Kategorien, zuletzt "Profile". */
	/** Der Fullbright-Schalter steht über "Standard", aber nur, wenn er dort nicht in "Profile" hineinragt. */
	private static boolean fullbrightFits(Frame f) {
		return f.fullbrightY >= navY(f, NAV_PROFILES) + NAV_HEIGHT + 4;
	}

	private static int navY(Frame f, int entry) {
		int y = f.sideY + 32 + (entry + 1) * NAV_STEP;
		return entry == NAV_PROFILES ? y + 7 : y;
	}

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

	private int pickHeight() {
		int content = config.colorMode == MODE_HEX ? FIELD_HEIGHT : 3 * ROW;
		return 2 * PICKER_PAD + ROW + 4 + content + 4 + SWATCH;
	}

	/** Farbwähler unter seiner Zeile, rechtsbündig; ist unten kein Platz, darüber. */
	private Pick pick(Frame f) {
		int[] anchor = pickerAnchor(f);
		int height = pickHeight();
		if (anchor == null)
			return pickAt(f.mainX + f.mainWidth - PICKER_WIDTH, f.listY);
		int below = anchor[1] + anchor[3] + 2;
		int y = below + height <= ctxHeight ? below : Math.max(0, anchor[1] - height - 2);
		return pickAt(anchor[0] + anchor[2] - PICKER_WIDTH, y);
	}

	/** Farbwähler der Gruppe: über dem Gruppenfeld im Bearbeiten-Modus. */
	private Pick groupPick() {
		Bar bar = bar(ctxWidth, ctxHeight);
		return pickAt(bar.x + BAR_WIDTH - PICKER_WIDTH, Math.max(0, panelY(bar) - pickHeight() - 2));
	}

	private Pick pickAt(int x, int y) {
		Pick pick = new Pick();
		int content = config.colorMode == MODE_HEX ? FIELD_HEIGHT : 3 * ROW;
		pick.height = pickHeight();
		pick.x = x;
		pick.y = y;
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
}
