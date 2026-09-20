package de.mclauncher.badge.hud;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.lang.management.ManagementFactory;
import java.nio.charset.Charset;
import java.util.Locale;
import java.util.concurrent.TimeUnit;

/**
 * Auslastung von Prozessor und Grafikkarte für die Anzeigen. Gemessen wird in einem Hintergrundfaden, der nur
 * läuft, solange eine der beiden Anzeigen gebraucht wird – die Abfragen der Grafikkarte starten ein kleines
 * Programm und sollen das Spiel nie ausbremsen.
 *
 * <ul>
 * <li>CPU: aus dem Betriebssystem (ganzes System oder nur dieses Spiel).</li>
 * <li>GPU: über "nvidia-smi" (NVIDIA), sonst unter Windows über den Leistungsindikator "GPU Engine". Ohne beides
 * gibt es keinen Wert.</li>
 * </ul>
 */
public final class HudSystem {
	private static final long IDLE_STOP_MILLIS = 10_000L;
	private static final long INTERVAL_MILLIS = 1500L;

	private static volatile double cpuSystem = Double.NaN;
	private static volatile double cpuProcess = Double.NaN;
	private static volatile double gpu = Double.NaN;
	private static volatile long lastRequest;
	private static Thread thread;
	/** 0 = noch unbekannt, 1 = nvidia-smi, 2 = Leistungsindikator, 3 = nichts geht. */
	private static int gpuMethod;

	private HudSystem() {}

	/** CPU-Auslastung in Prozent (0-100) oder null, solange es noch keine Messung gibt. */
	public static synchronized Double cpu(boolean gameOnly) {
		touch();
		double value = gameOnly ? cpuProcess : cpuSystem;
		return Double.isNaN(value) ? null : Math.max(0.0, Math.min(100.0, value));
	}

	/** GPU-Auslastung in Prozent (0-100) oder null, wenn sie sich nicht messen lässt. */
	public static synchronized Double gpu() {
		touch();
		return Double.isNaN(gpu) ? null : Math.max(0.0, Math.min(100.0, gpu));
	}

	private static void touch() {
		lastRequest = System.currentTimeMillis();
		if (thread != null && thread.isAlive())
			return;
		thread = new Thread(HudSystem::run, "AxoClient System");
		thread.setDaemon(true);
		thread.start();
	}

	private static void run() {
		com.sun.management.OperatingSystemMXBean os = null;
		try {
			if (ManagementFactory.getOperatingSystemMXBean() instanceof com.sun.management.OperatingSystemMXBean bean)
				os = bean;
		} catch (Throwable ignored) {
			// dann eben ohne CPU-Werte
		}
		while (System.currentTimeMillis() - lastRequest < IDLE_STOP_MILLIS) {
			if (os != null) {
				try {
					cpuSystem = os.getCpuLoad() * 100.0;
					cpuProcess = os.getProcessCpuLoad() * 100.0;
				} catch (Throwable ignored) {
					// nicht unterstützt
				}
			}
			gpu = readGpu();
			try {
				Thread.sleep(INTERVAL_MILLIS);
			} catch (InterruptedException e) {
				return;
			}
		}
	}

	private static double readGpu() {
		if (gpuMethod == 3)
			return Double.NaN;
		if (gpuMethod != 2) {
			double nvidia = nvidiaSmi();
			if (!Double.isNaN(nvidia)) {
				gpuMethod = 1;
				return nvidia;
			}
			if (gpuMethod == 1)
				return Double.NaN; // einmalige Fehlmessung
		}
		if (!System.getProperty("os.name", "").toLowerCase(Locale.ROOT).contains("win")) {
			gpuMethod = 3;
			return Double.NaN;
		}
		double counter = windowsCounter();
		if (Double.isNaN(counter)) {
			gpuMethod = gpuMethod == 2 ? 2 : 3; // schon einmal gegangen: nur diese Messung verpasst
			return Double.NaN;
		}
		gpuMethod = 2;
		return counter;
	}

	/** Größte Auslastung aller NVIDIA-Karten. */
	private static double nvidiaSmi() {
		String output = run("nvidia-smi", "--query-gpu=utilization.gpu", "--format=csv,noheader,nounits");
		if (output == null)
			return Double.NaN;
		double best = Double.NaN;
		for (String line : output.split("\\R")) {
			try {
				double value = Double.parseDouble(line.trim());
				best = Double.isNaN(best) ? value : Math.max(best, value);
			} catch (NumberFormatException ignored) {
				// Kopfzeile oder Fehlermeldung
			}
		}
		return best;
	}

	/**
	 * Windows-Leistungsindikator für alle Grafikkarten (auch AMD und Intel): Summe der 3D-Last aller Prozesse,
	 * so wie es der Task-Manager zeigt.
	 */
	private static double windowsCounter() {
		String output = run("typeperf", "\\GPU Engine(*engtype_3D)\\Utilization Percentage", "-sc", "1");
		if (output == null)
			return Double.NaN;
		String[] lines = output.split("\\R");
		String data = null;
		for (String line : lines)
			if (line.startsWith("\"") && !line.startsWith("\"(PDH"))
				data = line;
		if (data == null)
			return Double.NaN;
		double sum = 0.0;
		boolean any = false;
		String[] cells = data.split("\",\"");
		for (int i = 1; i < cells.length; i++) {
			try {
				sum += Double.parseDouble(cells[i].replace("\"", "").replace(',', '.').trim());
				any = true;
			} catch (NumberFormatException ignored) {
				// leere Zelle
			}
		}
		return any ? sum : Double.NaN;
	}

	/** Startet ein Programm und liefert seine Ausgabe, oder null bei Fehler oder nach 4 Sekunden. */
	private static String run(String... command) {
		Process process = null;
		try {
			process = new ProcessBuilder(command).redirectErrorStream(true).start();
			StringBuilder text = new StringBuilder();
			try (BufferedReader reader = new BufferedReader(new InputStreamReader(process.getInputStream(), Charset.defaultCharset()))) {
				String line;
				while ((line = reader.readLine()) != null)
					text.append(line).append('\n');
			}
			if (!process.waitFor(4, TimeUnit.SECONDS) || process.exitValue() != 0)
				return null;
			return text.toString();
		} catch (Exception e) {
			return null;
		} finally {
			if (process != null && process.isAlive())
				process.destroyForcibly();
		}
	}
}
