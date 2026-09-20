package de.mclauncher.badge.hud;

import com.google.gson.JsonArray;
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

	/** Höchstens so viele Farbregeln je Anzeige. */
	public static final int MAX_RULES = 4;
	public static final int MAX_DECIMALS = 3;
	/** Trenner zwischen den Koordinaten nebeneinander. */
	public static final String[] SEPARATORS = { " / ", " | ", ", ", "  " };
	public static final String[] SEPARATOR_NAMES = { "/", "|", ",", "Leerzeichen" };
	public static final String[] ALIGN_NAMES = { "Links", "Mitte", "Rechts" };

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

		// ---- Anker: die Position gilt ab Bildrand (0), Bildmitte (1) oder gegenüberliegendem Rand (2) ----
		public int anchorX;
		public int anchorY;
		/** Beim Verschieben den nächstgelegenen Anker wählen. */
		public boolean autoAnchor;

		// ---- Text und Format ----
		/** Kurzen Namen vor den Wert schreiben ("Ping: 45 ms"). */
		public boolean labels;
		/** Einheit hinter den Wert schreiben. */
		public boolean units;
		/** Nachkommastellen bei Koordinaten, Winkel, Tempo, Speicher. */
		public int decimals;
		/** Nummer der Darstellung, siehe HudModule.variants. */
		public int variant;
		/** Koordinaten untereinander statt in einer Zeile. */
		public boolean stacked;
		/** Trenner bei Koordinaten in einer Zeile, siehe SEPARATORS. */
		public int separator;
		/** Welche Achsen die Koordinaten zeigen (X, Y, Z). */
		public final boolean[] axes = { true, true, true };
		/** Himmelsrichtung hinter den Koordinaten. */
		public boolean facing;
		/** Ausrichtung mehrzeiliger Texte: 0 links, 1 Mitte, 2 rechts. */
		public int align;

		// ---- Farbe nach Wert und Sichtbarkeit ----
		public boolean rulesOn;
		public int ruleCount;
		/** Ab diesem Wert gilt die Farbe daneben. */
		public final int[] ruleAt = new int[MAX_RULES];
		public final int[] ruleRgb = new int[MAX_RULES];
		/** 0 immer zeigen, 1 nur unter der Grenze, 2 nur ab der Grenze. */
		public int showMode;
		public int showLimit;
		/** Farbe einer gedrückten Taste (Tastenanzeige) als 0xRRGGBB. */
		public int pressRgb = 0xFFFFFF;

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
			this.labels = module == HudModule.LIGHT;
			this.units = true;
			this.decimals = module == HudModule.COORDS ? 0 : 1;
			this.variant = 0;
			this.showLimit = module.metric == null ? 0 : module.metric.max() / 2;
			seedRules(module);
			if (module == HudModule.EFFECTS) {
				// Wie in Minecraft: oben rechts
				this.anchorX = 2;
				this.x = 1;
				this.y = 1;
			}
			if (module == HudModule.SCOREBOARD) {
				// Wie in Minecraft: am rechten Rand, in der Mitte
				this.anchorX = 2;
				this.anchorY = 1;
				this.x = 3;
				this.y = 0;
				// Und mit dem dunklen Hintergrund, den Minecraft sonst selbst zeichnet
				this.background = true;
				this.backgroundAlpha = 30;
			}
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

		/** Setzt die Standardregeln der Anzeige (Ping grün bis rot usw.). */
		public void seedRules(HudModule module) {
			if (module.metric == null)
				return;
			ruleCount = Math.min(MAX_RULES, module.metric.defaultAt().length);
			for (int i = 0; i < ruleCount; i++) {
				ruleAt[i] = module.metric.defaultAt()[i];
				ruleRgb[i] = module.metric.defaultRgb()[i];
			}
		}

		/** Textfarbe für einen gemessenen Wert; ohne Regeln die gewählte Farbe. */
		public int colorFor(Double value, int fallbackArgb) {
			if (!rulesOn || value == null || ruleCount == 0)
				return fallbackArgb;
			int best = -1;
			for (int i = 0; i < ruleCount; i++)
				if (value >= ruleAt[i] && (best < 0 || ruleAt[i] >= ruleAt[best]))
					best = i;
			return best < 0 ? fallbackArgb : 0xFF000000 | (ruleRgb[best] & 0xFFFFFF);
		}

		/** Ob die Anzeige bei diesem Wert im Bild steht ("nur bei Bedarf"). */
		public boolean visibleFor(Double value) {
			if (showMode == 0 || value == null)
				return true;
			return showMode == 1 ? value < showLimit : value >= showLimit;
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

	/** Name des zuletzt geladenen oder gespeicherten Profils (nur zur Anzeige), sonst leer. */
	public String activeProfile = "";
	/** Gruppen: mehrere Anzeigen mit einem gemeinsamen Hintergrund, die zusammen wandern. */
	public final java.util.List<Group> groups = new java.util.ArrayList<>();

	/** Eine Gruppe von Anzeigen samt gemeinsamer Hintergrundfläche. */
	public static final class Group {
		public final java.util.List<HudModule> members = new java.util.ArrayList<>();
		public int alpha = 45;
		public int rgb = 0x000000;
		public int corner = 4;
		public boolean border;

		public int argb() {
			return (Math.max(0, Math.min(100, alpha)) * 255 / 100) << 24 | (rgb & 0xFFFFFF);
		}
	}

	/** Die Gruppe, zu der eine Anzeige gehört, sonst null. */
	public Group groupOf(HudModule module) {
		for (Group group : groups)
			if (group.members.contains(module))
				return group;
		return null;
	}

	/** Fasst Anzeigen zu einer Gruppe zusammen; der Hintergrund der ersten mit Hintergrund wird übernommen. */
	public Group makeGroup(java.util.Collection<HudModule> modules) {
		Group group = new Group();
		for (HudModule module : HudModule.values()) {
			if (!modules.contains(module))
				continue;
			Group old = groupOf(module);
			if (old != null)
				old.members.remove(module);
			group.members.add(module);
		}
		groups.removeIf(g -> g.members.size() < 2);
		for (HudModule module : group.members) {
			Entry entry = entries.get(module);
			if (entry.background && group.rgb == 0 && group.alpha == 45) {
				group.alpha = entry.backgroundAlpha;
				group.rgb = entry.backgroundRgb;
				group.corner = entry.corner;
				group.border = entry.border;
			}
			entry.background = false;
		}
		groups.add(group);
		return group;
	}

	/** Server, auf denen dieses Profil automatisch gilt (Adresse oder Domain). */
	public final java.util.List<String> servers = new java.util.ArrayList<>();

	private HudConfig(Path file) {
		this.file = file;
		reset();
		HudText.bind(this);
	}

	/** Ordner mit den gespeicherten Profilen. */
	public Path profilesDir() {
		return file.getParent().resolve("axoclient-hud-profiles");
	}

	/** Lädt die Einstellungen; bei Fehlern (oder beim ersten Start) gelten die Standardwerte. */
	public static HudConfig load(Path gameDir) {
		HudConfig config = new HudConfig(gameDir.resolve("config").resolve("axoclient-hud.json"));
		try {
			if (!Files.exists(config.file)) {
				HudProfiles.ensureDefault(config);
				return config;
			}
			config.apply(JsonParser.parseString(Files.readString(config.file, StandardCharsets.UTF_8))
				.getAsJsonObject());
		} catch (Exception e) {
			config.reset(); // kaputte Datei: lieber von vorn als gar keine Anzeigen
		}
		HudProfiles.ensureDefault(config);
		return config;
	}

	/**
	 * Übernimmt Einstellungen aus JSON (Datei oder Profil) über die aktuellen. Fehlendes bleibt wie es
	 * war; wer alles ersetzen will, ruft vorher {@link #reset()}.
	 */
	public void apply(JsonObject json) {
		readGlobals(json);
		for (HudModule module : HudModule.values()) {
			if (json.has(module.id) && json.get(module.id).isJsonObject())
				readEntry(entries.get(module), json.getAsJsonObject(module.id));
		}
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
		if (json.has("profil"))
			activeProfile = json.get("profil").getAsString();
		if (json.has("gruppen") && json.get("gruppen").isJsonArray()) {
			groups.clear();
			for (var element : json.getAsJsonArray("gruppen")) {
				JsonObject saved = element.getAsJsonObject();
				Group group = new Group();
				if (saved.has("mitglieder"))
					for (var id : saved.getAsJsonArray("mitglieder"))
						for (HudModule module : HudModule.values())
							if (module.id.equals(id.getAsString()) && groupOf(module) == null
								&& !group.members.contains(module))
								group.members.add(module);
				if (saved.has("alpha"))
					group.alpha = Math.max(0, Math.min(100, saved.get("alpha").getAsInt()));
				if (saved.has("rgb"))
					group.rgb = saved.get("rgb").getAsInt() & 0xFFFFFF;
				if (saved.has("corner"))
					group.corner = clampCorner(saved.get("corner").getAsInt());
				if (saved.has("border"))
					group.border = saved.get("border").getAsBoolean();
				if (group.members.size() >= 2)
					groups.add(group);
			}
		}
		if (json.has("server") && json.get("server").isJsonArray()) {
			servers.clear();
			for (var element : json.getAsJsonArray("server"))
				if (servers.size() < 8 && !element.getAsString().isBlank())
					servers.add(element.getAsString().trim());
		}
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
		if (saved.has("ankerX"))
			entry.anchorX = HudSkin.wrap(saved.get("ankerX").getAsInt(), 3);
		if (saved.has("ankerY"))
			entry.anchorY = HudSkin.wrap(saved.get("ankerY").getAsInt(), 3);
		if (saved.has("autoAnker"))
			entry.autoAnchor = saved.get("autoAnker").getAsBoolean();
		if (saved.has("labels"))
			entry.labels = saved.get("labels").getAsBoolean();
		if (saved.has("units"))
			entry.units = saved.get("units").getAsBoolean();
		if (saved.has("decimals"))
			entry.decimals = Math.max(0, Math.min(MAX_DECIMALS, saved.get("decimals").getAsInt()));
		if (saved.has("variant"))
			entry.variant = Math.max(0, saved.get("variant").getAsInt());
		if (saved.has("stacked"))
			entry.stacked = saved.get("stacked").getAsBoolean();
		if (saved.has("separator"))
			entry.separator = HudSkin.wrap(saved.get("separator").getAsInt(), SEPARATORS.length);
		if (saved.has("facing"))
			entry.facing = saved.get("facing").getAsBoolean();
		if (saved.has("align"))
			entry.align = HudSkin.wrap(saved.get("align").getAsInt(), ALIGN_NAMES.length);
		if (saved.has("axes") && saved.get("axes").isJsonArray()) {
			JsonArray axes = saved.getAsJsonArray("axes");
			for (int i = 0; i < entry.axes.length && i < axes.size(); i++)
				entry.axes[i] = axes.get(i).getAsBoolean();
		}
		if (saved.has("farbregeln"))
			entry.rulesOn = saved.get("farbregeln").getAsBoolean();
		if (saved.has("regeln") && saved.get("regeln").isJsonArray()) {
			JsonArray rules = saved.getAsJsonArray("regeln");
			entry.ruleCount = Math.min(MAX_RULES, rules.size());
			for (int i = 0; i < entry.ruleCount; i++) {
				JsonObject rule = rules.get(i).getAsJsonObject();
				entry.ruleAt[i] = rule.get("ab").getAsInt();
				entry.ruleRgb[i] = rule.get("farbe").getAsInt() & 0xFFFFFF;
			}
		}
		if (saved.has("zeigen"))
			entry.showMode = Math.max(0, Math.min(2, saved.get("zeigen").getAsInt()));
		if (saved.has("tastenfarbe"))
			entry.pressRgb = saved.get("tastenfarbe").getAsInt() & 0xFFFFFF;
		if (saved.has("grenze"))
			entry.showLimit = saved.get("grenze").getAsInt();
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

	/** Alle Einstellungen als JSON – für die Datei und für Profile. */
	public JsonObject toJson() {
			JsonObject json = new JsonObject();
			JsonObject raster = new JsonObject();
			raster.addProperty("an", grid);
			raster.addProperty("weite", gridSize);
			json.add("raster", raster);
			json.addProperty("einrasten", snap);
			json.addProperty("farbmodus", colorMode);
			json.addProperty("profil", activeProfile);
			JsonArray serverList = new JsonArray();
			servers.forEach(serverList::add);
			json.add("server", serverList);
			JsonArray groupList = new JsonArray();
			for (Group group : groups) {
				JsonObject saved = new JsonObject();
				JsonArray ids = new JsonArray();
				group.members.forEach(m -> ids.add(m.id));
				saved.add("mitglieder", ids);
				saved.addProperty("alpha", group.alpha);
				saved.addProperty("rgb", group.rgb);
				saved.addProperty("corner", group.corner);
				saved.addProperty("border", group.border);
				groupList.add(saved);
			}
			json.add("gruppen", groupList);

			for (HudModule module : HudModule.values())
				json.add(module.id, write(entries.get(module)));
			return json;
	}

	public void save() {
		try {
			JsonObject json = toJson();
			Files.createDirectories(file.getParent());
			Files.writeString(file, json.toString(), StandardCharsets.UTF_8);
			HudProfiles.writeActive(this, json);
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
		saved.addProperty("ankerX", entry.anchorX);
		saved.addProperty("ankerY", entry.anchorY);
		saved.addProperty("autoAnker", entry.autoAnchor);
		saved.addProperty("labels", entry.labels);
		saved.addProperty("units", entry.units);
		saved.addProperty("decimals", entry.decimals);
		saved.addProperty("variant", entry.variant);
		saved.addProperty("stacked", entry.stacked);
		saved.addProperty("separator", entry.separator);
		saved.addProperty("facing", entry.facing);
		saved.addProperty("align", entry.align);
		JsonArray axes = new JsonArray();
		for (boolean axis : entry.axes)
			axes.add(axis);
		saved.add("axes", axes);
		saved.addProperty("farbregeln", entry.rulesOn);
		JsonArray rules = new JsonArray();
		for (int i = 0; i < entry.ruleCount; i++) {
			JsonObject rule = new JsonObject();
			rule.addProperty("ab", entry.ruleAt[i]);
			rule.addProperty("farbe", entry.ruleRgb[i]);
			rules.add(rule);
		}
		saved.add("regeln", rules);
		saved.addProperty("zeigen", entry.showMode);
		saved.addProperty("grenze", entry.showLimit);
		saved.addProperty("tastenfarbe", entry.pressRgb);
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
		groups.clear();
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
