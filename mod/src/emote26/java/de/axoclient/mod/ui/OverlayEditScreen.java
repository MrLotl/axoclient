package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.InputConstants;
import de.axoclient.mod.AxoHud;
import de.axoclient.mod.MenuHooks;
import de.axoclient.mod.hud.HudConfig;
import de.axoclient.mod.hud.HudElement;
import de.axoclient.mod.hud.HudModule;
import de.axoclient.mod.hud.HudOverlay;
import de.axoclient.mod.hud.HudPrivacy;
import de.axoclient.mod.hud.HudProfiles;
import de.axoclient.mod.hud.HudSkin;
import de.axoclient.mod.hud.HudSnap;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

import java.util.ArrayList;
import java.util.List;
import java.util.Map;

/**
 * Bearbeiten-Modus des Overlays (Mockup "edit"): alle Widgets im Bild verschieben, an Raster und Widgets einrasten,
 * an den Ecken skalieren; rechts bzw. links die Einstellungen des gewählten Widgets.
 */
public final class OverlayEditScreen extends Screen {
	private static final int[] SWATCHES = { 0xF2F2F2, 0xF5C542, 0x10B981, 0xEC4899, 0xA78BFA, 0x6FA8DC };
	private static final String[] SWATCH_NAMES = { "Weiß", "Gelb", "Mint", "Rosa", "Lila", "Blau" };

	/** Ein greifbares Stück im Bild: Widget oder die Einblendungen (module == null). */
	private record Piece(HudElement element, int x, int y, int w, int h) {
		boolean toasts() {
			return element == null;
		}
	}

	private final MenuScreen parent;
	private final Ui ui = new Ui();
	private final HudConfig config = AxoHud.config();

	/** Gewähltes Widget (null + toastsSelected = Einblendungen). */
	private HudModule selected;
	private boolean toastsSelected;
	private List<Piece> pieces = new ArrayList<>();

	// Ziehen im Bild (GUI-Einheiten)
	private Piece dragging;
	/** Maßstab und Größe des Rasters der Anzeigen (siehe AxoHud.hudScale). */
	private float hs = 1;
	private int vw;
	private int vh;
	private int grabX;
	private int grabY;
	private boolean resizing;
	private int startScale;
	private int startW;
	private int guideX = -1;
	private int guideY = -1;

	private boolean profileMenu;
	private boolean addMenu;
	private long resetArmedUntil;

	public OverlayEditScreen(MenuScreen parent) {
		super(Component.literal("Overlay bearbeiten"));
		this.parent = parent;
		for (HudModule module : HudModule.values())
			if (config.isEnabled(module)) {
				selected = module;
				break;
			}
	}

	@Override
	protected void init() {
		MenuHooks.hideHud = true;
	}

	@Override
	public void removed() {
		MenuHooks.hideHud = false;
		config.save();
		MenuConfig.save();
		super.removed();
	}

	@Override
	public void extractBackground(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
		// Kein Weichzeichner: man sieht das Spiel und die Widgets an ihrer echten Stelle
	}

	@Override
	public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
		HudPrivacy.editorShown();
		AxoHud.Painter painter = AxoHud.hudPainter(graphics);
		Map<HudModule, String> texts = AxoHud.texts();
		hs = AxoHud.hudScale();
		vw = Math.round(width / hs);
		vh = Math.round(height / hs);

		// Schleier, dann Raster und Widgets im (feineren) Raster der Anzeigen
		graphics.fill(0, 0, width, height, 0x5208080C);
		graphics.pose().pushMatrix();
		graphics.pose().scale(hs, hs);
		HudSnap.drawGrid(config, painter, vw, vh);

		HudOverlay.sampleMode = true;
		List<HudElement> elements = HudOverlay.elements(config, painter, texts, true);
		HudOverlay.drawGroups(config, elements, painter, texts, vw, vh);
		List<Piece> found = new ArrayList<>();
		for (HudElement element : elements) {
			int[] box = HudOverlay.box(config, element, painter, texts, vw, vh);
			HudOverlay.draw(config, element, painter, texts, box);
			found.add(new Piece(element, box[0], box[1], box[2], box[3]));
		}
		HudOverlay.sampleMode = false;
		graphics.pose().popMatrix();

		// Einblendungen als Beispiel (in Design-Pixeln)
		Gfx g = Gfx.begin(graphics, width, height);
		try {
			if (MenuConfig.toastsOn) {
				List<Toasts.Toast> samples = Toasts.samples();
				Toasts.draw(g, samples, System.currentTimeMillis());
				int[] o = Toasts.origin(g, Toasts.WIDTH, Toasts.stackHeight(samples));
				float k = g.scale / hs;
				found.add(new Piece(null, Math.round(o[0] * k), Math.round(o[1] * k), Math.round(o[2] * k), Math.round(o[3] * k)));
			}
		} finally {
			g.end();
		}
		pieces = found;

		// Rahmen um die Stücke, Hilfslinien
		graphics.pose().pushMatrix();
		graphics.pose().scale(hs, hs);
		for (Piece p : pieces) {
			boolean on = isSelected(p);
			int pad = 2;
			if (on) {
				HudSkin.outline(painter, p.x() - pad - 2, p.y() - pad - 2, p.w() + 2 * pad + 4, p.h() + 2 * pad + 4, 5, Theme.alpha(0xFFE07AD8, 0.22F));
				HudSkin.outline(painter, p.x() - pad, p.y() - pad, p.w() + 2 * pad, p.h() + 2 * pad, 4, Theme.ACCENT_LINE);
				if (!p.toasts())
					for (int[] c : corners(p))
						HudSkin.rounded(painter, c[0], c[1], c[2], c[2], 1, Theme.ACCENT_LINE);
			} else
				HudSkin.outline(painter, p.x() - pad, p.y() - pad, p.w() + 2 * pad, p.h() + 2 * pad, 4, Theme.alpha(0xFFFFFFFF, 0.35F));
			boolean priv = p.toasts() ? MenuConfig.toastsPrivate : config.isPrivate(p.element().module());
			if (priv && HudPrivacy.available())
				painter.text("Privat", p.x() + p.w() - 24, p.y() - pad - 9, Theme.ACCENT_TEXT, true);
		}
		if (dragging != null)
			HudSnap.drawGuides(painter, guideX, guideY, vw, vh);
		graphics.pose().popMatrix();

		// Leiste, Einstellungen, Hinweis (Design-Pixel)
		g = Gfx.begin(graphics, width, height);
		try {
			ui.begin(g, g.mx(mouseX), g.my(mouseY));
			toolbar(g);
			inspector(g);
			hint(g);
			menus(g);
			ui.finish();
		} finally {
			g.end();
		}
	}

	private boolean isSelected(Piece p) {
		return p.toasts() ? toastsSelected : !toastsSelected && p.element().module() == selected;
	}

	/** Griffe an den vier Ecken: {x, y, Größe}. */
	private static int[][] corners(Piece p) {
		int s = 3, pad = 2;
		int l = p.x() - pad - 1, t = p.y() - pad - 1, r = p.x() + p.w() + pad - 2, b = p.y() + p.h() + pad - 2;
		return new int[][] { { l, t, s }, { r, t, s }, { l, b, s }, { r, b, s } };
	}

	// ---------- Werkzeugleiste ----------

	private int toolbarX;
	private int toolbarY;
	private int profileX;
	private int addX;

	private void toolbar(Gfx g) {
		String profile = config.activeProfile.isBlank() ? HudProfiles.DEFAULT : config.activeProfile;
		float labelW = g.width("Axo", 13, Gfx.SEMIBOLD) + g.width(" Overlay", 13, Gfx.SEMIBOLD);
		int profileW = Math.round(12 + g.width("Profil", 12.5F, Gfx.MEDIUM) + 8 + g.width(profile, 12.5F, Gfx.MEDIUM) + 8 + 14 + 10);
		int gridW = Math.round(12 + 15 + 7 + g.width("Raster", 12.5F, Gfx.MEDIUM) + 12);
		int addW = Math.round(12 + 14 + 7 + g.width("Widget hinzufügen", 12.5F, Gfx.MEDIUM) + 12);
		String resetLabel = System.currentTimeMillis() < resetArmedUntil ? "Wirklich zurücksetzen?" : "Zurücksetzen";
		int resetW = Math.round(g.width(resetLabel, 12.5F, Gfx.MEDIUM) + 24);
		int doneW = Math.round(16 + 13 + 7 + g.width("Fertig", 12.5F, Gfx.SEMIBOLD) + 16);
		int w = 6 + Math.round(6 + labelW + 8) + 4 + profileW + 4 + gridW + 4 + addW + 4 + 9 + resetW + 4 + doneW + 6;
		int x = (g.width - w) / 2, y = 14;
		toolbarX = x;
		toolbarY = y;
		g.shadow(x, y, w, 48, 12, 18, 14, 0.5F);
		g.box(x, y, w, 48, 12, Theme.alpha(0xFF242424, 0.96F), Theme.WINDOW_LINE);
		ui.block(x, y, w, 48);
		int cy = y + 24;
		float at = x + 12;
		at += g.gradientText("Axo", at, cy, 13, Gfx.SEMIBOLD);
		g.textMid(" Overlay", at, cy, 13, Gfx.SEMIBOLD, Theme.TEXT);
		int bx = Math.round(x + 6 + 6 + labelW + 8 + 4);

		// Profil
		profileX = bx;
		boolean hover = ui.hover(bx, y + 6, profileW, 36);
		g.rect(bx, y + 6, profileW, 36, 8, hover || profileMenu ? Theme.BUTTON_HOVER : Theme.ROW);
		float tx = bx + 12;
		g.textMid("Profil", tx, cy, 12.5F, Gfx.MEDIUM, Theme.MUTED_2);
		tx += g.width("Profil", 12.5F, Gfx.MEDIUM) + 8;
		g.textMid(profile, tx, cy, 12.5F, Gfx.MEDIUM, Theme.TEXT);
		g.icon(Icons.CHEVRON_DOWN, bx + profileW - 10 - 14, cy - 7, 14, Theme.TEXT_DIM);
		ui.hit(bx, y + 6, profileW, 36, () -> {
			profileMenu = !profileMenu;
			addMenu = false;
		});
		bx += profileW + 4;

		// Raster
		hover = ui.hover(bx, y + 6, gridW, 36);
		if (config.grid) {
			g.gradient(bx, y + 6, gridW, 36, 8, Theme.alpha(0xFFEC4899, 0.16F), Theme.alpha(0xFF8B5CF6, 0.16F));
		} else if (hover)
			g.rect(bx, y + 6, gridW, 36, 8, Theme.BUTTON_HOVER);
		int gc = config.grid ? Theme.ACCENT_TEXT : Theme.TEXT_SOFT;
		g.icon(Icons.GRID, bx + 12, cy - 7.5F, 15, gc);
		g.textMid("Raster", bx + 12 + 15 + 7, cy, 12.5F, Gfx.MEDIUM, gc);
		ui.hit(bx, y + 6, gridW, 36, () -> config.grid = !config.grid);
		bx += gridW + 4;

		// Widget hinzufügen
		addX = bx;
		hover = ui.hover(bx, y + 6, addW, 36);
		if (hover || addMenu)
			g.rect(bx, y + 6, addW, 36, 8, Theme.BUTTON_HOVER);
		g.icon(Icons.PLUS, bx + 12, cy - 7, 14, Theme.TEXT_SOFT);
		g.textMid("Widget hinzufügen", bx + 12 + 14 + 7, cy, 12.5F, Gfx.MEDIUM, Theme.TEXT_SOFT);
		ui.hit(bx, y + 6, addW, 36, () -> {
			addMenu = !addMenu;
			profileMenu = false;
		});
		bx += addW + 4;
		g.vline(bx + 4, y + 13, 22, 0xFF3C3C3C);
		bx += 9;

		// Zurücksetzen
		hover = ui.hover(bx, y + 6, resetW, 36);
		if (hover)
			g.rect(bx, y + 6, resetW, 36, 8, Theme.BUTTON_HOVER);
		g.textMid(resetLabel, bx + 12, cy, 12.5F, Gfx.MEDIUM, System.currentTimeMillis() < resetArmedUntil ? Theme.YELLOW_TEXT : Theme.MUTED);
		ui.hit(bx, y + 6, resetW, 36, () -> {
			if (System.currentTimeMillis() < resetArmedUntil) {
				config.reset();
				config.save();
				resetArmedUntil = 0;
			} else
				resetArmedUntil = System.currentTimeMillis() + 3000;
		});
		bx += resetW + 4;

		// Fertig
		hover = ui.hover(bx, y + 6, doneW, 36);
		g.shadow(bx, y + 6, doneW, 36, 8, 6, 4, 0.25F);
		g.gradient(bx, y + 6, doneW, 36, 8, hover ? Theme.mix(Theme.ACCENT_FROM, 0xFFFFFFFF, 0.12F) : Theme.ACCENT_FROM,
			hover ? Theme.mix(Theme.ACCENT_TO, 0xFFFFFFFF, 0.12F) : Theme.ACCENT_TO);
		g.icon(Icons.CHECK, bx + 16, cy - 6.5F, 13, Theme.WHITE);
		g.textMid("Fertig", bx + 16 + 13 + 7, cy, 12.5F, Gfx.SEMIBOLD, Theme.WHITE);
		ui.hit(bx, y + 6, doneW, 36, this::onClose);
	}

	/** Aufklappmenüs der Leiste (über allem). */
	private void menus(Gfx g) {
		if (profileMenu) {
			List<String> names = HudProfiles.list(config);
			int w = 200, rowH = 32;
			int h = 8 + (names.size() + 1) * rowH;
			int x = profileX, y = toolbarY + 48 + 6;
			g.shadow(x, y, w, h, 10, 14, 10, 0.5F);
			g.box(x, y, w, h, 10, Theme.alpha(0xFF242424, 0.98F), Theme.WINDOW_LINE);
			ui.block(x, y, w, h);
			int at = y + 4;
			for (String name : names) {
				boolean on = name.equals(config.activeProfile);
				if (ui.hover(x + 4, at, w - 8, rowH))
					g.rect(x + 4, at, w - 8, rowH, 7, Theme.BUTTON_HOVER);
				if (on)
					g.icon(Icons.CHECK, x + 12, at + 9.5F, 13, Theme.ACCENT_TEXT);
				g.textMid(name, x + 32, at + rowH / 2.0F, 12.5F, on ? Gfx.SEMIBOLD : Gfx.MEDIUM, on ? Theme.ACCENT_TEXT : Theme.TEXT);
				ui.hit(x + 4, at, w - 8, rowH, () -> {
					HudProfiles.load(config, name);
					profileMenu = false;
				});
				at += rowH;
			}
			if (ui.hover(x + 4, at, w - 8, rowH))
				g.rect(x + 4, at, w - 8, rowH, 7, Theme.BUTTON_HOVER);
			g.icon(Icons.PLUS, x + 12, at + 9, 14, Theme.TEXT_SOFT);
			g.textMid("Als neues Profil speichern", x + 32, at + rowH / 2.0F, 12.5F, Gfx.MEDIUM, Theme.TEXT_SOFT);
			ui.hit(x + 4, at, w - 8, rowH, () -> {
				HudProfiles.save(config, HudProfiles.freeName(config));
				profileMenu = false;
			});
		}
		if (addMenu) {
			List<HudModule> off = new ArrayList<>();
			for (HudModule module : HudModule.values())
				if (!config.isEnabled(module))
					off.add(module);
			int w = 220, rowH = 30;
			int count = off.size() + (MenuConfig.toastsOn ? 0 : 1);
			int h = 8 + Math.max(1, count) * rowH;
			int x = addX, y = toolbarY + 48 + 6;
			g.shadow(x, y, w, h, 10, 14, 10, 0.5F);
			g.box(x, y, w, h, 10, Theme.alpha(0xFF242424, 0.98F), Theme.WINDOW_LINE);
			ui.block(x, y, w, h);
			int at = y + 4;
			if (count == 0)
				g.textMid("Alle Widgets sind schon im Bild", x + 12, at + rowH / 2.0F, 12.5F, Gfx.REGULAR, Theme.MUTED);
			for (HudModule module : off) {
				if (ui.hover(x + 4, at, w - 8, rowH))
					g.rect(x + 4, at, w - 8, rowH, 7, Theme.BUTTON_HOVER);
				g.textMid(module.label, x + 12, at + rowH / 2.0F, 12.5F, Gfx.MEDIUM, Theme.TEXT);
				ui.hit(x + 4, at, w - 8, rowH, () -> {
					config.get(module).enabled = true;
					selected = module;
					toastsSelected = false;
					addMenu = false;
				});
				at += rowH;
			}
			if (!MenuConfig.toastsOn) {
				if (ui.hover(x + 4, at, w - 8, rowH))
					g.rect(x + 4, at, w - 8, rowH, 7, Theme.BUTTON_HOVER);
				g.textMid("Einblendungen", x + 12, at + rowH / 2.0F, 12.5F, Gfx.MEDIUM, Theme.TEXT);
				ui.hit(x + 4, at, w - 8, rowH, () -> {
					MenuConfig.toastsOn = true;
					toastsSelected = true;
					addMenu = false;
				});
			}
		}
	}

	// ---------- Einstellungen des gewählten Widgets ----------

	private void inspector(Gfx g) {
		if (selected == null && !toastsSelected)
			return;
		int anchor;
		if (toastsSelected)
			anchor = MenuConfig.toastAnchor;
		else {
			HudConfig.Entry e = config.get(selected);
			anchor = Math.max(0, Math.min(8, e.anchorY * 3 + e.anchorX));
		}
		boolean rightSide = anchor % 3 == 2;
		int w = 268;
		int x = rightSide ? 230 : g.width - 16 - w;
		int y = toastsSelected ? 76 : 130;
		int h = toastsSelected ? inspectorToasts(g, x, y, w, false) : inspectorWidget(g, x, y, w, false);
		g.shadow(x, y, w, h, 14, 18, 14, 0.55F);
		g.box(x, y, w, h, 14, Theme.alpha(0xFF242424, 0.97F), Theme.WINDOW_LINE);
		ui.block(x, y, w, h);
		if (toastsSelected)
			inspectorToasts(g, x, y, w, true);
		else
			inspectorWidget(g, x, y, w, true);
	}

	/** Zeichnet (draw) bzw. misst nur die Einstellungen eines Widgets; gibt die Höhe zurück. */
	private int inspectorWidget(Gfx g, int x, int y, int w, boolean draw) {
		HudConfig.Entry e = config.get(selected);
		int px = x + 16, pw = w - 32;
		int at = y + 16;
		if (draw) {
			MenuScreen.heading(g, "Widget", px, at + 6);
			g.textMid(g.ellipsize(selected.label, 16, Gfx.SEMIBOLD, pw - 50), px, at + 25, 16, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
			g.toggle(px + pw - 36, at + 8, e.enabled);
			ui.hit(px + pw - 36, at + 8, 36, 20, () -> e.enabled = !e.enabled);
		}
		at += 36 + 16;
		at = visibility(g, px, at, pw, draw, e.privat, priv -> {
			String reason = priv ? HudPrivacy.reasonAgainst(config, selected) : null;
			if (reason == null)
				e.privat = priv;
		}, e.privat ? "Zeichnet der Launcher in einem eigenen Fenster. Nicht sichtbar in Discord, OBS und Screenshots."
			: "Wird ins Spielbild gezeichnet und ist im Stream und auf Screenshots zu sehen.");
		at += 16;

		// Farbe
		if (draw)
			g.textMid("Farbe", px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		at += 14 + 8;
		if (draw) {
			int sx = px;
			for (int i = 0; i < SWATCHES.length; i++) {
				int rgb = SWATCHES[i];
				boolean on = (e.textRgb & 0xFFFFFF) == rgb;
				if (on)
					g.rect(sx - 3, at - 3, 36, 36, 10, Theme.ACCENT_LINE);
				if (on)
					g.rect(sx - 1, at - 1, 32, 32, 9, 0xFF242424);
				g.rect(sx, at, 30, 30, 8, 0xFF000000 | rgb);
				int pick = rgb;
				ui.hit(sx, at, 30, 30, () -> e.textRgb = pick);
				sx += 30 + 8;
			}
		}
		at += 30 + 16;

		// Größe
		at = slider(g, px, at, pw, draw, "Größe", e.scale + " %", (e.scale - 50) / 150.0F, t -> {
			int value = Math.round((50 + t * 150) / 10.0F) * 10;
			e.scale = HudConfig.clampScale(value);
		});
		at += 16;

		// Verankert
		at = anchorRow(g, px, at, pw, draw, Math.max(0, Math.min(8, e.anchorY * 3 + e.anchorX)), cell -> setAnchor(selected, cell));
		at += 16;

		// Hintergrund
		if (draw) {
			g.textMid("Hintergrund", px, at + 10, 13, Gfx.REGULAR, Theme.TEXT_SOFT);
			String value = e.background ? "Dunkel, " + e.backgroundAlpha + " %" : "Aus";
			g.textRight(value, px + pw - 36 - 10, at + 10, 12, Gfx.REGULAR, Theme.MUTED_2);
			g.toggle(px + pw - 36, at, e.background);
			ui.hit(px, at, pw, 20, () -> e.background = !e.background);
		}
		at += 20;

		// Mehr: Format, Beschriftung, Schatten
		if (selected.variants != null && selected.variants.length > 1) {
			at += 12;
			if (draw) {
				g.textMid("Format", px, at + 10, 13, Gfx.REGULAR, Theme.TEXT_SOFT);
				String value = selected.variants[Math.min(e.variant, selected.variants.length - 1)];
				float vw = g.width(value, 12, Gfx.MEDIUM);
				int bw = Math.round(vw) + 34;
				int bx = px + pw - bw;
				boolean hover = ui.hover(bx, at - 2, bw, 24);
				g.rect(bx, at - 2, bw, 24, 7, hover ? Theme.BUTTON_HOVER : Theme.ROW);
				g.textMid(value, bx + 10, at + 10, 12, Gfx.MEDIUM, Theme.TEXT);
				g.icon(Icons.CHEVRON_RIGHT, bx + bw - 20, at + 4, 12, Theme.TEXT_DIM);
				ui.hit(bx, at - 2, bw, 24, () -> e.variant = (e.variant + 1) % selected.variants.length);
			}
			at += 20;
		}
		at += 12;
		if (draw) {
			g.textMid("Schatten", px, at + 10, 13, Gfx.REGULAR, Theme.TEXT_SOFT);
			g.toggle(px + pw - 36, at, e.shadow);
			ui.hit(px, at, pw, 20, () -> e.shadow = !e.shadow);
		}
		at += 20;
		return at + 16 - y;
	}

	private int inspectorToasts(Gfx g, int x, int y, int w, boolean draw) {
		int px = x + 16, pw = w - 32;
		int at = y + 16;
		if (draw) {
			MenuScreen.heading(g, "Widget", px, at + 6);
			g.textMid("Einblendungen", px, at + 25, 16, Gfx.SEMIBOLD, Theme.TEXT_STRONG);
			g.toggle(px + pw - 36, at + 8, MenuConfig.toastsOn);
			ui.hit(px + pw - 36, at + 8, 36, 20, () -> MenuConfig.toastsOn = !MenuConfig.toastsOn);
		}
		at += 36 + 13;
		at = visibility(g, px, at, pw, draw, MenuConfig.toastsPrivate, priv -> MenuConfig.toastsPrivate = priv,
			MenuConfig.toastsPrivate ? "Standard. Einladungen und Namen deiner Freunde landen nicht im Stream."
				: "Achtung: Einladungen und Namen deiner Freunde sind im Stream zu sehen.");
		at += 13;
		if (draw)
			g.textMid("Neue Einblendungen", px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		at += 14 + 8;
		if (draw) {
			g.box(px, at, pw, 34, 9, Theme.WELL, 0xFF343434);
			int half = (pw - 9) / 2;
			g.segment("Nach unten", Icons.ARROW_DOWN, px + 3, at + 3, half, 28, 7, MenuConfig.toastsDown, 12);
			g.segment("Nach oben", Icons.ARROW_UP, px + 6 + half, at + 3, half, 28, 7, !MenuConfig.toastsDown, 12);
			ui.hit(px + 3, at + 3, half, 28, () -> MenuConfig.toastsDown = true);
			ui.hit(px + 6 + half, at + 3, half, 28, () -> MenuConfig.toastsDown = false);
		}
		at += 34 + 8;
		if (draw)
			MenuScreen.paragraph(g, "Passt sich beim Verankern automatisch an, z. B. unten verankert = nach oben.", px, at, pw, 11.5F,
				Gfx.REGULAR, Theme.MUTED_2, 16.7F, false);
		at += 34 + 13;
		at = slider(g, px, at, pw, draw, "Anzeigedauer", MenuConfig.toastSeconds + " s", (MenuConfig.toastSeconds - 3) / 12.0F,
			t -> MenuConfig.toastSeconds = Math.round(3 + t * 12));
		at += 13;
		if (draw)
			g.textMid("Was erscheint", px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		at += 14 + 6;
		for (String[] kind : MenuConfig.TOAST_KINDS) {
			if (draw) {
				boolean on = MenuConfig.TOAST_KIND_ON.getOrDefault(kind[0], true);
				if (on)
					g.accent(px, at + 5, 15, 15, 4);
				else
					g.box(px, at + 5, 15, 15, 4, Theme.WELL, 0xFF555555);
				if (on)
					g.icon(Icons.CHECK_BOLD, px + 2.5F, at + 7.5F, 10, Theme.WHITE);
				g.textMid(kind[1], px + 24, at + 13, 12.5F, Gfx.REGULAR, Theme.TEXT_SOFT);
				ui.hit(px, at, pw, 26, () -> MenuConfig.TOAST_KIND_ON.put(kind[0], !MenuConfig.TOAST_KIND_ON.getOrDefault(kind[0], true)));
			}
			at += 26 + 4;
		}
		at += 9;
		at = slider(g, px, at, pw, draw, "Größe", MenuConfig.toastScale + " %", (MenuConfig.toastScale - 50) / 150.0F,
			t -> MenuConfig.toastScale = Math.max(50, Math.min(200, Math.round((50 + t * 150) / 10.0F) * 10)));
		at += 13;
		at = anchorRow(g, px, at, pw, draw, MenuConfig.toastAnchor, cell -> {
			MenuConfig.toastAnchor = cell;
			MenuConfig.toastX = 12;
			MenuConfig.toastY = 12;
			MenuConfig.toastsDown = cell / 3 != 2;
		});
		return at + 16 - y;
	}

	private interface Pick {
		void pick(boolean priv);
	}

	/** "Sichtbar für": Im Spiel / Privat mit Erklärung; gibt die neue Höhe zurück. */
	private int visibility(Gfx g, int px, int at, int pw, boolean draw, boolean priv, Pick pick, String hint) {
		boolean privacy = HudPrivacy.available();
		boolean shownPriv = priv && privacy;
		if (draw)
			g.textMid("Sichtbar für", px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
		at += 14 + 8;
		if (draw) {
			g.box(px, at, pw, 36, 9, Theme.WELL, 0xFF343434);
			int half = (pw - 9) / 2;
			g.segment("Im Spiel", Icons.EYE, px + 3, at + 3, half, 30, 7, !shownPriv, 12);
			g.segment("Privat", Icons.EYE_OFF, px + 6 + half, at + 3, half, 30, 7, shownPriv, 12);
			if (!privacy)
				g.rect(px + 6 + half, at + 3, half, 30, 7, Theme.alpha(Theme.WELL, 0.55F));
			ui.hit(px + 3, at + 3, half, 30, () -> pick.pick(false));
			if (privacy)
				ui.hit(px + 6 + half, at + 3, half, 30, () -> pick.pick(true));
		}
		at += 36 + 8;
		String text = privacy ? hint : "Privat braucht das Overlay-Fenster des Launchers. Bis es das gibt, steht alles im Spielbild.";
		int lines = MenuScreen.wrap(g, text, 11.5F, Gfx.REGULAR, pw).size();
		if (draw)
			MenuScreen.paragraph(g, text, px, at, pw, 11.5F, Gfx.REGULAR, Theme.MUTED_2, 16.7F, false);
		return at + Math.round(lines * 16.7F);
	}

	private interface Value {
		void set(float t);
	}

	/** Regler mit Beschriftung und Wert; gibt die neue Höhe zurück. */
	private int slider(Gfx g, int px, int at, int pw, boolean draw, String label, String value, float t, Value set) {
		if (draw) {
			g.textMid(label, px, at + 7, 12, Gfx.SEMIBOLD, Theme.MUTED);
			g.textRight(value, px + pw, at + 7, 12, Gfx.SEMIBOLD, Theme.TEXT);
		}
		at += 14 + 8;
		if (draw) {
			float clamped = Math.max(0, Math.min(1, t));
			g.rect(px, at + 6, pw, 4, 2, 0xFF3A3A3A);
			g.gradient(px, at + 6, Math.max(4, Math.round(pw * clamped)), 4, 2, 0xFFDB2777, 0xFFC026D3);
			int knob = px + Math.round((pw - 14) * clamped);
			g.rect(knob, at + 1, 14, 14, 7, Theme.WHITE);
			ui.drag(px - 6, at - 4, pw + 12, 24, mx -> set.set(Math.max(0, Math.min(1, (mx - px) / (float) pw))));
		}
		return at + 16;
	}

	private interface Cell {
		void set(int cell);
	}

	/** "Verankert" mit Namen und 3 × 3-Auswahl; gibt die neue Höhe zurück. */
	private int anchorRow(Gfx g, int px, int at, int pw, boolean draw, int anchor, Cell set) {
		if (draw) {
			g.textMid("Verankert", px, at + 12, 12, Gfx.SEMIBOLD, Theme.MUTED);
			String name = OverlayTab.ANCHOR_NAMES[anchor];
			g.textMid(Character.toUpperCase(name.charAt(0)) + name.substring(1), px, at + 31, 13, Gfx.REGULAR, Theme.TEXT);
			int gx = px + pw - 70;
			g.box(gx, at, 70, 70, 8, Theme.WELL, 0xFF343434);
			for (int i = 0; i < 9; i++) {
				int cx = gx + 4 + (i % 3) * 21, cy = at + 4 + (i / 3) * 21;
				boolean on = i == anchor;
				boolean hover = ui.hover(cx, cy, 18, 18);
				if (on)
					g.accent(cx, cy, 18, 18, 4);
				else
					g.rect(cx, cy, 18, 18, 4, hover ? 0xFF4A4A4A : Theme.BUTTON);
				int cell = i;
				ui.hit(cx, cy, 18, 18, () -> set.set(cell));
			}
		}
		return at + 70;
	}

	/** Neue Verankerung, das Widget bleibt dabei an seiner Stelle. */
	private void setAnchor(HudModule module, int cell) {
		HudConfig.Entry e = config.get(module);
		Piece p = pieceOf(module);
		e.autoAnchor = false;
		e.anchorX = cell % 3;
		e.anchorY = cell / 3;
		if (p != null)
			HudOverlay.move(e, p.element(), p.x(), p.y(), p.w(), p.h(), vw, vh);
	}

	private Piece pieceOf(HudModule module) {
		for (Piece p : pieces)
			if (!p.toasts() && p.element().module() == module && !p.element().isSlot())
				return p;
		return null;
	}

	private void hint(Gfx g) {
		String[] parts = { "Ziehen zum Verschieben", "Ecken zum Skalieren", "Rastet am Raster und an Widgets ein" };
		float w = 14;
		for (String p : parts)
			w += g.width(p, 12, Gfx.REGULAR) + 28;
		w += g.width("Esc", 12, Gfx.BOLD) + g.width(" beendet", 12, Gfx.REGULAR) + 14;
		int x = Math.round((g.width - w) / 2), y = g.height - 112 - 34;
		g.rect(x, y, Math.round(w), 34, 17, Theme.alpha(0xFF141414, 0.82F));
		g.outline(x, y, Math.round(w), 34, 17, 0xFF333333);
		float at = x + 14;
		for (String p : parts) {
			g.textMid(p, at, y + 17, 12, Gfx.REGULAR, Theme.MUTED);
			at += g.width(p, 12, Gfx.REGULAR) + 14 - 3;
			g.textMid("·", at, y + 17, 12, Gfx.REGULAR, 0xFF555555);
			at += 17;
		}
		g.textMid("Esc", at, y + 17, 12, Gfx.BOLD, Theme.TEXT);
		g.textMid(" beendet", at + g.width("Esc", 12, Gfx.BOLD), y + 17, 12, Gfx.REGULAR, Theme.MUTED);
	}

	// ---------- Eingabe ----------

	@Override
	public boolean mouseClicked(MouseButtonEvent event, boolean doubled) {
		if (event.button() != InputConstants.MOUSE_BUTTON_LEFT)
			return true;
		Gfx g = Gfx.measure(width, height);
		boolean menuWasOpen = profileMenu || addMenu;
		if (ui.click(g.mx(event.x()), g.my(event.y())))
			return true;
		profileMenu = false;
		addMenu = false;
		if (menuWasOpen)
			return true;
		int mx = (int) (event.x() / hs), my = (int) (event.y() / hs);
		// Erst die Ecken des gewählten Widgets (Skalieren), dann die Widgets selbst (oberstes zuerst)
		for (Piece p : pieces)
			if (isSelected(p) && !p.toasts())
				for (int[] c : corners(p))
					if (mx >= c[0] - 2 && my >= c[1] - 2 && mx <= c[0] + c[2] + 2 && my <= c[1] + c[2] + 2) {
						dragging = p;
						resizing = true;
						startScale = config.get(p.element().module()).scale;
						startW = Math.max(1, p.w());
						grabX = p.x();
						grabY = p.y();
						return true;
					}
		for (int i = pieces.size() - 1; i >= 0; i--) {
			Piece p = pieces.get(i);
			if (mx >= p.x() - 2 && my >= p.y() - 2 && mx < p.x() + p.w() + 2 && my < p.y() + p.h() + 2) {
				toastsSelected = p.toasts();
				if (!p.toasts())
					selected = p.element().module();
				dragging = p;
				resizing = false;
				grabX = mx - p.x();
				grabY = my - p.y();
				return true;
			}
		}
		return true;
	}

	@Override
	public boolean mouseDragged(MouseButtonEvent event, double dx, double dy) {
		Gfx g = Gfx.measure(width, height);
		if (ui.dragTo(g.mx(event.x())))
			return true;
		if (dragging == null)
			return true;
		int mx = (int) (event.x() / hs), my = (int) (event.y() / hs);
		if (resizing) {
			HudConfig.Entry e = config.get(dragging.element().module());
			int newW = Math.max(4, mx - grabX);
			int scale = Math.round(startScale * newW / (float) startW / HudConfig.SCALE_STEP) * HudConfig.SCALE_STEP;
			e.scale = HudConfig.clampScale(scale);
			return true;
		}
		int x = mx - grabX, y = my - grabY;
		if (dragging.toasts()) {
			moveToasts(g, x, y);
			return true;
		}
		List<int[]> others = new ArrayList<>();
		for (Piece p : pieces)
			if (p != dragging)
				others.add(new int[] { p.x(), p.y(), p.w(), p.h() });
		HudSnap.Result r = HudSnap.apply(config, x, y, dragging.w(), dragging.h(), others, vw, vh);
		guideX = r.guideX;
		guideY = r.guideY;
		HudOverlay.move(config.get(dragging.element().module()), dragging.element(), r.x, r.y, dragging.w(), dragging.h(), vw, vh);
		dragging = new Piece(dragging.element(), r.x, r.y, dragging.w(), dragging.h());
		return true;
	}

	/** Einblendungen verschieben: nächstgelegener Anker, Abstand in Design-Pixeln. */
	private void moveToasts(Gfx g, int hudX, int hudY) {
		float k = hs / g.scale;
		int x = Math.round(hudX * k), y = Math.round(hudY * k);
		int w = Math.round(dragging.w() * k), h = Math.round(dragging.h() * k);
		int cx = x + w / 2, cy = y + h / 2;
		int ax = cx < g.width / 3 ? 0 : cx > g.width * 2 / 3 ? 2 : 1;
		int ay = cy < g.height / 3 ? 0 : cy > g.height * 2 / 3 ? 2 : 1;
		MenuConfig.toastAnchor = ay * 3 + ax;
		MenuConfig.toastX = ax == 0 ? x : ax == 1 ? x - (g.width - w) / 2 : g.width - w - x;
		MenuConfig.toastY = ay == 0 ? y : ay == 1 ? y - (g.height - h) / 2 : g.height - h - y;
		dragging = new Piece(null, hudX, hudY, dragging.w(), dragging.h());
	}

	@Override
	public boolean mouseReleased(MouseButtonEvent event) {
		ui.release();
		dragging = null;
		resizing = false;
		guideX = -1;
		guideY = -1;
		return true;
	}

	@Override
	public boolean mouseScrolled(double mouseX, double mouseY, double horizontal, double vertical) {
		if (toastsSelected) {
			MenuConfig.toastScale = Math.max(50, Math.min(200, MenuConfig.toastScale + (vertical > 0 ? 10 : -10)));
			return true;
		}
		if (selected != null) {
			HudConfig.Entry e = config.get(selected);
			e.scale = HudConfig.clampScale(e.scale + (vertical > 0 ? HudConfig.SCALE_STEP : -HudConfig.SCALE_STEP));
		}
		return true;
	}

	@Override
	public boolean keyPressed(KeyEvent event) {
		if (event.isEscape() || Keys.edit != null && Keys.edit.matches(event)) {
			onClose();
			return true;
		}
		// Pfeiltasten schieben das gewählte Widget um einen Punkt (mit Umschalt um zehn)
		int step = event.hasShiftDown() ? 10 : 1;
		int dx = event.isLeft() ? -step : event.isRight() ? step : 0;
		int dy = event.isUp() ? -step : event.isDown() ? step : 0;
		if ((dx != 0 || dy != 0) && selected != null && !toastsSelected) {
			Piece p = pieceOf(selected);
			if (p != null)
				HudOverlay.move(config.get(selected), p.element(), p.x() + dx, p.y() + dy, p.w(), p.h(), vw, vh);
			return true;
		}
		return true;
	}

	@Override
	public boolean charTyped(CharacterEvent event) {
		return true;
	}

	@Override
	public void onClose() {
		config.save();
		MenuConfig.save();
		if (parent != null)
			MenuScreen.open(MenuScreen.Tab.OVERLAY);
		else
			super.onClose();
	}

	@Override
	public boolean isPauseScreen() {
		return false;
	}
}
