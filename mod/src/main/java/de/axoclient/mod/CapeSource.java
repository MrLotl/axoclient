package de.axoclient.mod;

import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.HexFormat;
import java.util.regex.Pattern;

/**
 * Woher das Bild eines Axo-Capes kommt. Der Dienst nennt ein Cape entweder per ID (dann {@code <api>/capes/<id>.png})
 * oder per Adresse; Adressen werden nur auf dem Host des AxoClient-Dienstes geladen, nie von fremden Servern.
 */
public final class CapeSource {
	private static final Pattern VALID_ID = Pattern.compile("[a-z0-9_-]{1,32}");

	private CapeSource() {}

	/** Adresse des Bildes oder null, wenn das Cape unbekannt oder nicht erlaubt ist. */
	public static URI uri(String cape) {
		String base = BadgeService.apiBase();
		if (base == null || cape == null)
			return null;
		if (VALID_ID.matcher(cape).matches())
			return URI.create(base + "/capes/" + cape + ".png");
		try {
			URI uri = URI.create(cape);
			URI api = URI.create(base);
			if (!"https".equals(uri.getScheme()) && !api.getScheme().equals(uri.getScheme()))
				return null;
			return uri.getHost() != null && uri.getHost().equalsIgnoreCase(api.getHost()) && uri.getUserInfo() == null ? uri : null;
		} catch (IllegalArgumentException e) {
			return null;
		}
	}

	/** Name für die Textur (Kleinbuchstaben, Ziffern, _ und -). */
	public static String key(String cape) {
		if (VALID_ID.matcher(cape).matches())
			return cape;
		try {
			byte[] hash = MessageDigest.getInstance("SHA-256").digest(cape.getBytes(StandardCharsets.UTF_8));
			return "u" + HexFormat.of().formatHex(hash, 0, 12);
		} catch (Exception e) {
			return "u" + Integer.toHexString(cape.hashCode());
		}
	}
}
