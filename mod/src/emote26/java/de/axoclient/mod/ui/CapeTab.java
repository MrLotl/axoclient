package de.axoclient.mod.ui;

import net.minecraft.client.Minecraft;
import net.minecraft.resources.Identifier;

/** Tab "Cape": links die eigene Figur mit dem gewählten Cape, rechts alle Capes zum Anlegen. */
final class CapeTab {
	private final MenuScreen m;
	private String selected;
	private boolean front;

	CapeTab(MenuScreen m) {
		this.m = m;
	}

	int render(Gfx g, int x, int y, int w, int h) {
		Ui ui = m.ui;
		if (selected == null || MenuData.CAPES.stream().noneMatch(c -> c.key().equals(selected)))
			selected = MenuData.capeWorn;

		// Vorschau
		int pw = 230;
		g.rect(x, y, pw, h, 14, Theme.PANEL);
		g.rect(x + 25, y + 60, pw - 50, pw - 50, (pw - 50) / 2, Theme.alpha(0xFF8B5CF6, 0.10F));
		g.rect(x + 50, y + 85, pw - 100, pw - 100, (pw - 100) / 2, Theme.alpha(0xFF8B5CF6, 0.08F));
		int figureH = Math.min(330, h - 70);
		figure(g, x + pw / 2, y + (h - 46 - figureH) / 2 + 4, figureH, selected, front);
		int ry = y + h - 14 - 30;
		int cx = x + pw / 2;
		arrow(g, cx - 54, ry, Icons.CHEVRON_LEFT);
		g.textCentered(front ? "Vorderseite" : "Rückseite", cx, ry + 15, 12, Gfx.REGULAR, Theme.MUTED_2);
		arrow(g, cx + 54 - 30, ry, Icons.CHEVRON_RIGHT);

		// Liste
		int rx = x + pw + 18;
		int rw = w - pw - 18;
		MenuScreen.heading(g, "Deine Capes", rx, y + 7);
		MenuData.Cape worn = find(MenuData.capeWorn);
		g.textRight("Getragen: " + (worn == null ? "Kein Cape" : worn.name()), rx + rw, y + 7, 12, Gfx.REGULAR, Theme.MUTED_2);
		int gap = 10;
		int cw = (rw - 3 * gap) / 4;
		int ch = 8 + 82 + 8 + 16 + 8;
		int gy = y + 14 + 14;
		if (!MenuData.capesLoaded) {
			if (MenuData.connected()) {
				MenuScreen.spinner(g, rx + rw / 2, gy + 60, 30);
				g.textCentered("Lade Capes …", rx + rw / 2.0F, gy + 96, 13, Gfx.REGULAR, Theme.MUTED);
			} else
				MenuScreen.paragraph(g, "Capes wechseln geht nur mit dem Launcher. Dein aktuelles Cape siehst du trotzdem.", rx, gy + 10, rw, 13,
					Gfx.REGULAR, Theme.MUTED, 19.5F, false);
		}
		int i = 0;
		for (MenuData.Cape cape : MenuData.CAPES) {
			int bx = rx + (i % 4) * (cw + gap);
			int by = gy + (i / 4) * (ch + gap);
			boolean on = cape.key().equals(selected);
			boolean hover = ui.hover(bx, by, cw, ch);
			if (on) {
				g.gradient(bx, by, cw, ch, 12, Theme.tint(Theme.CARD, 0.10F, 0), Theme.tint(Theme.CARD, 0.10F, 1));
				g.outline(bx, by, cw, ch, 12, Theme.alpha(0xFFD68CE6, 0.5F));
			} else
				g.rect(bx, by, cw, ch, 12, hover ? 0xFF323232 : Theme.CARD);
			g.rect(bx + 8, by + 8, cw - 16, 82, 8, Theme.WELL);
			if ("none".equals(cape.kind()))
				g.icon(Icons.NO_CAPE, bx + cw / 2.0F - 13, by + 8 + 28, 26, Theme.FAINT);
			else
				capeFront(g, cape.key(), bx + cw / 2, by + 8 + 6, 70);
			if (cape.key().equals(MenuData.capeWorn)) {
				g.accent(bx + cw - 8 - 5 - 18, by + 8 + 5, 18, 18, 9);
				g.icon(Icons.CHECK_BOLD, bx + cw - 8 - 5 - 14, by + 8 + 9, 10, Theme.WHITE);
			}
			g.textMid(g.ellipsize(cape.name(), 12.5F, Gfx.MEDIUM, cw - 16), bx + 8, by + 8 + 82 + 8 + 8, 12.5F, Gfx.MEDIUM, Theme.TEXT);
			ui.hit(bx, by, cw, ch, () -> selected = cape.key());
			i++;
		}
		int rows = (MenuData.CAPES.size() + 3) / 4;
		int listBottom = gy + rows * (ch + gap);

		// Unten: Hinweis und Anlegen
		int by = Math.max(listBottom + 8, y + h - 40);
		boolean same = selected == null || selected.equals(MenuData.capeWorn);
		String label = same ? "Angelegt" : "Anlegen";
		int bw = Math.round(g.width(label, 13, Gfx.SEMIBOLD)) + 40;
		m.primary(g, label, null, rx + rw - bw, by, 40, 20, 10, 13, !same && MenuData.connected(), () -> {
			MenuData.send("capeWear", "key", selected);
			MenuData.capeWorn = selected;
			MenuData.Cape c = find(selected);
			m.notice((c == null ? "Cape" : c.name()) + " angelegt", null, null);
		});
		MenuData.Cape sel = find(selected);
		String hint = sel != null && "mojang".equals(sel.kind())
			? "Mojang-Capes sehen alle Spieler. Eigene Capes erstellst und lädst du im Launcher hoch."
			: "Alle AxoClient-Spieler sehen dein Cape sofort. Eigene Capes erstellst und lädst du im Launcher hoch.";
		MenuScreen.paragraph(g, hint, rx, by + (40 - 35) / 2, rw - bw - 12, 12, Gfx.REGULAR, Theme.MUTED_2, 17.4F, false);
		return Math.max(h, by + 40 - y);
	}

	private void arrow(Gfx g, int bx, int by, Icons.Icon icon) {
		boolean hover = m.ui.hover(bx, by, 30, 30);
		g.rect(bx, by, 30, 30, 8, hover ? Theme.BUTTON_HOVER : Theme.BUTTON);
		g.icon(icon, bx + 8, by + 8, 14, Theme.TEXT_SOFT);
		m.ui.hit(bx, by, 30, 30, () -> front = !front);
	}

	private static MenuData.Cape find(String key) {
		for (MenuData.Cape c : MenuData.CAPES)
			if (c.key().equals(key))
				return c;
		return null;
	}

	/** Außenseite des Capes (10 × 16 ab Punkt 1/1 der Textur), height hoch, mittig auf cx. */
	private static void capeFront(Gfx g, String key, int cx, int top, int height) {
		Identifier texture = MenuData.capeImage(key);
		int[] size = MenuData.capeSize(key);
		if (texture == null || size == null) {
			g.rect(cx - height * 10 / 32, top, height * 10 / 16, height, 4, 0xFF333333);
			return;
		}
		int k = Math.max(1, size[0] / 64);
		float width = height * 10 / 16.0F;
		g.image(texture, cx - width / 2, top, width, height, k, k, 10 * k, 16 * k, size[0], size[1], 0xFFFFFFFF);
	}

	/** Eigene Figur von hinten (oder vorne) mit dem Cape, flach aus dem Skin gezeichnet; height = ganze Figur. */
	private static void figure(Gfx g, int cx, int top, int height, String capeKey, boolean front) {
		String uuid = Minecraft.getInstance().getUser().getProfileId().toString().replace("-", "");
		Identifier skin = MenuData.skin(uuid);
		float u = height / 32.0F;
		float left = cx - 8 * u;
		int shade = 0xFFFFFFFF;
		if (skin == null) {
			// Ohne Skin: schlichte Silhouette
			g.rect(Math.round(left + 4 * u), top, Math.round(8 * u), Math.round(8 * u), 4, 0xFF3A3A3A);
			g.rect(Math.round(left), Math.round(top + 8 * u), Math.round(16 * u), Math.round(24 * u), 6, 0xFF3A3A3A);
		} else if (!front) {
			part(g, skin, left + 4 * u, top, 8 * u, 8 * u, 24, 8, 8, 8, 56, 8);
			part(g, skin, left + 4 * u, top + 8 * u, 8 * u, 12 * u, 32, 20, 8, 12, 32, 36);
			part(g, skin, left, top + 8 * u, 4 * u, 12 * u, 44, 52, 4, 12, 60, 52);          // linker Arm (von hinten links)
			part(g, skin, left + 12 * u, top + 8 * u, 4 * u, 12 * u, 52, 20, 4, 12, 52, 36);  // rechter Arm
			part(g, skin, left + 4 * u, top + 20 * u, 4 * u, 12 * u, 28, 52, 4, 12, 12, 52);  // linkes Bein
			part(g, skin, left + 8 * u, top + 20 * u, 4 * u, 12 * u, 12, 20, 4, 12, 12, 36);  // rechtes Bein
		} else {
			part(g, skin, left + 4 * u, top, 8 * u, 8 * u, 8, 8, 8, 8, 40, 8);
			part(g, skin, left + 4 * u, top + 8 * u, 8 * u, 12 * u, 20, 20, 8, 12, 20, 36);
			part(g, skin, left, top + 8 * u, 4 * u, 12 * u, 44, 20, 4, 12, 44, 36);          // rechter Arm (von vorne links)
			part(g, skin, left + 12 * u, top + 8 * u, 4 * u, 12 * u, 36, 52, 4, 12, 52, 52);  // linker Arm
			part(g, skin, left + 4 * u, top + 20 * u, 4 * u, 12 * u, 4, 20, 4, 12, 4, 36);    // rechtes Bein
			part(g, skin, left + 8 * u, top + 20 * u, 4 * u, 12 * u, 20, 52, 4, 12, 4, 52);   // linkes Bein
		}
		if (!front && capeKey != null && !"none".equals(capeKey)) {
			Identifier cape = MenuData.capeImage(capeKey);
			int[] size = MenuData.capeSize(capeKey);
			if (cape != null && size != null) {
				int k = Math.max(1, size[0] / 64);
				g.image(cape, left + 3 * u, top + 8 * u, 10 * u, 16 * u, k, k, 10 * k, 16 * k, size[0], size[1], shade);
			}
		}
	}

	/** Ein Körperteil aus dem Skin, darüber die zweite Schicht. */
	private static void part(Gfx g, Identifier skin, float x, float y, float w, float h, int u, int v, int rw, int rh, int ou, int ov) {
		g.image(skin, x, y, w, h, u, v, rw, rh, 64, 64, 0xFFFFFFFF);
		g.image(skin, x, y, w, h, ou, ov, rw, rh, 64, 64, 0xFFFFFFFF);
	}
}
