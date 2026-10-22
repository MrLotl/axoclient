package de.axoclient.mod.ui;

import de.axoclient.mod.emote.Emotes;

import java.util.List;

/** Tab "Emotes": links das eigene Rad mit 8 Plätzen, rechts alle Emotes zum Belegen. */
final class EmotesTab {
	private final MenuScreen m;
	private int slot = firstEmpty();

	EmotesTab(MenuScreen m) {
		this.m = m;
	}

	private static int firstEmpty() {
		for (int i = 0; i < 8; i++)
			if (MenuData.SLOTS.get(i) == null)
				return i;
		return 0;
	}

	int render(Gfx g, int x, int y, int w, int h) {
		Ui ui = m.ui;
		List<Emotes.Emote> all = Emotes.available();

		// Linke Karte: das Rad
		int pw = 270;
		g.rect(x, y, pw, h, 14, Theme.PANEL);
		int px = x + 14;
		int pin = pw - 28;
		MenuScreen.heading(g, "Dein Emote-Rad", px, y + 14 + 7);
		int filled = 0;
		for (String id : MenuData.SLOTS)
			if (id != null)
				filled++;
		g.textRight(filled + " / 8", px + pin, y + 14 + 7, 12, Gfx.REGULAR, Theme.MUTED_2);

		int box = 240;
		int bx = x + (pw - box) / 2;
		int by = y + 14 + 14 + 12;
		int ccx = bx + box / 2, ccy = by + box / 2;
		g.rect(bx + 22, by + 22, box - 44, box - 44, (box - 44) / 2, Theme.alpha(0xFF8B5CF6, 0.10F));
		g.outline(bx + 22, by + 22, box - 44, box - 44, (box - 44) / 2, Theme.WINDOW_LINE);
		for (int i = 0; i < 8; i++) {
			double a = i * Math.PI / 4;
			int sx = (int) Math.round(ccx + 96 * Math.sin(a)) - 25;
			int sy = (int) Math.round(ccy - 96 * Math.cos(a)) - 25;
			String id = MenuData.SLOTS.get(i);
			Emotes.Emote e = id == null ? null : Emotes.find(id);
			boolean on = slot == i;
			boolean hover = ui.hover(sx, sy, 50, 50);
			if (on) {
				g.gradient(sx, sy, 50, 50, 25, Theme.tint(Theme.CARD, 0.22F, 0), Theme.tint(Theme.CARD, 0.22F, 1));
				g.outline(sx, sy, 50, 50, 25, Theme.alpha(0xFFD68CE6, 0.7F));
			} else if (e != null)
				g.rect(sx, sy, 50, 50, 25, hover ? 0xFF3A3A3A : 0xFF333333);
			else
				g.outline(sx, sy, 50, 50, 25, hover ? 0xFF555555 : Theme.OUTLINE);
			if (e != null)
				g.icon(Icons.emote(e.id()), sx + 12, sy + 12, 26, on ? Theme.ACCENT_TEXT : Theme.TEXT_SOFT);
			else
				g.icon(Icons.PLUS, sx + 17, sy + 17, 16, on ? Theme.ACCENT_TEXT : Theme.FAINT);
			int index = i;
			ui.hit(sx, sy, 50, 50, () -> slot = index);
		}
		// Mitte: gewählter Platz
		g.rect(ccx - 45, ccy - 45, 90, 90, 45, Theme.INSET);
		g.outline(ccx - 45, ccy - 45, 90, 90, 45, Theme.WINDOW_LINE);
		String selId = MenuData.SLOTS.get(slot);
		Emotes.Emote sel = selId == null ? null : Emotes.find(selId);
		String label = "PLATZ " + (slot + 1);
		g.textCentered(label, ccx, ccy - 9, 10.5F, Gfx.SEMIBOLD, Theme.MUTED_3);
		g.textCentered(g.ellipsize(sel == null ? "leer" : sel.label(), 12.5F, Gfx.SEMIBOLD, 80), ccx, ccy + 8, 12.5F, Gfx.SEMIBOLD,
			Theme.TEXT_STRONG);

		int ty = by + box + 12;
		ty += MenuScreen.paragraph(g, "Platz wählen, dann rechts ein Emote antippen.", px, ty, pin, 12, Gfx.REGULAR, Theme.MUTED_2, 17.4F, true);
		ty += 12;
		int clearW = Math.round(g.width("Platz leeren", 12, Gfx.MEDIUM)) + 24;
		m.ghost(g, "Platz leeren", null, x + (pw - clearW) / 2, ty, 30, 12, 8, 12, Theme.BUTTON, Theme.TEXT_SOFT, Gfx.MEDIUM,
			sel == null ? null : () -> {
				MenuData.SLOTS.set(slot, null);
				saveSlots();
			});

		// Rechts: alle Emotes
		int rx = x + pw + 18;
		int rw = w - pw - 18;
		MenuScreen.heading(g, "Verfügbare Emotes", rx, y + 7);
		int imported = 0;
		for (Emotes.Emote e : all)
			if (e.imported())
				imported++;
		g.textRight(all.size() + " Emotes · " + imported + " importiert", rx + rw, y + 7, 12, Gfx.REGULAR, Theme.MUTED_2);
		int gap = 8;
		int cw = (rw - 3 * gap) / 4;
		int ch = 6 + 52 + 6 + 34;
		int gy = y + 14 + 12;
		for (int i = 0; i < all.size(); i++) {
			Emotes.Emote e = all.get(i);
			int cx = rx + (i % 4) * (cw + gap);
			int cy = gy + (i / 4) * (ch + gap);
			int inSlot = MenuData.SLOTS.indexOf(e.id());
			boolean picked = sel != null && sel.id().equals(e.id());
			boolean hover = ui.hover(cx, cy, cw, ch);
			if (picked) {
				g.gradient(cx, cy, cw, ch, 10, Theme.tint(Theme.CARD, 0.10F, 0), Theme.tint(Theme.CARD, 0.10F, 1));
				g.outline(cx, cy, cw, ch, 10, Theme.alpha(0xFFD68CE6, 0.5F));
			} else
				g.rect(cx, cy, cw, ch, 10, hover ? 0xFF323232 : Theme.CARD);
			g.rect(cx + 6, cy + 6, cw - 12, 52, 8, Theme.WELL);
			g.icon(Icons.emote(e.id()), cx + cw / 2.0F - 16, cy + 6 + 10, 32, Theme.ACCENT_LINE);
			if (inSlot >= 0) {
				String n = String.valueOf(inSlot + 1);
				int bw = Math.max(16, Math.round(g.width(n, 9.5F, Gfx.BOLD)) + 8);
				g.accent(cx + cw - 6 - 4 - bw, cy + 6 + 4, bw, 16, 8);
				g.textCentered(n, cx + cw - 10 - bw / 2.0F, cy + 18, 9.5F, Gfx.BOLD, Theme.WHITE);
			}
			g.textMid(g.ellipsize(e.label(), 12.5F, Gfx.MEDIUM, cw - 12), cx + 6, cy + 6 + 52 + 6 + 9, 12.5F, Gfx.MEDIUM, Theme.TEXT);
			g.textMid(e.imported() ? "Importiert" : "AxoClient", cx + 6, cy + 6 + 52 + 6 + 25, 10.5F, Gfx.REGULAR, Theme.MUTED_3);
			ui.hit(cx, cy, cw, ch, () -> put(e.id()));
		}
		int rows = (all.size() + 3) / 4;
		int gridBottom = gy + rows * (ch + gap) - gap;

		// Unten: Hinweis zur Taste
		int hintY = Math.max(gridBottom + 16, y + h - 10);
		int kw = g.kbd(Keys.label(Keys.wheel), rx, hintY, 20, 11, Theme.KBD, Theme.TEXT_SOFT);
		g.textMid("halten öffnet das Rad · eigene Emotes importierst du im Launcher", rx + kw + 8, hintY, 12, Gfx.REGULAR, Theme.MUTED_2);
		return Math.max(h, hintY + 10 - y);
	}

	/** Emote auf den gewählten Platz legen; liegt es schon woanders, tauschen die beiden. */
	private void put(String id) {
		int old = MenuData.SLOTS.indexOf(id);
		if (old >= 0)
			MenuData.SLOTS.set(old, MenuData.SLOTS.get(slot));
		MenuData.SLOTS.set(slot, id);
		saveSlots();
	}

	private static void saveSlots() {
		MenuConfig.save();
		MenuData.sendSlots();
	}
}
