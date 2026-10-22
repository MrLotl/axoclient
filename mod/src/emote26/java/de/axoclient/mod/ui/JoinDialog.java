package de.axoclient.mod.ui;

import com.google.gson.JsonElement;
import com.google.gson.JsonObject;

import java.util.List;

/**
 * "Nachjoinen bestätigen" im Menü: der Launcher prüft (Ping, Version, Mods der offenen Welt) und schickt einen Plan –
 * Server wechseln, offene Welt betreten, mit einer anderen Instanz neu starten oder fehlende Mods im Launcher holen.
 */
final class JoinDialog {
	private final MenuScreen m;
	private String uuid;
	private String inviteId;
	private String name;
	private boolean open;
	private long openedAt;

	JoinDialog(MenuScreen m) {
		this.m = m;
	}

	boolean open() {
		return open;
	}

	void show(String uuid, String inviteId, String name) {
		this.uuid = uuid;
		this.inviteId = inviteId;
		this.name = name;
		this.open = true;
		this.openedAt = System.currentTimeMillis();
		MenuData.joinCheck(uuid, inviteId);
	}

	void close() {
		open = false;
	}

	private String key() {
		return inviteId != null ? inviteId : uuid;
	}

	void render(Gfx g, int wx, int wy, int ww, int wh) {
		Ui ui = m.ui;
		g.rect(wx, wy, ww, wh, 18, 0xB3141414);
		ui.hit(wx, wy, ww, wh, this::close);

		String key = key();
		JsonObject plan = MenuData.PLANS.get(key);
		String phase = MenuData.JOIN_PHASE.get(key);
		String error = MenuData.JOIN_ERROR.get(key);
		if ("done".equals(phase)) {
			close();
			m.onClose();
			return;
		}

		int w = 420;
		int dx = wx + (ww - w) / 2;
		int pad = 22;
		int inner = w - 2 * pad;

		// Höhe vorab bestimmen (Inhalt hängt vom Plan ab)
		Content c = content(g, plan, phase, error, inner);
		int h = pad * 2 + c.height;
		int dy = wy + (wh - h) / 2;
		g.shadow(dx, dy, w, h, 16, 30, 24, 0.55F);
		g.box(dx, dy, w, h, 16, Theme.CARD, Theme.WINDOW_LINE);
		ui.block(dx, dy, w, h);
		c.draw.draw(dx + pad, dy + pad, inner);
	}

	private interface Draw {
		void draw(int x, int y, int w);
	}

	private record Content(int height, Draw draw) {}

	private Content content(Gfx g, JsonObject plan, String phase, String error, int w) {
		if (error != null)
			return message(g, w, "Nachjoinen geht gerade nicht", MenuData.errorText(error), "Schließen", null);
		if (plan == null) {
			boolean slow = System.currentTimeMillis() - openedAt > 12_000;
			if (slow && !MenuData.connected())
				return message(g, w, "Keine Verbindung zum Launcher", MenuData.errorText("ingame.offline"), "Schließen", null);
			return busy(w, "Prüfe, wo " + name + " spielt …", "Server, Version und Mods werden abgeglichen");
		}
		String kind = str(plan, "kind");
		String title = str(plan, "title");
		String address = str(plan, "address");
		MenuData.Here here = MenuData.here;
		String leaving = here == null ? "deine aktuelle Welt" : here.world() ? "deine offene Welt" : here.label();
		JsonObject next = plan.has("next") && plan.get("next").isJsonObject() ? plan.getAsJsonObject("next") : null;
		JsonObject now = plan.has("now") && plan.get("now").isJsonObject() ? plan.getAsJsonObject("now") : null;

		if ("busy".equals(phase) || "launcher".equals(phase)) {
			return switch (kind == null ? "" : kind) {
				case "instance" -> busy(w, "Minecraft wird neu gestartet …",
					"Der Launcher startet „" + str(next, "name") + "“ und verbindet mit " + address);
				case "mods" -> busy(w, "Launcher ist geöffnet",
					"Installiere dort die fehlenden Mods. Danach kannst du von hier aus nachjoinen.");
				case "none" -> busy(w, "Launcher ist geöffnet", "Lege dort eine passende Instanz an.");
				case "world" -> busy(w, "Verbinde mit der Welt von " + name + " …", "Tunnel über e4mc wird aufgebaut");
				default -> busy(w, "Verbinde mit " + title + " …", "Verlasse " + leaving);
			};
		}

		String heading;
		String text;
		String confirm;
		boolean mods = "mods".equals(kind);
		switch (kind == null ? "" : kind) {
			case "world" -> {
				heading = "Welt von " + name + " betreten?";
				text = "Du verlässt " + leaving + " und betrittst die offene Welt von " + name
					+ ". Die Verbindung läuft über e4mc, deine Instanz bleibt gleich.";
				confirm = "Welt betreten";
			}
			case "instance" -> {
				heading = "Mit „" + str(next, "name") + "“ neu starten?";
				text = name + " spielt auf " + title + (str(plan, "version") != null ? " mit " + str(plan, "version") : "")
					+ ". Minecraft wird geschlossen, der Launcher startet deine Instanz „" + str(next, "name") + "“ und verbindet dich direkt.";
				confirm = "Neu starten und beitreten";
			}
			case "mods" -> {
				int n = plan.has("mods") ? plan.getAsJsonArray("mods").size() : 0;
				heading = n == 1 ? "Dir fehlt 1 Mod" : "Dir fehlen " + n + " Mods";
				text = "Die Welt von " + name + " braucht Mods, die in deiner Instanz fehlen.";
				confirm = "Im Launcher öffnen";
			}
			case "none" -> {
				String version = str(plan, "version");
				return message(g, w, "Keine passende Instanz",
					name + " spielt" + (version != null ? " mit Minecraft " + version : " mit einer anderen Version")
						+ ". Lege im Launcher eine Instanz mit dieser Version an, dann kannst du nachjoinen.",
					"Abbrechen", "Im Launcher öffnen");
			}
			default -> {
				heading = "Zu " + name + " wechseln?";
				text = "Du verlässt " + leaving + " und betrittst " + title + ". Deine Instanz bleibt gleich.";
				confirm = "Server wechseln";
			}
		}

		int textW = w - 42 - 12;
		List<String> textLines = MenuScreen.wrap(g, text, 13, Gfx.REGULAR, textW);
		List<String> headLines = MenuScreen.wrap(g, heading, 17, Gfx.SEMIBOLD, textW);
		int headerH = Math.max(42, headLines.size() * 22 + 5 + textLines.size() * 19);
		boolean card = !"instance".equals(kind);
		boolean instance = "instance".equals(kind);
		List<JsonElement> modList = mods ? plan.getAsJsonArray("mods").asList() : List.of();
		int shownMods = Math.min(4, modList.size());
		int modsH = mods ? 18 + 6 + shownMods * (44 + 6) + (modList.size() > shownMods ? 18 : 0) + 40 : 0;
		int height = headerH + 18 + (card ? 56 + 18 : 0) + (instance ? 76 + 18 : 0) + (mods ? modsH + 18 : 0) + 38;

		Draw draw = (x, y, cw) -> {
			// Kopf: Kopf des Freundes mit Abzeichen, Titel und Text
			g.head(MenuData.skin(uuid), x, y, 42, 8, Theme.CARD, false);
			int bx = x + 42 - 19 + 5, by = y + 42 - 19 + 5;
			g.dot(bx + 9, by + 9, 23, Theme.CARD);
			g.dot(bx + 9, by + 9, 19, mods ? Theme.YELLOW : Theme.GREEN_BG);
			if (mods)
				g.icon(Icons.WARN, bx + 4, by + 4, 11, 0xFF1F1F1F);
			else
				g.icon(Icons.PLAY, bx + 4.5F, by + 4.5F, 10, Theme.GREEN);
			int tx = x + 42 + 12;
			int ty = y;
			for (String line : headLines) {
				g.textMid(line, tx, ty + 11, 17, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
				ty += 22;
			}
			ty += 5;
			for (String line : textLines) {
				g.textMid(line, tx, ty + 9.5F, 13, Gfx.REGULAR, Theme.MUTED);
				ty += 19;
			}
			int at = y + headerH + 18;

			if (card) {
				g.box(x, at, cw, 56, 10, Theme.INSET, 0xFF363636);
				g.rect(x + 10, at + 10, 36, 36, 7, 0xFF333333);
				g.icon(mods || "world".equals(kind) ? Icons.CUBE : Icons.PLAY, x + 19, at + 19, 18, Theme.TEXT_DIM);
				String right = str(plan, "right");
				int rightW = right == null ? 0 : Math.round(g.width(right, 12, Gfx.SEMIBOLD)) + 10;
				float maxText = cw - 56 - 10 - rightW;
				g.textMid(g.ellipsize(title, 13.5F, Gfx.SEMIBOLD, maxText), x + 56, at + 20, 13.5F, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
				g.textMid(g.ellipsize(str(plan, "meta"), 12, Gfx.REGULAR, maxText), x + 56, at + 37, 12, Gfx.REGULAR, Theme.MUTED_2);
				if (right != null)
					g.textRight(right, x + cw - 10, at + 28, 12, Gfx.SEMIBOLD, Theme.parse(str(plan, "rightColor"), Theme.TEXT_DIM));
				at += 56 + 18;
			}
			if (instance) {
				int half = (cw - 24 - 12) / 2;
				instanceBox(g, x, at, half, "Jetzt", now, false);
				g.icon(Icons.ARROW_RIGHT, x + half + 9, at + 29, 18, Theme.ACCENT_LINE);
				instanceBox(g, x + half + 36, at, cw - half - 36, "Danach", next, true);
				at += 76 + 18;
			}
			if (mods) {
				g.textMid("Fehlt in „" + str(now, "name") + "“", x, at + 9, 12, Gfx.SEMIBOLD, Theme.MUTED);
				at += 18 + 6;
				for (int i = 0; i < shownMods; i++) {
					JsonObject mod = modList.get(i).getAsJsonObject();
					g.box(x, at, cw, 44, 9, Theme.INSET, 0xFF363636);
					g.rect(x + 10, at + 8, 28, 28, 7, 0xFF333333);
					g.icon(Icons.CUBE, x + 17, at + 15, 14, Theme.TEXT_DIM);
					String meta = str(mod, "meta");
					float metaW = meta == null ? 0 : g.width(meta, 12, Gfx.REGULAR);
					g.textMid(g.ellipsize(str(mod, "name"), 13, Gfx.MEDIUM, cw - 58 - metaW - 16), x + 48, at + 22, 13, Gfx.MEDIUM, Theme.TEXT);
					if (meta != null)
						g.textRight(meta, x + cw - 10, at + 22, 12, Gfx.REGULAR, Theme.MUTED_2);
					at += 44 + 6;
				}
				if (modList.size() > shownMods) {
					g.textMid("und " + (modList.size() - shownMods) + " weitere", x, at + 9, 12, Gfx.REGULAR, Theme.MUTED_2);
					at += 18;
				}
				MenuScreen.paragraph(g, "Mods installieren geht nur im Launcher. Er öffnet die Instanz mit den fehlenden Mods schon ausgewählt.",
					x, at, cw, 12, Gfx.REGULAR, Theme.MUTED_2, 17.4F, false);
			}
			buttons(g, x, y + height - 38, cw, "Abbrechen", confirm, () -> MenuData.join(uuid, inviteId));
		};
		return new Content(height, draw);
	}

	private void instanceBox(Gfx g, int x, int y, int w, String label, JsonObject info, boolean accent) {
		if (accent) {
			g.gradient(x, y, w, 76, 10, Theme.tint(Theme.INSET, 0.08F, 0), Theme.tint(Theme.INSET, 0.08F, 1));
			g.outline(x, y, w, 76, 10, Theme.alpha(0xFFD68CE6, 0.3F));
		} else
			g.box(x, y, w, 76, 10, Theme.INSET, 0xFF363636);
		g.rect(x + 10, y + 22, 32, 32, 7, 0xFF333333);
		g.icon(Icons.CUBE, x + 18, y + 30, 16, Theme.TEXT_DIM);
		int tx = x + 10 + 32 + 9;
		float max = w - (tx - x) - 8;
		MenuScreen.heading(g, label, tx, y + 21);
		g.textMid(g.ellipsize(str(info, "name"), 12.5F, Gfx.SEMIBOLD, max), tx, y + 38, 12.5F, Gfx.SEMIBOLD, accent ? Theme.TEXT_STRONG : Theme.TEXT_SOFT);
		String loader = str(info, "loader");
		g.textMid(g.ellipsize(str(info, "minecraft") + (loader != null ? " · " + loader : ""), 11.5F, Gfx.REGULAR, max), tx, y + 55, 11.5F,
			Gfx.REGULAR, Theme.MUTED_2);
	}

	private Content busy(int w, String title, String sub) {
		return new Content(14 + 34 + 14 + 20 + 6 + 40, (x, y, cw) -> {
			Gfx g = m.ui.g;
			MenuScreen.spinner(g, x + cw / 2, y + 12 + 17, 34);
			g.textCentered(title, x + cw / 2.0F, y + 12 + 34 + 14 + 10, 14, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
			MenuScreen.paragraph(g, sub, x, y + 12 + 34 + 14 + 20 + 6, cw, 12.5F, Gfx.REGULAR, Theme.MUTED_2, 18, true);
		});
	}

	private Content message(Gfx g, int w, String title, String text, String cancel, String action) {
		List<String> lines = MenuScreen.wrap(g, text, 13, Gfx.REGULAR, w);
		int height = 22 + 8 + lines.size() * 19 + 18 + 38;
		return new Content(height, (x, y, cw) -> {
			g.textMid(title, x, y + 11, 17, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
			MenuScreen.paragraph(g, text, x, y + 30, cw, 13, Gfx.REGULAR, Theme.MUTED, 19, false);
			buttons(g, x, y + height - 38, cw, cancel, action, action == null ? null : () -> {
				MenuData.send("openLauncher", null, null);
				close();
			});
		});
	}

	private void buttons(Gfx g, int x, int y, int w, String cancel, String confirm, Runnable action) {
		int right = x + w;
		if (confirm != null) {
			int cw = Math.round(g.width(confirm, 13, Gfx.SEMIBOLD)) + 36;
			right -= cw;
			m.primary(g, confirm, null, right, y, 38, 18, 9, 13, true, action);
			right -= 8;
		}
		int bw = Math.round(g.width(cancel, 13, Gfx.MEDIUM)) + 32;
		right -= bw;
		m.ghost(g, cancel, null, right, y, 38, 16, 9, 13, Theme.BUTTON_2, Theme.TEXT_SOFT, Gfx.MEDIUM, this::close);
	}

	private static String str(JsonObject o, String name) {
		return MenuData.str(o, name);
	}
}
