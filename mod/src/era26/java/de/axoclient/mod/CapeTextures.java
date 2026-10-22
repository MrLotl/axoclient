package de.axoclient.mod;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.core.ClientAsset;
import net.minecraft.resources.Identifier;

import java.io.ByteArrayInputStream;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/**
 * AxoClient-Umhänge: Die Bilder liefert der AxoClient-Dienst (siehe {@link CapeSource}). Jedes Bild wird beim
 * ersten Bedarf im Hintergrund geladen und dann als Textur angemeldet.
 */
public final class CapeTextures {
	/** Fertig geladene Umhänge; fehlgeschlagene stehen mit null drin (kein erneuter Versuch). */
	private static final Map<String, ClientAsset.Texture> LOADED = new ConcurrentHashMap<>();
	private static final Map<String, Boolean> LOADING = new ConcurrentHashMap<>();

	private static HttpClient http;

	private CapeTextures() {}

	static void start() {
		if (BadgeService.apiBase() == null)
			return;
		http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).followRedirects(HttpClient.Redirect.NORMAL).build();
	}

	/** Textur des Umhangs oder null, solange sie noch lädt bzw. nicht geladen werden konnte. Blockiert nie. */
	public static ClientAsset.Texture get(String capeId) {
		if (http == null || CapeSource.uri(capeId) == null)
			return null;
		ClientAsset.Texture texture = LOADED.get(capeId);
		if (texture == null && LOADING.putIfAbsent(capeId, Boolean.TRUE) == null)
			load(capeId);
		return texture;
	}

	private static void load(String capeId) {
		HttpRequest request = HttpRequest.newBuilder(CapeSource.uri(capeId))
			.timeout(Duration.ofSeconds(15))
			.build();
		http.sendAsync(request, HttpResponse.BodyHandlers.ofByteArray()).thenAccept(response -> {
			if (response.statusCode() != 200)
				return; // bleibt in LOADING: kein erneuter Versuch bis zum Neustart
			byte[] png = response.body();
			// Texturen dürfen nur auf dem Render-Thread angemeldet werden
			Minecraft.getInstance().execute(() -> {
				try {
					NativeImage image = NativeImage.read(new ByteArrayInputStream(png));
					Identifier id = Identifier.fromNamespaceAndPath("axoclient", "capes/" + CapeSource.key(capeId));
					Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient Cape " + capeId, image));
					LOADED.put(capeId, new ClientAsset.ResourceTexture(id, id));
				} catch (Exception ignored) {
					// kein gültiges PNG
				}
			});
		});
	}
}
