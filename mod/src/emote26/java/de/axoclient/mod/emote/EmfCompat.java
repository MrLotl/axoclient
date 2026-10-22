package de.axoclient.mod.emote;

import net.fabricmc.loader.api.FabricLoader;

import java.lang.reflect.Method;
import java.util.UUID;
import java.util.function.Function;
import java.util.function.Predicate;

/**
 * Entity Model Features (z. B. mit Fresh Animations Player) ersetzt das Spielermodell durch ein eigenes, animiertes
 * Modell. Unsere Emotes bewegen aber das normale Modell – beides zusammen zerreißt die Figur (Cape bzw. Elytra stehen
 * als verdrehte Platte neben dem Spieler). Darum bekommt EMF eine Bedingung: Solange ein Spieler ein AxoClient-Emote
 * spielt, nimmt EMF für ihn das normale Modell. Über Reflection, damit die Mod auch ohne EMF läuft.
 */
final class EmfCompat {
	private EmfCompat() {}

	static void init(Predicate<UUID> emoting) {
		if (!FabricLoader.getInstance().isModLoaded("entity_model_features"))
			return;
		try {
			Class<?> api = Class.forName("traben.entity_model_features.EMFAnimationApi");
			Method register = api.getMethod("registerVanillaModelCondition", Function.class);
			Method uuidOf = Class.forName("traben.entity_texture_features.utils.ETFEntity").getMethod("etf$getUuid");
			Function<Object, Boolean> condition = entity -> {
				if (entity == null)
					return false;
				try {
					return uuidOf.invoke(entity) instanceof UUID uuid && emoting.test(uuid);
				} catch (ReflectiveOperationException | RuntimeException e) {
					return false;
				}
			};
			register.invoke(null, condition);
			System.out.println("[AxoClient] Entity Model Features erkannt: beim Emote gilt das normale Spielermodell");
		} catch (ReflectiveOperationException | LinkageError | RuntimeException e) {
			System.err.println("[AxoClient] Anbindung an Entity Model Features fehlgeschlagen: " + e);
		}
	}
}
