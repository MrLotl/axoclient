package de.axoclient.mod;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.loader.api.FabricLoader;

import java.io.IOException;
import java.io.Reader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Properties;

/**
 * Einstiegspunkt: liest die Adresse des AxoClient-Dienstes und verbindet sich mit dem Launcher. Der Launcher übergibt
 * beim Start -Daxoclient.api=... sowie Port und Token für {@link AxoLink}; ohne Launcher kann die Dienst-Adresse auch in
 * config/axoclient.properties (api=...) stehen.
 */
public class BadgeClient implements ClientModInitializer {
	@Override
	public void onInitializeClient() {
		String api = System.getProperty("axoclient.api");
		if (api == null || api.isBlank())
			api = readConfig();
		BadgeService.start(api);
		AxoLink.start(FabricLoader.getInstance().getModContainer("minecraft")
			.map(mod -> mod.getMetadata().getVersion().getFriendlyString()).orElse(""));
		ServerStatus.start();
		CapeTextures.start();
	}

	private static String readConfig() {
		Path file = FabricLoader.getInstance().getConfigDir().resolve("axoclient.properties");
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
