package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.InputConstants;
import net.fabricmc.fabric.api.client.keymapping.v1.KeyMappingHelper;
import net.minecraft.client.KeyMapping;
import net.minecraft.resources.Identifier;

/** Die Tasten des Axo-Menüs als Minecraft-Tastenbelegung (Optionen → Steuerung → Tastenbelegung → AxoClient). */
public final class Keys {
	public static KeyMapping menu;
	public static KeyMapping wheel;
	public static KeyMapping edit;

	private Keys() {}

	static void register() {
		KeyMapping.Category category = KeyMapping.Category.register(Identifier.fromNamespaceAndPath("axoclient", "axoclient"));
		menu = KeyMappingHelper.registerKeyMapping(new KeyMapping("key.axoclient.menu", InputConstants.KEY_K, category));
		wheel = KeyMappingHelper.registerKeyMapping(new KeyMapping("key.axoclient.emotes", InputConstants.KEY_B, category));
		edit = KeyMappingHelper.registerKeyMapping(new KeyMapping("key.axoclient.overlay", InputConstants.KEY_RSHIFT, category));
	}

	/** Kurzer Name der belegten Taste für Hinweise im Menü ("K", "B", "Rechte Umschalttaste"). */
	public static String label(KeyMapping key) {
		if (key == null)
			return "?";
		String text = key.getTranslatedKeyMessage().getString();
		return text.length() == 1 ? text.toUpperCase(java.util.Locale.ROOT) : text;
	}

	/** Ob die Taste gerade gedrückt gehalten wird (auch während ein Fenster offen ist). */
	public static boolean held(KeyMapping key) {
		if (key == null || key.isUnbound())
			return false;
		InputConstants.Key bound = KeyMappingHelper.getBoundKeyOf(key);
		if (bound.getType() == InputConstants.Type.MOUSE)
			return key.isDown();
		return de.axoclient.mod.HudPlatform.isKeyDown(net.minecraft.client.Minecraft.getInstance(), bound.getValue());
	}
}
