package de.mclauncher.badge.hud;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;

import java.io.RandomAccessFile;

import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Der Privatsphäre-Modus: Anzeigen, die als privat markiert sind, zeichnet das Spiel nicht selbst.
 * Stattdessen gehen ihre Zeichenbefehle an den Launcher, der sie in einem eigenen Fenster darstellt -
 * und dieses Fenster hält Windows aus jeder Bildschirmaufnahme heraus (Discord, OBS und Co.).
 *
 * <p>Geht die Verbindung zum Launcher nicht (er läuft nicht, oder das Spiel wurde von Hand gestartet),
 * bleiben die privaten Anzeigen aus. Sichtbar werden sie dann nicht: lieber gar nicht zeigen als
 * versehentlich mitstreamen.
 */
public final class HudPrivacy {
	/** Dieselbe Leitung wie im Launcher (PrivacyLink.PipeName). */
	private static final String PIPE = "\\\\.\\pipe\\axoclient-privathud";

	/** Höchstens so viele Bilder je Sekunde; mehr sieht man ohnehin nicht. */
	private static final long MIN_GAP_MS = 33;

	/** Nach einem Fehlschlag erst nach dieser Zeit wieder versuchen. */
	private static final long RETRY_MS = 3000;

	private static volatile RandomAccessFile pipe;
	private static long lastSend;
	private static long nextTry;
	private static boolean warned;

	/** Das zuletzt fertige Bild, das noch auf die Leitung wartet (ältere werden übersprungen). */
	private static final java.util.concurrent.atomic.AtomicReference<String> PENDING =
		new java.util.concurrent.atomic.AtomicReference<>();

	private static Thread sender;

	private HudPrivacy() {}

	/** Ob das Fenster des Launchers gerade etwas zeigt (dann muss es beim Wegfallen geleert werden). */
	private static boolean showing;

	/** Zeitpunkt des letzten Bildes, das das Anzeigen-Menü gezeichnet hat. */
	private static volatile long editorFrame;

	/** Vom Menü bei jedem Bild aufgerufen: solange es offen ist, zeichnet es alle Anzeigen selbst. */
	public static void editorShown() {
		editorFrame = System.currentTimeMillis();
	}

	/**
	 * Ob das Menü gerade offen ist. Dann bleibt das Fenster des Launchers leer, sonst stünden private
	 * Anzeigen doppelt im Bild (und beim Verschieben versetzt). Über die Zeit statt über ein Schließen-
	 * Ereignis, damit es auch stimmt, wenn das Menü auf anderem Weg verschwindet.
	 */
	public static boolean editorOpen() {
		return System.currentTimeMillis() - editorFrame < 250;
	}

	/** Ob der Launcher gerade zuhört - nur dann lassen sich Anzeigen verstecken. */
	public static boolean available() {
		return pipe != null;
	}

	/**
	 * Ob sich diese Anzeige überhaupt aus dem Bild nehmen lässt. Alles, was aus Text und Flächen besteht,
	 * kann der Launcher nachzeichnen; Gegenstände (Ausrüstung), die Effekt-Symbole und die Seitenleiste
	 * des Servers zeichnet Minecraft selbst - die bleiben im Spielbild.
	 */
	public static boolean canHide(HudConfig config, HudModule module) {
		if (module.equipment || module == HudModule.SCOREBOARD)
			return false;
		return module != HudModule.EFFECTS || config.get(module).variant == 1; // Symbole nein, Text ja
	}

	/** Der Grund, warum eine Anzeige nicht privat sein kann (für den Editor), sonst null. */
	public static String reasonAgainst(HudConfig config, HudModule module) {
		if (module.equipment)
			return "Gegenstände zeichnet Minecraft selbst";
		if (module == HudModule.SCOREBOARD)
			return "Die Seitenleiste zeichnet Minecraft selbst";
		if (module == HudModule.EFFECTS && config.get(module).variant == 0)
			return "Als Symbole nicht möglich, als Text schon";
		return null;
	}

	/**
	 * Zeichnet die privaten Anzeigen in eine Befehlsliste und schickt sie zum Launcher.
	 *
	 * @param secret die Anzeigen, die nicht ins Spielbild gehören
	 */
	public static void send(HudConfig config, List<HudElement> secret, HudPainter measure,
						   Map<HudModule, String> texts, int width, int height) {
		// Nichts Privates und das Fenster ist schon leer: gar nicht erst mit dem Launcher reden
		if (secret.isEmpty() && !showing)
			return;
		long now = System.currentTimeMillis();
		if (now - lastSend < MIN_GAP_MS)
			return;
		lastSend = now;
		if (!connect(now))
			return;

		Recorder recorder = new Recorder(measure);
		HudOverlay.drawGroups(config, secret, recorder, texts, width, height);
		for (HudElement element : secret)
			HudOverlay.draw(config, element, recorder, texts,
				HudOverlay.box(config, element, measure, texts, width, height));

		JsonObject frame = new JsonObject();
		frame.addProperty("pid", ProcessHandle.current().pid());
		frame.addProperty("guiWidth", width);
		frame.addProperty("guiHeight", height);
		frame.add("cmds", recorder.commands);
		write(frame.toString());
		// Nur Wechsel protokollieren, nicht jedes Bild: so sieht man im Log, ob Privates überhaupt ankommt
		if (showing != !secret.isEmpty())
			System.out.println("[AxoClient] Privates Overlay: " + (secret.isEmpty() ? "leer"
				: secret.size() + " Anzeige(n), " + recorder.commands.size() + " Befehle an den Launcher"));
		showing = !secret.isEmpty();
	}

	/** Verbindet sich mit dem Launcher, wenn nötig; false, wenn gerade keine Leitung steht. */
	private static boolean connect(long now) {
		if (pipe != null)
			return true;
		if (now < nextTry)
			return false;
		nextTry = now + RETRY_MS;
		try {
			// Named Pipes muss der Client mit "rw" öffnen; der Launcher liest nur, was hier hineingeht
			pipe = new RandomAccessFile(PIPE, "rw");
			warned = false;
			System.out.println("[AxoClient] Privates Overlay: mit dem Launcher verbunden");
			startSender();
			return true;
		} catch (Exception e) {
			if (!warned) {
				warned = true; // einmal genügt, sonst steht es bei jedem Versuch im Protokoll
				System.out.println("[AxoClient] Privates Overlay: Launcher antwortet nicht (" + e + ")");
			}
			return false;
		}
	}

	/**
	 * Das Schreiben läuft in einem eigenen Thread: hängt der Launcher, wartet hier niemand - das Spiel
	 * würde sonst bei jedem Bild stocken. Wartende Bilder werden überschrieben, es zählt nur das neueste.
	 */
	private static void startSender() {
		if (sender != null && sender.isAlive())
			return;
		sender = new Thread(() -> {
			while (pipe != null) {
				String line = PENDING.getAndSet(null);
				if (line == null) {
					try {
						Thread.sleep(5);
					} catch (InterruptedException interrupted) {
						return;
					}
					continue;
				}
				RandomAccessFile open = pipe;
				if (open == null)
					return;
				try {
					open.write(line.getBytes(StandardCharsets.UTF_8));
				} catch (Exception e) {
					System.out.println("[AxoClient] Privates Overlay: Verbindung verloren (" + e + ")");
					close();
					return;
				}
			}
		}, "AxoClient-Privatoverlay");
		sender.setDaemon(true);
		sender.start();
	}

	private static void write(String line) {
		PENDING.set(line + "\n");
	}

	/** Beim Verlassen der Welt oder bei einem Fehler: die Leitung schließen. */
	public static void close() {
		RandomAccessFile open = pipe;
		pipe = null;
		PENDING.set(null);
		if (open == null)
			return;
		try {
			open.close();
		} catch (Exception e) {
			// Leitung war ohnehin hin
		}
	}

	/**
	 * Ein Maler, der nichts zeichnet, sondern mitschreibt. Gemessen (Textbreiten, Ausrüstung) wird
	 * weiter über den echten Maler des Spiels, damit alles genauso angeordnet ist wie im Bild.
	 */
	private static final class Recorder implements HudPainter {
		private final HudPainter measure;
		private final JsonArray commands = new JsonArray();

		/** Aktuelle Umrechnung: Punkt -> Punkt * scale + offset (je Achse). */
		private float scale = 1;
		private float offsetX;
		private float offsetY;
		private final List<float[]> stack = new ArrayList<>();

		private Recorder(HudPainter measure) {
			this.measure = measure;
		}

		private float mapX(float x) {
			return x * scale + offsetX;
		}

		private float mapY(float y) {
			return y * scale + offsetY;
		}

		@Override
		public int textWidth(String text) {
			return measure.textWidth(text);
		}

		@Override
		public void text(String text, int x, int y, int argb, boolean shadow) {
			if (text == null || text.isEmpty())
				return;
			JsonObject command = new JsonObject();
			command.addProperty("t", "t");
			command.addProperty("x", mapX(x));
			command.addProperty("y", mapY(y));
			command.addProperty("w", measure.textWidth(text));
			command.addProperty("s", scale);
			command.addProperty("c", argb & 0xFFFFFFFFL);
			command.addProperty("sh", shadow);
			command.addProperty("v", text);
			commands.add(command);
		}

		@Override
		public void fill(int x, int y, int width, int height, int argb) {
			JsonObject command = new JsonObject();
			command.addProperty("t", "r");
			command.addProperty("x", mapX(x));
			command.addProperty("y", mapY(y));
			command.addProperty("w", width);
			command.addProperty("h", height);
			command.addProperty("s", scale);
			command.addProperty("r", 0);
			command.addProperty("c", argb & 0xFFFFFFFFL);
			commands.add(command);
		}

		@Override
		public void push(int x, int y, float factor) {
			stack.add(new float[] { scale, offsetX, offsetY });
			// Innen gilt: Q -> (Q - x) * factor + x, außen das bisherige P -> P * scale + offset
			offsetX = scale * x * (1 - factor) + offsetX;
			offsetY = scale * y * (1 - factor) + offsetY;
			scale = scale * factor;
		}

		@Override
		public void pop() {
			if (stack.isEmpty())
				return;
			float[] saved = stack.remove(stack.size() - 1);
			scale = saved[0];
			offsetX = saved[1];
			offsetY = saved[2];
		}

		@Override
		public boolean hasEquipment(int slot) {
			return measure.hasEquipment(slot);
		}

		@Override
		public void equipment(int slot, int x, int y) {
			// Gegenstände kann der Launcher nicht zeichnen; solche Anzeigen sind gar nicht erst privat
		}

		@Override
		public String durability(int slot) {
			return measure.durability(slot);
		}

		@Override
		public int[] effectsSize() {
			return measure.effectsSize();
		}

		@Override
		public int[] scoreboardSize() {
			return measure.scoreboardSize();
		}
	}
}
