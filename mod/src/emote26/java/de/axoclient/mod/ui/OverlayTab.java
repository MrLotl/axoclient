package de.axoclient.mod.ui;

import de.axoclient.mod.AxoHud;
import de.axoclient.mod.hud.HudConfig;
import de.axoclient.mod.hud.HudModule;
import de.axoclient.mod.hud.HudPrivacy;
import de.axoclient.mod.hud.HudProfiles;
import net.minecraft.client.Minecraft;

import java.util.List;

/** Tab "Overlay": Profil, alle Widgets mit Schalter und "Im Spiel / Privat", Knopf in den Bearbeiten-Modus. */
final class OverlayTab {
	static final String[] ANCHOR_NAMES = { "oben links", "oben Mitte", "oben rechts", "Mitte links", "Mitte", "Mitte rechts",
		"unten links", "unten Mitte", "unten rechts" };

	private final MenuScreen m;
	private boolean dirty;

	OverlayTab(MenuScreen m) {
		this.m = m;
	}

	int render(Gfx g, int x, int y, int w, int h) {
		Ui ui = m.ui;
		HudConfig config = AxoHud.config();

		// Profile
		int cy = y + 15;
		g.textMid("Profil", x, cy, 12, Gfx.SEMIBOLD, Theme.MUTED);
		int px = x + Math.round(g.width("Profil", 12, Gfx.SEMIBOLD)) + 10;
		List<String> profiles = HudProfiles.list(config);
		for (String name : profiles) {
			boolean on = name.equals(config.activeProfile);
			int weight = on ? Gfx.SEMIBOLD : Gfx.MEDIUM;
			int cw = Math.round(g.width(name, 12.5F, weight)) + 24;
			boolean hover = ui.hover(px, cy - 15, cw, 30);
			if (on) {
				g.gradient(px, cy - 15, cw, 30, 8, Theme.alpha(0xFFEC4899, 0.16F), Theme.alpha(0xFF8B5CF6, 0.16F));
				g.outline(px, cy - 15, cw, 30, 8, Theme.alpha(0xFFD68CE6, 0.3F));
			} else
				g.rect(px, cy - 15, cw, 30, 8, hover ? 0xFF3A3A3A : 0xFF333333);
			g.textMid(name, px + 12, cy, 12.5F, weight, on ? Theme.ACCENT_TEXT : Theme.TEXT_DIM);
			ui.hit(px, cy - 15, cw, 30, () -> {
				HudProfiles.load(config, name);
				config.save();
			});
			px += cw + 6;
		}
		if (profiles.size() < 8) {
			boolean hover = ui.hover(px, cy - 15, 30, 30);
			g.rect(px, cy - 15, 30, 30, 8, hover ? 0xFF3A3A3A : 0xFF333333);
			g.icon(Icons.PLUS, px + 8, cy - 7, 14, Theme.TEXT_DIM);
			ui.hit(px, cy - 15, 30, 30, () -> {
				String name = HudProfiles.freeName(config);
				HudProfiles.save(config, name);
				config.save();
				m.notice("Profil „" + name + "“ angelegt", null, null);
			});
		}
		// Hinweis rechts, nur so lang, wie neben den Profilen Platz ist
		int free = profiles.size() < 8 ? px + 30 + 16 : px + 10;
		for (String note : new String[] { "Privat = nur du siehst es, nicht Discord oder OBS", "Privat = nur du siehst es", "Privat = nur du" }) {
			float nw = g.width(note, 11.5F, Gfx.REGULAR);
			if (x + w - nw - 19 < free)
				continue;
			g.icon(Icons.EYE_OFF, x + w - nw - 19, cy - 6.5F, 13, Theme.MUTED_2);
			g.textMid(note, x + w - nw, cy, 11.5F, Gfx.REGULAR, Theme.MUTED_2);
			break;
		}

		// Widgets
		int at = y + 30 + 16;
		boolean privacy = HudPrivacy.available();
		for (HudModule module : HudModule.values()) {
			HudConfig.Entry entry = config.get(module);
			row(g, x, at, w, module.label, ANCHOR_NAMES[Math.max(0, Math.min(8, entry.anchorY * 3 + entry.anchorX))], entry.enabled,
				entry.privat && privacy, privacy, () -> {
					entry.enabled = !entry.enabled;
					dirty = true;
				}, priv -> {
					if (priv && !privacy) {
						m.notice("Privat braucht das Overlay-Fenster des Launchers – noch nicht verfügbar", null, null);
						return;
					}
					String reason = priv ? HudPrivacy.reasonAgainst(config, module) : null;
					if (reason != null) {
						m.notice(reason, null, null);
						return;
					}
					entry.privat = priv;
					dirty = true;
				});
			at += 40 + 4;
		}
		// Einblendungen sind ein eigenes Widget des Menüs
		row(g, x, at, w, "Einblendungen", ANCHOR_NAMES[MenuConfig.toastAnchor], MenuConfig.toastsOn, MenuConfig.toastsPrivate && privacy, privacy,
			() -> {
				MenuConfig.toastsOn = !MenuConfig.toastsOn;
				MenuConfig.save();
			}, priv -> {
				if (priv && !privacy) {
					m.notice("Privat braucht das Overlay-Fenster des Launchers – noch nicht verfügbar", null, null);
					return;
				}
				MenuConfig.toastsPrivate = priv;
				MenuConfig.save();
			});
		at += 40 + 16;

		// In den Bearbeiten-Modus
		boolean hover = ui.hover(x, at, w, 48);
		g.shadow(x, at, w, 48, 11, 10, 6, 0.28F);
		int from = hover ? Theme.mix(Theme.ACCENT_FROM, 0xFFFFFFFF, 0.12F) : Theme.ACCENT_FROM;
		int to = hover ? Theme.mix(Theme.ACCENT_TO, 0xFFFFFFFF, 0.12F) : Theme.ACCENT_TO;
		g.gradient(x, at, w, 48, 11, from, to);
		String label = "Overlay bearbeiten";
		String key = Keys.label(Keys.edit);
		float lw = g.width(label, 14, Gfx.SEMIBOLD);
		float kw = g.width(key, 11, Gfx.SEMIBOLD) + 14;
		float total = 16 + 10 + lw + 10 + kw;
		float sx = x + (w - total) / 2;
		g.icon(Icons.MOVE, sx, at + 16, 16, Theme.WHITE);
		g.textMid(label, sx + 26, at + 24, 14, Gfx.SEMIBOLD, Theme.WHITE);
		int kx = Math.round(sx + 26 + lw + 10);
		g.rect(kx, at + 13, Math.round(kw), 22, 6, Theme.alpha(0xFFFFFFFF, 0.18F));
		g.textCentered(key, kx + kw / 2, at + 24, 11, Gfx.SEMIBOLD, Theme.WHITE);
		ui.hit(x, at, w, 48, () -> {
			closed();
			Minecraft.getInstance().setScreenAndShow(new OverlayEditScreen(m));
		});
		at += 48;
		return at - y;
	}

	private interface Choice {
		void pick(boolean priv);
	}

	private void row(Gfx g, int x, int y, int w, String name, String where, boolean on, boolean priv, boolean privacy, Runnable toggle,
					 Choice choice) {
		Ui ui = m.ui;
		int segW = 170;
		int sx = x + w - 6 - segW;
		g.rect(x, y, w, 40, 10, Theme.ROW);
		// Ganze Zeile (bis zur Auswahl rechts) schaltet ein und aus
		ui.hit(x, y, sx - x - 4, 40, toggle);
		g.toggle(x + 12, y + 10, on);
		g.textMid(name, x + 12 + 36 + 12, y + 20, 13.5F, Gfx.MEDIUM, on ? Theme.TEXT : Theme.MUTED_2);
		g.textRight(where, sx - 12, y + 20, 12, Gfx.REGULAR, Theme.MUTED_2);
		g.rect(sx, y + 4, segW, 32, 8, Theme.TRACK);
		int half = (segW - 6 - 3) / 2;
		g.segment("Im Spiel", null, sx + 3, y + 7, half, 26, 6, !priv, 12);
		g.segment("Privat", null, sx + 3 + half + 3, y + 7, half, 26, 6, priv, 12);
		if (!privacy)
			g.rect(sx + 3 + half + 3, y + 7, half, 26, 6, Theme.alpha(Theme.TRACK, 0.5F));
		ui.hit(sx + 3, y + 7, half, 26, () -> choice.pick(false));
		ui.hit(sx + 3 + half + 3, y + 7, half, 26, () -> choice.pick(true));
	}

	/** Änderungen speichern (beim Verlassen des Tabs bzw. Schließen). */
	void closed() {
		if (dirty) {
			AxoHud.config().save();
			dirty = false;
		}
	}
}
