package de.mclauncher.badge;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.ArrayList;
import java.util.HashSet;
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
 * Fragt beim Symbol-Dienst nach, welche Spieler den Launcher nutzen.
 * Die Tabliste ruft {@link #hasBadge} bei jedem Bild auf, daher wird nie direkt dort gewartet:
 * unbekannte Spieler werden gesammelt und alle paar Sekunden gebündelt im Hintergrund abgefragt.
 */
public final class BadgeService {
	/** So schnell erscheint ein geänderter Umhang bei anderen Spielern (der Dienst wird nur gebündelt gefragt). */
	private static final long CACHE_MILLIS = TimeUnit.SECONDS.toMillis(15);
	/** Der Launcher legt hier ab, wenn der Spieler seinen Umhang ändert – dann gilt das sofort. */
	private static final String LOCAL_CAPE_FILE = "axoclient-cape.txt";
	private static final long ERROR_RETRY_MILLIS = TimeUnit.MINUTES.toMillis(1);
	private static final int MAX_BATCH = 90; // Cloudflare D1 erlaubt höchstens 100 Parameter pro Abfrage

	/** Ergebnis je Spieler: hat Symbol?, AxoClient-Umhang (oder null) + gültig bis. */
	private record Entry(boolean badge, String cape, long validUntil) {}

	private static final Map<UUID, Entry> CACHE = new ConcurrentHashMap<>();
	private static final Set<UUID> QUEUE = ConcurrentHashMap.newKeySet();

	private static HttpClient http;
	private static URI checkUri;

	private BadgeService() {}

	static void start(String api) {
		if (api == null || api.isBlank())
			return; // ohne Dienst-Adresse bleibt die Mod einfach inaktiv
		checkUri = URI.create(api.replaceAll("/+$", "") + "/check");
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

	/** Gewählter AxoClient-Umhang des Spielers oder null. Blockiert nie. */
	public static String capeOf(UUID player) {
		Entry entry = lookup(player);
		return entry != null ? entry.cape() : null;
	}

	private static Entry lookup(UUID player) {
		if (checkUri == null || player == null)
			return null;
		Entry entry = CACHE.get(player);
		if (entry == null || entry.validUntil() < System.currentTimeMillis())
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
			HttpRequest request = HttpRequest.newBuilder(checkUri)
				.timeout(Duration.ofSeconds(10))
				.header("Content-Type", "application/json")
				.POST(HttpRequest.BodyPublishers.ofString(body.toString()))
				.build();
			HttpResponse<String> response = http.send(request, HttpResponse.BodyHandlers.ofString());
			if (response.statusCode() != 200)
				throw new IllegalStateException("HTTP " + response.statusCode());

			JsonObject json = JsonParser.parseString(response.body()).getAsJsonObject();
			Set<String> users = new HashSet<>();
			json.getAsJsonArray("users").forEach(element -> users.add(element.getAsString()));
			// Ältere Dienst-Versionen liefern noch keine Umhänge
			JsonObject capes = json.has("capes") ? json.getAsJsonObject("capes") : new JsonObject();
			for (UUID id : batch) {
				String key = compact(id);
				String cape = capes.has(key) ? capes.get(key).getAsString() : null;
				CACHE.put(id, new Entry(users.contains(key), cape, now + CACHE_MILLIS));
			}
		} catch (Exception e) {
			// Dienst nicht erreichbar: bisherige Ergebnisse behalten, in einer Minute erneut versuchen
			for (UUID id : batch) {
				Entry old = CACHE.get(id);
				CACHE.put(id, new Entry(old != null && old.badge(), old != null ? old.cape() : null, now + ERROR_RETRY_MILLIS));
			}
		}
	}

	private static long localCapeStamp;

	/**
	 * Der Launcher schreibt "<uuid>
<umhang>" nach axoclient-cape.txt im Spielordner, sobald der Spieler seinen
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
			if (lines.length == 0 || !lines[0].trim().matches("[0-9a-f]{32}"))
				return;
			String hex = lines[0].trim();
			UUID id = UUID.fromString(hex.replaceFirst("(\\p{XDigit}{8})(\\p{XDigit}{4})(\\p{XDigit}{4})(\\p{XDigit}{4})(\\p{XDigit}+)", "$1-$2-$3-$4-$5"));
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
