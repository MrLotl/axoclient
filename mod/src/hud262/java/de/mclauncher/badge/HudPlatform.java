package de.mclauncher.badge;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;

/** Die wenigen Stellen, die sich in 26.2 von 26.1 und 26.3 unterscheiden. */
public final class HudPlatform {
	private HudPlatform() {}

	public static boolean noScreenOpen(Minecraft client) {
		return client.gui.screen() == null;
	}

	public static void openScreen(Minecraft client, Screen screen) {
		client.setScreenAndShow(screen);
	}

	public static boolean isKeyDown(Minecraft client, int key) {
		return client.getWindow() != null && InputConstants.isKeyDown(client.getWindow(), key);
	}
}
