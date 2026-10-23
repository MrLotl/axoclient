package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.InputConstants;
import de.axoclient.mod.emote.Emotes;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

/**
 * Das Emote-Rad (Mockup "wheel"): Taste gedrückt halten, Maus in eine Richtung bewegen, loslassen spielt ab. Ein Klick
 * spielt sofort, Rechtsklick oder Esc bricht ab.
 */
public final class EmoteWheelScreen extends Screen {
	private static final int SIZE = 460;
	private static final int RADIUS = 170;
	private static final int SLOT = 92;
	/** Innerhalb der Mitte ist nichts gewählt. */
	private static final int DEAD_ZONE = 60;

	private int hovered = -1;
	/** Erst nach dem ersten Tick auf das Loslassen achten (sonst schließt ein kurzes Antippen sofort). */
	private int ticks;
	private boolean done;

	public EmoteWheelScreen() {
		super(Component.literal("Emote-Rad"));
	}

	@Override
	public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
		Gfx g = Gfx.begin(graphics, width, height);
		try {
			int cx = g.width / 2, cy = g.height / 2;
			hovered = indexAt(g.mx(mouseX) - cx, g.my(mouseY) - cy);
			draw(g, cx, cy);
		} finally {
			g.end();
		}
	}

	private void draw(Gfx g, int cx, int cy) {
		// Abgedunkelt zur Mitte hin
		g.rect(cx - 420, cy - 420, 840, 840, 420, Theme.alpha(0xFF0A0A0E, 0.18F));
		g.rect(cx - 300, cy - 300, 600, 600, 300, Theme.alpha(0xFF0A0A0E, 0.22F));
		int x = cx - SIZE / 2, y = cy - SIZE / 2;
		g.shadow(x, y, SIZE, SIZE, SIZE / 2, 30, 24, 0.5F);
		g.rect(x, y, SIZE, SIZE, SIZE / 2, Theme.alpha(0xFF1E1E22, 0.94F));
		g.outline(x, y, SIZE, SIZE, SIZE / 2, Theme.alpha(0xFFFFFFFF, 0.08F));

		Emotes.Emote hover = null;
		for (int i = 0; i < 8; i++) {
			String id = MenuData.SLOTS.get(i);
			Emotes.Emote e = id == null ? null : Emotes.find(id);
			double a = i * Math.PI / 4;
			int sx = (int) Math.round(cx + RADIUS * Math.sin(a));
			int sy = (int) Math.round(cy - RADIUS * Math.cos(a));
			boolean on = hovered == i && e != null;
			if (on) {
				hover = e;
				// Zeiger von der Mitte zum gewählten Platz
				for (int k = 0; k < 24; k++) {
					float t = k / 24.0F;
					int px = Math.round(cx + (sx - cx) * (0.38F + t * 0.32F));
					int py = Math.round(cy + (sy - cy) * (0.38F + t * 0.32F));
					g.dot(px, py, 3, Theme.alpha(0xFFEC4899, 0.15F + 0.65F * t));
				}
			}
		}
		for (int i = 0; i < 8; i++) {
			String id = MenuData.SLOTS.get(i);
			Emotes.Emote e = id == null ? null : Emotes.find(id);
			double a = i * Math.PI / 4;
			int sx = (int) Math.round(cx + RADIUS * Math.sin(a));
			int sy = (int) Math.round(cy - RADIUS * Math.cos(a));
			boolean on = hovered == i && e != null;
			int size = on ? Math.round(SLOT * 1.1F) : SLOT;
			int bx = sx - size / 2, by = sy - size / 2;
			if (on) {
				g.shadow(bx, by, size, size, size / 2, 12, 10, 0.4F);
				g.gradient(bx, by, size, size, size / 2, Theme.ACCENT_FROM, Theme.ACCENT_TO);
			} else if (e != null) {
				g.rect(bx, by, size, size, size / 2, Theme.alpha(0xFF343438, 0.9F));
				g.outline(bx, by, size, size, size / 2, Theme.alpha(0xFFFFFFFF, 0.06F));
			} else
				g.outline(bx, by, size, size, size / 2, Theme.alpha(0xFFFFFFFF, 0.12F));
			if (e != null) {
				float k = size / (float) SLOT;
				g.icon(Icons.emote(e.id()), sx - 20 * k, sy - 26 * k, 40 * k, on ? Theme.WHITE : Theme.TEXT);
				g.textCentered(g.ellipsize(e.label(), 11.5F * k, Gfx.SEMIBOLD, size - 14), sx, sy + 24 * k, 11.5F * k, Gfx.SEMIBOLD,
					on ? Theme.WHITE : Theme.TEXT);
			} else
				g.textCentered("Leer", sx, sy, 11.5F, Gfx.REGULAR, Theme.FAINT);
		}

		// Mitte
		g.rect(cx - 76, cy - 76, 152, 152, 76, Theme.alpha(0xFFE07AD8, 0.08F));
		g.rect(cx - 70, cy - 70, 140, 140, 70, Theme.INSET);
		g.outline(cx - 70, cy - 70, 140, 140, 70, Theme.WINDOW_LINE);
		g.textCentered(g.ellipsize(hover == null ? "Leer" : hover.label(), 16, Gfx.SEMIBOLD, 120), cx, cy - 9, 16, Gfx.SEMIBOLD,
			Theme.TEXT_STRONG);
		MenuScreen.paragraph(g, hover == null ? "Maus in Richtung bewegen" : "Loslassen zum Abspielen", cx - 55, cy + 4, 110, 11.5F,
			Gfx.REGULAR, Theme.MUTED_2, 16, true);

		// Hinweis unten
		String key = Keys.label(Keys.wheel);
		String[] parts = { "gedrückt halten", "Maus in Richtung bewegen", "Rechtsklick bricht ab" };
		float tw = 0;
		for (String p : parts)
			tw += g.width(p, 12, Gfx.REGULAR);
		float kw = g.width(key, 11, Gfx.BOLD) + 12;
		float total = 14 + kw + 10 + tw + 2 * (10 + 5 + 10) + 14;
		int hx = Math.round(cx - total / 2), hy = g.height - 112 - 34;
		g.rect(hx, hy, Math.round(total), 34, 17, Theme.alpha(0xFF141414, 0.82F));
		g.outline(hx, hy, Math.round(total), 34, 17, 0xFF333333);
		int at = hx + 14;
		at += g.kbd(key, at, hy + 17, 20, 11, Theme.BUTTON_2, Theme.TEXT_STRONG) + 10;
		for (int i = 0; i < parts.length; i++) {
			g.textMid(parts[i], at, hy + 17, 12, Gfx.REGULAR, Theme.MUTED);
			at += Math.round(g.width(parts[i], 12, Gfx.REGULAR));
			if (i < parts.length - 1) {
				g.textMid("·", at + 10, hy + 17, 12, Gfx.REGULAR, 0xFF555555);
				at += 25;
			}
		}
	}

	/** Platz in Richtung (dx, dy) von der Mitte, oben beginnend im Uhrzeigersinn, oder -1. */
	private static int indexAt(double dx, double dy) {
		if (dx * dx + dy * dy < DEAD_ZONE * DEAD_ZONE)
			return -1;
		double angle = Math.atan2(dx, -dy);
		int index = (int) Math.floor((angle + Math.PI / 8) / (Math.PI / 4));
		return Math.floorMod(index, 8);
	}

	@Override
	public void tick() {
		super.tick();
		ticks++;
		if (ticks > 1 && !done && !Keys.held(Keys.wheel))
			playAndClose();
	}

	private void playAndClose() {
		done = true;
		String id = hovered >= 0 ? MenuData.SLOTS.get(hovered) : null;
		onClose();
		if (id != null)
			Emotes.playLocal(id);
	}

	@Override
	public boolean mouseClicked(MouseButtonEvent event, boolean doubled) {
		if (event.button() == InputConstants.MOUSE_BUTTON_LEFT) {
			playAndClose();
			return true;
		}
		if (event.button() == InputConstants.MOUSE_BUTTON_RIGHT) {
			done = true;
			onClose();
			return true;
		}
		return true;
	}

	@Override
	public boolean keyPressed(KeyEvent event) {
		if (event.isEscape()) {
			done = true;
			onClose();
			return true;
		}
		return true;
	}

	@Override
	public void extractBackground(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
		// Kein Weichzeichner: das Spiel bleibt sichtbar
	}

	@Override
	public boolean isPauseScreen() {
		return false;
	}
}
