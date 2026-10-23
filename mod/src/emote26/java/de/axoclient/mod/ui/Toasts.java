package de.axoclient.mod.ui;

import com.google.gson.JsonObject;
import de.axoclient.mod.hud.HudPrivacy;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;

import java.util.ArrayList;
import java.util.List;

/**
 * Einblendungen im Spiel (Mockup "Einblendungen"): Einladungen groß mit Hinweis aufs Axo-Menü, alles andere als
 * kleine Zeile. Position, Richtung, Dauer und Arten stellt man im Bearbeiten-Modus ein.
 */
public final class Toasts {
	record Toast(String kind, String uuid, String name, String server, String title, long created, long until) {
		boolean big() {
			return "invite".equals(kind) || "request".equals(kind);
		}
	}

	static final List<Toast> SHOWN = new ArrayList<>();
	private static final int MAX = 4;
	static final int WIDTH = 340;

	private Toasts() {}

	static void add(JsonObject data) {
		String kind = MenuData.str(data, "kind");
		if (kind == null || !MenuConfig.toastsOn || !MenuConfig.TOAST_KIND_ON.getOrDefault(kind, true))
			return;
		long now = System.currentTimeMillis();
		long seconds = "invite".equals(kind) ? 60 : "request".equals(kind) ? Math.max(20, MenuConfig.toastSeconds) : MenuConfig.toastSeconds;
		SHOWN.add(new Toast(kind, MenuData.str(data, "uuid"), MenuData.str(data, "name"), MenuData.str(data, "server"),
			MenuData.str(data, "title"), now, now + seconds * 1000));
		while (SHOWN.size() > MAX)
			SHOWN.remove(0);
	}

	/** Im Spielbild (über den Anzeigen); nicht, solange das Menü oder der Bearbeiten-Modus offen ist. */
	public static void render(Object graphics) {
		if (!(graphics instanceof GuiGraphicsExtractor g) || SHOWN.isEmpty())
			return;
		Minecraft client = Minecraft.getInstance();
		if (client.gui.screen() instanceof MenuScreen || client.gui.screen() instanceof OverlayEditScreen)
			return;
		if (MenuConfig.toastsPrivate && HudPrivacy.available())
			return; // zeichnet das private Fenster des Launchers
		long now = System.currentTimeMillis();
		SHOWN.removeIf(t -> now > t.until());
		if (SHOWN.isEmpty())
			return;
		Gfx gfx = Gfx.begin(g, g.guiWidth(), g.guiHeight());
		try {
			draw(gfx, SHOWN, now);
		} finally {
			gfx.end();
		}
	}

	/** Höhe aller Einblendungen untereinander (ohne Größe). */
	static int stackHeight(List<Toast> toasts) {
		int h = 0;
		for (Toast t : toasts)
			h += height(t) + 8;
		return Math.max(0, h - 8);
	}

	private static int height(Toast t) {
		return t.big() ? 98 : 48;
	}

	/** Linke obere Ecke des Stapels in Design-Pixeln. */
	static int[] origin(Gfx g, int w, int h) {
		float s = MenuConfig.toastScale / 100.0F;
		int sw = Math.round(w * s), sh = Math.round(h * s);
		int ax = MenuConfig.toastAnchor % 3, ay = MenuConfig.toastAnchor / 3;
		int x = ax == 0 ? MenuConfig.toastX : ax == 1 ? (g.width - sw) / 2 + MenuConfig.toastX : g.width - sw - MenuConfig.toastX;
		int y = ay == 0 ? MenuConfig.toastY : ay == 1 ? (g.height - sh) / 2 + MenuConfig.toastY : g.height - sh - MenuConfig.toastY;
		return new int[] { Math.max(0, Math.min(g.width - sw, x)), Math.max(0, Math.min(g.height - sh, y)), sw, sh };
	}

	static void draw(Gfx g, List<Toast> toasts, long now) {
		int h = stackHeight(toasts);
		int[] o = origin(g, WIDTH, h);
		float s = MenuConfig.toastScale / 100.0F;
		Gfx z = g.at(o[0], o[1], s);
		// "Nach unten": neue Einblendungen kommen unten dazu; "nach oben": oben
		List<Toast> order = new ArrayList<>(toasts);
		if (!MenuConfig.toastsDown)
			java.util.Collections.reverse(order);
		int at = 0;
		for (Toast t : order) {
			if (t.big())
				big(z, t, 0, at, now);
			else
				small(z, t, 0, at);
			at += height(t) + 8;
		}
	}

	private static void big(Gfx g, Toast t, int x, int y, long now) {
		int w = WIDTH, h = 98;
		g.shadow(x, y, w, h, 12, 18, 14, 0.5F);
		g.box(x, y, w, h, 12, Theme.alpha(0xFF222222, 0.97F), 0xFF3C3C3C);
		g.head(MenuData.skin(t.uuid()), x + 12, y + 12, 38, 7, 0xF7222222, false);
		g.dot(x + 12 + 38 - 9 + 5, y + 12 + 38 - 9 + 5, 23, 0xFF222222);
		g.gradient(x + 12 + 38 - 19 + 5, y + 12 + 38 - 19 + 5, 19, 19, 10, Theme.ACCENT_FROM, Theme.ACCENT_TO);
		g.icon("invite".equals(t.kind()) ? Icons.SEND : Icons.PLUS, x + 12 + 38 - 14.5F + 5, y + 12 + 38 - 14.5F + 5, 10, Theme.WHITE);
		int tx = x + 12 + 38 + 12;
		float max = w - (tx - x) - 12;
		String title = "invite".equals(t.kind()) ? t.name() + " lädt dich ein" : t.name() + " möchte mit dir befreundet sein";
		g.textMid(g.ellipsize(title, 13.5F, Gfx.SEMIBOLD, max), tx, y + 21, 13.5F, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
		String sub = "invite".equals(t.kind()) ? (t.server() != null ? t.server() : "Auf einen Server") : "Freundesanfrage";
		g.textMid(g.ellipsize(sub, 12.5F, Gfx.REGULAR, max), tx, y + 39, 12.5F, Gfx.REGULAR, Theme.MUTED);
		int kw = g.kbd(Keys.label(Keys.menu), tx, y + 66, 22, 11.5F, Theme.BUTTON_2, Theme.TEXT_STRONG);
		g.textMid("Im Axo-Menü annehmen", tx + kw + 7, y + 66, 12, Gfx.REGULAR, Theme.TEXT_DIM);
		// Ablaufbalken
		float left = Math.max(0, Math.min(1, (t.until() - now) / (float) (t.until() - t.created())));
		g.gradient(x + 4, y + h - 3, Math.round((w - 8) * left), 3, 1, Theme.ACCENT_FROM, Theme.ACCENT_TO);
	}

	private static void small(Gfx g, Toast t, int x, int y) {
		int w = WIDTH, h = 48;
		g.shadow(x, y, w, h, 12, 14, 10, 0.4F);
		g.box(x, y, w, h, 12, Theme.alpha(0xFF222222, 0.94F), 0xFF363636);
		g.head(MenuData.skin(t.uuid()), x + 12, y + 10, 28, 6, 0xF0222222, false);
		String name = t.name() == null ? "" : t.name();
		String rest = switch (t.kind()) {
			case "accepted" -> " hat deine Anfrage angenommen";
			case "online" -> " ist online";
			case "onServer" -> t.server() != null ? " spielt auf " + t.server() : " ist auf deinem Server";
			case "openWorld" -> " hat eine Welt zum Nachjoinen geöffnet";
			case "share" -> t.title() != null ? " hat dir „" + t.title() + "“ geschickt" : " hat dir etwas geschickt";
			default -> "";
		};
		int tx = x + 12 + 28 + 10;
		float nameW = g.width(name, 12.5F, Gfx.SEMIBOLD);
		g.textMid(name, tx, y + 24, 12.5F, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
		g.textMid(g.ellipsize(rest, 12.5F, Gfx.REGULAR, w - (tx - x) - 12 - nameW - 18), tx + nameW, y + 24, 12.5F, Gfx.REGULAR, Theme.TEXT_SOFT);
		int dot = "online".equals(t.kind()) || "onServer".equals(t.kind()) || "openWorld".equals(t.kind()) ? Theme.GREEN : 0xFFE07AD8;
		g.dot(x + w - 12 - 4, y + 24, 8, dot);
	}

	/** Beispiel-Einblendungen für den Bearbeiten-Modus. */
	static List<Toast> samples() {
		long now = System.currentTimeMillis();
		List<Toast> list = new ArrayList<>();
		String me = MenuData.meName != null ? MenuData.meName : "Mr_L0tl";
		list.add(new Toast("online", null, "Freund", null, null, now - 1000, now + 999_000));
		list.add(new Toast("invite", null, me, "play.beispiel.de", null, now - 20_000, now + 40_000));
		return list;
	}
}
