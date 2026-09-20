package de.mclauncher.badge.hud;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;

/**
 * Benannte Sätze aller Einstellungen ("PvP", "Bauen", ...). Jedes Profil ist eine JSON-Datei in
 * <code>config/axoclient-hud-profiles</code>; der Launcher kann sie bearbeiten, einzeln exportieren und an
 * Freunde schicken. Es gibt immer das Profil "Standard". Das aktive Profil folgt jeder Änderung im Spiel;
 * ein Profil kann Server nennen, auf denen es sich beim Betreten automatisch einschaltet.
 */
public final class HudProfiles {
	public static final int MAX_NAME = 24;
	/** Dieses Profil gibt es immer; es gilt auch, wenn ein Server-Profil wieder verlassen wird. */
	public static final String DEFAULT = "Standard";
	private static final String EXTENSION = ".json";

	/** Vom Status-Thread gemeldet, vom Render-Thread abgearbeitet (die Einstellungen gehören diesem). */
	private static volatile String pendingServer;
	private static volatile String lastServer = "";
	private static boolean applying;
	/** Profil, zu dem beim Verlassen des Servers zurückgekehrt wird. */
	private static String returnProfile;

	private HudProfiles() {}

	/** Nur Buchstaben, Ziffern, Leerzeichen, Bindestrich und Unterstrich – es wird ein Dateiname. */
	public static String clean(String name) {
		StringBuilder clean = new StringBuilder();
		for (char c : name.toCharArray())
			if (Character.isLetterOrDigit(c) || c == ' ' || c == '-' || c == '_')
				clean.append(c);
		String result = clean.toString().trim();
		return result.length() > MAX_NAME ? result.substring(0, MAX_NAME).trim() : result;
	}

	public static boolean allowed(int codepoint) {
		return codepoint < 0x10000 && (Character.isLetterOrDigit(codepoint) || codepoint == ' ' || codepoint == '-'
			|| codepoint == '_');
	}

	private static Path fileOf(HudConfig config, String name) {
		return config.profilesDir().resolve(clean(name) + EXTENSION);
	}

	/** Namen der gespeicherten Profile: "Standard" zuerst, dann alphabetisch. */
	public static List<String> list(HudConfig config) {
		List<String> names = new ArrayList<>();
		Path dir = config.profilesDir();
		if (Files.isDirectory(dir)) {
			try (Stream<Path> files = Files.list(dir)) {
				files.map(path -> path.getFileName().toString())
					.filter(name -> name.endsWith(EXTENSION))
					.map(name -> name.substring(0, name.length() - EXTENSION.length()))
					.sorted(String.CASE_INSENSITIVE_ORDER)
					.forEach(names::add);
			} catch (IOException e) {
				// Ordner nicht lesbar: dann eben keine Profile
			}
		}
		names.remove(DEFAULT);
		names.add(0, DEFAULT);
		return names;
	}

	/** Ein noch freier Name wie "Profil 3". */
	public static String freeName(HudConfig config) {
		List<String> existing = list(config);
		for (int i = 1; ; i++) {
			String name = "Profil " + i;
			if (!existing.contains(name))
				return name;
		}
	}

	/**
	 * Sorgt dafür, dass es "Standard" gibt: fehlt es, wird es aus den aktuellen Einstellungen angelegt, und wer noch
	 * kein Profil hat, arbeitet ab jetzt darin.
	 */
	static void ensureDefault(HudConfig config) {
		try {
			if (!Files.exists(fileOf(config, DEFAULT))) {
				Files.createDirectories(config.profilesDir());
				Files.writeString(fileOf(config, DEFAULT), config.toJson().toString(), StandardCharsets.UTF_8);
			}
		} catch (IOException e) {
			// Nicht schreibbar: es geht auch ohne Profil-Datei
		}
		if (config.activeProfile.isBlank())
			config.activeProfile = DEFAULT;
	}

	/** Schreibt die Einstellungen zusätzlich in die Datei des aktiven Profils. */
	static void writeActive(HudConfig config, JsonObject json) {
		if (config.activeProfile.isBlank() || applying)
			return;
		try {
			Files.createDirectories(config.profilesDir());
			Files.writeString(fileOf(config, config.activeProfile), json.toString(), StandardCharsets.UTF_8);
		} catch (IOException e) {
			// wie oben
		}
	}

	/** Speichert die aktuellen Einstellungen als eigenes Profil (ein gleichnamiges wird ersetzt) und wechselt dorthin. */
	public static boolean save(HudConfig config, String rawName) {
		String name = clean(rawName);
		if (name.isEmpty())
			return false;
		config.activeProfile = name;
		if (!name.equals(DEFAULT))
			config.servers.clear(); // Server gehören zum Profil, nicht zu seiner Kopie
		config.save();
		return Files.exists(fileOf(config, name));
	}

	/** Ersetzt die aktuellen Einstellungen durch das Profil. */
	public static boolean load(HudConfig config, String rawName) {
		String name = clean(rawName);
		try {
			JsonObject json = JsonParser.parseString(Files.readString(fileOf(config, name), StandardCharsets.UTF_8))
				.getAsJsonObject();
			applying = true;
			config.reset();
			config.servers.clear();
			config.apply(json);
			config.activeProfile = name;
			applying = false;
			config.save();
			return true;
		} catch (Exception e) {
			return false;
		} finally {
			applying = false;
		}
	}

	/** Löscht ein Profil; "Standard" bleibt immer erhalten. */
	public static boolean delete(HudConfig config, String rawName) {
		String name = clean(rawName);
		if (name.equals(DEFAULT))
			return false;
		try {
			boolean deleted = Files.deleteIfExists(fileOf(config, name));
			if (deleted && name.equals(config.activeProfile))
				load(config, DEFAULT);
			return deleted;
		} catch (IOException e) {
			return false;
		}
	}

	// ---------- Automatisch je Server ----------

	/** Der Server, auf dem der Spieler gerade ist ("" = keiner). */
	public static String currentServer() {
		return lastServer;
	}

	/** Vom Status-Thread: der Spieler ist auf diesem Server ("" = auf keinem). */
	public static void serverChanged(String address) {
		lastServer = address == null ? "" : address;
		pendingServer = lastServer;
	}

	/** Jedes Bild im Render-Thread: setzt eine gemeldete Serveränderung um. */
	static void applyPending(HudConfig config) {
		String address = pendingServer;
		if (address == null)
			return;
		pendingServer = null;
		if (address.isEmpty()) {
			if (returnProfile != null) {
				String back = returnProfile;
				returnProfile = null;
				if (!back.equals(config.activeProfile))
					load(config, back);
			}
			return;
		}
		String match = profileFor(config, address);
		if (match == null || match.equals(config.activeProfile))
			return;
		if (returnProfile == null)
			returnProfile = config.activeProfile.isBlank() ? DEFAULT : config.activeProfile;
		load(config, match);
	}

	/** Das Profil, das diesen Server nennt (das erste in alphabetischer Reihenfolge), oder null. */
	private static String profileFor(HudConfig config, String address) {
		String wanted = normalize(address);
		for (String name : list(config)) {
			try {
				JsonObject json = JsonParser.parseString(Files.readString(fileOf(config, name), StandardCharsets.UTF_8))
					.getAsJsonObject();
				if (!json.has("server") || !json.get("server").isJsonArray())
					continue;
				for (var element : json.getAsJsonArray("server")) {
					String entry = normalize(element.getAsString());
					if (!entry.isEmpty() && (wanted.equals(entry) || wanted.endsWith("." + entry)))
						return name;
				}
			} catch (Exception e) {
				// kaputtes Profil überspringen
			}
		}
		return null;
	}

	/** Kleinschreibung, ohne Standard-Port und ohne Punkt am Ende. */
	static String normalize(String address) {
		String value = address.trim().toLowerCase(java.util.Locale.ROOT);
		if (value.endsWith(":25565"))
			value = value.substring(0, value.length() - 6);
		while (value.endsWith("."))
			value = value.substring(0, value.length() - 1);
		return value;
	}
}
