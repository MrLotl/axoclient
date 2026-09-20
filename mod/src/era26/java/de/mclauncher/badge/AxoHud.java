package de.mclauncher.badge;

import de.mclauncher.badge.hud.HudConfig;
import de.mclauncher.badge.hud.HudModule;
import de.mclauncher.badge.hud.HudOverlay;
import de.mclauncher.badge.hud.HudPainter;
import de.mclauncher.badge.hud.HudText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.multiplayer.ClientLevel;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.client.player.LocalPlayer;
import net.minecraft.core.BlockPos;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import org.joml.Matrix3x2fStack;

import java.util.EnumMap;
import java.util.Map;

/** Die eigenen Anzeigen im Bild (FPS, Ping, ...) und das Auswahlmenü dazu. */
public final class AxoHud {
	/** Taste, die das Auswahlmenü öffnet: rechte Umschalttaste (GLFW-Nummer). */
	public static final int MENU_KEY = 344;

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
			config = HudConfig.load(Minecraft.getInstance().gameDirectory.toPath());
		return config;
	}

	/** Jeden Tick: öffnet das Auswahlmenü, wenn die Taste neu gedrückt wurde. */
	public static void tick() {
		Minecraft client = Minecraft.getInstance();
		boolean down = HudPlatform.isKeyDown(client, MENU_KEY);
		if (down && !keyWasDown && HudPlatform.noScreenOpen(client) && client.player != null)
			HudPlatform.openScreen(client, new HudScreen());
		keyWasDown = down;
	}

	/** Zeichnet die eingeschalteten Anzeigen (am Ende der normalen Anzeige). */
	public static void render(GuiGraphicsExtractor graphics) {
		Minecraft client = Minecraft.getInstance();
		if (client.player == null || client.getDebugOverlay().showDebugScreen())
			return;
		HudOverlay.draw(config(), new Painter(graphics), texts(), graphics.guiWidth(), graphics.guiHeight());
	}

	/** Aktuelle Werte der Anzeigen; fehlende Werte (z.B. Ping im Einzelspieler) bleiben leer. */
	public static Map<HudModule, String> texts() {
		Minecraft client = Minecraft.getInstance();
		Map<HudModule, String> texts = new EnumMap<>(HudModule.class);
		texts.put(HudModule.FPS, HudText.fps(client.getFps()));
		texts.put(HudModule.TIME, HudText.clock());
		texts.put(HudModule.MEMORY, HudText.memory());

		LocalPlayer player = client.player;
		if (player == null)
			return texts;
		texts.put(HudModule.COORDS, HudText.coords(player.getX(), player.getY(), player.getZ()));
		texts.put(HudModule.CHUNK, HudText.chunk(player.getX(), player.getZ()));
		texts.put(HudModule.DIRECTION, HudText.direction(player.getYRot()));
		texts.put(HudModule.ANGLE, HudText.angle(player.getYRot(), player.getXRot()));
		texts.put(HudModule.SPEED, HudText.speed(player.getX(), player.getZ()));

		if (client.level != null) {
			texts.put(HudModule.WORLD_TIME, HudText.worldTime(client.level.getOverworldClockTime()));
			texts.put(HudModule.BIOME, HudText.biome(biome(client.level, player.blockPosition())));
		}
		if (client.getConnection() != null) {
			PlayerInfo info = client.getConnection().getPlayerInfo(player.getUUID());
			if (info != null)
				texts.put(HudModule.PING, HudText.ping(info.getLatency()));
		}
		return texts;
	}

	/** Name des Bioms in der Sprache des Spielers; null, wenn er sich nicht ermitteln lässt. */
	private static String biome(ClientLevel level, BlockPos pos) {
		return level.getBiome(pos).unwrapKey()
			.map(key -> Component.translatable("biome." + key.identifier().getNamespace() + "."
				+ key.identifier().getPath()).getString())
			.orElse(null);
	}

	/** Zeichnet über Minecraft; ohne Zeichenfläche (nur zum Messen) wird nichts gezeichnet. */
	public static final class Painter implements HudPainter {
		private final GuiGraphicsExtractor graphics;

		public Painter(GuiGraphicsExtractor graphics) {
			this.graphics = graphics;
		}

		private static Font font() {
			return Minecraft.getInstance().font;
		}

		/** Was auf dem Ausrüstungsplatz liegt; nie null. */
		private static ItemStack equipped(int slot) {
			LocalPlayer player = Minecraft.getInstance().player;
			if (player == null || slot < 0 || slot >= SLOTS.length)
				return ItemStack.EMPTY;
			return player.getItemBySlot(SLOTS[slot]);
		}

		@Override
		public int textWidth(String text) {
			return font().width(text);
		}

		@Override
		public void text(String text, int x, int y, int argb, boolean shadow) {
			if (graphics != null)
				graphics.text(font(), text, x, y, argb, shadow);
		}

		@Override
		public void fill(int x, int y, int width, int height, int argb) {
			if (graphics != null)
				graphics.fill(x, y, x + width, y + height, argb);
		}

		@Override
		public void push(int x, int y, float scale) {
			if (graphics == null)
				return;
			// Um (x, y) herum vergrößern, damit die Anzeige an ihrer Ecke stehen bleibt
			Matrix3x2fStack pose = graphics.pose();
			pose.pushMatrix();
			pose.translate(x, y);
			pose.scale(scale, scale);
			pose.translate(-x, -y);
		}

		@Override
		public void pop() {
			if (graphics != null)
				graphics.pose().popMatrix();
		}

		@Override
		public boolean hasEquipment(int slot) {
			return !equipped(slot).isEmpty();
		}

		@Override
		public void equipment(int slot, int x, int y) {
			ItemStack stack = equipped(slot);
			if (graphics == null || stack.isEmpty())
				return;
			graphics.item(stack, x, y);
			graphics.itemDecorations(font(), stack, x, y);
		}

		@Override
		public String durability(int slot) {
			ItemStack stack = equipped(slot);
			if (stack.isEmpty() || !stack.isDamageableItem())
				return null;
			return Math.round((1.0F - (float) stack.getDamageValue() / stack.getMaxDamage()) * 100) + "%";
		}
	}
}
