package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import de.mclauncher.badge.BadgeService;
import de.mclauncher.badge.BadgeTexture;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.render.RenderLayer;
import net.minecraft.client.render.VertexConsumer;
import net.minecraft.client.render.VertexConsumerProvider;
import net.minecraft.client.render.entity.EntityRenderer;
import net.minecraft.client.render.entity.state.EntityRenderState;
import net.minecraft.client.render.entity.state.PlayerEntityRenderState;
import net.minecraft.entity.Entity;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import org.joml.Matrix4f;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Zeigt das AxoClient-Logo auch am Namensschild über dem Spieler, links vor dem Namen.
 * Gezeichnet wird in dieselbe Textur-Ebene wie die Schrift, damit Drehung, Licht und Tiefe passen.
 */
@Mixin(EntityRenderer.class)
public class EntityRendererMixin {
	/** Größe des Logos in Schrift-Pixeln (eine Zeile ist 8 hoch) und Abstand zum Namen. */
	@Unique private static final float W = 10.0F;
	@Unique private static final float H = 8.0F;
	@Unique private static final float GAP = 1.0F;

	@WrapOperation(
		method = "renderLabelIfPresent",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/font/TextRenderer;draw(Lnet/minecraft/text/Text;FFIZLorg/joml/Matrix4f;Lnet/minecraft/client/render/VertexConsumerProvider;Lnet/minecraft/client/font/TextRenderer$TextLayerType;II)I",
			ordinal = 1
		),
		require = 0
	)
	private int mclauncher$nameTagBadge(TextRenderer font, Text text, float x, float y, int color, boolean shadow,
										Matrix4f matrix, VertexConsumerProvider vertexConsumers,
										TextRenderer.TextLayerType layer, int background, int light,
										Operation<Integer> original, @Local(argsOnly = true) EntityRenderState state) {
		int width = original.call(font, text, x, y, color, shadow, matrix, vertexConsumers, layer, background, light);
		if (mclauncher$has(state) && BadgeTexture.get() instanceof Identifier badge)
			mclauncher$drawBadge(vertexConsumers.getBuffer(RenderLayer.getText(badge)), matrix,
				x - W - GAP, y, light);
		return width;
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
