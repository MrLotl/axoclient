package de.axoclient.mod.ui;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.google.gson.JsonPrimitive;
import net.minecraft.client.Minecraft;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Einstellungen des Axo-Menüs, die nur das Spiel betreffen: die Einblendungen (wo, wie lange, welche) und eine Kopie
 * der Emote-Belegung für Spiele ohne Launcher. Liegt als {@code config/axoclient-menu.json} in der Instanz.
 */
public final class MenuConfig {
	/** Arten von Einblendungen und ihr Name im Menü (Reihenfolge = Anzeige). */
	public static final String[][] TOAST_KINDS = {
		{ "invite", "Einladungen" }, { "request", "Freundesanfragen" }, { "accepted", "Anfrage angenommen" },
		{ "online", "Freund ist online" }, { "onServer", "Freund auf deinem Server" }, { "openWorld", "Freund öffnet eine Welt" },
		{ "share", "Etwas geschickt bekommen" }
	};

	public static boolean toastsOn = true;
	/** Privat: nur über das Launcher-Fenster (nicht im Stream); ohne dieses Fenster im Spiel. */
	public static boolean toastsPrivate = true;
	/** Neue Einblendungen nach unten (true) oder nach oben anhängen. */
	public static boolean toastsDown = true;
	public static int toastSeconds = 6;
	/** Größe in Prozent. */
	public static int toastScale = 100;
	/** Verankerung 0–8 (oben links … unten rechts) und Abstand dazu in Design-Pixeln. */
	public static int toastAnchor = 2;
	public static int toastX = 12;
	public static int toastY = 12;
	public static final Map<String, Boolean> TOAST_KIND_ON = new LinkedHashMap<>();

	private static boolean loaded;

	static {
		for (String[] kind : TOAST_KINDS)
			TOAST_KIND_ON.put(kind[0], !"online".equals(kind[0]));
	}

	private MenuConfig() {}

	private static Path file() {
		return Minecraft.getInstance().gameDirectory.toPath().resolve("config").resolve("axoclient-menu.json");
	}

	public static void load() {
		if (loaded)
			return;
		loaded = true;
		Path file = file();
		if (!Files.exists(file))
			return;
		try {
			JsonObject json = JsonParser.parseString(Files.readString(file, StandardCharsets.UTF_8)).getAsJsonObject();
			JsonObject t = json.has("toasts") && json.get("toasts").isJsonObject() ? json.getAsJsonObject("toasts") : new JsonObject();
			toastsOn = bool(t, "on", toastsOn);
			toastsPrivate = bool(t, "privat", toastsPrivate);
			toastsDown = bool(t, "down", toastsDown);
			toastSeconds = Math.max(3, Math.min(15, integer(t, "seconds", toastSeconds)));
			toastScale = Math.max(50, Math.min(200, integer(t, "scale", toastScale)));
			toastAnchor = Math.max(0, Math.min(8, integer(t, "anchor", toastAnchor)));
			toastX = integer(t, "x", toastX);
			toastY = integer(t, "y", toastY);
			if (t.has("kinds") && t.get("kinds").isJsonObject())
				for (Map.Entry<String, JsonElement> e : t.getAsJsonObject("kinds").entrySet())
					if (TOAST_KIND_ON.containsKey(e.getKey()) && e.getValue().isJsonPrimitive())
						TOAST_KIND_ON.put(e.getKey(), e.getValue().getAsBoolean());
			if (json.has("slots") && json.get("slots").isJsonArray()) {
				JsonArray slots = json.getAsJsonArray("slots");
				for (int i = 0; i < 8 && i < slots.size(); i++)
					MenuData.SLOTS.set(i, slots.get(i).isJsonPrimitive() ? slots.get(i).getAsString() : null);
			}
		} catch (IOException | RuntimeException e) {
			System.err.println("[AxoClient] config/axoclient-menu.json nicht lesbar: " + e);
		}
	}

	public static void save() {
		JsonObject json = new JsonObject();
		JsonObject t = new JsonObject();
		t.addProperty("on", toastsOn);
		t.addProperty("privat", toastsPrivate);
		t.addProperty("down", toastsDown);
		t.addProperty("seconds", toastSeconds);
		t.addProperty("scale", toastScale);
		t.addProperty("anchor", toastAnchor);
		t.addProperty("x", toastX);
		t.addProperty("y", toastY);
		JsonObject kinds = new JsonObject();
		TOAST_KIND_ON.forEach(kinds::addProperty);
		t.add("kinds", kinds);
		json.add("toasts", t);
		JsonArray slots = new JsonArray();
		List<String> current = MenuData.SLOTS;
		for (String id : current)
			slots.add(id == null ? JsonNull.INSTANCE : new JsonPrimitive(id));
		json.add("slots", slots);
		try {
			Path file = file();
			Files.createDirectories(file.getParent());
			Path tmp = file.resolveSibling(file.getFileName() + ".tmp");
			Files.writeString(tmp, json.toString(), StandardCharsets.UTF_8);
			Files.move(tmp, file, java.nio.file.StandardCopyOption.REPLACE_EXISTING);
		} catch (IOException e) {
			System.err.println("[AxoClient] config/axoclient-menu.json nicht gespeichert: " + e);
		}
	}

	private static boolean bool(JsonObject o, String name, boolean fallback) {
		return o.has(name) && o.get(name).isJsonPrimitive() ? o.get(name).getAsBoolean() : fallback;
	}

	private static int integer(JsonObject o, String name, int fallback) {
		return o.has(name) && o.get(name).isJsonPrimitive() ? o.get(name).getAsInt() : fallback;
	}
}
