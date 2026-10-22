package de.axoclient.mod;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.net.URI;
import java.net.URLEncoder;
import java.net.http.HttpClient;
import java.net.http.WebSocket;
import java.nio.charset.StandardCharsets;
import java.time.Duration;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.CompletionStage;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.function.Consumer;
import java.util.regex.Pattern;

/**
 * Verbindung zum AxoClient-Launcher: eine WebSocket-Verbindung, ausschließlich zu 127.0.0.1. Port, Token und Instanz
 * übergibt der Launcher beim Start ({@code -Daxoclient.ipc=127.0.0.1:<port>}, {@code -Daxoclient.token},
 * {@code -Daxoclient.instance}). Ohne diese Angaben (Spiel nicht vom Launcher gestartet) bleibt sie aus.
 *
 * <p>Nachrichten sind JSON: {@code {"type": "...", "data": ...}}. Die Mod bekommt nie das Token des Online-Service.
 */
public final class AxoLink {
	/** Zum Launcher passendes Protokoll; ändert sich nur bei inkompatiblen Änderungen. */
	public static final int PROTOCOL = 2;

	private static final Pattern TOKEN = Pattern.compile("[0-9a-f]{16,128}");
	private static final Pattern INSTANCE = Pattern.compile("[A-Za-z0-9._-]{1,64}");
	/** Der Launcher liest höchstens 64 KB je Nachricht. */
	private static final int MAX_MESSAGE = 60_000;
	/** Vom Launcher kommen auch große Emote-Animationen (bis 200 000 Zeichen, escaped deutlich mehr). */
	private static final int MAX_INCOMING = 1024 * 1024;
	private static final long MIN_RETRY_MS = 2_000;
	private static final long MAX_RETRY_MS = 30_000;

	private static final Map<String, Consumer<JsonElement>> HANDLERS = new ConcurrentHashMap<>();
	private static final Set<String> FEATURES = ConcurrentHashMap.newKeySet();

	private static URI uri;
	private static HttpClient http;
	private static ScheduledExecutorService executor;
	private static volatile WebSocket socket;
	private static CompletableFuture<?> lastSend = CompletableFuture.completedFuture(null);
	private static long retryMs = MIN_RETRY_MS;
	private static String minecraft = "";
	private static boolean warned;
	private static volatile long retryAt;
	private static volatile java.util.concurrent.ScheduledFuture<?> pending;

	private AxoLink() {}

	/** Baut die Verbindung auf, wenn der Launcher die nötigen Angaben übergeben hat. */
	public static synchronized void start(String minecraftVersion) {
		if (uri != null)
			return;
		minecraft = minecraftVersion == null ? "" : minecraftVersion;
		String ipc = System.getProperty("axoclient.ipc");
		String token = System.getProperty("axoclient.token");
		String instance = System.getProperty("axoclient.instance");
		if (ipc == null || token == null || instance == null)
			return;
		int port = loopbackPort(ipc.trim());
		if (port <= 0 || !TOKEN.matcher(token).matches() || !INSTANCE.matcher(instance).matches()) {
			System.out.println("[AxoClient] Launcher-Verbindung: ungültige Angaben, bleibt aus");
			return;
		}
		uri = URI.create("ws://127.0.0.1:" + port + "/?instance=" + URLEncoder.encode(instance, StandardCharsets.UTF_8)
			+ "&token=" + token);
		http = HttpClient.newBuilder()
			.connectTimeout(Duration.ofSeconds(5))
			.proxy(HttpClient.Builder.NO_PROXY)
			.build();
		executor = Executors.newSingleThreadScheduledExecutor(runnable -> {
			Thread thread = new Thread(runnable, "AxoClient Link");
			thread.setDaemon(true);
			return thread;
		});
		executor.execute(AxoLink::connect);
	}

	/**
	 * Port aus {@code 127.0.0.1:<port>}. Andere Hosts (auch "localhost", das sich umbiegen ließe) werden abgelehnt.
	 *
	 * @return Port oder -1
	 */
	static int loopbackPort(String ipc) {
		if (!ipc.startsWith("127.0.0.1:"))
			return -1;
		try {
			int port = Integer.parseInt(ipc.substring("127.0.0.1:".length()));
			return port > 0 && port <= 65535 ? port : -1;
		} catch (NumberFormatException e) {
			return -1;
		}
	}

	/** Meldet einen Empfänger für einen Nachrichtentyp an (läuft auf dem Verbindungs-Thread, nicht dem Render-Thread). */
	public static void on(String type, Consumer<JsonElement> handler) {
		HANDLERS.put(type, handler);
	}

	public static boolean connected() {
		return socket != null;
	}

	/** Ob der verbundene Launcher eine Funktion anbietet (aus seiner "welcome"-Nachricht). */
	public static boolean supports(String feature) {
		return socket != null && FEATURES.contains(feature);
	}

	/** Schickt eine Nachricht; false, wenn keine Verbindung steht oder sie zu groß ist. */
	public static boolean send(String type, JsonElement data) {
		return send(type, data, false);
	}

	/**
	 * Schickt eine Nachricht.
	 *
	 * @param droppable bei true wird sie verworfen, solange die vorige noch unterwegs ist (für laufend erneuerte
	 *                  Werte wie das private Overlay: nur das neueste zählt, das Spiel wartet nie)
	 */
	public static synchronized boolean send(String type, JsonElement data, boolean droppable) {
		WebSocket open = socket;
		if (open == null)
			return false;
		if (droppable && !lastSend.isDone())
			return false;
		JsonObject message = new JsonObject();
		message.addProperty("type", type);
		if (data != null)
			message.add("data", data);
		String text = message.toString();
		if (text.length() > MAX_MESSAGE)
			return false;
		// java.net.http.WebSocket erlaubt nur eine laufende Sendung: hintereinander hängen
		lastSend = lastSend.handle((ok, error) -> null).thenCompose(ignored -> open.sendText(text, true));
		return true;
	}

	private static void connect() {
		http.newWebSocketBuilder()
			.connectTimeout(Duration.ofSeconds(5))
			.buildAsync(uri, new Listener())
			.whenComplete((ws, error) -> {
				if (error != null) {
					if (!warned) {
						warned = true; // einmal genügt
						System.out.println("[AxoClient] Launcher-Verbindung nicht möglich (" + error.getMessage() + ")");
					}
					retry();
				}
			});
	}

	private static void retry() {
		long delay = retryMs;
		retryMs = Math.min(retryMs * 2, MAX_RETRY_MS);
		retryAt = System.currentTimeMillis() + delay;
		pending = executor.schedule(AxoLink::connect, delay, TimeUnit.MILLISECONDS);
	}

	/** Nächster Verbindungsversuch (Zeitpunkt in ms) oder 0, wenn verbunden bzw. gerade einer läuft. */
	public static long retryAt() {
		return socket == null ? retryAt : 0;
	}

	/** Ob die Verbindung überhaupt vorgesehen ist (Spiel vom Launcher gestartet). */
	public static boolean configured() {
		return uri != null;
	}

	/** "Erneut verbinden": nicht auf den nächsten geplanten Versuch warten. */
	public static synchronized void reconnectNow() {
		if (uri == null || socket != null)
			return;
		java.util.concurrent.ScheduledFuture<?> waiting = pending;
		if (waiting != null)
			waiting.cancel(false);
		retryMs = MIN_RETRY_MS;
		retryAt = 0;
		executor.execute(AxoLink::connect);
	}

	private static void lost(WebSocket ws) {
		synchronized (AxoLink.class) {
			if (socket != ws)
				return;
			socket = null;
			FEATURES.clear();
		}
		System.out.println("[AxoClient] Launcher-Verbindung getrennt");
		retry();
	}

	private static void dispatch(String text) {
		try {
			JsonObject message = JsonParser.parseString(text).getAsJsonObject();
			String type = message.get("type").getAsString();
			JsonElement data = message.has("data") ? message.get("data") : null;
			if ("welcome".equals(type) && data != null && data.isJsonObject() && data.getAsJsonObject().has("features")) {
				JsonArray features = data.getAsJsonObject().getAsJsonArray("features");
				FEATURES.clear();
				features.forEach(feature -> FEATURES.add(feature.getAsString()));
			}
			Consumer<JsonElement> handler = HANDLERS.get(type);
			if (handler != null)
				handler.accept(data);
		} catch (Exception e) {
			// Unbekanntes oder kaputtes Format: ignorieren
		}
	}

	private static final class Listener implements WebSocket.Listener {
		private final StringBuilder buffer = new StringBuilder();

		@Override
		public void onOpen(WebSocket ws) {
			synchronized (AxoLink.class) {
				socket = ws;
				lastSend = CompletableFuture.completedFuture(null);
			}
			retryMs = MIN_RETRY_MS;
			warned = false;
			System.out.println("[AxoClient] Mit dem Launcher verbunden");
			JsonObject hello = new JsonObject();
			hello.addProperty("protocol", PROTOCOL);
			hello.addProperty("minecraft", minecraft);
			hello.addProperty("pid", ProcessHandle.current().pid());
			send("hello", hello);
			ws.request(1);
		}

		@Override
		public CompletionStage<?> onText(WebSocket ws, CharSequence data, boolean last) {
			if (buffer.length() + data.length() <= MAX_INCOMING)
				buffer.append(data);
			if (last) {
				String text = buffer.toString();
				buffer.setLength(0);
				dispatch(text);
			}
			ws.request(1);
			return null;
		}

		@Override
		public CompletionStage<?> onClose(WebSocket ws, int statusCode, String reason) {
			lost(ws);
			return null;
		}

		@Override
		public void onError(WebSocket ws, Throwable error) {
			lost(ws);
		}
	}
}
