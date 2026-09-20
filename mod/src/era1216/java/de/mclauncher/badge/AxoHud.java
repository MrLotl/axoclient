package de.mclauncher.badge;

import de.mclauncher.badge.hud.HudConfig;
import de.mclauncher.badge.hud.HudInput;
import de.mclauncher.badge.hud.HudModule;
import de.mclauncher.badge.hud.HudOverlay;
import de.mclauncher.badge.hud.HudPainter;
import de.mclauncher.badge.hud.HudText;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.network.ClientPlayerEntity;
import net.minecraft.client.option.GameOptions;
import net.minecraft.client.network.PlayerListEntry;
import net.minecraft.entity.effect.StatusEffectInstance;
import net.minecraft.entity.player.PlayerInventory;
import net.minecraft.item.Item;
import net.minecraft.registry.Registries;
import net.minecraft.util.Identifier;
import net.minecraft.world.LightType;
import net.minecraft.client.util.InputUtil;
import net.minecraft.client.world.ClientWorld;
import net.minecraft.entity.EquipmentSlot;
import net.minecraft.item.ItemStack;
import net.minecraft.text.Text;
import net.minecraft.util.math.BlockPos;
import org.joml.Matrix3x2fStack;
import org.lwjgl.glfw.GLFW;

import java.util.ArrayList;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;

/** Die eigenen Anzeigen im Bild (FPS, Ping, ...) und das Auswahlmenü dazu. */
public final class AxoHud {
	/** Taste, die das Auswahlmenü öffnet. */
	public static final int MENU_KEY = GLFW.GLFW_KEY_RIGHT_SHIFT;

	/** Ausrüstungsplätze in der Reihenfolge von HudSlot. */
	private static final EquipmentSlot[] SLOTS = {
		EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET,
		EquipmentSlot.MAINHAND, EquipmentSlot.OFFHAND
	};

	private static HudConfig config;
	private static boolean keyWasDown;

	private AxoHud() {}

	public static HudConfig config() {
		if (config == null)
			config = HudConfig.load(MinecraftClient.getInstance().runDirectory.toPath());
		return config;
	}

	/** Jeden Tick: öffnet das Auswahlmenü, wenn die Taste neu gedrückt wurde. */
	public static void tick() {
		MinecraftClient client = MinecraftClient.getInstance();
		if (client.getWindow() == null)
			return;
		boolean down = InputUtil.isKeyPressed(client.getWindow().getHandle(), MENU_KEY);
		if (down && !keyWasDown && client.currentScreen == null && client.player != null)
			client.setScreen(new HudScreen());
		keyWasDown = down;
	}

	/** Ob die Effekte als Text statt als Symbole erscheinen sollen. */
	public static boolean effectsAsText() {
		return config().get(HudModule.EFFECTS).variant == 1;
	}

	/** {Breite, Höhe} der Effekt-Symbole, wie Minecraft sie zeichnet (oben rechts); null ohne Effekte. */
	public static int[] effectsSize() {
		ClientPlayerEntity player = MinecraftClient.getInstance().player;
		if (player == null)
			return null;
		int good = 0;
		int bad = 0;
		for (StatusEffectInstance effect : player.getStatusEffects()) {
			if (!effect.shouldShowIcon())
				continue;
			if (effect.getEffectType().value().isBeneficial())
				good++;
			else
				bad++;
		}
		if (good + bad == 0)
			return null;
		return new int[] { 25 * Math.max(good, bad) - 1, bad > 0 ? 50 : 24 };
	}

	private static Painter effectsPainter;

	/** Verschiebt das Zeichnen der Effekt-Symbole von Minecraft an die Stelle der Anzeige (bis endEffects). */
	public static void beginEffects(DrawContext context) {
		effectsPainter = null;
		int[] size = effectsSize();
		if (size == null || MinecraftClient.getInstance().player == null)
			return;
		int width = context.getScaledWindowWidth();
		int height = context.getScaledWindowHeight();
		Painter painter = new Painter(context);
		int[] box = HudOverlay.box(config(), de.mclauncher.badge.hud.HudElement.of(HudModule.EFFECTS), painter, Map.of(), width, height);
		// Minecraft zeichnet die Symbole rechtsbündig ab y = 1
		painter.pushMoved(box[0] - (width - 1 - size[0]), box[1] - 1, box[0], box[1], config().get(HudModule.EFFECTS).factor());
		effectsPainter = painter;
	}

	public static void endEffects() {
		if (effectsPainter != null) {
			effectsPainter.pop();
			effectsPainter = null;
		}
	}

	/** Ob wir das Scoreboard übernehmen (dann zeichnet Minecraft es nicht mehr). */
	public static boolean takesScoreboard() {
		return config().isEnabled(HudModule.SCOREBOARD);
	}

	/** Ob wir die Effekte übernehmen (dann zeichnet Minecraft sie nicht mehr). */
	public static boolean takesEffects() {
		return config().isEnabled(HudModule.EFFECTS);
	}

	/** Zeichnet die eingeschalteten Anzeigen (am Ende der normalen Anzeige). */
	public static void render(DrawContext context) {
		MinecraftClient client = MinecraftClient.getInstance();
		if (client.player == null || client.options.hudHidden || client.getDebugHud().shouldShowDebugHud())
			return;
		HudOverlay.draw(config(), new Painter(context), texts(),
			context.getScaledWindowWidth(), context.getScaledWindowHeight());
	}

	/** Aktuelle Werte der Anzeigen; fehlende Werte (z.B. Ping im Einzelspieler) bleiben leer. */
	public static Map<HudModule, String> texts() {
		MinecraftClient client = MinecraftClient.getInstance();
		Map<HudModule, String> texts = new EnumMap<>(HudModule.class);
		texts.put(HudModule.FPS, HudText.fps(client.getCurrentFps()));
		texts.put(HudModule.TIME, HudText.clock());
		texts.put(HudModule.MEMORY, HudText.memory());
		if (config().isEnabled(HudModule.CPU))
			texts.put(HudModule.CPU, HudText.cpu());
		if (config().isEnabled(HudModule.GPU))
			texts.put(HudModule.GPU, HudText.gpu());

		ClientPlayerEntity player = client.player;
		if (player == null)
			return texts;
		sampleInput(client);
		HudConfig settings = config();
		if (settings.isEnabled(HudModule.CPS))
			texts.put(HudModule.CPS, HudText.cps());
		if (settings.isEnabled(HudModule.EFFECTS) && settings.get(HudModule.EFFECTS).variant == 1)
			texts.put(HudModule.EFFECTS, HudText.effects(effects(player)));
		if (settings.isEnabled(HudModule.LIGHT) && client.world != null) {
			BlockPos feet = player.getBlockPos();
			texts.put(HudModule.LIGHT, HudText.light(client.world.getLightLevel(LightType.BLOCK, feet),
				client.world.getLightLevel(LightType.SKY, feet)));
		}
		texts.put(HudModule.COORDS, HudText.coords(player.getX(), player.getY(), player.getZ()));
		texts.put(HudModule.CHUNK, HudText.chunk(player.getX(), player.getZ()));
		texts.put(HudModule.DIRECTION, HudText.direction(player.getYaw()));
		texts.put(HudModule.SPEED, HudText.speed(player.getX(), player.getZ()));

		if (client.world != null) {
			texts.put(HudModule.WORLD_TIME, HudText.worldTime(client.world.getTimeOfDay()));
			texts.put(HudModule.BIOME, HudText.biome(biome(client.world, player.getBlockPos())));
		}
		if (client.getNetworkHandler() != null) {
			PlayerListEntry entry = client.getNetworkHandler().getPlayerListEntry(player.getUuid());
			if (entry != null)
				texts.put(HudModule.PING, HudText.ping(entry.getLatency()));
		}
		return texts;
	}

	/** Meldet der Tasten- und Klickanzeige, was gerade gedrückt ist. */
	private static void sampleInput(MinecraftClient client) {
		GameOptions options = client.options;
		HudInput.sample(options.forwardKey.isPressed(), options.leftKey.isPressed(), options.backKey.isPressed(),
			options.rightKey.isPressed(), options.jumpKey.isPressed(), options.attackKey.isPressed(),
			options.useKey.isPressed());
	}

	/** Aktive Effekte mit Stufe und Restdauer. */
	private static List<HudText.EffectLine> effects(ClientPlayerEntity player) {
		List<HudText.EffectLine> lines = new ArrayList<>();
		for (StatusEffectInstance effect : player.getStatusEffects())
			lines.add(new HudText.EffectLine(effect.getEffectType().value().getName().getString(),
				effect.getAmplifier() + 1, effect.getDuration()));
		return lines;
	}

	/** Name des Bioms in der Sprache des Spielers; null, wenn er sich nicht ermitteln lässt. */
	private static String biome(ClientWorld world, BlockPos pos) {
		return world.getBiome(pos).getKey()
			.map(key -> Text.translatable("biome." + key.getValue().getNamespace() + "."
				+ key.getValue().getPath()).getString())
			.orElse(null);
	}

	/** Zeichnet über Minecraft; ohne DrawContext (nur zum Messen) wird nichts gezeichnet. */
	public static final class Painter implements HudPainter {
		private final DrawContext context;

		public Painter(DrawContext context) {
			this.context = context;
		}

		private static TextRenderer font() {
			return MinecraftClient.getInstance().textRenderer;
		}

		/** Was auf dem Ausrüstungsplatz liegt; nie null. */
		private static ItemStack equipped(int slot) {
			ClientPlayerEntity player = MinecraftClient.getInstance().player;
			if (player == null || slot < 0 || slot >= SLOTS.length)
				return ItemStack.EMPTY;
			return player.getEquippedStack(SLOTS[slot]);
		}

		@Override
		public int textWidth(String text) {
			return font().getWidth(text);
		}

		@Override
		public void text(String text, int x, int y, int argb, boolean shadow) {
			if (context != null)
				context.drawText(font(), text, x, y, argb, shadow);
		}

		@Override
		public void fill(int x, int y, int width, int height, int argb) {
			if (context != null)
				context.fill(x, y, x + width, y + height, argb);
		}

		@Override
		public void push(int x, int y, float scale) {
			if (context == null)
				return;
			// Um (x, y) herum vergrößern, damit die Anzeige an ihrer Ecke stehen bleibt
			Matrix3x2fStack matrices = context.getMatrices();
			matrices.pushMatrix();
			matrices.translate(x, y);
			matrices.scale(scale, scale);
			matrices.translate(-x, -y);
		}

		@Override
		public int[] effectsSize() {
			return AxoHud.effectsSize();
		}

		@Override
		public void pushMoved(int dx, int dy, int ax, int ay, float scale) {
			if (context == null)
				return;
			Matrix3x2fStack matrices = context.getMatrices();
			matrices.pushMatrix();
			matrices.translate(ax, ay);
			matrices.scale(scale, scale);
			matrices.translate(dx - ax, dy - ay);
		}

		@Override
		public void pop() {
			if (context != null)
				context.getMatrices().popMatrix();
		}

		@Override
		public boolean hasEquipment(int slot) {
			return !equipped(slot).isEmpty();
		}

		@Override
		public void equipment(int slot, int x, int y) {
			ItemStack stack = equipped(slot);
			if (context == null || stack.isEmpty())
				return;
			context.drawItem(stack, x, y);
			context.drawStackOverlay(font(), stack, x, y);
		}

		@Override
		public int[] scoreboardSize() {
			return AxoBoard.size();
		}

		@Override
		public void scoreboard(int x, int y) {
			if (context != null)
				AxoBoard.draw(context, x, y);
		}

		@Override
		public String durability(int slot) {
			ItemStack stack = equipped(slot);
			if (stack.isEmpty() || !stack.isDamageable())
				return null;
			return Math.round((1.0F - (float) stack.getDamage() / stack.getMaxDamage()) * 100) + "%";
		}
	}
}
