package de.mclauncher.badge;

import de.mclauncher.badge.hud.HudConfig;
import de.mclauncher.badge.hud.HudModule;
import de.mclauncher.badge.hud.HudOverlay;
import de.mclauncher.badge.hud.HudPainter;
import de.mclauncher.badge.hud.HudText;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.network.ClientPlayerEntity;
import net.minecraft.client.network.PlayerListEntry;
import net.minecraft.client.util.InputUtil;
import net.minecraft.client.world.ClientWorld;
import net.minecraft.entity.EquipmentSlot;
import net.minecraft.item.ItemStack;
import net.minecraft.text.Text;
import net.minecraft.util.math.BlockPos;
import org.joml.Matrix3x2fStack;
import org.lwjgl.glfw.GLFW;

import java.util.EnumMap;
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

		ClientPlayerEntity player = client.player;
		if (player == null)
			return texts;
		texts.put(HudModule.COORDS, HudText.coords(player.getX(), player.getY(), player.getZ()));
		texts.put(HudModule.CHUNK, HudText.chunk(player.getX(), player.getZ()));
		texts.put(HudModule.DIRECTION, HudText.direction(player.getYaw()));
		texts.put(HudModule.ANGLE, HudText.angle(player.getYaw(), player.getPitch()));
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
		public String durability(int slot) {
			ItemStack stack = equipped(slot);
			if (stack.isEmpty() || !stack.isDamageable())
				return null;
			return Math.round((1.0F - (float) stack.getDamage() / stack.getMaxDamage()) * 100) + "%";
		}
	}
}
