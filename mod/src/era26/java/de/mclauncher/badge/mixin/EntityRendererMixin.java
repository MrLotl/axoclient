package de.mclauncher.badge.mixin;

import com.mojang.blaze3d.vertex.PoseStack;
import com.mojang.blaze3d.vertex.VertexConsumer;
import de.mclauncher.badge.BadgeService;
import de.mclauncher.badge.BadgeTexture;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.SubmitNodeCollector;
import net.minecraft.client.renderer.entity.EntityRenderer;
import net.minecraft.client.renderer.entity.state.AvatarRenderState;
import net.minecraft.client.renderer.entity.state.EntityRenderState;
import net.minecraft.client.renderer.rendertype.RenderTypes;
import net.minecraft.client.renderer.state.level.CameraRenderState;
import net.minecraft.resources.Identifier;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.phys.Vec3;
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

	// Nur der Name wird angehängt (nicht die Punktzahl darüber): daher ordinal = 1.
	// Ohne Beschreibung des Aufrufs, weil submitNameTag je nach Minecraft-Version unterschiedlich viele Werte bekommt.
	@Inject(
		method = "submitNameDisplay(Lnet/minecraft/client/renderer/entity/state/EntityRenderState;Lcom/mojang/blaze3d/vertex/PoseStack;Lnet/minecraft/client/renderer/SubmitNodeCollector;Lnet/minecraft/client/renderer/state/level/CameraRenderState;I)V",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/renderer/SubmitNodeCollector;submitNameTag",
			ordinal = 1,
			shift = At.Shift.AFTER
		),
		require = 0
	)
	private void mclauncher$nameTagBadge(EntityRenderState state, PoseStack poses, SubmitNodeCollector collector,
										 CameraRenderState camera, int yOffset, CallbackInfo callback) {
		Vec3 position = state.nameTagAttachment;
		if (position == null || !mclauncher$has(state) || !(BadgeTexture.get() instanceof Identifier badge))
			return;

		// Gleiche Verschiebung wie beim Namen: an die Schildposition, zur Kamera gedreht, auf Schriftgröße
		float x = -Minecraft.getInstance().font.width(state.nameTag) / 2.0F - W - GAP;
		float y = yOffset;
		poses.pushPose();
		poses.translate(position.x, position.y + 0.5, position.z);
		// Als Matrix, weil die Drehung mit Quaternion je nach Minecraft-Version anders heißt
		poses.mulPose(new Matrix4f().rotation(camera.orientation));
		poses.scale(0.025F, -0.025F, 0.025F);
		collector.submitCustomGeometry(poses, RenderTypes.text(badge),
			(pose, buffer) -> mclauncher$drawBadge(buffer, pose.pose(), x, y, state.lightCoords));
		poses.popPose();
	}

	/** Nutzt der Spieler hinter diesem Render-Zustand AxoClient? */
	@Unique
	private static boolean mclauncher$has(EntityRenderState state) {
		if (!(state instanceof AvatarRenderState avatar))
			return false;
		Minecraft client = Minecraft.getInstance();
		Entity entity = client.level == null ? null : client.level.getEntity(avatar.id);
		return entity != null && BadgeService.hasBadge(entity.getUUID());
	}

	/** Das ganze Bild (60x46) als Viereck in Schriftgröße; Reihenfolge wie bei den Schriftzeichen. */
	@Unique
	private static void mclauncher$drawBadge(VertexConsumer buffer, Matrix4f matrix, float x, float y, int light) {
		buffer.addVertex(matrix, x, y, 0.0F).setColor(-1).setUv(0.0F, 0.0F).setLight(light);
		buffer.addVertex(matrix, x, y + H, 0.0F).setColor(-1).setUv(0.0F, 1.0F).setLight(light);
		buffer.addVertex(matrix, x + W, y + H, 0.0F).setColor(-1).setUv(1.0F, 1.0F).setLight(light);
		buffer.addVertex(matrix, x + W, y, 0.0F).setColor(-1).setUv(1.0F, 0.0F).setLight(light);
	}
}
