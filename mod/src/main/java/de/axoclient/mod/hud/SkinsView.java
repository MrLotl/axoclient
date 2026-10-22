package de.axoclient.mod.hud;

import java.util.List;

/**
 * Bereich "Skins & Capes" im AxoClient-Fenster: links die Skin-Bibliothek und die Umhänge, rechts eine Vorschau der
 * Figur von vorne und hinten. Skins wählt man aus und lädt sie mit "Anwenden" hoch; Umhänge gelten sofort.
 */
final class SkinsView {
	static final int PREVIEW_WIDTH = 108;
	private static final int GAP = 4;
	private static final int SKIN_CARD_MIN = 56;
	private static final int SKIN_CARD_HEIGHT = 70;
	private static final int CAPE_CARD = 48;
	private static final int CAPE_CARD_HEIGHT = 68;
	private static final int LABEL = 12;
	private static final int STATUS = 12;
	private static final int TAB_HEIGHT = 12;

	private final AxoPlatform platform;
	final SkinStore store;
	private SkinStore.Skin selected;
	private boolean axoTab = true;
	private int scroll;
	private boolean picked;

	SkinsView(AxoPlatform platform) {
		this.platform = platform;
		this.store = new SkinStore(platform);
		this.axoTab = store.hasAxo();
		store.refresh();
	}

	String countText() {
		return store.hasLibrary() ? store.skins.size() + (store.skins.size() == 1 ? " Skin" : " Skins") : "";
	}

	void addSkin() {
		store.addFromFile(skin -> selected = skin);
	}

	// ---------- Aufteilung ----------

	private int leftWidth(int width) {
		return width - PREVIEW_WIDTH - 8;
	}

	private int columns(int left) {
		return Math.max(2, (left + GAP) / (SKIN_CARD_MIN + GAP));
	}

	private int skinsHeight(int left) {
		if (!store.hasLibrary() || store.skins.isEmpty())
			return LABEL + 24;
		int rows = (store.skins.size() + columns(left) - 1) / columns(left);
		return LABEL + rows * (SKIN_CARD_HEIGHT + GAP);
	}

	private List<SkinStore.Cape> capes() {
		return axoTab ? store.axoCapes : store.mojangCapes;
	}

	private int capeColumns(int left) {
		return Math.max(1, (left + GAP) / (CAPE_CARD + GAP));
	}

	private int contentHeight(int left) {
		int capes = capes().size() + 1;
		int rows = (capes + capeColumns(left) - 1) / capeColumns(left);
		return skinsHeight(left) + 6 + LABEL + 4 + rows * (CAPE_CARD_HEIGHT + GAP);
	}

	private void clamp(int left, int visible) {
		scroll = Math.max(0, Math.min(scroll, Math.max(0, contentHeight(left) - visible)));
	}

	void scroll(double amount) {
		scroll -= (int) Math.round(amount * 20);
	}

	// ---------- Zeichnen ----------

	void render(HudPainter painter, int x, int y, int width, int height, int mouseX, int mouseY) {
		int left = leftWidth(width);
		int listBottom = y + height - STATUS - 2;
		clamp(left, listBottom - y);
		if (selected == null && !picked && store.activeSkin != null)
			store.skins.stream().filter(s -> s.id.equals(store.activeSkin)).findFirst().ifPresent(s -> selected = s);

		int top = y - scroll;
		label(painter, "DEINE SKINS" + (store.hasLibrary() ? " · AUS DEM LAUNCHER" : ""), x, top, y, listBottom);
		top += LABEL;
		if (!store.hasLibrary()) {
			if (top >= y && top + 20 <= listBottom)
				painter.text("Starte das Spiel über den AxoClient-Launcher, um deine Skins zu sehen.", x, top + 4, HudSkin.MUTED, false);
		} else if (store.skins.isEmpty()) {
			if (top >= y && top + 20 <= listBottom)
				painter.text("Noch keine Skins. Mit „+ Skin“ fügst du einen hinzu.", x, top + 4, HudSkin.MUTED, false);
		} else {
			int columns = columns(left);
			int cardWidth = (left - (columns - 1) * GAP) / columns;
			for (int i = 0; i < store.skins.size(); i++) {
				SkinStore.Skin skin = store.skins.get(i);
				int cx = x + (i % columns) * (cardWidth + GAP);
				int cy = top + (i / columns) * (SKIN_CARD_HEIGHT + GAP);
				if (cy < y || cy + SKIN_CARD_HEIGHT > listBottom)
					continue;
				boolean chosen = skin == selected;
				boolean hovered = HudSkin.inside(mouseX, mouseY, cx, cy, cardWidth, SKIN_CARD_HEIGHT);
				card(painter, cx, cy, cardWidth, SKIN_CARD_HEIGHT, chosen, hovered);
				Object texture = platform.texture("skin/" + skin.id, skin.png());
				figure(painter, texture, skin.png(), skin.slim, false, null, null,
					cx + cardWidth / 2 - 12, cy + 5, 1.5F);
				String name = fit(painter, skin.name, cardWidth - 6);
				HudSkin.centered(painter, name, cx + cardWidth / 2, cy + SKIN_CARD_HEIGHT - 12,
					chosen ? HudSkin.TEXT : HudSkin.TEXT_DIM);
				if (skin.id.equals(store.activeSkin))
					painter.fill(cx + cardWidth - 7, cy + 4, 3, 3, 0xFF5DCA8A);
			}
		}
		top = y - scroll + skinsHeight(left) + 6;

		if (top >= y && top + LABEL <= listBottom) {
			label(painter, "CAPES", x, top, y, listBottom);
			int tabX = x + left - 104;
			tabs(painter, tabX, top - 2, axoTab, mouseX, mouseY);
		}
		top += LABEL + 4;
		List<SkinStore.Cape> capes = capes();
		String active = axoTab ? store.activeAxoCape : store.activeMojangCape;
		int capeColumns = capeColumns(left);
		for (int i = 0; i <= capes.size(); i++) {
			int cx = x + (i % capeColumns) * (CAPE_CARD + GAP);
			int cy = top + (i / capeColumns) * (CAPE_CARD_HEIGHT + GAP);
			if (cy < y || cy + CAPE_CARD_HEIGHT > listBottom)
				continue;
			SkinStore.Cape cape = i == 0 ? null : capes.get(i - 1);
			boolean on = cape == null ? active == null : cape.id.equals(active);
			card(painter, cx, cy, CAPE_CARD, CAPE_CARD_HEIGHT, on, HudSkin.inside(mouseX, mouseY, cx, cy, CAPE_CARD, CAPE_CARD_HEIGHT));
			if (cape == null) {
				HudSkin.outline(painter, cx + 9, cy + 5, 30, 48, 3, 0xFF4A4652);
				HudSkin.centered(painter, "–", cx + CAPE_CARD / 2, cy + 25, HudSkin.MUTED);
			} else if (cape.png != null) {
				capeFront(painter, cape, cx + 9, cy + 5, 3);
			}
			HudSkin.centered(painter, fit(painter, cape == null ? "Keins" : cape.name, CAPE_CARD - 4),
				cx + CAPE_CARD / 2, cy + CAPE_CARD_HEIGHT - 11, on ? HudSkin.TEXT : HudSkin.MUTED);
		}
		if (capes.isEmpty() && top >= y && top + CAPE_CARD_HEIGHT <= listBottom) {
			String hint = axoTab
				? (store.hasAxo() ? (store.loadingAxo ? "Lädt …" : "Keine AxoClient-Umhänge.") : "Nur mit dem AxoClient-Launcher.")
				: (store.loadingProfile ? "Lädt …" : "Dein Konto hat keine Minecraft-Umhänge.");
			painter.text(hint, x + CAPE_CARD + 8, top + 28, HudSkin.MUTED, false);
		}

		if (!store.status.isEmpty())
			painter.text(fit(painter, store.status, left), x, y + height - STATUS + 2, store.busy ? HudSkin.ACCENT_TEXT : HudSkin.TEXT_DIM, false);

		renderPreview(painter, x + left + 8, y, PREVIEW_WIDTH, height, mouseX, mouseY);
	}

	private void renderPreview(HudPainter painter, int x, int y, int width, int height, int mouseX, int mouseY) {
		HudSkin.rounded(painter, x, y, width, height, 6, HudSkin.ROW_BACKGROUND);
		HudSkin.outline(painter, x, y, width, height, 6, HudSkin.ROW_BORDER);
		HudSkin.centered(painter, "VORSCHAU", x + width / 2, y + 7, HudSkin.MUTED);

		byte[] png = selected != null ? selected.png() : store.accountSkin;
		boolean slim = selected != null ? selected.slim : store.accountSlim;
		SkinStore.Cape cape = shownCape();
		if (png != null && png.length > 0) {
			Object texture = platform.texture(selected != null ? "skin/" + selected.id : "account/" + png.length, png);
			figure(painter, texture, png, slim, false, null, null, x + width / 2 - 34, y + 22, 2);
			figure(painter, texture, png, slim, true, cape, cape == null ? null : cape.png, x + width / 2 + 2, y + 22, 2);
		} else {
			HudSkin.centered(painter, store.loadingProfile ? "Lädt …" : "Kein Skin", x + width / 2, y + 50, HudSkin.MUTED);
		}

		String name = selected != null ? selected.name : "Aktueller Skin";
		HudSkin.centered(painter, fit(painter, name, width - 8), x + width / 2, y + 92, HudSkin.TEXT_DIM);

		int segmentY = y + 106;
		segment(painter, x + 8, segmentY, width - 16, "Breit", "Schmal", !slim, mouseX, mouseY, selected != null);

		boolean canApply = selected != null && !store.busy;
		int buttonY = y + height - 22;
		HudSkin.button(painter, x + 8, buttonY, width - 16, 15, store.busy ? "Bitte warten …" : "Anwenden",
			canApply && HudSkin.inside(mouseX, mouseY, x + 8, buttonY, width - 16, 15), canApply);
	}

	/** Welcher Umhang auf dem Rücken zu sehen ist: AxoClient überdeckt Minecraft. */
	private SkinStore.Cape shownCape() {
		for (SkinStore.Cape cape : store.axoCapes)
			if (cape.id.equals(store.activeAxoCape))
				return cape;
		for (SkinStore.Cape cape : store.mojangCapes)
			if (cape.id.equals(store.activeMojangCape))
				return cape;
		return null;
	}

	// ---------- Klicks ----------

	boolean click(int x, int y, int width, int height, int mouseX, int mouseY) {
		int left = leftWidth(width);
		int listBottom = y + height - STATUS - 2;
		int px = x + left + 8;
		if (HudSkin.inside(mouseX, mouseY, px, y, PREVIEW_WIDTH, height)) {
			if (selected != null && HudSkin.inside(mouseX, mouseY, px + 8, y + 106, PREVIEW_WIDTH - 16, TAB_HEIGHT)) {
				boolean slim = mouseX >= px + 8 + (PREVIEW_WIDTH - 16) / 2;
				if (slim != selected.slim)
					store.setSlim(selected, slim);
				return true;
			}
			if (selected != null && HudSkin.inside(mouseX, mouseY, px + 8, y + height - 22, PREVIEW_WIDTH - 16, 15))
				store.applySkin(selected);
			return true;
		}
		if (mouseY < y || mouseY >= listBottom)
			return false;

		int top = y - scroll + LABEL;
		if (store.hasLibrary() && !store.skins.isEmpty()) {
			int columns = columns(left);
			int cardWidth = (left - (columns - 1) * GAP) / columns;
			for (int i = 0; i < store.skins.size(); i++) {
				int cx = x + (i % columns) * (cardWidth + GAP);
				int cy = top + (i / columns) * (SKIN_CARD_HEIGHT + GAP);
				if (HudSkin.inside(mouseX, mouseY, cx, cy, cardWidth, SKIN_CARD_HEIGHT)) {
					selected = store.skins.get(i);
					picked = true;
					return true;
				}
			}
		}
		top = y - scroll + skinsHeight(left) + 6;
		int tabX = x + left - 104;
		if (HudSkin.inside(mouseX, mouseY, tabX, top - 2, 104, TAB_HEIGHT)) {
			axoTab = mouseX < tabX + 52;
			return true;
		}
		top += LABEL + 4;
		List<SkinStore.Cape> capes = capes();
		int capeColumns = capeColumns(left);
		for (int i = 0; i <= capes.size(); i++) {
			int cx = x + (i % capeColumns) * (CAPE_CARD + GAP);
			int cy = top + (i / capeColumns) * (CAPE_CARD_HEIGHT + GAP);
			if (!HudSkin.inside(mouseX, mouseY, cx, cy, CAPE_CARD, CAPE_CARD_HEIGHT))
				continue;
			SkinStore.Cape cape = i == 0 ? null : capes.get(i - 1);
			if (axoTab)
				store.selectAxoCape(cape);
			else
				store.selectMojangCape(cape);
			return true;
		}
		return false;
	}

	// ---------- Bausteine ----------

	private static void label(HudPainter painter, String text, int x, int y, int top, int bottom) {
		if (y >= top && y + 8 <= bottom)
			painter.text(text, x, y, HudSkin.MUTED, false);
	}

	private static void card(HudPainter painter, int x, int y, int width, int height, boolean chosen, boolean hovered) {
		HudSkin.rounded(painter, x, y, width, height, 5, chosen ? HudSkin.NAV_ACTIVE : (hovered ? 0xFF2A2830 : HudSkin.ROW_BACKGROUND));
		HudSkin.outline(painter, x, y, width, height, 5, chosen ? HudSkin.ACCENT : HudSkin.ROW_BORDER);
	}

	private static void tabs(HudPainter painter, int x, int y, boolean first, int mouseX, int mouseY) {
		segment(painter, x, y, 104, "AxoClient", "Minecraft", first, mouseX, mouseY, true);
	}

	/** Zwei Knöpfe nebeneinander, einer gewählt (wie die Umschalter im Launcher). */
	private static void segment(HudPainter painter, int x, int y, int width, String a, String b, boolean firstOn,
								int mouseX, int mouseY, boolean enabled) {
		HudSkin.rounded(painter, x, y, width, TAB_HEIGHT, 4, 0xFF18171C);
		int half = width / 2;
		int onX = firstOn ? x + 1 : x + half;
		if (enabled)
			HudSkin.rounded(painter, onX, y + 1, half - 1, TAB_HEIGHT - 2, 3, 0xFF3A2140);
		HudSkin.centered(painter, a, x + half / 2, y + 2, firstOn && enabled ? HudSkin.TEXT : HudSkin.MUTED);
		HudSkin.centered(painter, b, x + half + half / 2, y + 2, !firstOn && enabled ? HudSkin.TEXT : HudSkin.MUTED);
	}

	/** Vorderseite des Umhangs (10x16 Pixel der Vorlage) in {@code scale}-facher Größe. */
	private void capeFront(HudPainter painter, SkinStore.Cape cape, int x, int y, int scale) {
		int[] size = SkinStore.size(cape.png);
		Object texture = platform.texture("cape/" + cape.id, cape.png);
		if (texture == null || size == null)
			return;
		int k = Math.max(1, size[0] / 64);
		painter.image(texture, x, y, 10 * scale, 16 * scale, k, k, 10 * k, 16 * k, size[0], size[1]);
	}

	/**
	 * Flache Figur aus der Skin-Textur, 16x32 Vorlagenpixel groß, mit Kopf, Körper, Armen, Beinen und der zweiten
	 * Schicht darüber. Von hinten liegt der Umhang über dem Rücken.
	 */
	private void figure(HudPainter painter, Object texture, byte[] png, boolean slim, boolean back,
						SkinStore.Cape cape, byte[] capePng, int x, int y, float scale) {
		int[] size = SkinStore.size(png);
		if (texture == null || size == null)
			return;
		boolean modern = size[1] == size[0];
		int k = Math.max(1, size[0] / 64);
		int arm = slim ? 3 : 4;
		painter.push(x, y, scale);
		Part[] parts = back ? backParts(modern, slim, arm) : frontParts(modern, slim, arm);
		for (Part part : parts)
			painter.image(texture, x + part.x, y + part.y, part.w, part.h, part.u * k, part.v * k, part.w * k, part.h * k,
				size[0], size[1]);
		if (back && cape != null && capePng != null) {
			int[] capeSize = SkinStore.size(capePng);
			Object capeTexture = platform.texture("cape/" + cape.id, capePng);
			if (capeTexture != null && capeSize != null) {
				int c = Math.max(1, capeSize[0] / 64);
				painter.image(capeTexture, x + 3, y + 8, 10, 16, c, c, 10 * c, 16 * c, capeSize[0], capeSize[1]);
			}
		}
		painter.pop();
	}

	private record Part(int x, int y, int w, int h, int u, int v) {}

	private static Part[] frontParts(boolean modern, boolean slim, int arm) {
		int armX = slim ? 1 : 0;
		return modern ? new Part[] {
			new Part(4, 20, 4, 12, 4, 20), new Part(8, 20, 4, 12, 20, 52),
			new Part(4, 20, 4, 12, 4, 36), new Part(8, 20, 4, 12, 4, 52),
			new Part(4, 8, 8, 12, 20, 20), new Part(4, 8, 8, 12, 20, 36),
			new Part(armX, 8, arm, 12, 44, 20), new Part(armX, 8, arm, 12, 44, 36),
			new Part(12, 8, arm, 12, 36, 52), new Part(12, 8, arm, 12, 52, 52),
			new Part(4, 0, 8, 8, 8, 8), new Part(4, 0, 8, 8, 40, 8)
		} : new Part[] {
			new Part(4, 20, 4, 12, 4, 20), new Part(8, 20, 4, 12, 4, 20),
			new Part(4, 8, 8, 12, 20, 20),
			new Part(armX, 8, arm, 12, 44, 20), new Part(12, 8, arm, 12, 44, 20),
			new Part(4, 0, 8, 8, 8, 8), new Part(4, 0, 8, 8, 40, 8)
		};
	}

	private static Part[] backParts(boolean modern, boolean slim, int arm) {
		int armX = slim ? 1 : 0;
		int backU = slim ? 51 : 52;
		return modern ? new Part[] {
			new Part(4, 20, 4, 12, 28, 52), new Part(8, 20, 4, 12, 12, 20),
			new Part(4, 20, 4, 12, 12, 52), new Part(8, 20, 4, 12, 12, 36),
			new Part(4, 8, 8, 12, 32, 20), new Part(4, 8, 8, 12, 32, 36),
			new Part(armX, 8, arm, 12, slim ? 43 : 44, 52), new Part(armX, 8, arm, 12, slim ? 59 : 60, 52),
			new Part(12, 8, arm, 12, backU, 20), new Part(12, 8, arm, 12, backU, 36),
			new Part(4, 0, 8, 8, 24, 8), new Part(4, 0, 8, 8, 56, 8)
		} : new Part[] {
			new Part(4, 20, 4, 12, 12, 20), new Part(8, 20, 4, 12, 12, 20),
			new Part(4, 8, 8, 12, 32, 20),
			new Part(armX, 8, arm, 12, backU, 20), new Part(12, 8, arm, 12, backU, 20),
			new Part(4, 0, 8, 8, 24, 8), new Part(4, 0, 8, 8, 56, 8)
		};
	}

	private static String fit(HudPainter painter, String text, int width) {
		if (painter.textWidth(text) <= width)
			return text;
		String cut = text;
		while (cut.length() > 1 && painter.textWidth(cut + "…") > width)
			cut = cut.substring(0, cut.length() - 1);
		return cut + "…";
	}
}
