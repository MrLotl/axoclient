package de.axoclient.mod;

/**
 * Verbindung zu den Emotes, die es nur in Versionen mit Player Animation Library gibt (ab 26.2, Quelle src/emote26).
 * Gemeinsamer Code ruft nur diese Klasse auf; fehlen die Emotes, passiert nichts.
 */
public final class EmoteHooks {
	/** Jeden Tick (Taste fürs Emote-Rad, Abbruch beim Laufen); gesetzt von den Emotes. */
	public static Runnable tick;

	private EmoteHooks() {}

	/** Lädt die Emotes, falls diese Version sie enthält. */
	public static void init() {
		try {
			Class.forName("de.axoclient.mod.emote.Emotes").getMethod("init").invoke(null);
		} catch (ClassNotFoundException e) {
			// Version ohne Emotes
		} catch (ReflectiveOperationException | LinkageError e) {
			System.err.println("[AxoClient] Emotes nicht verfügbar: " + e);
		}
	}

	public static void tick() {
		Runnable hook = tick;
		if (hook != null)
			hook.run();
	}
}
