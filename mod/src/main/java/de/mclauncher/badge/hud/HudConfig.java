package de.mclauncher.badge.hud;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.EnumMap;
import java.util.Map;

/**
 * Alle Einstellungen der Anzeigen: was eingeschaltet ist, wo es steht, wie groß es ist, wie es
 * aussieht. Liegt als <code>config/axoclient-hud.json</code> im Spielordner, also je Instanz
 * eigene Einstellungen. Fehlende Einträge behalten ihren Standardwert, ältere Dateien lassen
 * sich also weiter lesen.
 */
public final class HudConfig {
	/** Kleinste, größte und Schrittweite der Größe in Prozent. */
	public static final int MIN_SCALE = 50;
	public static final int MAX_SCALE = 250;
	public static final int SCALE_STEP = 5;

	/** Maschenweite des Rasters in Bildpunkten. */
	public static final int MIN_GRID = 4;
	public static final int MAX_GRID = 32;
	public static final int GRID_STEP = 2;

	/** Ab diesem Abstand rastet eine Anzeige an einer anderen ein. */
	public static final int SNAP_DISTANCE = 5;

	/** Grenzen des Eckenradius der Hintergrundfläche. */
	public static final int MIN_CORNER = 0;
	public static final int MAX_CORNER = 10;

	/** Zustand und Aussehen einer einzelnen Anzeige. */
	public static final class Entry {
		public boolean enabled;
		public int x;
		public int y;
		/** Größe in Prozent, siehe {@link #MIN_SCALE} und {@link #MAX_SCALE}. */
		public int scale;

		/** Hintergrundfläche hinter der Anzeige. */
		public boolean background;
		/** Deckkraft des Hintergrunds in Prozent. */
		public int backgroundAlpha;
		/** Frei gewählte Hintergrundfarbe als 0xRRGGBB. */
		public int backgroundRgb;
		/** Eckenradius der Hintergrundfläche in Bildpunkten. */
		public int corner;
		/** Feiner Rahmen um die Hintergrundfläche, in der Textfarbe. */
		public boolean border;

		/** Frei gewählte Textfarbe als 0xRRGGBB. */
		public int textRgb;
		public boolean shadow;

		// ---- nur bei Anzeigen mit HudModule.equipment ----
		/** Ausrüstung untereinander statt nebeneinander. */
		public boolean vertical;
		/** Haltbarkeit in Prozent dazuschreiben. */
		public boolean percent;
		/** Jedes Ausrüstungsstück einzeln verschiebbar statt als Block. */
		public boolean split;
		/** Welche Ausrüstungsplätze überhaupt gezeigt werden. */
		public final boolean[] slots = new boolean[HudSlot.COUNT];
		/** Positionen der einzelnen Stücke, wenn {@link #split} an ist. */
		public final int[] slotX = new int[HudSlot.COUNT];
		public final int[] slotY = new int[HudSlot.COUNT];

		Entry(HudModule module) {
			this.enabled = module.defaultEnabled;
			this.x = module.defaultX;
			this.y = module.defaultY;
			this.scale = 100;
			this.background = false;
			this.backgroundAlpha = 45;
			this.backgroundRgb = 0x000000;
			this.corner = 3;
			this.border = false;
			this.textRgb = 0xFFFFFF;
			this.shadow = true;
			this.vertical = false;
			this.percent = true;
			this.split = false;
			for (HudSlot slot : HudSlot.values()) {
				this.slots[slot.ordinal()] = slot.defaultEnabled;
				// Beim ersten Zerlegen stehen die Stücke untereinander, damit sie sich nicht überdecken
				this.slotX[slot.ordinal()] = module.defaultX;
				this.slotY[slot.ordinal()] = module.defaultY + slot.ordinal() * 18;
			}
		}

		/** Die Größe als Faktor, sicher innerhalb der erlaubten Grenzen. */
		public float factor() {
			return clampScale(scale) / 100.0F;
		}

		/** Die Hintergrundfarbe samt Deckkraft, fertig zum Zeichnen. */
		public int backgroundArgb() {
			int alpha = Math.max(0, Math.min(100, backgroundAlpha)) * 255 / 100;
			return (alpha << 24) | (backgroundRgb & 0xFFFFFF);
		}

		public int textArgb() {
			return 0xFF000000 | (textRgb & 0xFFFFFF);
		}

		/** Ob der Platz gezeigt werden soll; unbekannte Nummern zählen als "aus". */
		public boolean slotEnabled(int slot) {
			return slot >= 0 && slot < slots.length && slots[slot];
		}
	}

	private final Path file;
	private final Map<HudModule, Entry> entries = new EnumMap<>(HudModule.class);

	/** Raster beim Verschieben. */
	public boolean grid;
	public int gridSize = 8;
	/** An anderen Anzeigen und am Bildrand einrasten. */
	public boolean snap = true;
	/** Zuletzt benutzte Art, Farben einzustellen: 0 = RGB, 1 = HSL, 2 = Hex. */
	public int colorMode;

	private HudConfig(Path file) {
		this.file = file;
		reset();
	}

	/** Lädt die Einstellungen; bei Fehlern (oder beim ersten Start) gelten die Standardwerte. */
	public static HudConfig load(Path gameDir) {
		HudConfig config = new HudConfig(gameDir.resolve("config").resolve("axoclient-hud.json"));
		try {
			if (!Files.exists(config.file))
				return config;
			JsonObject json = JsonParser.parseString(Files.readString(config.file, StandardCharsets.UTF_8))
				.getAsJsonObject();
			config.readGlobals(json);
			for (HudModule module : HudModule.values()) {
				if (!json.has(module.id))
					continue;
				config.readEntry(config.entries.get(module), json.getAsJsonObject(module.id));
			}
		} catch (Exception e) {
			config.reset(); // kaputte Datei: lieber von vorn als gar keine Anzeigen
		}
		return config;
	}

	private void readGlobals(JsonObject json) {
		if (json.has("raster")) {
			JsonObject raster = json.getAsJsonObject("raster");
			if (raster.has("an"))
				grid = raster.get("an").getAsBoolean();
			if (raster.has("weite"))
				gridSize = clampGrid(raster.get("weite").getAsInt());
		}
		if (json.has("einrasten"))
			snap = json.get("einrasten").getAsBoolean();
		if (json.has("farbmodus"))
			colorMode = Math.max(0, Math.min(2, json.get("farbmodus").getAsInt()));
	}

	private void readEntry(Entry entry, JsonObject saved) {
		if (saved.has("enabled"))
			entry.enabled = saved.get("enabled").getAsBoolean();
		if (saved.has("x"))
			entry.x = saved.get("x").getAsInt();
		if (saved.has("y"))
			entry.y = saved.get("y").getAsInt();
		if (saved.has("scale"))
			entry.scale = clampScale(saved.get("scale").getAsInt());
		if (saved.has("vertical"))
			entry.vertical = saved.get("vertical").getAsBoolean();
		if (saved.has("percent"))
			entry.percent = saved.get("percent").getAsBoolean();
		if (saved.has("split"))
			entry.split = saved.get("split").getAsBoolean();
		if (saved.has("background"))
			entry.background = saved.get("background").getAsBoolean();
		if (saved.has("backgroundAlpha"))
			entry.backgroundAlpha = Math.max(0, Math.min(100, saved.get("backgroundAlpha").getAsInt()));
		if (saved.has("backgroundColor")) // frühere Fassung: Nummer aus einer festen Liste
			entry.backgroundRgb = HudSkin.LEGACY_BACKGROUND_COLORS[HudSkin.wrap(saved.get("backgroundColor").getAsInt(),
				HudSkin.LEGACY_BACKGROUND_COLORS.length)] & 0xFFFFFF;
		if (saved.has("backgroundRgb"))
			entry.backgroundRgb = saved.get("backgroundRgb").getAsInt() & 0xFFFFFF;
		if (saved.has("corner"))
			entry.corner = clampCorner(saved.get("corner").getAsInt());
		if (saved.has("border"))
			entry.border = saved.get("border").getAsBoolean();
		if (saved.has("textColor")) // frühere Fassung: Nummer aus einer festen Liste
			entry.textRgb = HudSkin.LEGACY_TEXT_COLORS[HudSkin.wrap(saved.get("textColor").getAsInt(),
				HudSkin.LEGACY_TEXT_COLORS.length)] & 0xFFFFFF;
		if (saved.has("textRgb"))
			entry.textRgb = saved.get("textRgb").getAsInt() & 0xFFFFFF;
		if (saved.has("shadow"))
			entry.shadow = saved.get("shadow").getAsBoolean();
		if (!saved.has("slots"))
			return;
		JsonObject slots = saved.getAsJsonObject("slots");
		for (HudSlot slot : HudSlot.values()) {
			if (!slots.has(slot.id))
				continue;
			JsonObject one = slots.getAsJsonObject(slot.id);
			if (one.has("an"))
				entry.slots[slot.ordinal()] = one.get("an").getAsBoolean();
			if (one.has("x"))
				entry.slotX[slot.ordinal()] = one.get("x").getAsInt();
			if (one.has("y"))
				entry.slotY[slot.ordinal()] = one.get("y").getAsInt();
		}
	}

	public void save() {
		try {
			JsonObject json = new JsonObject();
			JsonObject raster = new JsonObject();
			raster.addProperty("an", grid);
			raster.addProperty("weite", gridSize);
			json.add("raster", raster);
			json.addProperty("einrasten", snap);
			json.addProperty("farbmodus", colorMode);

			for (HudModule module : HudModule.values())
				json.add(module.id, write(entries.get(module)));
			Files.createDirectories(file.getParent());
			Files.writeString(file, json.toString(), StandardCharsets.UTF_8);
		} catch (IOException e) {
			// Nicht schreibbar: die Einstellungen gelten dann nur bis zum Spielende
		}
	}

	private static JsonObject write(Entry entry) {
		JsonObject saved = new JsonObject();
		saved.addProperty("enabled", entry.enabled);
		saved.addProperty("x", entry.x);
		saved.addProperty("y", entry.y);
		saved.addProperty("scale", entry.scale);
		saved.addProperty("vertical", entry.vertical);
		saved.addProperty("percent", entry.percent);
		saved.addProperty("split", entry.split);
		saved.addProperty("background", entry.background);
		saved.addProperty("backgroundAlpha", entry.backgroundAlpha);
		saved.addProperty("backgroundRgb", entry.backgroundRgb);
		saved.addProperty("corner", entry.corner);
		saved.addProperty("border", entry.border);
		saved.addProperty("textRgb", entry.textRgb);
		saved.addProperty("shadow", entry.shadow);
		JsonObject slots = new JsonObject();
		for (HudSlot slot : HudSlot.values()) {
			JsonObject one = new JsonObject();
			one.addProperty("an", entry.slots[slot.ordinal()]);
			one.addProperty("x", entry.slotX[slot.ordinal()]);
			one.addProperty("y", entry.slotY[slot.ordinal()]);
			slots.add(slot.id, one);
		}
		saved.add("slots", slots);
		return saved;
	}

	/** Setzt alles auf die Standardwerte zurück (nicht gespeichert). */
	public void reset() {
		for (HudModule module : HudModule.values())
			entries.put(module, new Entry(module));
		grid = false;
		gridSize = 8;
		snap = true;
		colorMode = 0;
	}

	public Entry get(HudModule module) {
		return entries.get(module);
	}

	public boolean isEnabled(HudModule module) {
		return entries.get(module).enabled;
	}

	/** Übernimmt den Hintergrund einer Anzeige für alle anderen. */
	public void copyBackgroundToAll(HudModule from) {
		Entry source = entries.get(from);
		for (Entry entry : entries.values()) {
			entry.background = source.background;
			entry.backgroundAlpha = source.backgroundAlpha;
			entry.backgroundRgb = source.backgroundRgb;
			entry.corner = source.corner;
			entry.border = source.border;
		}
	}

	public static int clampScale(int scale) {
		return Math.max(MIN_SCALE, Math.min(MAX_SCALE, scale));
	}

	public static int clampGrid(int size) {
		return Math.max(MIN_GRID, Math.min(MAX_GRID, size));
	}

	public static int clampCorner(int corner) {
		return Math.max(MIN_CORNER, Math.min(MAX_CORNER, corner));
	}
}
