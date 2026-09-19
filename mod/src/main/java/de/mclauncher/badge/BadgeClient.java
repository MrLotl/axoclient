package de.mclauncher.badge;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.io.Reader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

/**
 * Einstiegspunkt: liest die Adresse des Symbol-Dienstes. Der Launcher übergibt sie beim Start als
 * -Dmclauncher.badge.api=...; alternativ steht sie in config/mclauncher-badge.properties (api=...).
 */
public class BadgeClient implements ClientModInitializer {
	@Override
	public void onInitializeClient() {
		String api = System.getProperty("mclauncher.badge.api");
		if (api == null || api.isBlank())
			api = readConfig();
		BadgeService.start(api);
		ServerStatus.start();
		CapeTextures.start(System.getProperty("axoclient.capes"));
	}

	private static String readConfig() {
		Path file = FabricLoader.getInstance().getConfigDir().resolve("mclauncher-badge.properties");
		if (!Files.exists(file))
			return null;
		try (Reader reader = Files.newBufferedReader(file)) {
			Properties properties = new Properties();
			properties.load(reader);
			return properties.getProperty("api");
		} catch (IOException e) {
			return null;
		}
	}
}
