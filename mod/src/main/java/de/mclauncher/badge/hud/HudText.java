package de.mclauncher.badge.hud;

import java.time.LocalTime;
import java.time.format.DateTimeFormatter;

/** Beschriftungen der Anzeigen – für alle Minecraft-Versionen gleich. */
public final class HudText {
	private static final DateTimeFormatter CLOCK = DateTimeFormatter.ofPattern("HH:mm");
	private static final double GIGABYTE = 1024.0 * 1024.0 * 1024.0;
	/** So oft wird die Geschwindigkeit neu gemessen. */
	private static final long SPEED_INTERVAL_NANOS = 100_000_000L;

	private static double lastX;
	private static double lastZ;
	private static long lastNanos;
	private static double blocksPerSecond;

	private HudText() {}

	public static String fps(int fps) {
		return fps + " FPS";
	}

	public static String ping(Integer milliseconds) {
		return milliseconds == null ? null : milliseconds + " ms";
	}

	public static String coords(double x, double y, double z) {
		return String.format("%.0f / %.0f / %.0f", Math.floor(x), Math.floor(y), Math.floor(z));
	}

	/** Chunk-Koordinaten und die Stelle innerhalb des Chunks. */
	public static String chunk(double x, double z) {
		int blockX = (int) Math.floor(x);
		int blockZ = (int) Math.floor(z);
		return (blockX >> 4) + " / " + (blockZ >> 4) + " (" + (blockX & 15) + ", " + (blockZ & 15) + ")";
	}

	/** Blickrichtung aus dem Drehwinkel: 0 = Süden, 90 = Westen, 180 = Norden, 270 = Osten. */
	public static String direction(float yaw) {
		int quarter = (int) Math.floor((yaw * 4.0F / 360.0F) + 0.5) & 3;
		return switch (quarter) {
			case 0 -> "Süden (+Z)";
			case 1 -> "Westen (-X)";
			case 2 -> "Norden (-Z)";
			default -> "Osten (+X)";
		};
	}

	/** Dreh- und Neigungswinkel in Grad, wie im Debug-Bildschirm. */
	public static String angle(float yaw, float pitch) {
		float turned = ((yaw % 360.0F) + 540.0F) % 360.0F - 180.0F;
		return String.format("%.1f / %.1f", turned, pitch);
	}

	/**
	 * Waagerechte Geschwindigkeit in Blöcken je Sekunde. Wird aus der zurückgelegten Strecke
	 * gemessen, weil die Geschwindigkeit des Spielers auf der Spielerseite nicht verlässlich ist.
	 */
	public static String speed(double x, double z) {
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
		return String.format("%.1f B/s", blocksPerSecond);
	}

	/** Uhrzeit des Rechners. */
	public static String clock() {
		return LocalTime.now().format(CLOCK);
	}

	/** Uhrzeit und Tag in der Welt; 0 Ticks sind 6 Uhr morgens. */
	public static String worldTime(long dayTime) {
		long ticks = ((dayTime % 24000L) + 24000L) % 24000L;
		long hours = (ticks / 1000L + 6L) % 24L;
		long minutes = (ticks % 1000L) * 60L / 1000L;
		return String.format("%02d:%02d · Tag %d", hours, minutes, Math.max(0L, dayTime / 24000L));
	}

	/** Belegter und höchstens verfügbarer Arbeitsspeicher. */
	public static String memory() {
		Runtime runtime = Runtime.getRuntime();
		double used = (runtime.totalMemory() - runtime.freeMemory()) / GIGABYTE;
		return String.format("%.1f / %.1f GB", used, runtime.maxMemory() / GIGABYTE);
	}
}
