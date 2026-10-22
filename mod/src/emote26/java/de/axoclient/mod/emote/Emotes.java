package de.axoclient.mod.emote;

import com.google.gson.JsonElement;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.google.gson.JsonPrimitive;
import com.zigythebird.playeranim.animation.PlayerAnimResources;
import com.zigythebird.playeranim.animation.PlayerAnimationController;
import com.zigythebird.playeranim.api.PlayerAnimationAccess;
import com.zigythebird.playeranim.api.PlayerAnimationFactory;
import com.zigythebird.playeranimcore.animation.Animation;
import com.zigythebird.playeranimcore.animation.AnimationController;
import com.zigythebird.playeranimcore.animation.RawAnimation;
import com.zigythebird.playeranimcore.enums.PlayState;
import com.zigythebird.playeranimcore.loading.UniversalAnimLoader;
import de.axoclient.mod.AxoLink;
import de.axoclient.mod.EmoteHooks;
import de.axoclient.mod.ui.Features;
import de.axoclient.mod.ui.MenuData;
import net.minecraft.client.CameraType;
import net.minecraft.client.Minecraft;
import net.minecraft.client.player.AbstractClientPlayer;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.player.Player;

import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.UUID;

/**
 * Emotes mit der Player Animation Library. Die eigene Figur spielt sofort; der Launcher reicht das Emote über den
 * AxoClient-Dienst an alle anderen AxoClient-Spieler auf demselben Server weiter (kein Plugin auf dem Server nötig).
 * Eingebaute Animationen: assets/axoclient/player_animations/emotes.json. Eigene (importierte) Emotes schickt der
 * Launcher als Animation mit; andere bekommen sie beim Abspielen gleich mitgeliefert.
 */
public final class Emotes {
	/** Eigene Animationsebene für Emotes (Priorität 1000 = kosmetisch, über Laufen und Stehen). */
	private static final Identifier LAYER = Identifier.fromNamespaceAndPath("axoclient", "emote");

	public record Emote(String id, String label, boolean imported) {}

	/** Eingebaute Emotes in der Reihenfolge des Menüs (Namen wie im Launcher). */
	public static final List<Emote> ALL = List.of(
		new Emote("wave", "Winken", false),
		new Emote("bow", "Verbeugen", false),
		new Emote("clap", "Klatschen", false),
		new Emote("cheer", "Jubeln", false),
		new Emote("dance", "Tanzen", false),
		new Emote("sit", "Hinsetzen", false),
		new Emote("point", "Zeigen", false),
		new Emote("salute", "Salutieren", false),
		new Emote("facepalm", "Facepalm", false),
		new Emote("nod", "Nicken", false));

	/** Geladene eigene Animationen (eigene und die anderer Spieler), nach Emote-ID. */
	private static final Map<String, Animation> CUSTOM = new LinkedHashMap<>(16, 0.75F, true) {
		@Override
		protected boolean removeEldestEntry(Map.Entry<String, Animation> eldest) {
			return size() > 80;
		}
	};

	/** Spieler (Entity-ID), die gerade ein Emote spielen (für Entity Model Features: so lange das normale Modell). */
	private static final Map<Integer, AbstractClientPlayer> ACTIVE = new java.util.concurrent.ConcurrentHashMap<>();

	/** Eigenes laufendes Emote (oder null) und wo man dabei stand: Bewegung bricht es ab. */
	private static String playing;
	private static double startX;
	private static double startZ;
	/** Kameraperspektive vor dem Emote; währenddessen sieht man sich von hinten. */
	private static CameraType cameraBefore;

	private Emotes() {}

	/** Von {@link EmoteHooks#init()} per Reflection aufgerufen. */
	public static void init() {
		PlayerAnimationFactory.ANIMATION_DATA_FACTORY.registerFactory(LAYER, 1000,
			player -> new PlayerAnimationController(player, (controller, state, setter) -> PlayState.STOP));
		AxoLink.on("emote", Emotes::onRemote);
		MenuData.animReceived = Emotes::load;
		EmfCompat.init(Emotes::emoting);
		Features.init();
		EmoteHooks.tick = Features::tick;
	}

	/** Ob der Spieler mit dieser UUID gerade ein AxoClient-Emote spielt. */
	static boolean emoting(UUID uuid) {
		for (AbstractClientPlayer player : ACTIVE.values())
			if (player.getUUID().equals(uuid))
				return true;
		return false;
	}

	/** Eingebaute und eigene Emotes für Menü und Rad. */
	public static List<Emote> available() {
		List<Emote> list = new ArrayList<>(ALL);
		for (MenuData.CustomEmote e : MenuData.CUSTOM)
			list.add(new Emote(e.id(), e.name() == null ? "Emote" : e.name(), true));
		return list;
	}

	public static Emote find(String id) {
		for (Emote e : available())
			if (e.id().equals(id))
				return e;
		return null;
	}

	/** Eigenes Emote abspielen und (wenn der Launcher es kann) an die anderen schicken. */
	public static boolean playLocal(String id) {
		Minecraft client = Minecraft.getInstance();
		LocalPlayer player = client.player;
		if (player == null || !play(player, id))
			return false;
		playing = id;
		startX = player.getX();
		startZ = player.getZ();
		if (cameraBefore == null && client.options.getCameraType() == CameraType.FIRST_PERSON) {
			cameraBefore = CameraType.FIRST_PERSON;
			client.options.setCameraType(CameraType.THIRD_PERSON_BACK);
		}
		send(id);
		return true;
	}

	/** Eigenes Emote abbrechen (auch bei den anderen). */
	public static void stopLocal() {
		Minecraft client = Minecraft.getInstance();
		if (client.player != null)
			stop(client.player);
		finishLocal(client);
		send(null);
	}

	public static boolean playingNow() {
		return playing != null;
	}

	private static void finishLocal(Minecraft client) {
		playing = null;
		if (cameraBefore != null) {
			client.options.setCameraType(cameraBefore);
			cameraBefore = null;
		}
	}

	private static void send(String id) {
		if (!AxoLink.supports("emotes"))
			return;
		JsonObject data = new JsonObject();
		data.add("emote", id == null ? JsonNull.INSTANCE : new JsonPrimitive(id));
		AxoLink.send("emote", data);
	}

	/**
	 * Vom Launcher: {"uuid": "...", "emote": "wave" | "c_…" | null, "anim": "...", "animation": "..."} – ein anderer Spieler
	 * im selben Raum.
	 */
	private static void onRemote(JsonElement data) {
		if (data == null || !data.isJsonObject())
			return;
		JsonObject object = data.getAsJsonObject();
		UUID uuid = parseUuid(text(object, "uuid"));
		String emote = text(object, "emote");
		String anim = text(object, "anim");
		String animation = text(object, "animation");
		if (uuid == null)
			return;
		Minecraft client = Minecraft.getInstance();
		client.execute(() -> {
			if (emote != null && emote.startsWith("c_") && anim != null)
				load(emote, anim, animation);
			if (emote != null && find(emote) == null && !CUSTOM.containsKey(emote))
				return;
			if (client.level == null || client.player != null && client.player.getUUID().equals(uuid))
				return;
			Player player = client.level.getPlayerByUUID(uuid);
			if (!(player instanceof AbstractClientPlayer other))
				return;
			if (emote == null)
				stop(other);
			else
				play(other, emote);
		});
	}

	/** Eine eigene Animation laden (JSON im Bedrock-/GeckoLib- oder PlayerAnimator-Format). */
	private static void load(String id, String json, String animation) {
		if (id == null || json == null || CUSTOM.containsKey(id))
			return;
		try {
			JsonObject root = JsonParser.parseString(json).getAsJsonObject();
			Map<String, Animation> animations = UniversalAnimLoader.loadAnimations(root);
			Animation picked = animation != null ? animations.get(animation) : null;
			if (picked == null && !animations.isEmpty())
				picked = animations.values().iterator().next();
			if (picked != null)
				CUSTOM.put(id, picked);
		} catch (RuntimeException e) {
			System.err.println("[AxoClient] Eigenes Emote " + id + " nicht lesbar: " + e);
		}
	}

	private static AnimationController controller(AbstractClientPlayer player) {
		return (AnimationController) PlayerAnimationAccess.getPlayerAnimationLayer(player, LAYER);
	}

	private static boolean play(AbstractClientPlayer player, String id) {
		AnimationController controller = controller(player);
		Animation animation = id.startsWith("c_") ? CUSTOM.get(id)
			: PlayerAnimResources.getAnimation(Identifier.fromNamespaceAndPath("axoclient", id));
		if (controller == null || animation == null)
			return false;
		controller.triggerAnimation(RawAnimation.begin().thenPlay(animation));
		ACTIVE.put(player.getId(), player);
		return true;
	}

	private static void stop(AbstractClientPlayer player) {
		AnimationController controller = controller(player);
		if (controller != null)
			controller.stop();
		ACTIVE.remove(player.getId());
	}

	/** Jeden Tick: eigenes Emote bei Bewegung abbrechen, Ende erkennen. */
	public static void tick() {
		Minecraft client = Minecraft.getInstance();
		// Beendete Emotes (auch die anderer Spieler): Elytra wieder zeigen
		ACTIVE.entrySet().removeIf(entry -> {
			AnimationController c = controller(entry.getValue());
			return client.level == null || entry.getValue().isRemoved() || c == null || !c.isActive();
		});
		LocalPlayer player = client.player;
		if (playing == null)
			return;
		if (player == null) {
			finishLocal(client);
			return;
		}
		double dx = player.getX() - startX;
		double dz = player.getZ() - startZ;
		boolean moved = dx * dx + dz * dz > 0.04 || client.options.keyJump.isDown();
		AnimationController controller = controller(player);
		if (moved)
			stopLocal();
		else if (controller == null || !controller.isActive())
			finishLocal(client);
	}

	private static String text(JsonObject object, String name) {
		return object.has(name) && object.get(name).isJsonPrimitive() ? object.get(name).getAsString() : null;
	}

	private static UUID parseUuid(String text) {
		if (text == null)
			return null;
		String hex = text.replace("-", "");
		if (!hex.matches("[0-9a-fA-F]{32}"))
			return null;
		return new UUID(Long.parseUnsignedLong(hex.substring(0, 16), 16), Long.parseUnsignedLong(hex.substring(16), 16));
	}
}
