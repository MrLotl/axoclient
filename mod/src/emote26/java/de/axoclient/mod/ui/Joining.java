package de.axoclient.mod.ui;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.ConnectScreen;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.multiplayer.ServerData;
import net.minecraft.client.multiplayer.resolver.ServerAddress;
import net.minecraft.network.chat.Component;

/** Wechselt aus dem Spiel direkt auf einen anderen Server bzw. in eine offene Welt (gleiche Instanz). */
final class Joining {
	private Joining() {}

	static void connect(String address, String name) {
		Minecraft client = Minecraft.getInstance();
		if (address == null || !ServerAddress.isValidAddress(address))
			return;
		ServerData data = new ServerData(name == null || name.isBlank() ? address : name, address, ServerData.Type.OTHER);
		if (client.level != null)
			client.disconnectFromWorld(Component.literal("Wechsle zu " + data.name + " …"));
		ConnectScreen.startConnecting(new TitleScreen(), client, ServerAddress.parseString(address), data, false, null);
	}
}
