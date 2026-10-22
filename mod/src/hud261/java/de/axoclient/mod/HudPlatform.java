package de.axoclient.mod;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;

/** Die wenigen Stellen, die sich in 26.1 von 26.2/26.3 unterscheiden. */
public final class HudPlatform {
	private HudPlatform() {}

	public static Screen currentScreen(Minecraft client) {
		return client.screen;
	}

	public static boolean noScreenOpen(Minecraft client) {
		return client.screen == null;
	}

	public static void openScreen(Minecraft client, Screen screen) {
		client.setScreen(screen);
	}

	public static boolean isKeyDown(Minecraft client, int key) {
		return client.getWindow() != null && InputConstants.isKeyDown(client.getWindow(), key);
	}

	/** Vor 26.3 kommen getippte Zeichen immer an; nichts zu tun. */
	public static void textInput(Object owner, boolean on) {}
}
