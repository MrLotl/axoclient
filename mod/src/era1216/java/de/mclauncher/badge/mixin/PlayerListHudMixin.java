package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyExpressionValue;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Local;
import de.mclauncher.badge.BadgeService;
import de.mclauncher.badge.BadgeTexture;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gl.RenderPipelines;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.hud.PlayerListHud;
import net.minecraft.client.network.PlayerListEntry;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;

/**
 * Zeigt in der Tabliste zwischen Spielerkopf und Name das AxoClient-Logo, wenn der Spieler AxoClient nutzt.
 * Der Name rÃ¼ckt dafÃ¼r nach rechts, und die Spalte wird entsprechend breiter.
 */
@Mixin(PlayerListHud.class)
public class PlayerListHudMixin {
	/** GrÃ¶ÃŸe des Logos in GUI-Pixeln (der Kopf ist 8x8). */
	@Unique private static final int W = 10;
	@Unique private static final int H = 8;
	/** Platz, den das Logo samt Abstand vor dem Namen belegt. */
	@Unique private static final int SHIFT = W + 2;

	@Unique
	private static boolean mclauncher$has(PlayerListEntry entry) {
		return entry != null && BadgeService.hasBadge(entry.getProfile().getId());
	}

	/** Breite des Namens: Platz fÃ¼r das Logo einrechnen. */
	@ModifyExpressionValue(
		method = "render",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/font/TextRenderer;getWidth(Lnet/minecraft/text/StringVisitable;)I", ordinal = 0)
	)
	private int mclauncher$widen(int width, @Local PlayerListEntry entry) {
		return mclauncher$has(entry) ? width + SHIFT : width;
	}

	/** Logo rechts neben den Kopf zeichnen. */
	@WrapOperation(
		method = "render",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/gui/PlayerSkinDrawer;draw(Lnet/minecraft/client/gui/DrawContext;Lnet/minecraft/util/Identifier;IIIZZI)V"
		)
	)
	private void mclauncher$drawBadge(DrawContext context, Identifier skin, int x, int y, int size,
									  boolean hat, boolean upsideDown, int color, Operation<Void> original,
									  @Local PlayerListEntry entry) {
		original.call(context, skin, x, y, size, hat, upsideDown, color);
		if (mclauncher$has(entry) && BadgeTexture.get() instanceof Identifier badge) {
			context.drawTexture(RenderPipelines.GUI_TEXTURED, badge, x + size + 1, y, 0.0F, 0.0F, W, H, 60, 46, 60, 46);
		}
	}

	/** Name um die Logo-Breite nach rechts schieben. */
	@WrapOperation(
		method = "render",
		at = @At(
			value = "INVOKE",
			target = "Lnet/minecraft/client/gui/DrawContext;drawTextWithShadow(Lnet/minecraft/client/font/TextRenderer;Lnet/minecraft/text/Text;III)V",
			ordinal = 0
		)
	)
	private void mclauncher$shiftName(DrawContext context, TextRenderer font, Text name, int x, int y,
									  int color, Operation<Void> original, @Local PlayerListEntry entry) {
		original.call(context, font, name, mclauncher$has(entry) ? x + SHIFT : x, y, color);
	}
}

