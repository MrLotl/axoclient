package de.mclauncher.badge;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.network.ServerInfo;

import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.util.concurrent.Executors;
import java.util.concurrent.TimeUnit;

/**
 * Schreibt die Adresse des Servers, auf dem gerade gespielt wird, nach "axoclient-status.txt" im Spielordner
 * (leer = kein Server, z.B. Menü oder Einzelspieler). Der Launcher liest die Datei für Discord und die Freundesliste.
 */
public final class ServerStatus {
	private static final String FILE_NAME = "axoclient-status.txt";
	private static String written;

	private ServerStatus() {}

	static void start() {
		var executor = Executors.newSingleThreadScheduledExecutor(runnable -> {
			Thread thread = new Thread(runnable, "AxoClient Status");
			thread.setDaemon(true);
			return thread;
		});
		executor.scheduleWithFixedDelay(ServerStatus::update, 2, 3, TimeUnit.SECONDS);
	}

	private static void update() {
		MinecraftClient minecraft = MinecraftClient.getInstance();
		if (minecraft == null)
			return;
		ServerInfo server = minecraft.getCurrentServerEntry();
		String address = server != null && minecraft.getNetworkHandler() != null && !minecraft.isIntegratedServerRunning() && !server.isRealm()
			? server.address.trim()
			: "";
		if (address.equals(written))
			return;
		de.mclauncher.badge.hud.HudProfiles.serverChanged(address);
		try {
			Path file = minecraft.runDirectory.toPath().resolve(FILE_NAME);
			Path temp = file.resolveSibling(FILE_NAME + ".tmp");
			Files.writeString(temp, address, StandardCharsets.UTF_8);
			Files.move(temp, file, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
			written = address;
		} catch (IOException ignored) {
			// beim nächsten Durchlauf erneut versuchen
		}
	}
}
