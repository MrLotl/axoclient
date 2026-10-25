package de.axoclient.mod.ui;

import de.axoclient.mod.AxoHud;
import de.axoclient.mod.hud.HudConfig;
import de.axoclient.mod.hud.HudModule;
import de.axoclient.mod.hud.HudPrivacy;
import de.axoclient.mod.hud.HudProfiles;
import net.minecraft.client.Minecraft;

import java.util.List;

/** Tab "Overlay": Profil, alle Widgets mit Schalter (Einstellungen per Klick), Knopf in den Bearbeiten-Modus. */
final class OverlayTab {
	static final String[] ANCHOR_NAMES = { "oben links", "oben Mitte", "oben rechts", "Mitte links", "Mitte", "Mitte rechts",
		"unten links", "unten Mitte", "unten rechts" };

	/** Die Widgets nach Themen; was hier fehlt, landet unter "Weitere". */
	private record Group(String name, HudModule... modules) {}

	private static final Group[] GROUPS = {
		new Group("Leistung", HudModule.FPS, HudModule.PING, HudModule.CPU, HudModule.GPU, HudModule.MEMORY),
		new Group("Position und Welt", HudModule.COORDS, HudModule.DIRECTION, HudModule.SPEED, HudModule.BIOME, HudModule.CHUNK,
			HudModule.LIGHT),
		new Group("Zeit", HudModule.TIME, HudModule.WORLD_TIME),
		new Group("Kampf und Steuerung", HudModule.CPS, HudModule.KEYS, HudModule.ARMOR, HudModule.EFFECTS),
		new Group("Server", HudModule.SCOREBOARD)
	};

	private final MenuScreen m;
	private boolean dirty;
	/** Detailansicht eines Widgets (statt der Liste), siehe {@link WidgetDetail}. */
	final WidgetDetail detail;

	OverlayTab(MenuScreen m) {
		this.m = m;
		this.detail = new WidgetDetail(m);
	}

	int render(Gfx g, int x, int y, int w, int h) {
		if (detail.open())
			return detail.render(g, x, y, w, h);
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

		// Widgets, nach Themen
		int at = y + 30 + 10;
		boolean privacy = HudPrivacy.available();
		java.util.Set<HudModule> listed = java.util.EnumSet.noneOf(HudModule.class);
		for (Group group : GROUPS)
			java.util.Collections.addAll(listed, group.modules());
		java.util.List<HudModule> rest = new java.util.ArrayList<>();
		for (HudModule module : HudModule.values())
			if (!listed.contains(module))
				rest.add(module);
		for (int gi = 0; gi <= GROUPS.length; gi++) {
			String name = gi < GROUPS.length ? GROUPS[gi].name() : "Weitere";
			java.util.List<HudModule> modules = gi < GROUPS.length ? java.util.List.of(GROUPS[gi].modules()) : rest;
			boolean server = gi == GROUPS.length - 1;
			if (modules.isEmpty() && !server)
				continue;
			at += 16;
			MenuScreen.heading(g, name, x, at + 7);
			at += 14 + 8;
			for (HudModule module : modules) {
				HudConfig.Entry entry = config.get(module);
				row(g, x, at, w, module.label, ANCHOR_NAMES[Math.max(0, Math.min(8, entry.anchorY * 3 + entry.anchorX))], entry.enabled,
					entry.privat && privacy && HudPrivacy.reasonAgainst(config, module) == null, () -> {
						entry.enabled = !entry.enabled;
						dirty = true;
					}, () -> open(module));
				at += 40 + 4;
			}
			// Einblendungen sind ein eigenes Widget des Menüs und gehören zum Server
			if (server) {
				row(g, x, at, w, "Einblendungen", ANCHOR_NAMES[MenuConfig.toastAnchor], MenuConfig.toastsOn, MenuConfig.toastsPrivate && privacy,
					() -> {
						MenuConfig.toastsOn = !MenuConfig.toastsOn;
						MenuConfig.save();
					}, () -> {
						closed();
						detail.showToasts();
						m.resetScroll();
					});
				at += 40 + 4;
			}
		}
		at += 12;

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

	/** Detailansicht eines Widgets öffnen. */
	void open(HudModule module) {
		closed();
		detail.show(module);
		m.resetScroll();
	}

	/** Zeile der Liste: Schalter, Name, Verankerung; "Im Spiel / Privat" gibt es erst in der Detailansicht. */
	private void row(Gfx g, int x, int y, int w, String name, String where, boolean on, boolean priv, Runnable toggle, Runnable details) {
		Ui ui = m.ui;
		boolean hover = ui.hover(x, y, w, 40);
		g.rect(x, y, w, 40, 10, hover ? Theme.BUTTON_2 : Theme.ROW);
		// Zeile öffnet die Einstellungen, der Schalter schaltet ein und aus
		ui.hit(x, y, w, 40, details);
		ui.hit(x + 6, y + 4, 36 + 12, 32, toggle);
		g.toggle(x + 12, y + 10, on);
		int chevron = x + w - 6 - 18;
		g.icon(Icons.CHEVRON_RIGHT, chevron, y + 13, 14, hover ? Theme.TEXT_SOFT : Theme.MUTED_3);
		g.textMid(name, x + 12 + 36 + 12, y + 20, 13.5F, Gfx.MEDIUM, on ? Theme.TEXT : Theme.MUTED_2);
		int right = chevron - 10;
		g.textRight(where, right, y + 20, 12, Gfx.REGULAR, Theme.MUTED_2);
		if (priv)
			// Nur ein kleiner Hinweis, dass das Widget privat ist
			g.icon(Icons.EYE_OFF, right - g.width(where, 12, Gfx.REGULAR) - 10 - 13, y + 13.5F, 13, Theme.MUTED_2);
	}

	/** Änderungen speichern (beim Verlassen des Tabs bzw. Schließen). */
	void closed() {
		if (dirty) {
			AxoHud.config().save();
			dirty = false;
		}
	}
}
