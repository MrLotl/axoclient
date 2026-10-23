package de.axoclient.mod.ui;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;
import com.google.gson.JsonPrimitive;
import de.axoclient.mod.AxoLink;
import de.axoclient.mod.hud.AxoPlatform;
import net.minecraft.client.Minecraft;
import net.minecraft.resources.Identifier;

import java.util.ArrayList;
import java.util.Base64;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.function.Consumer;

/**
 * Was der Launcher dem Axo-Menü schickt (Freunde, Einladungen, Capes, Emotes, Pläne zum Nachjoinen) und die Befehle
 * zurück. Alle Felder werden nur auf dem Render-Thread geändert und gelesen.
 */
public final class MenuData {
	public record Friend(String uuid, String name, String relation, String state, long since, String join, String joinKind) {
		public boolean incoming() {
			return "incoming".equals(relation);
		}

		public boolean pending() {
			return "pending".equals(relation);
		}

		public boolean ingame() {
			return "ingame".equals(state);
		}

		public boolean online() {
			return !"offline".equals(state);
		}
	}

	public record Invite(String id, String uuid, String name, String address, long created) {}

	/** Ein Spieler auf der eigenen Blockliste. */
	public record Blocked(String uuid, String name) {}

	public record Here(String address, String kind, String name, byte[] icon) {
		public boolean world() {
			return "world".equals(kind);
		}

		/** Name für Texte: Server-Name, sonst Adresse bzw. "deine Welt". */
		public String label() {
			if (world())
				return "deine Welt";
			return name != null && !name.isBlank() ? name : address;
		}
	}

	public record Cape(String key, String name, String kind) {}

	/** Eigenes Emote; {@code moving} = man darf dabei laufen (nur Schleichen beendet es). */
	public record CustomEmote(String id, String name, boolean moving) {}

	public static boolean available;
	public static String meUuid;
	public static String meName;
	public static Here here;
	public static final List<Friend> FRIENDS = new ArrayList<>();
	public static final List<Invite> INVITES = new ArrayList<>();
	public static final Set<String> INVITED = new HashSet<>();
	public static final List<Blocked> BLOCKED = new ArrayList<>();
	/** Ob der Launcher schon einmal Freunde geschickt hat (sonst "lädt …"). */
	public static boolean socialLoaded;

	private static final Map<String, byte[]> SKINS = new HashMap<>();

	public static String capeWorn = "none";
	public static final List<Cape> CAPES = new ArrayList<>();
	private static final Map<String, byte[]> CAPE_IMAGES = new HashMap<>();
	public static boolean capesLoaded;

	public static final List<String> SLOTS = new ArrayList<>(java.util.Arrays.asList("wave", "cheer", "dance", "clap", "sit", "point", "bow", null));
	public static final List<CustomEmote> CUSTOM = new ArrayList<>();

	/** Plan zum Nachjoinen je Schlüssel (Einladungs-ID bzw. UUID) und Stand ("busy", "done", "error", "launcher"). */
	public static final Map<String, JsonObject> PLANS = new HashMap<>();
	public static final Map<String, String> JOIN_PHASE = new HashMap<>();
	public static final Map<String, String> JOIN_ERROR = new HashMap<>();

	/** Letzte Fehlermeldung einer Aktion (Code vom Launcher) und wann. */
	public static String lastError;
	public static long lastErrorAt;

	/** Wird nach jeder Änderung aufgerufen (Render-Thread). */
	public static Runnable changed = () -> {};
	/** Neue Einblendung (Render-Thread). */
	public static Consumer<JsonObject> toast = t -> {};
	/** Animation eines eigenen Emotes angekommen: (ID, JSON, Name der Animation darin). */
	public static AnimReceiver animReceived = (id, json, animation) -> {};

	public interface AnimReceiver {
		void accept(String id, String json, String animation);
	}
	/** Mojang-Cape gewechselt: Bild oder null (eigene Anzeige sofort). */
	public static Consumer<byte[]> mojangCapeShown = png -> {};

	private MenuData() {}

	public static void init() {
		on("social", MenuData::social);
		on("skin", d -> {
			String uuid = str(d, "uuid");
			byte[] png = base64(str(d, "png"));
			if (uuid != null && png != null)
				SKINS.put(uuid, png);
		});
		on("capes", MenuData::capes);
		on("capeImage", d -> {
			String key = str(d, "key");
			byte[] png = base64(str(d, "png"));
			if (key != null && png != null) {
				CAPE_IMAGES.put(key, png);
				if (key.equals(capeWorn) && key.startsWith("mojang:"))
					mojangCapeShown.accept(png);
			}
		});
		on("emotes", MenuData::emotes);
		on("emoteAnim", d -> animReceived.accept(str(d, "id"), str(d, "anim"), str(d, "animation")));
		on("joinPlan", d -> {
			String key = str(d, "key");
			if (key != null) {
				PLANS.put(key, d);
				JOIN_PHASE.remove(key);
				JOIN_ERROR.remove(key);
			}
		});
		on("joinState", d -> {
			String key = str(d, "key") != null ? str(d, "key") : str(d, "uuid");
			if (key == null)
				return;
			String phase = str(d, "phase");
			JOIN_PHASE.put(key, phase);
			if ("error".equals(phase))
				JOIN_ERROR.put(key, str(d, "code"));
		});
		on("inviteState", d -> {
			String uuid = str(d, "uuid");
			if (uuid == null)
				return;
			if (d.has("invited") && d.get("invited").getAsBoolean())
				INVITED.add(uuid);
			else
				INVITED.remove(uuid);
		});
		on("toast", d -> toast.accept(d));
		on("error", d -> {
			lastError = str(d, "code");
			lastErrorAt = System.currentTimeMillis();
		});
		on("connect", d -> Joining.connect(str(d, "address"), str(d, "name")));
		on("quit", d -> Minecraft.getInstance().stop());
	}

	/** Empfänger auf dem Render-Thread; danach {@link #changed}. */
	private static void on(String type, Consumer<JsonObject> handler) {
		AxoLink.on(type, data -> {
			if (data == null || !data.isJsonObject())
				return;
			JsonObject object = data.getAsJsonObject();
			Minecraft.getInstance().execute(() -> {
				try {
					handler.accept(object);
				} catch (RuntimeException e) {
					System.err.println("[AxoClient] Menü-Daten (" + type + ") nicht lesbar: " + e);
				}
				changed.run();
			});
		});
	}

	private static void social(JsonObject d) {
		socialLoaded = true;
		available = d.has("available") && d.get("available").getAsBoolean();
		JsonObject me = obj(d, "me");
		meUuid = me == null ? null : str(me, "uuid");
		meName = me == null ? null : str(me, "name");
		JsonObject h = obj(d, "here");
		here = h == null || str(h, "address") == null ? null
			: new Here(str(h, "address"), str(h, "kind"), str(h, "name"), base64(str(h, "icon")));
		FRIENDS.clear();
		for (JsonElement e : arr(d, "friends")) {
			if (!e.isJsonObject())
				continue;
			JsonObject f = e.getAsJsonObject();
			if (str(f, "uuid") == null || str(f, "name") == null)
				continue;
			FRIENDS.add(new Friend(str(f, "uuid"), str(f, "name"), str(f, "relation"), str(f, "state"),
				f.has("since") && f.get("since").isJsonPrimitive() ? f.get("since").getAsLong() : 0, str(f, "join"), str(f, "joinKind")));
		}
		INVITES.clear();
		for (JsonElement e : arr(d, "invites")) {
			if (!e.isJsonObject())
				continue;
			JsonObject i = e.getAsJsonObject();
			if (str(i, "id") != null)
				INVITES.add(new Invite(str(i, "id"), str(i, "uuid"), str(i, "name"), str(i, "address"),
					i.has("created") ? i.get("created").getAsLong() : 0));
		}
		BLOCKED.clear();
		for (JsonElement e : arr(d, "blocked")) {
			if (!e.isJsonObject())
				continue;
			JsonObject b = e.getAsJsonObject();
			if (str(b, "uuid") != null)
				BLOCKED.add(new Blocked(str(b, "uuid"), str(b, "name") == null ? "?" : str(b, "name")));
		}
		INVITED.clear();
		for (JsonElement e : arr(d, "invited"))
			if (e.isJsonPrimitive())
				INVITED.add(e.getAsString());
	}

	private static void capes(JsonObject d) {
		capesLoaded = true;
		String before = capeWorn;
		capeWorn = str(d, "worn") == null ? "none" : str(d, "worn");
		CAPES.clear();
		for (JsonElement e : arr(d, "list"))
			if (e.isJsonObject() && str(e.getAsJsonObject(), "key") != null)
				CAPES.add(new Cape(str(e.getAsJsonObject(), "key"), str(e.getAsJsonObject(), "name"), str(e.getAsJsonObject(), "kind")));
		if (!capeWorn.equals(before)) {
			if (capeWorn.startsWith("mojang:"))
				mojangCapeShown.accept(CAPE_IMAGES.get(capeWorn));
			else if (before.startsWith("mojang:"))
				mojangCapeShown.accept(null);
		}
	}

	private static void emotes(JsonObject d) {
		JsonArray slots = d.has("slots") && d.get("slots").isJsonArray() ? d.getAsJsonArray("slots") : new JsonArray();
		for (int i = 0; i < 8; i++)
			SLOTS.set(i, i < slots.size() && slots.get(i).isJsonPrimitive() ? slots.get(i).getAsString() : null);
		CUSTOM.clear();
		for (JsonElement e : arr(d, "list")) {
			if (!e.isJsonObject())
				continue;
			JsonObject c = e.getAsJsonObject();
			if (str(c, "id") != null)
				CUSTOM.add(new CustomEmote(str(c, "id"), str(c, "name"), c.has("moving") && c.get("moving").isJsonPrimitive() && c.get("moving").getAsBoolean()));
		}
	}

	// ---------- Befehle an den Launcher ----------

	public static boolean connected() {
		return AxoLink.connected();
	}

	public static boolean can(String feature) {
		return AxoLink.supports(feature);
	}

	public static void send(String type, String key, String value) {
		JsonObject data = new JsonObject();
		if (key != null)
			data.add(key, value == null ? JsonNull.INSTANCE : new JsonPrimitive(value));
		AxoLink.send(type, data);
	}

	public static void sendSlots() {
		JsonObject data = new JsonObject();
		JsonArray slots = new JsonArray();
		for (String id : SLOTS)
			slots.add(id == null ? JsonNull.INSTANCE : new JsonPrimitive(id));
		data.add("slots", slots);
		AxoLink.send("emoteSlots", data);
	}

	public static void joinCheck(String uuid, String inviteId) {
		JsonObject data = new JsonObject();
		if (uuid != null)
			data.addProperty("uuid", uuid);
		if (inviteId != null)
			data.addProperty("inviteId", inviteId);
		String key = inviteId != null ? inviteId : uuid;
		PLANS.remove(key);
		JOIN_PHASE.remove(key);
		JOIN_ERROR.remove(key);
		if (!AxoLink.send("joinCheck", data))
			JOIN_ERROR.put(key, "ingame.offline");
	}

	public static void join(String uuid, String inviteId) {
		JsonObject data = new JsonObject();
		if (uuid != null)
			data.addProperty("uuid", uuid);
		if (inviteId != null)
			data.addProperty("inviteId", inviteId);
		JOIN_PHASE.put(inviteId != null ? inviteId : uuid, "busy");
		AxoLink.send("join", data);
	}

	// ---------- Bilder ----------

	/** Skin-Textur eines Spielers (oder null, solange keiner da ist). */
	public static Identifier skin(String uuid) {
		byte[] png = uuid == null ? null : SKINS.get(uuid);
		AxoPlatform platform = AxoPlatform.current();
		if (png == null || platform == null)
			return null;
		return platform.texture("skin/" + uuid, png) instanceof Identifier id ? id : null;
	}

	/** Skin-Bild (PNG) eines Spielers, oder null. */
	public static byte[] skinPng(String uuid) {
		return uuid == null ? null : SKINS.get(uuid);
	}

	public static boolean hasSkin(String uuid) {
		return uuid != null && SKINS.containsKey(uuid);
	}

	public static Identifier capeImage(String key) {
		byte[] png = key == null ? null : CAPE_IMAGES.get(key);
		AxoPlatform platform = AxoPlatform.current();
		if (png == null || platform == null)
			return null;
		return platform.texture("cape/" + key.replace(':', '_'), png) instanceof Identifier id ? id : null;
	}

	/** Größe eines Cape-Bildes {Breite, Höhe} (64 × 32 oder ein Vielfaches) oder null. */
	public static int[] capeSize(String key) {
		byte[] png = key == null ? null : CAPE_IMAGES.get(key);
		if (png == null || png.length < 24)
			return null;
		int w = ((png[16] & 0xFF) << 24) | ((png[17] & 0xFF) << 16) | ((png[18] & 0xFF) << 8) | (png[19] & 0xFF);
		int h = ((png[20] & 0xFF) << 24) | ((png[21] & 0xFF) << 16) | ((png[22] & 0xFF) << 8) | (png[23] & 0xFF);
		return w > 0 && h > 0 ? new int[] { w, h } : null;
	}

	public static Identifier hereIcon() {
		Here h = here;
		AxoPlatform platform = AxoPlatform.current();
		if (h == null || h.icon() == null || platform == null)
			return null;
		return platform.texture("server/" + h.address().toLowerCase(Locale.ROOT), h.icon()) instanceof Identifier id ? id : null;
	}

	// ---------- Texte ----------

	/** "Im Spiel · seit 1 Std. · skyfall.gg:25566" wie im Launcher. */
	public static String status(Friend f) {
		if (f.pending())
			return "Anfrage gesendet";
		if (!f.online())
			return "Offline";
		StringBuilder text = new StringBuilder(f.ingame() ? "Im Spiel" : "Im Launcher");
		if (f.since() > 0)
			text.append(" · seit ").append(duration(System.currentTimeMillis() - f.since()));
		if (f.join() != null)
			text.append(" · ").append("world".equals(f.joinKind()) ? "Offene Welt" : f.join());
		return text.toString();
	}

	public static String duration(long millis) {
		long minutes = Math.max(1, millis / 60_000);
		if (minutes < 60)
			return minutes + " Min.";
		long hours = minutes / 60;
		if (hours < 24)
			return hours + " Std.";
		long days = hours / 24;
		return days == 1 ? "1 Tag" : days + " Tagen";
	}

	/** Fehler-Codes des Launchers als kurzer Satz. */
	public static String errorText(String code) {
		if (code == null)
			return "Das hat nicht geklappt.";
		return switch (code) {
			case "ingame.offline" -> "Keine Verbindung zum Launcher.";
			case "ingame.notOnServer" -> "Du bist gerade auf keinem Server und in keiner offenen Welt.";
			case "ingame.noJoin" -> "Nachjoinen ist gerade nicht möglich.";
			case "ingame.friendGone" -> "Diesen Freund gibt es nicht mehr.";
			case "ingame.inviteGone" -> "Die Einladung ist abgelaufen.";
			case "errors.offline", "online.unavailable" -> "Der AxoClient-Dienst ist gerade nicht erreichbar.";
			case "rate_limited", "errors.rate_limited" -> "Zu viele Anfragen – versuch es gleich noch einmal.";
			case "not_mutual", "errors.not_mutual" -> "Ihr seid nicht gegenseitig befreundet.";
			case "launch.alreadyStarting" -> "Die Instanz startet bereits.";
			default -> "Das hat nicht geklappt.";
		};
	}

	// ---------- JSON ----------

	static String str(JsonObject o, String name) {
		return o != null && o.has(name) && o.get(name).isJsonPrimitive() ? o.get(name).getAsString() : null;
	}

	static JsonObject obj(JsonObject o, String name) {
		return o != null && o.has(name) && o.get(name).isJsonObject() ? o.getAsJsonObject(name) : null;
	}

	static JsonArray arr(JsonObject o, String name) {
		return o != null && o.has(name) && o.get(name).isJsonArray() ? o.getAsJsonArray(name) : new JsonArray();
	}

	private static byte[] base64(String text) {
		if (text == null || text.isEmpty())
			return null;
		try {
			return Base64.getDecoder().decode(text);
		} catch (IllegalArgumentException e) {
			return null;
		}
	}
}
