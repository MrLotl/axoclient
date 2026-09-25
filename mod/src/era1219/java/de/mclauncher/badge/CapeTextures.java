package de.mclauncher.badge;

import net.minecraft.client.texture.NativeImage;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.texture.NativeImageBackedTexture;
import net.minecraft.util.AssetInfo;
import net.minecraft.util.Identifier;

import java.io.ByteArrayInputStream;
import java.net.URI;
import java.net.http.HttpClient;
import java.net.http.HttpRequest;
import java.net.http.HttpResponse;
import java.time.Duration;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.regex.Pattern;

/**
 * AxoClient-Umhänge: Die Bilder liefert der AxoClient-Dienst (Admins laden sie im Launcher hoch; Adresse vom Launcher als
 * -Daxoclient.capes=...). Jedes Bild wird beim ersten Bedarf im Hintergrund geladen und dann als Textur angemeldet.
 */
public final class CapeTextures {
	private static final Pattern VALID_ID = Pattern.compile("[a-z0-9_-]{1,32}");

	/** Fertig geladene Umhänge; fehlgeschlagene stehen mit null drin (kein erneuter Versuch). */
	private static final Map<String, AssetInfo.TextureAsset> LOADED = new ConcurrentHashMap<>();
	private static final Map<String, Boolean> LOADING = new ConcurrentHashMap<>();

	private static String baseUrl;
	private static HttpClient http;

	private CapeTextures() {}

	static void start(String base) {
		if (base == null || base.isBlank())
			return;
		baseUrl = base.endsWith("/") ? base : base + "/";
		http = HttpClient.newBuilder().connectTimeout(Duration.ofSeconds(5)).followRedirects(HttpClient.Redirect.NORMAL).build();
	}

	/** Textur des Umhangs oder null, solange sie noch lädt bzw. nicht geladen werden konnte. Blockiert nie. */
	public static AssetInfo.TextureAsset get(String capeId) {
		if (baseUrl == null || capeId == null || !VALID_ID.matcher(capeId).matches())
			return null;
		AssetInfo.TextureAsset texture = LOADED.get(capeId);
		if (texture == null && LOADING.putIfAbsent(capeId, Boolean.TRUE) == null)
			load(capeId);
		return texture;
	}

	private static void load(String capeId) {
		HttpRequest request = HttpRequest.newBuilder(URI.create(baseUrl + capeId + ".png"))
			.timeout(Duration.ofSeconds(15))
			.build();
		http.sendAsync(request, HttpResponse.BodyHandlers.ofByteArray()).thenAccept(response -> {
			if (response.statusCode() != 200)
				return; // bleibt in LOADING: kein erneuter Versuch bis zum Neustart
			byte[] png = response.body();
			// Texturen dürfen nur auf dem Render-Thread angemeldet werden
			MinecraftClient.getInstance().execute(() -> {
				try {
					NativeImage image = NativeImage.read(new ByteArrayInputStream(png));
					Identifier id = Identifier.of("mclauncher_badge", "capes/" + capeId);
					MinecraftClient.getInstance().getTextureManager().registerTexture(id, new NativeImageBackedTexture(() -> "AxoClient Cape " + capeId, image));
					LOADED.put(capeId, new AssetInfo.TextureAssetInfo(id, id));
				} catch (Exception ignored) {
					// kein gültiges PNG
				}
			});
		});
	}
}
