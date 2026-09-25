package de.mclauncher.badge.hud;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;
import java.util.EnumMap;
import java.util.Map;

/**
 * Beschriftungen der Anzeigen – für alle Minecraft-Versionen gleich. Wie ein Wert aussieht
 * (Beschriftung, Einheit, Stellen, Darstellung) steht in den Einstellungen der jeweiligen
 * Anzeige; gemessene Zahlen merkt sich die Klasse für die Farbregeln.
 */
public final class HudText {
	/**
	 * Farbmarke im Text: danach folgt '0' bis '2' (ab hier Farbe der Achse X, Y, Z) oder
	 * {@link #COLOUR_RESET} (zurück zur Textfarbe). Die Marken selbst werden nie gezeichnet.
	 */
	public static final char COLOUR_MARK = '\u0001';
	public static final char COLOUR_RESET = 'r';

	/** Text ohne Farbmarken, z.B. zum Messen der Breite. */
	public static String plain(String text) {
		if (text.indexOf(COLOUR_MARK) < 0)
			return text;
		StringBuilder plain = new StringBuilder(text.length());
		for (int i = 0; i < text.length(); i++) {
			if (text.charAt(i) == COLOUR_MARK)
				i++; // Marke und das Zeichen danach überspringen
			else
				plain.append(text.charAt(i));
		}
		return plain.toString();
	}

	private static final double GIGABYTE = 1024.0 * 1024.0 * 1024.0;
	/** So oft wird die Geschwindigkeit neu gemessen. */
	private static final long SPEED_INTERVAL_NANOS = 100_000_000L;
	private static final String[] FACING_SHORT = { "S", "W", "N", "O" };

	private static double lastX;
	private static double lastZ;
	private static long lastNanos;
	private static double blocksPerSecond;
	private static int lastQuarter = 2;

	private static HudConfig config;
	private static final Map<HudModule, HudConfig.Entry> FALLBACK = new EnumMap<>(HudModule.class);
	private static final Map<HudModule, Double> METRICS = new EnumMap<>(HudModule.class);

	private HudText() {}

	/** Damit die Beschriftungen die Einstellungen kennen (einmal beim Laden). */
	static void bind(HudConfig bound) {
		config = bound;
	}

	private static HudConfig.Entry entry(HudModule module) {
		if (config != null)
			return config.get(module);
		return FALLBACK.computeIfAbsent(module, HudConfig.Entry::new);
	}

	/** Der zuletzt gemessene Zahlenwert einer Anzeige (für Farbregeln), oder null. */
	public static Double metric(HudModule module) {
		return METRICS.get(module);
	}

	// ---------- Bausteine ----------

	/** Wert mit Einheit und/oder Beschriftung, je nach Einstellung. */
	private static String decorate(HudModule module, HudConfig.Entry entry, String value, String unit) {
		String text = value;
		boolean unitIsLabel = unit != null && unit.equalsIgnoreCase(module.shortLabel);
		if (entry.units && unit != null && !unit.isEmpty() && !(entry.labels && unitIsLabel))
			text += " " + unit;
		return entry.labels && !module.shortLabel.isEmpty() ? module.shortLabel + ": " + text : text;
	}

	private static String number(double value, int decimals) {
		if (decimals <= 0)
			return Long.toString((long) Math.floor(value));
		return String.format("%." + decimals + "f", value);
	}

	// ---------- Anzeigen ----------

	public static String fps(int fps) {
		METRICS.put(HudModule.FPS, (double) fps);
		return decorate(HudModule.FPS, entry(HudModule.FPS), Integer.toString(fps), HudModule.FPS.unit);
	}

	public static String ping(Integer milliseconds) {
		if (milliseconds == null) {
			METRICS.remove(HudModule.PING);
			return null;
		}
		METRICS.put(HudModule.PING, (double) milliseconds);
		return decorate(HudModule.PING, entry(HudModule.PING), Integer.toString(milliseconds), HudModule.PING.unit);
	}

	/** Koordinaten nach Einstellung: Achsen, Stellen, Trenner, untereinander, Beschriftung, Himmelsrichtung. */
	public static String coords(double x, double y, double z) {
		HudConfig.Entry entry = entry(HudModule.COORDS);
		double[] values = { x, y, z };
		String[] names = { "X", "Y", "Z" };
		java.util.List<String> parts = new java.util.ArrayList<>();
		for (int i = 0; i < values.length; i++) {
			if (!entry.axes[i])
				continue;
			String value = number(values[i], entry.decimals);
			String label = entry.labels ? names[i] + (entry.stacked ? ": " : " ") : "";
			String on = "" + COLOUR_MARK + (char) ('0' + i);
			String off = "" + COLOUR_MARK + COLOUR_RESET;
			// Farbe je Achse: Marken im Text, die das Zeichnen auswertet (siehe HudOverlay)
			parts.add(switch (entry.axisColours) {
				case 1 -> label.isEmpty() ? value : on + names[i] + off + label.substring(1) + value;
				case 2 -> on + label + value + off;
				case 3 -> label + on + value + off;
				default -> label + value;
			});
		}
		if (parts.isEmpty())
			parts.add(number(x, entry.decimals)); // nie ganz leer, sonst verschwindet die Anzeige
		if (entry.facing)
			parts.add(FACING_SHORT[lastQuarter]);
		return String.join(entry.stacked ? "\n" : HudConfig.SEPARATORS[entry.separator], parts);
	}

	/** Chunk-Koordinaten und die Stelle innerhalb des Chunks. */
	public static String chunk(double x, double z) {
		HudConfig.Entry entry = entry(HudModule.CHUNK);
		int blockX = (int) Math.floor(x);
		int blockZ = (int) Math.floor(z);
		String chunk = (blockX >> 4) + " / " + (blockZ >> 4);
		String inside = (blockX & 15) + ", " + (blockZ & 15);
		String value = switch (entry.variant) {
			case 1 -> chunk;
			case 2 -> inside;
			default -> chunk + " (" + inside + ")";
		};
		return entry.labels ? HudModule.CHUNK.shortLabel + ": " + value : value;
	}

	/** Blickrichtung aus dem Drehwinkel: 0 = Süden, 90 = Westen, 180 = Norden, 270 = Osten. */
	public static String direction(float yaw) {
		HudConfig.Entry entry = entry(HudModule.DIRECTION);
		int quarter = (int) Math.floor((yaw * 4.0F / 360.0F) + 0.5) & 3;
		lastQuarter = quarter;
		String value = switch (entry.variant) {
			case 1 -> FACING_SHORT[quarter];
			case 2 -> new String[] { "Süden", "Westen", "Norden", "Osten" }[quarter];
			default -> new String[] { "Süden (+Z)", "Westen (-X)", "Norden (-Z)", "Osten (+X)" }[quarter];
		};
		return entry.labels ? HudModule.DIRECTION.shortLabel + ": " + value : value;
	}

	/**
	 * Waagerechte Geschwindigkeit in Blöcken je Sekunde. Wird aus der zurückgelegten Strecke
	 * gemessen, weil die Geschwindigkeit des Spielers auf der Spielerseite nicht verlässlich ist.
	 */
	public static String speed(double x, double z) {
		HudConfig.Entry entry = entry(HudModule.SPEED);
		long now = System.nanoTime();
		if (lastNanos == 0) {
			lastX = x;
			lastZ = z;
			lastNanos = now;
		} else if (now - lastNanos >= SPEED_INTERVAL_NANOS) {
			double stepX = x - lastX;
			double stepZ = z - lastZ;
			blocksPerSecond = Math.sqrt(stepX * stepX + stepZ * stepZ)
				/ ((now - lastNanos) / 1_000_000_000.0);
			lastX = x;
			lastZ = z;
			lastNanos = now;
		}
		METRICS.put(HudModule.SPEED, blocksPerSecond);
		boolean kmh = entry.variant == 1;
		double shown = kmh ? blocksPerSecond * 3.6 : blocksPerSecond;
		return decorate(HudModule.SPEED, entry, number(shown, entry.decimals), kmh ? "km/h" : "B/s");
	}

	/** Uhrzeit des Rechners. */
	public static String clock() {
		HudConfig.Entry entry = entry(HudModule.TIME);
		String pattern = switch (entry.variant) {
			case 1 -> "HH:mm:ss";
			case 2 -> "hh:mm a";
			default -> "HH:mm";
		};
		String value = LocalTime.now().format(DateTimeFormatter.ofPattern(pattern));
		return entry.labels ? HudModule.TIME.shortLabel + ": " + value : value;
	}

	/** Uhrzeit und Tag in der Welt; 0 Ticks sind 6 Uhr morgens. */
	public static String worldTime(long dayTime) {
		HudConfig.Entry entry = entry(HudModule.WORLD_TIME);
		long ticks = ((dayTime % 24000L) + 24000L) % 24000L;
		long hours = (ticks / 1000L + 6L) % 24L;
		long minutes = (ticks % 1000L) * 60L / 1000L;
		long day = Math.max(0L, dayTime / 24000L);
		String value = switch (entry.variant) {
			case 1 -> String.format("%02d:%02d", hours, minutes);
			case 2 -> "Tag " + day;
			default -> String.format("%02d:%02d · Tag %d", hours, minutes, day);
		};
		return entry.labels ? HudModule.WORLD_TIME.shortLabel + ": " + value : value;
	}

	/** Belegter und höchstens verfügbarer Arbeitsspeicher. */
	public static String memory() {
		HudConfig.Entry entry = entry(HudModule.MEMORY);
		Runtime runtime = Runtime.getRuntime();
		double used = (runtime.totalMemory() - runtime.freeMemory()) / GIGABYTE;
		double max = runtime.maxMemory() / GIGABYTE;
		METRICS.put(HudModule.MEMORY, used * 100.0 / Math.max(0.001, max));
		int decimals = Math.max(1, entry.decimals);
		return switch (entry.variant) {
			case 1 -> decorate(HudModule.MEMORY, entry, number(used, decimals), "GB");
			case 2 -> decorate(HudModule.MEMORY, entry, Math.round(used * 100.0 / Math.max(0.001, max)) + "%", null);
			default -> decorate(HudModule.MEMORY, entry, number(used, decimals) + " / " + number(max, decimals), "GB");
		};
	}

	/** Auslastung des Prozessors (ganzes System oder nur Minecraft); null, solange es keine Messung gibt. */
	public static String cpu() {
		HudConfig.Entry entry = entry(HudModule.CPU);
		Double value = HudSystem.cpu(entry.variant == 1);
		if (value == null) {
			METRICS.remove(HudModule.CPU);
			return null;
		}
		METRICS.put(HudModule.CPU, value);
		return decorate(HudModule.CPU, entry, Long.toString(Math.round(value)), HudModule.CPU.unit);
	}

	/** Auslastung der Grafikkarte; null, wenn sie sich auf diesem Rechner nicht messen lässt. */
	public static String gpu() {
		HudConfig.Entry entry = entry(HudModule.GPU);
		Double value = HudSystem.gpu();
		if (value == null) {
			METRICS.remove(HudModule.GPU);
			return null;
		}
		METRICS.put(HudModule.GPU, value);
		return decorate(HudModule.GPU, entry, Long.toString(Math.round(value)), HudModule.GPU.unit);
	}

	/** Klicks pro Sekunde, links und rechts. */
	public static String cps() {
		HudConfig.Entry entry = entry(HudModule.CPS);
		int left = HudInput.cps(HudInput.ATTACK);
		int right = HudInput.cps(HudInput.USE);
		String value = switch (entry.variant) {
			case 1 -> Integer.toString(left);
			case 2 -> Integer.toString(right);
			case 3 -> Integer.toString(left + right);
			default -> left + " | " + right;
		};
		return decorate(HudModule.CPS, entry, value, HudModule.CPS.unit);
	}

	/** Ein Trank- oder Statuseffekt mit Rest-Dauer in Ticks (-1 = unendlich). */
	public record EffectLine(String name, int level, int ticks) {}

	/** Aktive Effekte untereinander mit Stufe und Restzeit; null, wenn keiner wirkt. */
	public static String effects(java.util.List<EffectLine> lines) {
		if (lines == null || lines.isEmpty())
			return null;
		StringBuilder text = new StringBuilder();
		for (EffectLine line : lines) {
			if (text.length() > 0)
				text.append('\n');
			text.append(line.name());
			if (line.level() > 1)
				text.append(' ').append(roman(line.level()));
			text.append("  ").append(line.ticks() < 0 || line.ticks() > 20 * 3600 ? "--" : duration(line.ticks()));
		}
		return text.toString();
	}

	private static String duration(int ticks) {
		int seconds = (ticks + 19) / 20;
		return seconds >= 3600
			? String.format("%d:%02d:%02d", seconds / 3600, seconds / 60 % 60, seconds % 60)
			: String.format("%d:%02d", seconds / 60, seconds % 60);
	}

	private static String roman(int level) {
		String[] numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
		return level >= 1 && level <= numerals.length ? numerals[level - 1] : Integer.toString(level);
	}

	/** Lichtlevel am Standort des Spielers: Blocklicht (0-15) und Himmelslicht. */
	public static String light(int block, int sky) {
		METRICS.put(HudModule.LIGHT, (double) block);
		HudConfig.Entry entry = entry(HudModule.LIGHT);
		String value = switch (entry.variant) {
			case 1 -> "Block " + block + " / Himmel " + sky;
			case 2 -> Integer.toString(Math.max(block, sky));
			default -> Integer.toString(block);
		};
		return entry.labels ? HudModule.LIGHT.shortLabel + ": " + value : value;
	}

	/** Name des Bioms, auf Wunsch mit Beschriftung; null, wenn er sich nicht ermitteln ließ. */
	public static String biome(String name) {
		if (name == null)
			return null;
		return entry(HudModule.BIOME).labels ? HudModule.BIOME.shortLabel + ": " + name : name;
	}
}
