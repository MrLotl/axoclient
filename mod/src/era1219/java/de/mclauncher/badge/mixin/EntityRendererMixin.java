package de.mclauncher.badge.mixin;

import de.mclauncher.badge.BadgeService;
import de.mclauncher.badge.BadgeTexture;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.render.RenderLayers;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.command.OrderedRenderCommandQueue;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.state.EntityRenderState;
import net.minecraft.client.render.entity.state.PlayerEntityRenderState;
import net.minecraft.client.render.state.CameraRenderState;
import net.minecraft.client.util.math.MatrixStack;
import net.minecraft.entity.Entity;
import net.minecraft.util.Identifier;
import net.minecraft.util.math.Vec3d;
import org.joml.Matrix4f;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Zeigt das AxoClient-Logo auch am Namensschild über dem Spieler, links vor dem Namen.
 * Das Namensschild wird hier nur angemeldet und später gezeichnet, daher meldet die Mod das Logo
 * mit derselben Verschiebung/Drehung wie die Schrift als eigenes Viereck an.
 */
@Mixin(EntityRenderer.class)
public class EntityRendererMixin {
	/** Größe des Logos in Schrift-Pixeln (eine Zeile ist 8 hoch) und Abstand zum Namen. */
	@Unique private static final float W = 10.0F;
	@Unique private static final float H = 8.0F;
	@Unique private static final float GAP = 1.0F;

	// Ohne Beschreibung des Aufrufs, weil submitLabel je nach Minecraft-Version unterschiedlich viele Werte bekommt
	@Inject(
		method = "renderLabelIfPresent",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/render/command/OrderedRenderCommandQueue;submitLabel",
			shift = At.Shift.AFTER
		),
		require = 0
	)
	private void mclauncher$nameTagBadge(EntityRenderState state, MatrixStack matrices,
										 OrderedRenderCommandQueue queue, CameraRenderState camera,
										 CallbackInfo callback) {
		Vec3d position = state.nameLabelPos;
		if (position == null || !mclauncher$has(state) || !(BadgeTexture.get() instanceof Identifier badge))
			return;

		// Gleiche Verschiebung wie beim Namen: an die Schildposition, zur Kamera gedreht, auf Schriftgröße
		float x = -MinecraftClient.getInstance().textRenderer.getWidth(state.displayName) / 2.0F - W - GAP;
		int light = state.light;
		matrices.push();
		matrices.translate(position.x, position.y + 0.5, position.z);
		matrices.multiply(camera.orientation);
		matrices.scale(0.025F, -0.025F, 0.025F);
		queue.submitCustom(matrices, RenderLayers.text(badge),
			(entry, buffer) -> mclauncher$drawBadge(buffer, entry.getPositionMatrix(), x, 0.0F, light));
		matrices.pop();
	}

	/** Nutzt der Spieler hinter diesem Render-Zustand AxoClient? */
	@Unique
	private static boolean mclauncher$has(EntityRenderState state) {
		if (!(state instanceof PlayerEntityRenderState player))
			return false;
		MinecraftClient client = MinecraftClient.getInstance();
		Entity entity = client.world == null ? null : client.world.getEntityById(player.id);
		return entity != null && BadgeService.hasBadge(entity.getUuid());
	}

	/** Das ganze Bild (60x46) als Viereck in Schriftgröße; Reihenfolge wie bei den Schriftzeichen. */
	@Unique
	private static void mclauncher$drawBadge(VertexConsumer buffer, Matrix4f matrix, float x, float y, int light) {
		buffer.vertex(matrix, x, y, 0.0F).color(-1).texture(0.0F, 0.0F).light(light);
		buffer.vertex(matrix, x, y + H, 0.0F).color(-1).texture(0.0F, 1.0F).light(light);
		buffer.vertex(matrix, x + W, y + H, 0.0F).color(-1).texture(1.0F, 1.0F).light(light);
		buffer.vertex(matrix, x + W, y, 0.0F).color(-1).texture(1.0F, 0.0F).light(light);
	}
}
