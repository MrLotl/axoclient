package de.axoclient.mod.ui;

import de.axoclient.mod.MenuHooks;
import de.axoclient.mod.emote.Emotes;
import de.axoclient.mod.hud.AxoPlatform;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;

/**
 * Das neue Axo-Menü ab 26.2: Tastenbelegungen, Daten vom Launcher, Einblendungen und das Emote-Rad. Wird von den
 * Emotes beim Start eingerichtet (beide gibt es nur in diesen Versionen).
 */
public final class Features {
	private Features() {}

	public static void init() {
		Keys.register();
		MenuData.init();
		MenuData.toast = Toasts::add;
		MenuData.mojangCapeShown = png -> {
			AxoPlatform platform = AxoPlatform.current();
			if (platform != null)
				platform.showOwnCape(png);
		};
		MenuHooks.openMenu = () -> MenuScreen.open(null);
		MenuHooks.ownKeys = true;
		MenuHooks.fineHud = true;
		MenuHooks.overlay = Features::renderHud;
	}

	/** Jeden Tick: Tasten des Menüs, Emote-Rad, laufende Emotes. */
	public static void tick() {
		MenuConfig.load();
		Emotes.tick();
		Minecraft client = Minecraft.getInstance();
		boolean free = client.player != null && client.gui.screen() == null;
		while (Keys.menu.consumeClick())
			if (free)
				MenuScreen.open(null);
		while (Keys.wheel.consumeClick())
			if (free)
				client.setScreenAndShow(new EmoteWheelScreen());
		while (Keys.edit.consumeClick())
			if (free)
				client.setScreenAndShow(new OverlayEditScreen(null));
	}

	/** Über den Anzeigen: die Einblendungen. */
	private static void renderHud(Object graphics) {
		Toasts.render(graphics);
	}
}
