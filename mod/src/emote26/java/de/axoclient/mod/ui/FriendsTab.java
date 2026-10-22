package de.axoclient.mod.ui;

import de.axoclient.mod.AxoLink;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/** Tab "Freunde": Einladungen, Anfragen, Freundesliste mit Nachjoinen und Einladen; ohne Launcher ein Hinweis. */
final class FriendsTab {
	private final MenuScreen m;

	FriendsTab(MenuScreen m) {
		this.m = m;
	}

	/** Zeichnet den Tab ab (x, y) und gibt die benötigte Höhe zurück. */
	int render(Gfx g, int x, int y, int w, int h) {
		if (!MenuData.connected())
			return offline(g, x, y, w, h);
		if (!MenuData.socialLoaded) {
			MenuScreen.spinner(g, x + w / 2, y + h / 2 - 10, 34);
			g.textCentered("Lade Freunde …", x + w / 2.0F, y + h / 2.0F + 26, 13, Gfx.REGULAR, Theme.MUTED);
			return h;
		}
		if (!MenuData.available)
			return message(g, x, y, w, h, "Freunde sind gerade nicht erreichbar",
				"Der AxoClient-Dienst antwortet nicht. Overlay, Emotes und Cape funktionieren weiter.");

		int at = y;
		Ui ui = m.ui;

		// Einladungen auf einen Server oder in eine Welt
		if (!MenuData.INVITES.isEmpty()) {
			MenuScreen.heading(g, "Einladungen", x, at + 7);
			at += 14 + 8;
			for (MenuData.Invite invite : MenuData.INVITES) {
				accentRow(g, x, at, w, invite.uuid(), invite.name(), invite.name() + " lädt dich ein",
					invite.address() == null ? "Auf einen Server" : invite.address());
				int bx = x + w - 10;
				int declineW = buttonWidth(g, "Ablehnen", 12);
				bx -= declineW;
				m.ghost(g, "Ablehnen", null, bx, at + 13, 34, 12, 9, 12.5F, Theme.BUTTON, Theme.TEXT_SOFT, Gfx.MEDIUM,
					() -> MenuData.send("inviteDecline", "id", invite.id()));
				bx -= 6;
				bx -= joinWidth(g, "Beitreten");
				joinButton(g, "Beitreten", bx, at + 13, () -> m.join.show(invite.uuid(), invite.id(), invite.name()));
				at += 60 + 8;
			}
			at += 18 - 8;
		}

		// Freundesanfragen
		List<MenuData.Friend> incoming = new ArrayList<>();
		List<MenuData.Friend> list = new ArrayList<>();
		for (MenuData.Friend f : MenuData.FRIENDS)
			(f.incoming() ? incoming : list).add(f);
		if (!incoming.isEmpty()) {
			MenuScreen.heading(g, "Anfragen", x, at + 7);
			at += 14 + 8;
			for (MenuData.Friend f : incoming) {
				accentRow(g, x, at, w, f.uuid(), f.name(), f.name(), "Möchte mit dir befreundet sein");
				int bx = x + w - 10;
				bx -= buttonWidth(g, "Ablehnen", 12);
				m.ghost(g, "Ablehnen", null, bx, at + 13, 34, 12, 9, 12.5F, Theme.BUTTON, Theme.TEXT_SOFT, Gfx.MEDIUM,
					() -> MenuData.send("friendDecline", "uuid", f.uuid()));
				bx -= 6;
				int acceptW = Math.round(g.width("Annehmen", 12.5F, Gfx.SEMIBOLD)) + 28;
				bx -= acceptW;
				m.primary(g, "Annehmen", null, bx, at + 13, 34, 14, 9, 12.5F, true, () -> MenuData.send("friendAccept", "uuid", f.uuid()));
				at += 60 + 8;
			}
			at += 18 - 8;
		}

		// Freundesliste: erst im Spiel, dann im Launcher, dann offline, zuletzt gesendete Anfragen
		list.sort(Comparator.comparingInt(FriendsTab::rank).thenComparing(f -> f.name().toLowerCase(java.util.Locale.ROOT)));
		int online = 0, total = 0;
		for (MenuData.Friend f : list)
			if (!f.pending()) {
				total++;
				if (f.online())
					online++;
			}
		MenuScreen.heading(g, "Freunde", x, at + 7);
		g.textRight(online + " von " + total + " online", x + w, at + 7, 12, Gfx.REGULAR, Theme.MUTED_2);
		at += 14 + 8;
		if (list.isEmpty()) {
			g.rect(x, at, w, 60, 10, Theme.ROW);
			g.textCentered("Noch keine Freunde. Füge Freunde im Launcher hinzu.", x + w / 2.0F, at + 30, 13, Gfx.REGULAR, Theme.MUTED);
			at += 60;
		}
		for (MenuData.Friend f : list) {
			friendRow(g, x, at, w, f);
			at += 60 + 8;
		}
		return at - y;
	}

	private static int rank(MenuData.Friend f) {
		if (f.pending())
			return 3;
		return f.ingame() ? 0 : f.online() ? 1 : 2;
	}

	private void friendRow(Gfx g, int x, int y, int w, MenuData.Friend f) {
		boolean off = !f.online() || f.pending();
		g.rect(x, y, w, 60, 10, Theme.ROW);
		g.head(MenuData.skin(f.uuid()), x + 10, y + 10, 40, 7, Theme.ROW, off);

		// Knöpfe von rechts
		int bx = x + w - 10;
		MenuData.Here here = MenuData.here;
		boolean invited = MenuData.INVITED.contains(f.uuid());
		boolean canInvite = !f.pending() && f.online() && here != null && MenuData.can("social");
		boolean canJoin = !f.pending() && f.ingame() && f.join() != null && (here == null || !f.join().equalsIgnoreCase(here.address()));
		if (invited) {
			int iw = 10 + 13 + 7 + Math.round(g.width("Eingeladen", 12.5F, Gfx.SEMIBOLD)) + 12;
			bx -= iw;
			g.box(bx, y + 13, iw, 34, 9, Theme.alpha(0xFFEC4899, 0.10F), Theme.alpha(0xFFD68CE6, 0.28F));
			g.icon(Icons.CHECK, bx + 10, y + 23.5F, 13, Theme.ACCENT_TEXT);
			g.textMid("Eingeladen", bx + 30, y + 30, 12.5F, Gfx.SEMIBOLD, Theme.ACCENT_TEXT);
			bx -= 6;
		} else if (canInvite) {
			int iw = 10 + 13 + 7 + Math.round(g.width("Einladen", 12.5F, Gfx.MEDIUM)) + 12;
			bx -= iw;
			boolean hover = m.ui.hover(bx, y + 13, iw, 34);
			g.rect(bx, y + 13, iw, 34, 9, hover ? 0xFF444444 : Theme.BUTTON_HOVER);
			g.icon(Icons.SEND, bx + 10, y + 23.5F, 13, Theme.TEXT);
			g.textMid("Einladen", bx + 30, y + 30, 12.5F, Gfx.MEDIUM, Theme.TEXT);
			m.ui.hit(bx, y + 13, iw, 34, () -> invite(f));
			bx -= 6;
		}
		if (canJoin) {
			bx -= joinWidth(g, "Nachjoinen");
			joinButton(g, "Nachjoinen", bx, y + 13, () -> m.join.show(f.uuid(), null, f.name()));
			bx -= 6;
		}

		int textX = x + 62;
		float maxText = bx - textX - 6;
		g.textMid(g.ellipsize(f.name(), 14, Gfx.MEDIUM, maxText), textX, y + 21, 14, Gfx.MEDIUM, off ? 0xFF9A9A9A : Theme.TEXT);
		int dot = f.pending() ? Theme.FAINT : f.ingame() ? Theme.BLUE : f.online() ? Theme.GREEN : Theme.OFFLINE;
		g.dot(textX + 4, y + 40, 8, dot);
		g.textMid(g.ellipsize(MenuData.status(f), 12, Gfx.REGULAR, maxText - 14), textX + 14, y + 40, 12, Gfx.REGULAR, Theme.MUTED);
	}

	private void invite(MenuData.Friend f) {
		if (!AxoLink.send("invite", uuid(f.uuid()))) {
			m.notice(MenuData.errorText("ingame.offline"), null, null);
			return;
		}
		MenuData.INVITED.add(f.uuid());
		m.notice("Einladung an " + f.name() + " gesendet", "Zurückziehen", () -> {
			MenuData.INVITED.remove(f.uuid());
			AxoLink.send("inviteUndo", uuid(f.uuid()));
		});
	}

	private static com.google.gson.JsonObject uuid(String uuid) {
		com.google.gson.JsonObject data = new com.google.gson.JsonObject();
		data.addProperty("uuid", uuid);
		return data;
	}

	/** Zeile mit zartem Akzent (Anfragen, Einladungen). */
	private void accentRow(Gfx g, int x, int y, int w, String uuid, String name, String title, String sub) {
		int bg = Theme.tint(0xFF2E2E2E, 0.08F, 0.5F);
		g.gradient(x, y, w, 60, 10, Theme.tint(0xFF2E2E2E, 0.08F, 0), Theme.tint(0xFF2E2E2E, 0.08F, 1));
		g.outline(x, y, w, 60, 10, Theme.alpha(0xFFD68CE6, 0.22F));
		g.head(MenuData.skin(uuid), x + 10, y + 10, 40, 7, bg, false);
		g.textMid(g.ellipsize(title, 14, Gfx.SEMIBOLD, w - 320), x + 62, y + 22, 14, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
		g.textMid(g.ellipsize(sub, 12, Gfx.REGULAR, w - 320), x + 62, y + 40, 12, Gfx.REGULAR, Theme.MUTED);
	}

	private static int buttonWidth(Gfx g, String label, int padX) {
		return Math.round(g.width(label, 12.5F, Gfx.MEDIUM)) + 2 * padX;
	}

	private static int joinWidth(Gfx g, String label) {
		return 10 + 13 + 7 + Math.round(g.width(label, 12.5F, Gfx.SEMIBOLD)) + 12;
	}

	/** Grüner Knopf "Nachjoinen" mit Abspielen-Symbol. */
	private void joinButton(Gfx g, String label, int bx, int by, Runnable action) {
		int w = joinWidth(g, label);
		boolean hover = m.ui.hover(bx, by, w, 34);
		g.rect(bx, by, w, 34, 9, hover ? Theme.GREEN_BG_HOVER : Theme.GREEN_BG);
		g.icon(Icons.PLAY, bx + 10, by + 10.5F, 13, Theme.GREEN);
		g.textMid(label, bx + 30, by + 17, 12.5F, Gfx.SEMIBOLD, Theme.GREEN);
		m.ui.hit(bx, by, w, 34, action);
	}

	/** Ohne Launcher: Hinweis mit "Erneut verbinden". */
	private int offline(Gfx g, int x, int y, int w, int h) {
		int cx = x + w / 2;
		int block = 56 + 14 + 24 + 6 + 60 + 18 + 36;
		int top = y + Math.max(0, (h - block) / 2);
		g.rect(cx - 28, top, 56, 56, 16, Theme.alpha(Theme.YELLOW, 0.12F));
		g.icon(Icons.OFFLINE, cx - 13, top + 15, 26, Theme.YELLOW_TEXT);
		int at = top + 56 + 14;
		g.textCentered("Keine Verbindung zum Launcher", cx, at + 11, 17, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
		at += 22 + 6;
		String text = AxoLink.configured()
			? "Freunde, Anfragen und Einladungen laufen über AxoClient. Öffne den Launcher, dann verbindet sich das Menü von selbst. Overlay und Cape funktionieren weiter."
			: "Dieses Spiel wurde nicht vom AxoClient-Launcher gestartet. Freunde, Einladungen und eigene Emotes gibt es nur mit dem Launcher.";
		at += MenuScreen.paragraph(g, text, cx - 200, at, 400, 13, Gfx.REGULAR, Theme.MUTED, 19.5F, true);
		at += 18;
		if (AxoLink.configured()) {
			boolean busy = System.currentTimeMillis() < m.retryingUntil;
			String label = busy ? "Verbinde …" : "Erneut verbinden";
			String after = "Neuer Versuch in " + Math.max(1, MenuScreen.retryInSeconds()) + " s";
			int bw = 14 + 14 + 8 + Math.round(g.width(label, 13, Gfx.MEDIUM)) + 14;
			int total = bw + 10 + Math.round(g.width(after, 12, Gfx.REGULAR));
			int bx = cx - total / 2;
			boolean hover = m.ui.hover(bx, at, bw, 36);
			g.rect(bx, at, bw, 36, 9, hover ? Theme.BUTTON_HOVER : Theme.BUTTON);
			if (busy)
				MenuScreen.spinner(g, bx + 21, at + 18, 14);
			else
				g.icon(Icons.RETRY, bx + 14, at + 11, 14, Theme.TEXT);
			g.textMid(label, bx + 36, at + 18, 13, Gfx.MEDIUM, Theme.TEXT);
			m.ui.hit(bx, at, bw, 36, () -> {
				m.retryingUntil = System.currentTimeMillis() + 1600;
				AxoLink.reconnectNow();
			});
			if (!busy && MenuScreen.retryInSeconds() > 0)
				g.textMid(after, bx + bw + 10, at + 18, 12, Gfx.REGULAR, Theme.MUTED_3);
			at += 36;
		}
		return Math.max(h, at - y);
	}

	private int message(Gfx g, int x, int y, int w, int h, String title, String text) {
		int cx = x + w / 2;
		int top = y + h / 2 - 60;
		g.rect(cx - 28, top, 56, 56, 16, Theme.alpha(0xFF8B5CF6, 0.14F));
		g.icon(Icons.OFFLINE, cx - 13, top + 15, 26, Theme.ACCENT_LINE);
		g.textCentered(title, cx, top + 56 + 25, 17, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
		MenuScreen.paragraph(g, text, cx - 200, top + 56 + 42, 400, 13, Gfx.REGULAR, Theme.MUTED, 19.5F, true);
		return h;
	}
}
