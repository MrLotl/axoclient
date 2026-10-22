package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.InputConstants;
import de.axoclient.mod.AxoLink;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

import java.util.ArrayList;
import java.util.List;

/**
 * Das Axo-Menü im Spiel (Mockup "Ingame"): Fenster mit Tabs Freunde, Emotes, Overlay und Cape. Gezeichnet in
 * Design-Pixeln ({@link Gfx}), bedient über {@link Ui}.
 */
public final class MenuScreen extends Screen {
	public enum Tab {
		FRIENDS("Freunde"), EMOTES("Emotes"), OVERLAY("Overlay"), CAPE("Cape");

		final String label;

		Tab(String label) {
			this.label = label;
		}
	}

	public static final int WIDTH = 720;
	public static final int HEIGHT = 610;
	private static final int HEAD = 64;
	private static final int FOOT = 44;

	/** Zuletzt gewählter Tab (beim nächsten Öffnen wieder). */
	private static Tab lastTab = Tab.FRIENDS;

	final Ui ui = new Ui();
	Tab tab = lastTab;
	private final int[] scroll = new int[Tab.values().length];
	private int contentHeight;
	private int viewHeight;

	final FriendsTab friends = new FriendsTab(this);
	final EmotesTab emotes = new EmotesTab(this);
	final OverlayTab overlay = new OverlayTab(this);
	final CapeTab cape = new CapeTab(this);
	final JoinDialog join = new JoinDialog(this);

	/** Kurzer Hinweis unten im Fenster (z. B. "Einladung an … gesendet") und was "Zurückziehen" tut. */
	private String notice;
	private Runnable noticeUndo;
	private String noticeUndoLabel;
	private long noticeUntil;

	/** Zeitpunkt, bis zu dem "Erneut verbinden" dreht. */
	long retryingUntil;

	int x;
	int y;

	public MenuScreen() {
		super(Component.literal("AxoClient"));
	}

	public static void open(Tab tab) {
		MenuScreen screen = new MenuScreen();
		if (tab != null)
			screen.tab = tab;
		Minecraft.getInstance().setScreenAndShow(screen);
	}

	@Override
	public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
		super.extractRenderState(graphics, mouseX, mouseY, delta);
		Gfx g = Gfx.begin(graphics, width, height);
		try {
			ui.begin(g, g.mx(mouseX), g.my(mouseY));
			ui.inert = join.open();
			draw(g);
			ui.inert = false;
			if (join.open())
				join.render(g, x, y, WIDTH, HEIGHT);
			ui.finish();
		} finally {
			g.end();
		}
	}

	/** Zuletzt angezeigter Fehler vom Launcher (damit jeder nur einmal erscheint). */
	private static long errorShown;

	private void draw(Gfx g) {
		if (MenuData.lastErrorAt > errorShown) {
			errorShown = MenuData.lastErrorAt;
			notice(MenuData.errorText(MenuData.lastError), null, null);
		}
		g.fill(0, 0, g.width, g.height, Theme.SCRIM);
		x = (g.width - WIDTH) / 2;
		y = (g.height - HEIGHT) / 2;
		// Klick neben das Fenster schließt
		ui.hit(0, 0, g.width, g.height, this::onClose);
		ui.block(x, y, WIDTH, HEIGHT);

		g.shadow(x, y, WIDTH, HEIGHT, 18, 40, 30, 0.6F);
		g.box(x, y, WIDTH, HEIGHT, 18, Theme.WINDOW, Theme.WINDOW_LINE);

		header(g);
		g.hline(x, y + HEAD - 1, WIDTH, Theme.DIVIDER);

		// Inhalt (scrollbar)
		int top = y + HEAD;
		viewHeight = HEIGHT - HEAD - FOOT;
		ui.pushClip(x, top, WIDTH, viewHeight);
		int s = scroll[tab.ordinal()];
		int innerX = x + 20;
		int innerY = top + 18 - s;
		int innerW = WIDTH - 40;
		int innerH = viewHeight - 38;
		contentHeight = switch (tab) {
			case FRIENDS -> friends.render(g, innerX, innerY, innerW, innerH);
			case EMOTES -> emotes.render(g, innerX, innerY, innerW, innerH);
			case OVERLAY -> overlay.render(g, innerX, innerY, innerW, innerH);
			case CAPE -> cape.render(g, innerX, innerY, innerW, innerH);
		} + 38;
		ui.popClip();
		clampScroll();
		scrollbar(g, top);

		g.hline(x, y + HEIGHT - FOOT, WIDTH, Theme.DIVIDER);
		footer(g);
		noticeBar(g);
	}

	private void header(Gfx g) {
		int cy = y + HEAD / 2;
		float lx = x + 20;
		lx += g.gradientText("Axo", lx, cy, 20, Gfx.BOLD);
		g.textMid("Client", lx, cy, 20, Gfx.MEDIUM, Theme.MUTED_2);
		lx += g.width("Client", 20, Gfx.MEDIUM) + 12;

		int tx = Math.round(lx);
		boolean offline = !MenuData.connected();
		for (Tab t : Tab.values()) {
			boolean on = t == tab;
			int weight = on ? Gfx.SEMIBOLD : Gfx.MEDIUM;
			float tw = g.width(t.label, 13.5F, weight);
			int badge = t == Tab.FRIENDS && !offline ? requestCount() : 0;
			boolean warn = t == Tab.FRIENDS && offline;
			int extra = badge > 0 ? 7 + Math.max(18, Math.round(g.width(String.valueOf(badge), 10.5F, Gfx.BOLD)) + 10) : warn ? 7 + 7 : 0;
			int w = Math.round(tw) + 22 + extra;
			boolean hover = ui.hover(tx, cy - 17, w, 34);
			if (on) {
				g.gradient(tx, cy - 17, w, 34, 9, Theme.alpha(0xFFEC4899, 0.16F), Theme.alpha(0xFF8B5CF6, 0.16F));
				g.outline(tx, cy - 17, w, 34, 9, Theme.alpha(0xFFD68CE6, 0.2F));
			} else if (hover)
				g.rect(tx, cy - 17, w, 34, 9, 0xFF2E2E2E);
			g.textMid(t.label, tx + 11, cy, 13.5F, weight, on ? Theme.ACCENT_TEXT : hover ? Theme.TEXT_SOFT : Theme.MUTED);
			int bx = Math.round(tx + 11 + tw + 7);
			if (badge > 0) {
				String text = String.valueOf(badge);
				int bw = Math.max(18, Math.round(g.width(text, 10.5F, Gfx.BOLD)) + 10);
				g.accent(bx, cy - 9, bw, 18, 9);
				g.textCentered(text, bx + bw / 2.0F, cy, 10.5F, Gfx.BOLD, Theme.WHITE);
			} else if (warn)
				g.dot(bx + 3, cy, 7, Theme.YELLOW);
			ui.hit(tx, cy - 17, w, 34, () -> select(t));
			tx += w + 4;
		}

		// Rechts: wo man spielt bzw. "Launcher getrennt", dann Schließen
		int right = x + WIDTH - 14;
		int closeX = right - 34;
		boolean closeHover = ui.hover(closeX, cy - 17, 34, 34);
		if (closeHover)
			g.rect(closeX, cy - 17, 34, 34, 9, Theme.BUTTON_HOVER);
		g.icon(Icons.CLOSE, closeX + 10, cy - 7, 14, Theme.MUTED);
		ui.hit(closeX, cy - 17, 34, 34, this::onClose);

		int chipRight = closeX - 12;
		if (offline) {
			String text = "Launcher getrennt";
			int w = 12 + 14 + 8 + Math.round(g.width(text, 12.5F, Gfx.MEDIUM)) + 12;
			int cx = chipRight - w;
			g.box(cx, cy - 16, w, 32, 8, Theme.alpha(Theme.YELLOW, 0.12F), Theme.alpha(Theme.YELLOW, 0.28F));
			g.icon(Icons.OFFLINE, cx + 12, cy - 7, 14, Theme.YELLOW_TEXT);
			g.textMid(text, cx + 34, cy, 12.5F, Gfx.MEDIUM, Theme.YELLOW_TEXT);
		} else if (MenuData.here != null) {
			MenuData.Here here = MenuData.here;
			String text = g.ellipsize(here.world() ? "Offene Welt" : here.label(), 12.5F, Gfx.REGULAR, 170);
			boolean icon = MenuData.hereIcon() != null;
			int w = 6 + (icon ? 22 + 8 : 6) + Math.round(g.width(text, 12.5F, Gfx.REGULAR)) + 8 + 7 + 12;
			int cx = chipRight - w;
			g.rect(cx, cy - 16, w, 32, 8, Theme.CHIP);
			int at = cx + 6;
			if (icon) {
				g.image(MenuData.hereIcon(), at, cy - 11, 22, 22, 0, 0, 64, 64, 64, 64, 0xFFFFFFFF);
				g.cornerMask(at, cy - 11, 22, 22, 5, Theme.CHIP);
				at += 30;
			} else
				at += 6;
			g.textMid(text, at, cy, 12.5F, Gfx.REGULAR, Theme.TEXT_SOFT);
			g.dot(Math.round(at + g.width(text, 12.5F, Gfx.REGULAR) + 8 + 3), cy, 7, Theme.GREEN);
		}
	}

	private void footer(Gfx g) {
		int cy = y + HEIGHT - FOOT / 2;
		int at = x + 20;
		at += g.kbd("Esc", at, cy, 20, 11, Theme.KBD, Theme.TEXT_SOFT) + 7;
		g.textMid("oder", at, cy, 12, Gfx.REGULAR, Theme.MUTED_2);
		at += Math.round(g.width("oder", 12, Gfx.REGULAR)) + 7;
		at += g.kbd(Keys.label(Keys.menu), at, cy, 20, 11, Theme.KBD, Theme.TEXT_SOFT) + 7;
		g.textMid("Schließen", at, cy, 12, Gfx.REGULAR, Theme.MUTED_2);
		at += Math.round(g.width("Schließen", 12, Gfx.REGULAR)) + 18;
		at += g.kbd("Tab", at, cy, 20, 11, Theme.KBD, Theme.TEXT_SOFT) + 7;
		g.textMid("Bereich wechseln", at, cy, 12, Gfx.REGULAR, Theme.MUTED_2);
		at += Math.round(g.width("Bereich wechseln", 12, Gfx.REGULAR)) + 18;
		// Rechts der Hinweis zur Tastenbelegung – nur so lang, wie Platz ist (nie über den Tasten links)
		int right = x + WIDTH - 20;
		for (String hint : new String[] { "Taste ändern: Optionen → Steuerung → Tastenbelegung → AxoClient",
			"Taste ändern: Optionen → Steuerung → AxoClient", "Optionen → Steuerung → AxoClient" })
			if (right - g.width(hint, 12, Gfx.REGULAR) >= at) {
				g.textRight(hint, right, cy, 12, Gfx.REGULAR, Theme.MUTED_2);
				break;
			}
	}

	private void scrollbar(Gfx g, int top) {
		if (contentHeight <= viewHeight)
			return;
		int track = viewHeight - 12;
		int thumb = Math.max(30, track * viewHeight / contentHeight);
		int pos = (track - thumb) * scroll[tab.ordinal()] / Math.max(1, contentHeight - viewHeight);
		g.rect(x + WIDTH - 7, top + 6 + pos, 3, thumb, 2, 0x803A3A3A);
	}

	private void noticeBar(Gfx g) {
		if (notice == null || System.currentTimeMillis() > noticeUntil) {
			notice = null;
			return;
		}
		int cy = y + HEIGHT - 58 - 20;
		float tw = g.width(notice, 13, Gfx.REGULAR);
		int undoW = noticeUndo == null ? 0 : Math.round(g.width(noticeUndoLabel, 12.5F, Gfx.SEMIBOLD)) + 20 + 10;
		int w = 12 + 20 + 10 + Math.round(tw) + (noticeUndo == null ? 12 : 10 + undoW) ;
		int nx = x + (WIDTH - w) / 2;
		g.shadow(nx, cy - 20, w, 40, 10, 14, 12, 0.45F);
		g.box(nx, cy - 20, w, 40, 10, 0xFF333333, 0xFF404040);
		g.accent(nx + 12, cy - 10, 20, 20, 10);
		g.icon(Icons.CHECK_BOLD, nx + 17, cy - 5, 10, Theme.WHITE);
		g.textMid(notice, nx + 42, cy, 13, Gfx.REGULAR, Theme.TEXT);
		ui.block(nx, cy - 20, w, 40);
		if (noticeUndo != null) {
			int bx = nx + 42 + Math.round(tw) + 10;
			int bw = undoW - 10;
			if (ui.hover(bx, cy - 14, bw, 28))
				g.rect(bx, cy - 14, bw, 28, 7, Theme.BUTTON_HOVER);
			g.textMid(noticeUndoLabel, bx + 10, cy, 12.5F, Gfx.SEMIBOLD, Theme.ACCENT_TEXT);
			Runnable undo = noticeUndo;
			ui.hit(bx, cy - 14, bw, 28, () -> {
				undo.run();
				notice = null;
			});
		}
	}

	/** Hinweis unten im Fenster für 5 Sekunden, optional mit Rückgängig-Knopf. */
	void notice(String text, String undoLabel, Runnable undo) {
		notice = text;
		noticeUndoLabel = undoLabel;
		noticeUndo = undo;
		noticeUntil = System.currentTimeMillis() + 5000;
	}

	int requestCount() {
		int n = MenuData.INVITES.size();
		for (MenuData.Friend f : MenuData.FRIENDS)
			if (f.incoming())
				n++;
		return n;
	}

	void select(Tab t) {
		tab = t;
		lastTab = t;
	}

	private void clampScroll() {
		int max = Math.max(0, contentHeight - viewHeight);
		int i = tab.ordinal();
		scroll[i] = Math.max(0, Math.min(max, scroll[i]));
	}

	// ---------- Eingabe ----------

	private Gfx measure() {
		return Gfx.measure(width, height);
	}

	@Override
	public boolean mouseClicked(MouseButtonEvent event, boolean doubled) {
		Gfx g = measure();
		int mx = g.mx(event.x()), my = g.my(event.y());
		if (event.button() == InputConstants.MOUSE_BUTTON_RIGHT && join.open()) {
			join.close();
			return true;
		}
		if (event.button() != InputConstants.MOUSE_BUTTON_LEFT)
			return true;
		ui.click(mx, my);
		return true;
	}

	@Override
	public boolean mouseDragged(MouseButtonEvent event, double dx, double dy) {
		ui.dragTo(measure().mx(event.x()));
		return true;
	}

	@Override
	public boolean mouseReleased(MouseButtonEvent event) {
		ui.release();
		return true;
	}

	@Override
	public boolean mouseScrolled(double mouseX, double mouseY, double horizontal, double vertical) {
		if (join.open())
			return true;
		scroll[tab.ordinal()] -= (int) Math.round(vertical * 40);
		clampScroll();
		return true;
	}

	@Override
	public boolean keyPressed(KeyEvent event) {
		if (event.isEscape()) {
			if (join.open())
				join.close();
			else
				onClose();
			return true;
		}
		if (Keys.menu != null && Keys.menu.matches(event) && !join.open()) {
			onClose();
			return true;
		}
		if (event.isCycleFocus() && !join.open()) {
			Tab[] all = Tab.values();
			select(all[Math.floorMod(tab.ordinal() + (event.hasShiftDown() ? -1 : 1), all.length)]);
			return true;
		}
		return super.keyPressed(event);
	}

	@Override
	public boolean charTyped(CharacterEvent event) {
		return true;
	}

	@Override
	public boolean isPauseScreen() {
		return false;
	}

	@Override
	public void removed() {
		overlay.closed();
		super.removed();
	}

	// ---------- Gemeinsame Bausteine der Tabs ----------

	/** Abschnittsüberschrift: 11 px, Großbuchstaben, gesperrt. */
	static void heading(Gfx g, String text, int x, float cy) {
		String upper = text.toUpperCase(java.util.Locale.GERMAN);
		float at = x;
		for (int i = 0; i < upper.length(); i++) {
			String c = String.valueOf(upper.charAt(i));
			g.textMid(c, at, cy, 11, Gfx.SEMIBOLD, Theme.MUTED_3);
			at += g.width(c, 11, Gfx.SEMIBOLD) + 0.7F;
		}
	}

	/** Hauptknopf mit Verlauf; gibt die Breite zurück. */
	int primary(Gfx g, String label, Icons.Icon icon, int bx, int by, int h, int padX, int radius, float size, boolean enabled, Runnable action) {
		float tw = g.width(label, size, Gfx.SEMIBOLD);
		int iw = icon == null ? 0 : 13 + 7;
		int w = Math.round(tw) + iw + 2 * padX;
		boolean hover = enabled && ui.hover(bx, by, w, h);
		if (enabled)
			g.shadow(bx, by, w, h, radius, 6, 4, 0.25F);
		int from = enabled ? Theme.ACCENT_FROM : Theme.alpha(Theme.ACCENT_FROM, 0.45F);
		int to = enabled ? Theme.ACCENT_TO : Theme.alpha(Theme.ACCENT_TO, 0.45F);
		if (hover) {
			from = Theme.mix(from, 0xFFFFFFFF, 0.12F);
			to = Theme.mix(to, 0xFFFFFFFF, 0.12F);
		}
		g.gradient(bx, by, w, h, radius, from, to);
		int fg = enabled ? Theme.WHITE : Theme.alpha(Theme.WHITE, 0.6F);
		if (icon != null)
			g.icon(icon, bx + padX, by + (h - 13) / 2.0F, 13, fg);
		g.textMid(label, bx + padX + iw, by + h / 2.0F, size, Gfx.SEMIBOLD, fg);
		if (enabled && action != null)
			ui.hit(bx, by, w, h, action);
		return w;
	}

	/** Grauer Knopf; gibt die Breite zurück. */
	int ghost(Gfx g, String label, Icons.Icon icon, int bx, int by, int h, int padX, int radius, float size, int bg, int fg, int weight,
			  Runnable action) {
		float tw = g.width(label, size, weight);
		int iw = icon == null ? 0 : 14 + 8;
		int w = Math.round(tw) + iw + 2 * padX;
		boolean enabled = action != null;
		boolean hover = enabled && ui.hover(bx, by, w, h);
		g.rect(bx, by, w, h, radius, hover ? Theme.BUTTON_HOVER : enabled ? bg : Theme.alpha(bg, 0.55F));
		int color = enabled ? fg : Theme.alpha(fg, 0.45F);
		if (icon != null)
			g.icon(icon, bx + padX, by + (h - 14) / 2.0F, 14, color);
		g.textMid(label, bx + padX + iw, by + h / 2.0F, size, weight, color);
		if (enabled)
			ui.hit(bx, by, w, h, action);
		return w;
	}

	/** Zeilenumbruch nach Wörtern. */
	static List<String> wrap(Gfx g, String text, float size, int weight, float maxWidth) {
		List<String> lines = new ArrayList<>();
		StringBuilder line = new StringBuilder();
		for (String word : text.split(" ")) {
			String candidate = line.isEmpty() ? word : line + " " + word;
			if (g.width(candidate, size, weight) > maxWidth && !line.isEmpty()) {
				lines.add(line.toString());
				line = new StringBuilder(word);
			} else
				line = new StringBuilder(candidate);
		}
		if (!line.isEmpty())
			lines.add(line.toString());
		return lines;
	}

	/** Mehrzeiliger Text ab y (Oberkante), Zeilenhöhe lineHeight; gibt die Höhe zurück. */
	static int paragraph(Gfx g, String text, int px, int py, float maxWidth, float size, int weight, int color, float lineHeight, boolean center) {
		List<String> lines = wrap(g, text, size, weight, maxWidth);
		for (int i = 0; i < lines.size(); i++) {
			float cy = py + lineHeight * i + lineHeight / 2;
			if (center)
				g.textCentered(lines.get(i), px + maxWidth / 2, cy, size, weight, color);
			else
				g.textMid(lines.get(i), px, cy, size, weight, color);
		}
		return Math.round(lines.size() * lineHeight);
	}

	/** Lade-Kreis (Durchmesser size) um (cx, cy). */
	static void spinner(Gfx g, int cx, int cy, int size) {
		long t = System.currentTimeMillis();
		int dots = 12;
		float r = size / 2.0F - 2;
		for (int i = 0; i < dots; i++) {
			double a = Math.PI * 2 * i / dots + (t % 800) / 800.0 * Math.PI * 2;
			float strength = (float) i / dots;
			int d = Math.max(2, size / 9);
			g.dot(Math.round(cx + (float) Math.cos(a) * r), Math.round(cy + (float) Math.sin(a) * r), d,
				Theme.mix(0xFF3A3A3A, 0xFFE07AD8, strength));
		}
	}

	/** Für "Erneut verbinden": Sekunden bis zum nächsten automatischen Versuch. */
	static int retryInSeconds() {
		long at = AxoLink.retryAt();
		return at <= 0 ? 0 : (int) Math.max(0, Math.ceil((at - System.currentTimeMillis()) / 1000.0));
	}
}
