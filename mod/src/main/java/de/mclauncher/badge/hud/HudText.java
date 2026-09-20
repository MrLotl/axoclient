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
			parts.add(entry.labels ? names[i] + (entry.stacked ? ": " : " ") + value : value);
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

	/** Dreh- und Neigungswinkel in Grad, wie im Debug-Bildschirm. */
	public static String angle(float yaw, float pitch) {
		HudConfig.Entry entry = entry(HudModule.ANGLE);
		float turned = ((yaw % 360.0F) + 540.0F) % 360.0F - 180.0F;
		String value = number(turned, entry.decimals) + " / " + number(pitch, entry.decimals);
		return entry.labels ? HudModule.ANGLE.shortLabel + ": " + value : value;
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

	/** Name des Bioms, auf Wunsch mit Beschriftung; null, wenn er sich nicht ermitteln ließ. */
	public static String biome(String name) {
		if (name == null)
			return null;
		return entry(HudModule.BIOME).labels ? HudModule.BIOME.shortLabel + ": " + name : name;
	}
}
