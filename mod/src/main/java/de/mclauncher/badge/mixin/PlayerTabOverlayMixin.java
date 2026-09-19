package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyExpressionValue;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import de.mclauncher.badge.BadgeService;
import de.mclauncher.badge.BadgeTexture;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.PlayerFaceExtractor;
import net.minecraft.client.gui.components.PlayerTabOverlay;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Zeigt in der Tabliste zwischen Spielerkopf und Name das AxoClient-Logo, wenn der Spieler AxoClient nutzt.
 * Der Name rückt dafür nach rechts, und die Spalte wird entsprechend breiter.
 */
@Mixin(PlayerTabOverlay.class)
public class PlayerTabOverlayMixin {
	/** Größe des Logos in GUI-Pixeln (der Kopf ist 8x8). */
	@Unique private static final int W = 10;
	@Unique private static final int H = 8;
	/** Platz, den das Logo samt Abstand vor dem Namen belegt. */
	@Unique private static final int SHIFT = W + 2;

	@Unique
	private static boolean mclauncher$has(PlayerInfo info) {
		return info != null && BadgeService.hasBadge(info.getProfile().id());
	}

	/** Breite des Namens: Platz für das Logo einrechnen. */
	@ModifyExpressionValue(
		method = "extractRenderState",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/Font;width(Lnet/minecraft/network/chat/FormattedText;)I", ordinal = 0)
	)
	private int mclauncher$widen(int width, @Local PlayerInfo info) {
		return mclauncher$has(info) ? width + SHIFT : width;
	}

	/** Logo rechts neben den Kopf zeichnen. */
	@WrapOperation(
		method = "extractRenderState",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/gui/components/PlayerFaceExtractor;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/resources/Identifier;IIIZZI)V"
		)
	)
	private void mclauncher$drawBadge(GuiGraphicsExtractor graphics, Identifier skin, int x, int y, int size,
									  boolean hat, boolean upsideDown, int color, Operation<Void> original,
									  @Local PlayerInfo info) {
		original.call(graphics, skin, x, y, size, hat, upsideDown, color);
		if (mclauncher$has(info) && BadgeTexture.get() instanceof Identifier badge) {
			graphics.blit(RenderPipelines.GUI_TEXTURED, badge, x + size + 1, y, 0, 0, W, H, 60, 46, 60, 46);
		}
	}

	/** Name um die Logo-Breite nach rechts schieben. */
	@WrapOperation(
		method = "extractRenderState",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V",
			ordinal = 0
		)
	)
	private void mclauncher$shiftName(GuiGraphicsExtractor graphics, Font font, Component name, int x, int y,
									  int color, Operation<Void> original, @Local PlayerInfo info) {
		original.call(graphics, font, name, mclauncher$has(info) ? x + SHIFT : x, y, color);
	}
}
