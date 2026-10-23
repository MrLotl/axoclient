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
	private boolean gridMenu;
	private int gridX;
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
			gear(g, mouseX, mouseY);
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
		String gridLabel = "Raster " + (config.grid ? HudConfig.clampGrid(config.gridSize) + " px" : "aus");
		int gridW = Math.round(12 + 15 + 7 + g.width(gridLabel, 12.5F, Gfx.MEDIUM) + 8 + 14 + 10);
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
			gridMenu = false;
		});
		bx += profileW + 4;

		// Raster (öffnet die Einstellungen: an/aus, Weite, Einrasten)
		gridX = bx;
		hover = ui.hover(bx, y + 6, gridW, 36);
		if (config.grid) {
			g.gradient(bx, y + 6, gridW, 36, 8, Theme.alpha(0xFFEC4899, 0.16F), Theme.alpha(0xFF8B5CF6, 0.16F));
		} else if (hover || gridMenu)
			g.rect(bx, y + 6, gridW, 36, 8, Theme.BUTTON_HOVER);
		int gc = config.grid ? Theme.ACCENT_TEXT : Theme.TEXT_SOFT;
		g.icon(Icons.GRID, bx + 12, cy - 7.5F, 15, gc);
		g.textMid(gridLabel, bx + 12 + 15 + 7, cy, 12.5F, Gfx.MEDIUM, gc);
		g.icon(Icons.CHEVRON_DOWN, bx + gridW - 10 - 14, cy - 7, 14, Theme.TEXT_DIM);
		ui.hit(bx, y + 6, gridW, 36, () -> {
			gridMenu = !gridMenu;
			profileMenu = false;
			addMenu = false;
		});
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
			gridMenu = false;
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
		if (gridMenu) {
			int w = 250;
			int x = gridX, y = toolbarY + 48 + 6;
			int h = 16 + 20 + 16 + 38 + 16 + 20 + 16;
			g.shadow(x, y, w, h, 10, 14, 10, 0.5F);
			g.box(x, y, w, h, 10, Theme.alpha(0xFF242424, 0.98F), Theme.WINDOW_LINE);
			ui.block(x, y, w, h);
			int px = x + 16, pw = w - 32;
			int at = y + 16;
			g.textMid("Raster anzeigen", px, at + 10, 13, Gfx.REGULAR, Theme.TEXT_SOFT);
			g.toggle(px + pw - 36, at, config.grid);
			ui.hit(px, at, pw, 20, () -> config.grid = !config.grid);
			at += 20 + 16;
			int size = HudConfig.clampGrid(config.gridSize);
			at = slider(g, px, at, pw, true, "Weite", size + " px",
				(size - HudConfig.MIN_GRID) / (float) (HudConfig.MAX_GRID - HudConfig.MIN_GRID), t -> {
					int steps = Math.round(t * (HudConfig.MAX_GRID - HudConfig.MIN_GRID) / HudConfig.GRID_STEP);
					config.gridSize = HudConfig.clampGrid(HudConfig.MIN_GRID + steps * HudConfig.GRID_STEP);
				});
			at += 16;
			g.textMid("An Widgets einrasten", px, at + 10, 13, Gfx.REGULAR, Theme.TEXT_SOFT);
			g.toggle(px + pw - 36, at, config.snap);
			ui.hit(px, at, pw, 20, () -> config.snap = !config.snap);
		}
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

	/** Widget, über dem gerade das Zahnrad steht (bleibt, solange die Maus auf Widget oder Zahnrad ist). */
	private Piece gearOn;

	/** Zahnrad oben rechts am Widget unter der Maus: öffnet dessen Einstellungen im Axo-Menü. */
	private void gear(Gfx g, int mouseX, int mouseY) {
		if (dragging != null || profileMenu || addMenu || gridMenu) {
			gearOn = null;
			return;
		}
		float k = hs / g.scale; // Einheiten der Widgets → Design-Pixel
		int mx = g.mx(mouseX), my = g.my(mouseY);
		Piece over = null;
		for (int i = pieces.size() - 1; i >= 0 && over == null; i--) {
			Piece p = pieces.get(i);
			int x0 = Math.round(p.x() * k), y0 = Math.round(p.y() * k), x1 = Math.round((p.x() + p.w()) * k), y1 = Math.round((p.y() + p.h()) * k);
			// Etwas großzügiger, damit man vom Widget zum Zahnrad fahren kann
			if (mx >= x0 - 6 && my >= y0 - 20 && mx < x1 + 16 && my < y1 + 6 && (gearOn == null || samePiece(gearOn, p)
				|| mx >= x0 && my >= y0 && mx < x1 && my < y1))
				over = p;
		}
		gearOn = over;
		if (over == null)
			return;
		int size = 24;
		int bx = Math.round((over.x() + over.w()) * k) - size / 2 + 4, by = Math.round(over.y() * k) - size / 2 - 6;
		boolean hover = ui.hover(bx, by, size, size);
		g.shadow(bx, by, size, size, size / 2, 8, 4, 0.4F);
		if (hover)
			g.accent(bx, by, size, size, size / 2);
		else
			g.rect(bx, by, size, size, size / 2, 0xFF2C2C2C);
		g.outline(bx, by, size, size, size / 2, hover ? Theme.alpha(0xFFFFFFFF, 0.25F) : Theme.WINDOW_LINE);
		g.icon(Icons.SETTINGS, bx + 5, by + 5, 14, Theme.WHITE);
		Piece target = over;
		ui.hit(bx, by, size, size, () -> openSettings(target));
	}

	private static boolean samePiece(Piece a, Piece b) {
		return a.toasts() ? b.toasts() : !b.toasts() && a.element().module() == b.element().module() && a.element().slot() == b.element().slot();
	}

	/** Bearbeiten beenden (speichern) und die Einstellungen des Widgets im Menü zeigen. */
	private void openSettings(Piece p) {
		config.save();
		MenuConfig.save();
		MenuScreen.openWidget(p.toasts() ? null : p.element().module());
	}

	private Piece pieceOf(HudModule module) {
		for (Piece p : pieces)
			if (!p.toasts() && p.element().module() == module && !p.element().isSlot())
				return p;
		return null;
	}

	private void hint(Gfx g) {
		String[] parts = { "Ziehen zum Verschieben", "Ecken zum Skalieren", "Zahnrad für Einstellungen" };
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
		boolean menuWasOpen = profileMenu || addMenu || gridMenu;
		if (ui.click(g.mx(event.x()), g.my(event.y())))
			return true;
		profileMenu = false;
		addMenu = false;
		gridMenu = false;
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
