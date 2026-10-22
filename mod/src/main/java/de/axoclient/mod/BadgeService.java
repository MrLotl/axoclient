package de.axoclient.mod;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.Iterator;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;

/**
 * Fragt beim AxoClient-Dienst ({@code POST <api>/players}) nach, welche Spieler AxoClient nutzen und welches Axo-Cape sie
 * tragen. Die Tabliste ruft {@link #hasBadge} bei jedem Bild auf, daher wird nie direkt dort gewartet: unbekannte
 * Spieler werden gesammelt und alle paar Sekunden gebündelt im Hintergrund abgefragt.
 *
 * <p>Der Launcher kann über {@link AxoLink} zusätzlich sofort melden, wenn sich das eigene Cape ändert
 * (Typ "cape": {@code {"uuid": "...", "cape": "..."|null}}).
 */
public final class BadgeService {
	/** So schnell erscheint ein geänderter Umhang bei anderen Spielern (der Dienst wird nur gebündelt gefragt). */
	private static final long CACHE_MILLIS = TimeUnit.SECONDS.toMillis(15);
	/** Die Mod selbst legt hier ab, wenn der Spieler seinen Umhang im Spiel ändert – dann gilt das sofort. */
	private static final String LOCAL_CAPE_FILE = "axoclient-cape.txt";
	private static final long ERROR_RETRY_MILLIS = TimeUnit.MINUTES.toMillis(1);
	private static final int MAX_BATCH = 90; // Cloudflare D1 erlaubt höchstens 100 Parameter pro Abfrage

	/** Ergebnis je Spieler: hat Symbol?, AxoClient-Umhang (oder null) + gültig bis. */
	private record Entry(boolean badge, String cape, long validUntil) {}

	private static final Map<UUID, Entry> CACHE = new ConcurrentHashMap<>();
	private static final Set<UUID> QUEUE = ConcurrentHashMap.newKeySet();

	private static HttpClient http;
	private static URI playersUri;
	private static String apiBase;

	private BadgeService() {}

	/**
	 * Geprüfte Adresse des AxoClient-Dienstes ohne abschließenden Schrägstrich, oder null. Nur https, für die
	 * Entwicklung auch http auf 127.0.0.1.
	 */
	public static String apiBase() {
		return apiBase;
	}

	static String validApi(String api) {
		if (api == null || api.isBlank())
			return null;
		try {
			URI uri = URI.create(api.trim().replaceAll("/+$", ""));
			String scheme = uri.getScheme();
			boolean local = "http".equals(scheme) && ("127.0.0.1".equals(uri.getHost()) || "localhost".equals(uri.getHost()));
			if (!"https".equals(scheme) && !local || uri.getHost() == null || uri.getUserInfo() != null
				|| uri.getQuery() != null || uri.getFragment() != null)
				return null;
			return uri.toString();
		} catch (IllegalArgumentException e) {
			return null;
		}
	}

	static void start(String api) {
		AxoLink.on("cape", BadgeService::onLauncherCape);
		apiBase = validApi(api);
		if (apiBase == null)
			return; // ohne Dienst-Adresse bleibt die Tabliste einfach leer
		playersUri = URI.create(apiBase + "/players");
		http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).build();

		ScheduledExecutorService executor = Executors.newSingleThreadScheduledExecutor(runnable -> {
			Thread thread = new Thread(runnable, "AxoClient Badge");
			thread.setDaemon(true);
			return thread;
		});
		executor.scheduleWithFixedDelay(BadgeService::flush, 1, 2, TimeUnit.SECONDS);
		executor.scheduleWithFixedDelay(BadgeService::readLocalCape, 1, 1, TimeUnit.SECONDS);
	}

	/** Soll neben diesem Spieler das Symbol erscheinen? Blockiert nie. */
	public static boolean hasBadge(UUID player) {
		Entry entry = lookup(player);
		return entry != null && entry.badge();
	}

	/** Gewählter AxoClient-Umhang des Spielers (ID oder Adresse) oder null. Blockiert nie. */
	public static String capeOf(UUID player) {
		Entry entry = lookup(player);
		return entry != null ? entry.cape() : null;
	}

	private static Entry lookup(UUID player) {
		if (player == null)
			return null;
		Entry entry = CACHE.get(player);
		if (playersUri != null && (entry == null || entry.validUntil() < System.currentTimeMillis()))
			QUEUE.add(player); // (erneut) abfragen; bis dahin gilt das bisherige Ergebnis
		return entry;
	}

	private static void flush() {
		if (QUEUE.isEmpty())
			return;
		List<UUID> batch = new ArrayList<>();
		for (Iterator<UUID> it = QUEUE.iterator(); it.hasNext() && batch.size() < MAX_BATCH; ) {
			batch.add(it.next());
			it.remove();
		}

		JsonArray uuids = new JsonArray();
		batch.forEach(id -> uuids.add(compact(id)));
		JsonObject body = new JsonObject();
		body.add("uuids", uuids);

		long now = System.currentTimeMillis();
		try {
			HttpRequest request = HttpRequest.newBuilder(playersUri)
				.timeout(Duration.ofSeconds(10))
				.header("Content-Type", "application/json")
				.POST(HttpRequest.BodyPublishers.ofString(body.toString()))
				.build();
			HttpResponse<String> response = http.send(request, HttpResponse.BodyHandlers.ofString());
			if (response.statusCode() != 200)
				throw new IllegalStateException("HTTP " + response.statusCode());

			Map<String, String> players = parsePlayers(JsonParser.parseString(response.body()).getAsJsonObject());
			for (UUID id : batch) {
				String key = compact(id);
				CACHE.put(id, new Entry(players.containsKey(key), players.get(key), now + CACHE_MILLIS));
			}
		} catch (Exception e) {
			// Dienst nicht erreichbar: bisherige Ergebnisse behalten, in einer Minute erneut versuchen
			for (UUID id : batch) {
				Entry old = CACHE.get(id);
				CACHE.put(id, new Entry(old != null && old.badge(), old != null ? old.cape() : null, now + ERROR_RETRY_MILLIS));
			}
		}
	}

	/**
	 * AxoClient-Spieler aus der Antwort, je kompakter UUID das Cape (oder null). Verstanden werden
	 * {@code {"players": {"<uuid>": {"cape": ...}}}} und {@code {"users": [...], "capes": {"<uuid>": ...}}}.
	 */
	static Map<String, String> parsePlayers(JsonObject json) {
		Map<String, String> result = new HashMap<>();
		if (json.has("players") && json.get("players").isJsonObject()) {
			for (Map.Entry<String, JsonElement> player : json.getAsJsonObject("players").entrySet()) {
				JsonElement value = player.getValue();
				String cape = value.isJsonObject() ? text(value.getAsJsonObject().get("cape")) : null;
				result.put(normalize(player.getKey()), cape);
			}
		} else if (json.has("players") && json.get("players").isJsonArray()) {
			for (JsonElement player : json.getAsJsonArray("players")) {
				if (player.isJsonPrimitive())
					result.put(normalize(player.getAsString()), null);
				else if (player.isJsonObject() && player.getAsJsonObject().has("uuid"))
					result.put(normalize(player.getAsJsonObject().get("uuid").getAsString()),
						text(player.getAsJsonObject().get("cape")));
			}
		} else if (json.has("users")) {
			JsonObject capes = json.has("capes") && json.get("capes").isJsonObject() ? json.getAsJsonObject("capes") : new JsonObject();
			json.getAsJsonArray("users").forEach(element -> {
				String key = normalize(element.getAsString());
				result.put(key, capes.has(key) ? text(capes.get(key)) : null);
			});
		}
		return result;
	}

	private static String text(JsonElement element) {
		return element == null || element.isJsonNull() ? null : element.getAsString();
	}

	private static String normalize(String uuid) {
		return uuid.replace("-", "").toLowerCase(java.util.Locale.ROOT);
	}

	/** Der Launcher meldet ein geändertes eigenes Cape: sofort übernehmen. */
	private static void onLauncherCape(JsonElement data) {
		if (data == null || !data.isJsonObject())
			return;
		JsonObject object = data.getAsJsonObject();
		UUID id = parse(text(object.get("uuid")));
		if (id != null)
			CACHE.put(id, new Entry(true, text(object.get("cape")), System.currentTimeMillis() + CACHE_MILLIS));
	}

	private static UUID parse(String uuid) {
		if (uuid == null)
			return null;
		String hex = normalize(uuid.trim());
		if (!hex.matches("[0-9a-f]{32}"))
			return null;
		return UUID.fromString(hex.replaceFirst("(\\p{XDigit}{8})(\\p{XDigit}{4})(\\p{XDigit}{4})(\\p{XDigit}{4})(\\p{XDigit}+)", "$1-$2-$3-$4-$5"));
	}

	private static long localCapeStamp;

	/**
	 * Die Mod schreibt "<uuid>\n<umhang>" nach axoclient-cape.txt im Spielordner, sobald der Spieler seinen
	 * Umhang ändert. So sieht er ihn im laufenden Spiel sofort, ohne auf die nächste Abfrage zu warten.
	 */
	private static void readLocalCape() {
		try {
			java.nio.file.Path file = java.nio.file.Path.of(System.getProperty("user.dir")).resolve(LOCAL_CAPE_FILE);
			if (!java.nio.file.Files.exists(file))
				return;
			long stamp = java.nio.file.Files.getLastModifiedTime(file).toMillis();
			if (stamp == localCapeStamp)
				return;
			localCapeStamp = stamp;
			String[] lines = java.nio.file.Files.readString(file).split("\\R", -1);
			UUID id = lines.length == 0 ? null : parse(lines[0]);
			if (id == null)
				return;
			String cape = lines.length > 1 && !lines[1].isBlank() ? lines[1].trim() : null;
			CACHE.put(id, new Entry(true, cape, System.currentTimeMillis() + CACHE_MILLIS));
		} catch (Exception ignored) {
			// Datei gerade in Arbeit oder nicht lesbar: beim nächsten Mal
		}
	}

	/** UUID ohne Bindestriche, so wie Mojang und der Dienst sie verwenden. */
	private static String compact(UUID id) {
		return id.toString().replace("-", "");
	}
}
