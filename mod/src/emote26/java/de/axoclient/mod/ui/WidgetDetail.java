package de.axoclient.mod.ui;

import de.axoclient.mod.AxoHud;
import de.axoclient.mod.hud.HudConfig;
import de.axoclient.mod.hud.HudElement;
import de.axoclient.mod.hud.HudModule;
import de.axoclient.mod.hud.HudOverlay;
import de.axoclient.mod.hud.HudPrivacy;

/**
 * Detailansicht eines Widgets im Overlay-Tab: alle Einstellungen auf einer Seite (statt eines Dialogs über dem Spiel).
 * Erreichbar über einen Klick auf das Widget in der Liste oder über das Zahnrad im Bearbeiten-Modus.
 */
final class WidgetDetail {
	static final int[] SWATCHES = { 0xF2F2F2, 0xF5C542, 0x10B981, 0xEC4899, 0xA78BFA, 0x6FA8DC };

	private final MenuScreen m;
	/** Gezeigtes Widget, oder null mit {@link #toasts} = true für die Einblendungen. */
	HudModule module;
	boolean toasts;

	WidgetDetail(MenuScreen m) {
		this.m = m;
	}

	boolean open() {
		return module != null || toasts;
	}

	void show(HudModule module) {
		this.module = module;
		this.toasts = false;
	}

	void showToasts() {
		this.module = null;
		this.toasts = true;
	}

	void close() {
		module = null;
		toasts = false;
	}

	/** Zeichnet die Seite; gibt die Höhe zurück. */
	int render(Gfx g, int x, int y, int w, int h) {
		Ui ui = m.ui;
		HudConfig config = AxoHud.config();
		// Kopf: zurück, Name, an/aus
		boolean hover = ui.hover(x, y, 34, 34);
		g.rect(x, y, 34, 34, 9, hover ? Theme.BUTTON_HOVER : Theme.BUTTON);
		g.icon(Icons.CHEVRON_LEFT, x + 10, y + 10, 14, Theme.TEXT_SOFT);
		ui.hit(x, y, 34, 34, this::close);
		String title = toasts ? "Einblendungen" : module.label;
		boolean on = toasts ? MenuConfig.toastsOn : config.get(module).enabled;
		g.textMid(g.ellipsize(title, 18, Gfx.SEMIBOLD, w - 34 - 14 - 140), x + 34 + 14, y + 17, 18, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
		String state = on ? "Eingeschaltet" : "Ausgeschaltet";
		g.textRight(state, x + w - 36 - 10, y + 17, 12, Gfx.REGULAR, on ? Theme.TEXT_SOFT : Theme.MUTED_2);
		g.toggle(x + w - 36, y + 7, on);
		ui.hit(x + w - 36 - 10 - Math.round(g.width(state, 12, Gfx.REGULAR)), y + 2, 36 + 10 + Math.round(g.width(state, 12, Gfx.REGULAR)), 30, () -> {
			if (toasts)
				MenuConfig.toastsOn = !MenuConfig.toastsOn;
			else
				config.get(module).enabled = !config.get(module).enabled;
			changed();
		});

		int top = y + 34 + 18;
		int gap = 16;
		int colW = (w - gap) / 2;
		int lx = x, rx = x + colW + gap;
		int bottom;
		// Je zwei Karten nebeneinander sind immer gleich hoch
		if (toasts) {
			bottom = pair(g, top, lx, rx, colW, "Darstellung", this::toastsLook, "Sichtbarkeit",
				(gg, px, at, pw) -> toastsVisibility(gg, px, at, pw));
			bottom = pair(g, bottom + gap, lx, rx, colW, "Was erscheint", this::toastsKinds, "Position", this::toastsPosition);
		} else {
			bottom = pair(g, top, lx, rx, colW, "Darstellung", (gg, px, at, pw) -> look(gg, config, px, at, pw), "Sichtbarkeit",
				(gg, px, at, pw) -> visibilityOf(gg, config, px, at, pw));
			bottom = pair(g, bottom + gap, lx, rx, colW, "Hintergrund", (gg, px, at, pw) -> background(gg, config, px, at, pw),
				"Position", (gg, px, at, pw) -> position(gg, config, px, at, pw));
		}
		return bottom - y + 6;
	}

	/** Zwei Karten nebeneinander in derselben Höhe (die höhere bestimmt); gibt die Unterkante zurück. */
	private int pair(Gfx g, int y, int lx, int rx, int w, String leftTitle, Body left, String rightTitle, Body right) {
		int h = Math.max(measure(lx, y, w, left), measure(rx, y, w, right));
		section(g, leftTitle, lx, y, w, h, left);
		section(g, rightTitle, rx, y, w, h, right);
		return y + h;
	}

	private interface Body {
		int draw(Gfx g, int px, int at, int pw);
	}

	private static final int PAD = 16;

	/** Höhe, die eine Karte für ihren Inhalt braucht (nur gemessen, nichts gezeichnet). */
	private int measure(int x, int y, int w, Body body) {
		Ui ui = m.ui;
		boolean inert = ui.inert;
		ui.inert = true; // Messen: keine Klickflächen anmelden
		int end = body.draw(Gfx.measure(m.width, m.height), x + PAD, y + PAD + 14 + 12, w - 2 * PAD);
		ui.inert = inert;
		return end + PAD - y;
	}

	/** Karte mit Überschrift in der Höhe h. */
	private void section(Gfx g, String title, int x, int y, int w, int h, Body body) {
		g.rect(x, y, w, h, 12, Theme.PANEL);
		MenuScreen.heading(g, title, x + PAD, y + PAD + 7);
		body.draw(g, x + PAD, y + PAD + 14 + 12, w - 2 * PAD);
	}

	private void changed() {
		AxoHud.config().save();
		MenuConfig.save();
	}

	// ---------- Widget ----------

	/** Textfarbe, Größe, Schatten, Format. */
	private int look(Gfx g, HudConfig config, int px, int at, int pw) {
		HudConfig.Entry e = config.get(module);
		g.textMid("Textfarbe", px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		at += 14 + 8;
		int sx = px;
		int size = Math.min(30, (pw - 5 * 8) / 6);
		for (int rgb : SWATCHES) {
			boolean picked = (e.textRgb & 0xFFFFFF) == rgb;
			boolean hover = m.ui.hover(sx, at, size, size);
			if (picked || hover) {
				g.rect(sx - 3, at - 3, size + 6, size + 6, 10, picked ? Theme.ACCENT_LINE : 0xFF5A5A5A);
				g.rect(sx - 1, at - 1, size + 2, size + 2, 9, Theme.PANEL);
			}
			g.rect(sx, at, size, size, 8, hover && !picked ? Theme.mix(0xFF000000 | rgb, 0xFFFFFFFF, 0.12F) : 0xFF000000 | rgb);
			int pick = rgb;
			m.ui.hit(sx, at, size, size, () -> {
				e.textRgb = pick;
				changed();
			});
			sx += size + 8;
		}
		at += size + 18;
		at = slider(g, px, at, pw, "Größe", e.scale + " %", (e.scale - 50) / 150.0F, t -> {
			e.scale = HudConfig.clampScale(Math.round((50 + t * 150) / 10.0F) * 10);
			changed();
		});
		at += 18;
		at = toggleRow(g, px, at, pw, "Schatten unter der Schrift", null, e.shadow, () -> {
			e.shadow = !e.shadow;
			changed();
		});
		if (module.variants != null && module.variants.length > 1) {
			at += 16;
			g.textMid("Format", px, at + 10, 13, Gfx.REGULAR, Theme.TEXT_SOFT);
			String value = module.variants[Math.min(e.variant, module.variants.length - 1)];
			int bw = Math.min(pw - 70, Math.round(g.width(value, 12, Gfx.MEDIUM)) + 34);
			int bx = px + pw - bw;
			boolean hover = m.ui.hover(bx, at - 2, bw, 24);
			g.rect(bx, at - 2, bw, 24, 7, hover ? Theme.BUTTON_HOVER : Theme.ROW);
			g.textMid(g.ellipsize(value, 12, Gfx.MEDIUM, bw - 34), bx + 10, at + 10, 12, Gfx.MEDIUM, Theme.TEXT);
			g.icon(Icons.CHEVRON_RIGHT, bx + bw - 20, at + 4, 12, Theme.TEXT_DIM);
			m.ui.hit(bx, at - 2, bw, 24, () -> {
				e.variant = (e.variant + 1) % module.variants.length;
				changed();
			});
			at += 20;
		}
		return at;
	}

	/** Fläche hinter dem Widget: an/aus, Deckkraft, Ecken, Rahmen. */
	private int background(Gfx g, HudConfig config, int px, int at, int pw) {
		HudConfig.Entry e = config.get(module);
		at = toggleRow(g, px, at, pw, "Fläche hinter dem Widget", null, e.background, () -> {
			e.background = !e.background;
			changed();
		});
		if (!e.background)
			return at;
		at += 18;
		at = slider(g, px, at, pw, "Deckkraft", e.backgroundAlpha + " %", e.backgroundAlpha / 100.0F, t -> {
			e.backgroundAlpha = Math.round(t * 20) * 5;
			changed();
		});
		at += 18;
		int corner = HudConfig.clampCorner(e.corner);
		at = slider(g, px, at, pw, "Ecken", corner == 0 ? "Eckig" : corner + " px",
			(corner - HudConfig.MIN_CORNER) / (float) (HudConfig.MAX_CORNER - HudConfig.MIN_CORNER), t -> {
				e.corner = HudConfig.clampCorner(HudConfig.MIN_CORNER + Math.round(t * (HudConfig.MAX_CORNER - HudConfig.MIN_CORNER)));
				changed();
			});
		at += 18;
		return toggleRow(g, px, at, pw, "Rahmen in der Textfarbe", null, e.border, () -> {
			e.border = !e.border;
			changed();
		});
	}

	private int visibilityOf(Gfx g, HudConfig config, int px, int at, int pw) {
		HudConfig.Entry e = config.get(module);
		String reason = HudPrivacy.reasonAgainst(config, module);
		return visibility(g, px, at, pw, e.privat, priv -> {
			if (priv && reason != null) {
				m.notice(reason, null, null);
				return;
			}
			e.privat = priv;
			changed();
		}, reason != null ? reason + " – bleibt im Spielbild."
			: e.privat ? "Zeichnet der Launcher in einem eigenen Fenster über dem Spiel. Nicht sichtbar in Discord, OBS und Screenshots."
			: "Wird ins Spielbild gezeichnet und ist im Stream und auf Screenshots zu sehen.");
	}

	/** Verankerung und der Weg in den Bearbeiten-Modus. */
	private int position(Gfx g, HudConfig config, int px, int at, int pw) {
		HudConfig.Entry e = config.get(module);
		at = anchorRow(g, px, at, pw, Math.max(0, Math.min(8, e.anchorY * 3 + e.anchorX)), this::setAnchor);
		at += 16;
		return moveButton(g, px, at, pw, module);
	}

	/** Knopf "Im Bild verschieben": Bearbeiten-Modus mit genau diesem Widget. */
	private int moveButton(Gfx g, int px, int at, int pw, HudModule which) {
		boolean hover = m.ui.hover(px, at, pw, 34);
		g.rect(px, at, pw, 34, 9, hover ? Theme.BUTTON_HOVER : Theme.BUTTON);
		String label = "Im Bild verschieben";
		float lw = g.width(label, 12.5F, Gfx.MEDIUM);
		float start = px + (pw - 14 - 8 - lw) / 2;
		g.icon(Icons.MOVE, start, at + 10, 14, Theme.TEXT_SOFT);
		g.textMid(label, start + 22, at + 17, 12.5F, Gfx.MEDIUM, Theme.TEXT_SOFT);
		m.ui.hit(px, at, pw, 34, () -> {
			changed();
			net.minecraft.client.Minecraft.getInstance().setScreenAndShow(OverlayEditScreen.selecting(m, which));
		});
		return at + 34;
	}

	/** Neue Verankerung, das Widget bleibt dabei an seiner Stelle. */
	private void setAnchor(int cell) {
		HudConfig config = AxoHud.config();
		HudConfig.Entry e = config.get(module);
		float hs = AxoHud.hudScale();
		int vw = Math.round(m.width / hs), vh = Math.round(m.height / hs);
		HudElement element = HudElement.of(module);
		int[] box = HudOverlay.box(config, element, AxoHud.hudPainter(null), AxoHud.texts(), vw, vh);
		e.autoAnchor = false;
		e.anchorX = cell % 3;
		e.anchorY = cell / 3;
		HudOverlay.move(e, element, box[0], box[1], box[2], box[3], vw, vh);
		changed();
	}

	// ---------- Einblendungen ----------

	private int toastsVisibility(Gfx g, int px, int at, int pw) {
		return visibility(g, px, at, pw, MenuConfig.toastsPrivate, priv -> {
			MenuConfig.toastsPrivate = priv;
			changed();
		}, MenuConfig.toastsPrivate ? "Standard. Einladungen und Namen deiner Freunde landen nicht im Stream."
			: "Achtung: Einladungen und Namen deiner Freunde sind im Stream zu sehen.");
	}

	private int toastsLook(Gfx g, int px, int at, int pw) {
		at = slider(g, px, at, pw, "Größe", MenuConfig.toastScale + " %", (MenuConfig.toastScale - 50) / 150.0F, t -> {
			MenuConfig.toastScale = Math.max(50, Math.min(200, Math.round((50 + t * 150) / 10.0F) * 10));
			changed();
		});
		at += 18;
		return slider(g, px, at, pw, "Anzeigedauer", MenuConfig.toastSeconds + " s", (MenuConfig.toastSeconds - 3) / 12.0F, t -> {
			MenuConfig.toastSeconds = Math.round(3 + t * 12);
			changed();
		});
	}

	private int toastsPosition(Gfx g, int px, int at, int pw) {
		at = anchorRow(g, px, at, pw, MenuConfig.toastAnchor, cell -> {
			MenuConfig.toastAnchor = cell;
			MenuConfig.toastX = 12;
			MenuConfig.toastY = 12;
			MenuConfig.toastsDown = cell / 3 != 2;
			changed();
		});
		at += 16;
		g.textMid("Neue Einblendungen", px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		at += 14 + 8;
		g.box(px, at, pw, 34, 9, Theme.WELL, 0xFF343434);
		int half = (pw - 9) / 2;
		segHover(g, px + 3, at + 3, half, 28, MenuConfig.toastsDown);
		segHover(g, px + 6 + half, at + 3, half, 28, !MenuConfig.toastsDown);
		g.segment("Nach unten", Icons.ARROW_DOWN, px + 3, at + 3, half, 28, 7, MenuConfig.toastsDown, 12);
		g.segment("Nach oben", Icons.ARROW_UP, px + 6 + half, at + 3, half, 28, 7, !MenuConfig.toastsDown, 12);
		m.ui.hit(px + 3, at + 3, half, 28, () -> {
			MenuConfig.toastsDown = true;
			changed();
		});
		m.ui.hit(px + 6 + half, at + 3, half, 28, () -> {
			MenuConfig.toastsDown = false;
			changed();
		});
		at += 34 + 16;
		return moveButton(g, px, at, pw, null);
	}

	private int toastsKinds(Gfx g, int px, int at, int pw) {
		for (String[] kind : MenuConfig.TOAST_KINDS) {
			boolean on = MenuConfig.TOAST_KIND_ON.getOrDefault(kind[0], true);
			if (m.ui.hover(px, at, pw, 26))
				g.rect(px - 8, at, pw + 16, 26, 7, Theme.alpha(0xFFFFFFFF, 0.04F));
			if (on) {
				g.accent(px, at + 5, 15, 15, 4);
				g.icon(Icons.CHECK_BOLD, px + 2.5F, at + 7.5F, 10, Theme.WHITE);
			} else
				g.box(px, at + 5, 15, 15, 4, Theme.WELL, m.ui.hover(px, at, pw, 26) ? 0xFF777777 : 0xFF555555);
			g.textMid(kind[1], px + 24, at + 13, 12.5F, Gfx.REGULAR, Theme.TEXT_SOFT);
			m.ui.hit(px, at, pw, 26, () -> {
				MenuConfig.TOAST_KIND_ON.put(kind[0], !MenuConfig.TOAST_KIND_ON.getOrDefault(kind[0], true));
				changed();
			});
			at += 26 + 4;
		}
		return at - 4;
	}

	// ---------- Bausteine ----------

	private interface Pick {
		void pick(boolean priv);
	}

	/** "Sichtbar für": Im Spiel / Privat mit Erklärung; gibt die neue Höhe zurück. */
	private int visibility(Gfx g, int px, int at, int pw, boolean priv, Pick pick, String hint) {
		boolean privacy = HudPrivacy.available();
		boolean shownPriv = priv && privacy;
		g.textMid("Sichtbar für", px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		at += 14 + 8;
		g.box(px, at, pw, 36, 9, Theme.WELL, 0xFF343434);
		int half = (pw - 9) / 2;
		segHover(g, px + 3, at + 3, half, 30, !shownPriv);
		if (privacy)
			segHover(g, px + 6 + half, at + 3, half, 30, shownPriv);
		g.segment("Im Spiel", Icons.EYE, px + 3, at + 3, half, 30, 7, !shownPriv, 12);
		g.segment("Privat", Icons.EYE_OFF, px + 6 + half, at + 3, half, 30, 7, shownPriv, 12);
		if (!privacy)
			g.rect(px + 6 + half, at + 3, half, 30, 7, Theme.alpha(Theme.WELL, 0.55F));
		m.ui.hit(px + 3, at + 3, half, 30, () -> pick.pick(false));
		if (privacy)
			m.ui.hit(px + 6 + half, at + 3, half, 30, () -> pick.pick(true));
		else
			m.ui.hit(px + 6 + half, at + 3, half, 30,
				() -> m.notice("Privat braucht den AxoClient-Launcher (Windows) – Spiel über den Launcher starten", null, null));
		at += 36 + 8;
		String text = privacy ? hint : "Privat braucht den AxoClient-Launcher: Er zeichnet private Widgets in einem eigenen Fenster über dem Spiel.";
		int lines = MenuScreen.wrap(g, text, 11.5F, Gfx.REGULAR, pw).size();
		MenuScreen.paragraph(g, text, px, at, pw, 11.5F, Gfx.REGULAR, Theme.MUTED_2, 16.7F, false);
		return at + Math.round(lines * 16.7F);
	}

	/** Leichte Aufhellung eines nicht gewählten Segments unter der Maus. */
	private void segHover(Gfx g, int x, int y, int w, int h, boolean on) {
		if (!on && m.ui.hover(x, y, w, h))
			g.rect(x, y, w, h, 7, Theme.alpha(0xFFFFFFFF, 0.06F));
	}

	private interface Value {
		void set(float t);
	}

	/** Regler mit Beschriftung und Wert; gibt die neue Höhe zurück. */
	private int slider(Gfx g, int px, int at, int pw, String label, String value, float t, Value set) {
		g.textMid(label, px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		g.textRight(value, px + pw, at + 7, 12, Gfx.SEMIBOLD, Theme.TEXT);
		at += 14 + 8;
		float clamped = Math.max(0, Math.min(1, t));
		boolean hover = m.ui.hover(px - 6, at - 4, pw + 12, 24);
		g.rect(px, at + 6, pw, 4, 2, hover ? 0xFF454545 : 0xFF3A3A3A);
		g.gradient(px, at + 6, Math.max(4, Math.round(pw * clamped)), 4, 2, 0xFFDB2777, 0xFFC026D3);
		int knob = px + Math.round((pw - 14) * clamped);
		if (hover)
			g.rect(knob - 4, at - 3, 22, 22, 11, Theme.alpha(0xFFEC4899, 0.22F));
		g.rect(knob, at + 1, 14, 14, 7, Theme.WHITE);
		m.ui.drag(px - 6, at - 4, pw + 12, 24, mx -> set.set(Math.max(0, Math.min(1, (mx - px) / (float) pw))));
		return at + 16;
	}

	/** Zeile mit Text, Wert und Schalter; gibt die neue Höhe zurück. */
	private int toggleRow(Gfx g, int px, int at, int pw, String label, String value, boolean on, Runnable toggle) {
		boolean hover = m.ui.hover(px, at, pw, 20);
		if (hover)
			g.rect(px - 8, at - 5, pw + 16, 30, 8, Theme.alpha(0xFFFFFFFF, 0.04F));
		g.textMid(label, px, at + 10, 13, Gfx.REGULAR, hover ? Theme.TEXT : Theme.TEXT_SOFT);
		if (value != null)
			g.textRight(value, px + pw - 36 - 10, at + 10, 12, Gfx.REGULAR, Theme.MUTED_2);
		g.toggle(px + pw - 36, at, on);
		m.ui.hit(px, at, pw, 20, toggle);
		return at + 20;
	}

	private interface Cell {
		void set(int cell);
	}

	/** "Verankert" mit Namen und 3 × 3-Auswahl; gibt die neue Höhe zurück. */
	private int anchorRow(Gfx g, int px, int at, int pw, int anchor, Cell set) {
		g.textMid("Verankert", px, at + 12, 12, Gfx.SEMIBOLD, Theme.MUTED);
		String name = OverlayTab.ANCHOR_NAMES[anchor];
		g.textMid(Character.toUpperCase(name.charAt(0)) + name.substring(1), px, at + 31, 13, Gfx.REGULAR, Theme.TEXT);
		int gx = px + pw - 70;
		g.box(gx, at, 70, 70, 8, Theme.WELL, 0xFF343434);
		for (int i = 0; i < 9; i++) {
			int cx = gx + 4 + (i % 3) * 21, cy = at + 4 + (i / 3) * 21;
			boolean on = i == anchor;
			boolean hover = m.ui.hover(cx, cy, 18, 18);
			if (on)
				g.accent(cx, cy, 18, 18, 4);
			else
				g.rect(cx, cy, 18, 18, 4, hover ? 0xFF4A4A4A : Theme.BUTTON);
			int cell = i;
			m.ui.hit(cx, cy, 18, 18, () -> set.set(cell));
		}
		return at + 70;
	}
}
