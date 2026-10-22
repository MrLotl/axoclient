package de.axoclient.mod;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;

/** Die wenigen Stellen, die sich in 26.3 von 26.1 und 26.2 unterscheiden. */
public final class HudPlatform {
	private HudPlatform() {}

	public static Screen currentScreen(Minecraft client) {
		return client.gui.screen();
	}

	public static boolean noScreenOpen(Minecraft client) {
		return client.gui.screen() == null;
	}

	public static void openScreen(Minecraft client, Screen screen) {
		client.setScreenAndShow(screen);
	}

	public static boolean isKeyDown(Minecraft client, int key) {
		return InputConstants.isKeyDown(key);
	}

	/** Seit 26.3 (SDL) liefert Minecraft getippte Zeichen nur, solange jemand die Texteingabe eingeschaltet hat. */
	public static void textInput(Object owner, boolean on) {
		if (on)
			Minecraft.getInstance().textInputManager().startTextInput(owner);
		else
			Minecraft.getInstance().textInputManager().stopTextInput(owner);
	}
}
