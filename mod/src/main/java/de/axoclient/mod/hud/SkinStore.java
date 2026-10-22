package de.axoclient.mod.hud;

import com.google.gson.Gson;
import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.security.MessageDigest;
import java.time.Duration;
import java.util.ArrayList;
import java.util.HexFormat;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import java.util.concurrent.CompletableFuture;

/**
 * Daten für "Skins & Capes": die Skin-Bibliothek des Launchers (gleicher Ordner, gleiches Format), Skin und Umhänge
 * des Minecraft-Kontos und die AxoClient-Umhänge. Alles Netz läuft im Hintergrund; Ergebnisse landen über
 * {@link AxoPlatform#runOnMain} auf dem Render-Thread, der allein die Listen liest.
 */
public final class SkinStore {
	private static final String PROFILE = "https://api.minecraftservices.com/minecraft/profile";
	private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();

	public static final class Skin {
		public final String id;
		public String name;
		public boolean slim;
		public final Path file;
		byte[] png;

		Skin(String id, String name, boolean slim, Path file) {
			this.id = id;
			this.name = name;
			this.slim = slim;
			this.file = file;
		}

		public byte[] png() {
			if (png == null) {
				try {
					png = Files.readAllBytes(file);
				} catch (IOException e) {
					png = new byte[0];
				}
			}
			return png;
		}
	}

	/** Ein Umhang: Minecraft (Konto) oder AxoClient. png ist null, solange es lädt. */
	public static final class Cape {
		public final String id;
		public final String name;
		public final String url;
		public volatile byte[] png;

		Cape(String id, String name, String url) {
			this.id = id;
			this.name = name;
			this.url = url;
		}
	}

	private final AxoPlatform platform;
	private final HttpClient http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(8))
		.followRedirects(HttpClient.Redirect.NORMAL).build();

	public final List<Skin> skins = new ArrayList<>();
	public final List<Cape> mojangCapes = new ArrayList<>();
	public final List<Cape> axoCapes = new ArrayList<>();
	/** Aktiver Minecraft-Umhang, AxoClient-Umhang, zuletzt hochgeladener Bibliotheks-Skin (je null = keiner). */
	public String activeMojangCape;
	public String activeAxoCape;
	public String activeSkin;
	/** Skin des Kontos, wie Mojang ihn gerade hat (für die Vorschau, wenn keiner aus der Bibliothek passt). */
	public byte[] accountSkin;
	public boolean accountSlim;
	public boolean loadingProfile = true;
	public boolean loadingAxo = true;
	/** Rückmeldung unten im Bereich; leer = nichts. */
	public String status = "";
	public boolean busy;

	public SkinStore(AxoPlatform platform) {
		this.platform = platform;
	}

	public boolean hasLibrary() {
		return platform.launcherHome() != null;
	}

	public boolean hasAxo() {
		return platform.serviceUrl() != null && platform.serviceToken() != null;
	}

	// ---------- Laden ----------

	public void refresh() {
		loadLibrary();
		loadProfile();
		loadAxo();
	}

	private Path libraryDir() {
		Path home = platform.launcherHome();
		return home == null ? null : home.resolve("skins");
	}

	private void loadLibrary() {
		skins.clear();
		Path dir = libraryDir();
		if (dir == null)
			return;
		for (JsonElement element : readIndex()) {
			if (!element.isJsonObject())
				continue;
			JsonObject entry = element.getAsJsonObject();
			String id = string(entry, "Id");
			if (id == null || !id.matches("[0-9a-f]{6,40}"))
				continue;
			Path file = dir.resolve(id + ".png");
			if (Files.exists(file))
				skins.add(new Skin(id, orElse(string(entry, "Name"), id), entry.has("Slim") && entry.get("Slim").getAsBoolean(), file));
		}
	}

	private JsonArray readIndex() {
		Path index = libraryDir().resolve("library.json");
		try {
			if (Files.exists(index)) {
				JsonElement root = JsonParser.parseString(Files.readString(index));
				if (root.isJsonArray())
					return root.getAsJsonArray();
			}
		} catch (Exception ignored) {
			// kaputte Datei: so tun, als wäre sie leer (der Launcher meldet das selbst)
		}
		return new JsonArray();
	}

	private void writeIndex(JsonArray index) throws IOException {
		Path file = libraryDir().resolve("library.json");
		Files.createDirectories(file.getParent());
		Path temp = file.resolveSibling("library.json.tmp");
		Files.writeString(temp, GSON.toJson(index));
		Files.move(temp, file, java.nio.file.StandardCopyOption.REPLACE_EXISTING);
	}

	private void loadProfile() {
		loadingProfile = true;
		get(PROFILE, true).thenAccept(body -> {
			JsonObject profile = JsonParser.parseString(body).getAsJsonObject();
			List<Cape> capes = new ArrayList<>();
			String active = null;
			if (profile.has("capes"))
				for (JsonElement element : profile.getAsJsonArray("capes")) {
					JsonObject cape = element.getAsJsonObject();
					Cape item = new Cape(string(cape, "id"), orElse(string(cape, "alias"), "Umhang"), string(cape, "url"));
					capes.add(item);
					if ("ACTIVE".equals(string(cape, "state")))
						active = item.id;
					download(item);
				}
			String skinUrl = null;
			boolean slim = false;
			if (profile.has("skins"))
				for (JsonElement element : profile.getAsJsonArray("skins")) {
					JsonObject skin = element.getAsJsonObject();
					if ("ACTIVE".equals(string(skin, "state"))) {
						skinUrl = string(skin, "url");
						slim = "SLIM".equalsIgnoreCase(string(skin, "variant"));
					}
				}
			String activeCape = active;
			boolean activeSlim = slim;
			platform.runOnMain(() -> {
				mojangCapes.clear();
				mojangCapes.addAll(capes);
				activeMojangCape = activeCape;
				accountSlim = activeSlim;
				loadingProfile = false;
			});
			if (skinUrl != null)
				bytes(skinUrl).thenAccept(png -> platform.runOnMain(() -> accountSkin = png));
		}).exceptionally(error -> {
			platform.runOnMain(() -> {
				loadingProfile = false;
				status = "Das Minecraft-Konto konnte nicht abgefragt werden.";
			});
			return null;
		});
	}

	private void loadAxo() {
		loadingAxo = true;
		if (!hasAxo()) {
			loadingAxo = false;
			return;
		}
		String base = platform.serviceUrl().replaceAll("/+$", "");
		String me = platform.playerId();
		get(base + "/capes/capes.json", false).thenAccept(body -> {
			List<Cape> capes = new ArrayList<>();
			for (JsonElement element : JsonParser.parseString(body).getAsJsonArray()) {
				JsonObject cape = element.getAsJsonObject();
				String owner = string(cape, "owner");
				if (owner != null && !owner.equalsIgnoreCase(me))
					continue; // eigene Umhänge anderer Spieler darf man nicht tragen
				Cape item = new Cape(string(cape, "id"), orElse(string(cape, "name"), "Umhang"),
					base + "/capes/" + string(cape, "id") + ".png");
				capes.add(item);
				download(item);
			}
			platform.runOnMain(() -> {
				axoCapes.clear();
				axoCapes.addAll(capes);
			});
			return;
		}).thenCompose(ignored -> postService("/cape", new JsonObject())).thenAccept(answer -> {
			String cape = string(answer, "cape");
			platform.runOnMain(() -> {
				activeAxoCape = cape;
				loadingAxo = false;
			});
		}).exceptionally(error -> {
			platform.runOnMain(() -> loadingAxo = false);
			return null;
		});
	}

	private void download(Cape cape) {
		if (cape.url != null)
			bytes(cape.url).thenAccept(png -> cape.png = png);
	}

	// ---------- Ändern ----------

	/** Lädt den Skin bei Mojang hoch und zeigt ihn sofort im Spiel. */
	public void applySkin(Skin skin) {
		byte[] png = skin.png();
		if (busy || png.length == 0)
			return;
		busy = true;
		status = "Skin wird hochgeladen …";
		String boundary = "----AxoClient" + UUID.randomUUID().toString().replace("-", "");
		byte[] body = multipart(boundary, skin.slim ? "slim" : "classic", png);
		HttpRequest request = authorized(PROFILE + "/skins")
			.header("Content-Type", "multipart/form-data; boundary=" + boundary)
			.POST(HttpRequest.BodyPublishers.ofByteArray(body)).build();
		send(request).thenAccept(ignored -> platform.runOnMain(() -> {
			busy = false;
			activeSkin = skin.id;
			accountSkin = png;
			accountSlim = skin.slim;
			platform.showOwnSkin(png, skin.slim);
			status = "Skin „" + skin.name + "“ ist aktiv. Andere sehen ihn, sobald sie dich neu laden.";
		})).exceptionally(error -> failed("Der Skin konnte nicht hochgeladen werden", error));
	}

	public void selectMojangCape(Cape cape) {
		if (busy)
			return;
		busy = true;
		HttpRequest request = cape == null
			? authorized(PROFILE + "/capes/active").DELETE().build()
			: authorized(PROFILE + "/capes/active").header("Content-Type", "application/json")
				.PUT(HttpRequest.BodyPublishers.ofString("{\"capeId\":\"" + cape.id + "\"}")).build();
		send(request).thenAccept(ignored -> platform.runOnMain(() -> {
			busy = false;
			activeMojangCape = cape == null ? null : cape.id;
			platform.showOwnCape(cape == null ? null : cape.png);
			status = cape == null ? "Minecraft-Umhang abgelegt." : "Minecraft-Umhang „" + cape.name + "“ angelegt.";
		})).exceptionally(error -> failed("Der Umhang konnte nicht gewechselt werden", error));
	}

	public void selectAxoCape(Cape cape) {
		if (busy || !hasAxo())
			return;
		busy = true;
		JsonObject body = new JsonObject();
		if (cape == null)
			body.add("cape", com.google.gson.JsonNull.INSTANCE);
		else
			body.addProperty("cape", cape.id);
		postService("/cape", body).thenAccept(ignored -> platform.runOnMain(() -> {
			busy = false;
			activeAxoCape = cape == null ? null : cape.id;
			writeLocalCape(activeAxoCape);
			status = cape == null ? "AxoClient-Umhang abgelegt." : "AxoClient-Umhang „" + cape.name + "“ angelegt.";
		})).exceptionally(error -> failed("Der AxoClient-Umhang konnte nicht gewechselt werden", error));
	}

	/** Breite oder schmale Arme: gilt für die Bibliothek und beim nächsten Anwenden. */
	public void setSlim(Skin skin, boolean slim) {
		skin.slim = slim;
		try {
			JsonArray index = readIndex();
			for (JsonElement element : index)
				if (element.isJsonObject() && skin.id.equals(string(element.getAsJsonObject(), "Id")))
					element.getAsJsonObject().addProperty("Slim", slim);
			writeIndex(index);
		} catch (IOException e) {
			status = "Die Skin-Bibliothek konnte nicht gespeichert werden.";
		}
	}

	/** Öffnet den Windows-Dateidialog und nimmt das gewählte PNG in die Bibliothek auf. */
	public void addFromFile(java.util.function.Consumer<Skin> added) {
		if (busy || !hasLibrary())
			return;
		busy = true;
		status = "Datei auswählen …";
		CompletableFuture.supplyAsync(SkinStore::pickFile).thenAccept(path -> platform.runOnMain(() -> {
			busy = false;
			if (path == null) {
				status = "";
				return;
			}
			try {
				byte[] png = Files.readAllBytes(path);
				if (!isSkin(png)) {
					status = "Das ist kein Minecraft-Skin (PNG mit 64x64 oder 64x32 Pixeln).";
					return;
				}
				Skin skin = add(png, path.getFileName().toString().replaceFirst("(?i)\\.png$", ""));
				status = "„" + skin.name + "“ hinzugefügt. Mit „Anwenden“ wird er dein Skin.";
				added.accept(skin);
			} catch (IOException e) {
				status = "Die Datei konnte nicht gelesen werden.";
			}
		})).exceptionally(error -> failed("Der Dateidialog ließ sich nicht öffnen", error));
	}

	private Skin add(byte[] png, String name) throws IOException {
		String id = sha1(png).substring(0, 12);
		Path dir = libraryDir();
		Files.createDirectories(dir);
		Files.write(dir.resolve(id + ".png"), png);
		JsonArray index = readIndex();
		JsonArray updated = new JsonArray();
		JsonObject entry = new JsonObject();
		entry.addProperty("Id", id);
		entry.addProperty("Name", name.isBlank() ? "Skin" : name);
		entry.addProperty("Slim", false);
		entry.add("From", com.google.gson.JsonNull.INSTANCE);
		updated.add(entry);
		for (JsonElement element : index)
			if (!(element.isJsonObject() && id.equals(string(element.getAsJsonObject(), "Id"))))
				updated.add(element);
		writeIndex(updated);
		loadLibrary();
		return skins.stream().filter(s -> s.id.equals(id)).findFirst().orElseThrow();
	}

	private Void failed(String what, Throwable error) {
		Throwable cause = error.getCause() != null ? error.getCause() : error;
		platform.runOnMain(() -> {
			busy = false;
			status = what + ": " + (cause.getMessage() != null ? cause.getMessage() : cause.getClass().getSimpleName());
		});
		return null;
	}

	private void writeLocalCape(String capeId) {
		try {
			Files.writeString(platform.gameDir().resolve("axoclient-cape.txt"),
				platform.playerId() + "\n" + (capeId == null ? "" : capeId));
		} catch (IOException ignored) {
			// dann erscheint der Umhang eben nach der nächsten Abfrage des Dienstes
		}
	}

	// ---------- Netz ----------

	private HttpRequest.Builder authorized(String url) {
		return HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(20))
			.header("Authorization", "Bearer " + platform.accessToken());
	}

	private CompletableFuture<String> get(String url, boolean withToken) {
		HttpRequest.Builder builder = withToken ? authorized(url) : HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(20));
		return send(builder.GET().build());
	}

	private CompletableFuture<byte[]> bytes(String url) {
		HttpRequest request = HttpRequest.newBuilder(URI.create(url)).timeout(Duration.ofSeconds(20)).GET().build();
		return http.sendAsync(request, HttpResponse.BodyHandlers.ofByteArray()).thenApply(response -> {
			if (response.statusCode() / 100 != 2)
				throw new IllegalStateException("HTTP " + response.statusCode());
			return response.body();
		});
	}

	private CompletableFuture<JsonObject> postService(String path, JsonObject body) {
		body.addProperty("token", platform.serviceToken());
		HttpRequest request = HttpRequest.newBuilder(URI.create(platform.serviceUrl().replaceAll("/+$", "") + path))
			.timeout(Duration.ofSeconds(20)).header("Content-Type", "application/json")
			.POST(HttpRequest.BodyPublishers.ofString(body.toString())).build();
		return send(request).thenApply(text -> JsonParser.parseString(text).getAsJsonObject());
	}

	private CompletableFuture<String> send(HttpRequest request) {
		return http.sendAsync(request, HttpResponse.BodyHandlers.ofString()).thenApply(response -> {
			if (response.statusCode() / 100 != 2)
				throw new IllegalStateException(errorText(response));
			return response.body();
		});
	}

	private static String errorText(HttpResponse<String> response) {
		try {
			JsonObject json = JsonParser.parseString(response.body()).getAsJsonObject();
			for (String key : new String[] { "error", "errorMessage", "message" })
				if (string(json, key) != null)
					return string(json, key);
		} catch (Exception ignored) {
			// kein JSON
		}
		return switch (response.statusCode()) {
			case 401 -> "Die Anmeldung ist abgelaufen. Starte das Spiel über den Launcher neu.";
			case 429 -> "Zu viele Änderungen kurz hintereinander. Warte kurz.";
			default -> "Der Dienst antwortet mit Status " + response.statusCode() + ".";
		};
	}

	private static byte[] multipart(String boundary, String variant, byte[] png) {
		String head = "--" + boundary + "\r\nContent-Disposition: form-data; name=\"variant\"\r\n\r\n" + variant + "\r\n"
			+ "--" + boundary + "\r\nContent-Disposition: form-data; name=\"file\"; filename=\"skin.png\"\r\n"
			+ "Content-Type: image/png\r\n\r\n";
		byte[] start = head.getBytes(StandardCharsets.UTF_8);
		byte[] end = ("\r\n--" + boundary + "--\r\n").getBytes(StandardCharsets.UTF_8);
		byte[] body = new byte[start.length + png.length + end.length];
		System.arraycopy(start, 0, body, 0, start.length);
		System.arraycopy(png, 0, body, start.length, png.length);
		System.arraycopy(end, 0, body, start.length + png.length, end.length);
		return body;
	}

	// ---------- Hilfen ----------

	/** PNG mit 64x64 oder 64x32 Pixeln. */
	public static boolean isSkin(byte[] png) {
		int[] size = size(png);
		return size != null && size[0] == 64 && (size[1] == 64 || size[1] == 32);
	}

	/** {Breite, Höhe} eines PNG (stehen fest im Kopf der Datei) oder null, wenn es keins ist. */
	public static int[] size(byte[] png) {
		if (png == null || png.length < 24 || (png[0] & 0xFF) != 0x89 || png[1] != 'P' || png[2] != 'N' || png[3] != 'G')
			return null;
		int width = ((png[16] & 0xFF) << 24) | ((png[17] & 0xFF) << 16) | ((png[18] & 0xFF) << 8) | (png[19] & 0xFF);
		int height = ((png[20] & 0xFF) << 24) | ((png[21] & 0xFF) << 16) | ((png[22] & 0xFF) << 8) | (png[23] & 0xFF);
		return width > 0 && height > 0 ? new int[] { width, height } : null;
	}

	/** Der Windows-Dialog über PowerShell: läuft unabhängig von der Minecraft-Version und bleibt vor dem Spiel. */
	private static Path pickFile() {
		String script = "Add-Type -AssemblyName System.Windows.Forms;"
			+ "$o=New-Object System.Windows.Forms.Form -Property @{TopMost=$true;ShowInTaskbar=$false};"
			+ "$d=New-Object System.Windows.Forms.OpenFileDialog;"
			+ "$d.Title='Skin hinzufügen';$d.Filter='Minecraft-Skin (*.png)|*.png';"
			+ "if($d.ShowDialog($o) -eq 'OK'){[Console]::Out.Write($d.FileName)}";
		try {
			Process process = new ProcessBuilder("powershell.exe", "-NoProfile", "-STA", "-NonInteractive",
				"-ExecutionPolicy", "Bypass", "-Command", script).redirectErrorStream(true).start();
			String out = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8).trim();
			process.waitFor();
			return out.isEmpty() ? null : Path.of(out);
		} catch (IOException e) {
			throw new IllegalStateException("PowerShell ist nicht verfügbar.", e);
		} catch (InterruptedException e) {
			Thread.currentThread().interrupt();
			return null;
		}
	}

	private static String sha1(byte[] data) {
		try {
			return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-1").digest(data)).toLowerCase(Locale.ROOT);
		} catch (Exception e) {
			throw new IllegalStateException(e);
		}
	}

	private static String string(JsonObject json, String key) {
		return json != null && json.has(key) && json.get(key).isJsonPrimitive() ? json.get(key).getAsString() : null;
	}

	private static String orElse(String value, String fallback) {
		return value == null || value.isBlank() ? fallback : value;
	}
}
