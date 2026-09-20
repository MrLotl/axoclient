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
 * <code>config/axoclient-hud-profiles</code>; der Launcher kann sie einzeln exportieren und an
 * Freunde schicken. Geladen wird ein Profil, indem es die aktuellen Einstellungen ersetzt.
 */
public final class HudProfiles {
	public static final int MAX_NAME = 24;
	private static final String EXTENSION = ".json";

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

	/** Namen der gespeicherten Profile, alphabetisch. */
	public static List<String> list(HudConfig config) {
		List<String> names = new ArrayList<>();
		Path dir = config.profilesDir();
		if (!Files.isDirectory(dir))
			return names;
		try (Stream<Path> files = Files.list(dir)) {
			files.map(path -> path.getFileName().toString())
				.filter(name -> name.endsWith(EXTENSION))
				.map(name -> name.substring(0, name.length() - EXTENSION.length()))
				.sorted(String.CASE_INSENSITIVE_ORDER)
				.forEach(names::add);
		} catch (IOException e) {
			// Ordner nicht lesbar: dann eben keine Profile
		}
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

	/** Speichert die aktuellen Einstellungen unter einem Namen (überschreibt ein gleichnamiges Profil). */
	public static boolean save(HudConfig config, String rawName) {
		String name = clean(rawName);
		if (name.isEmpty())
			return false;
		try {
			config.activeProfile = name;
			Files.createDirectories(config.profilesDir());
			Files.writeString(config.profilesDir().resolve(name + EXTENSION), config.toJson().toString(),
				StandardCharsets.UTF_8);
			config.save();
			return true;
		} catch (IOException e) {
			return false;
		}
	}

	/** Ersetzt die aktuellen Einstellungen durch das Profil. */
	public static boolean load(HudConfig config, String rawName) {
		String name = clean(rawName);
		Path file = config.profilesDir().resolve(name + EXTENSION);
		try {
			JsonObject json = JsonParser.parseString(Files.readString(file, StandardCharsets.UTF_8))
				.getAsJsonObject();
			config.reset();
			config.apply(json);
			config.activeProfile = name;
			config.save();
			return true;
		} catch (Exception e) {
			return false;
		}
	}

	public static boolean delete(HudConfig config, String rawName) {
		try {
			return Files.deleteIfExists(config.profilesDir().resolve(clean(rawName) + EXTENSION));
		} catch (IOException e) {
			return false;
		}
	}
}
