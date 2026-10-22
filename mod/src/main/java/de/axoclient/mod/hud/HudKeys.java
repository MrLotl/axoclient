package de.axoclient.mod.hud;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.function.Predicate;

/**
 * Tasten werden als Minecraft-Tastenname gespeichert ("key.keyboard.g"). Der Name ist in allen Versionen gleich,
 * auch seit 26.3 mit SDL andere Nummern gelten; jede Version rechnet ihn selbst in ihre Nummer um.
 */
public final class HudKeys {
	private static final String KEYBOARD = "key.keyboard.";

	private static final Map<String, String> WORDS = Map.ofEntries(
		Map.entry("control", "Strg"), Map.entry("shift", "Shift"), Map.entry("alt", "Alt"), Map.entry("win", "Win"),
		Map.entry("space", "Leertaste"), Map.entry("enter", "Enter"), Map.entry("backspace", "Rücktaste"),
		Map.entry("tab", "Tab"), Map.entry("keypad", "Num"), Map.entry("caps", "Feststell"), Map.entry("lock", ""),
		Map.entry("page", "Bild"), Map.entry("up", "hoch"), Map.entry("down", "runter"), Map.entry("insert", "Einfg"),
		Map.entry("delete", "Entf"), Map.entry("home", "Pos1"), Map.entry("end", "Ende"), Map.entry("escape", "Esc"));

	private HudKeys() {}

	public static String label(String name) {
		if (name == null || name.isEmpty())
			return "Keine";
		String rest = name.startsWith(KEYBOARD) ? name.substring(KEYBOARD.length()) : name;
		String side = "";
		List<String> words = new ArrayList<>();
		for (String part : rest.split("\\.")) {
			if (part.equals("left") || part.equals("right")) {
				side = part.equals("left") ? " links" : " rechts";
				continue;
			}
			String word = WORDS.getOrDefault(part, part.length() <= 1 ? part.toUpperCase(Locale.ROOT)
				: part.substring(0, 1).toUpperCase(Locale.ROOT) + part.substring(1));
			if (!word.isEmpty())
				words.add(word);
		}
		return String.join(" ", words) + side;
	}

	/** Ob die Taste samt Zusatztaste gerade gedrückt ist; {@code down} fragt eine Taste über ihren Namen ab. */
	public static boolean pressed(String key, int modifier, Predicate<String> down) {
		if (key == null || key.isEmpty() || !down.test(key))
			return false;
		return switch (modifier) {
			case HudConfig.MODIFIER_SHIFT -> either("shift", down);
			case HudConfig.MODIFIER_CTRL -> either("control", down);
			case HudConfig.MODIFIER_ALT -> either("alt", down);
			default -> true;
		};
	}

	private static boolean either(String modifier, Predicate<String> down) {
		return down.test(KEYBOARD + "left." + modifier) || down.test(KEYBOARD + "right." + modifier);
	}

	/** Shift, Strg, Alt und Windows taugen nicht als eigene Taste – dafür gibt es die Zusatztaste. */
	public static boolean isModifier(String name) {
		return name != null && (name.endsWith(".shift") || name.endsWith(".control") || name.endsWith(".alt")
			|| name.endsWith(".win"));
	}
}
