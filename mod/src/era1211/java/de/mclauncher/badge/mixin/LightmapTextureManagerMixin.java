package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyExpressionValue;
import de.mclauncher.badge.AxoHud;
import net.minecraft.client.render.LightmapTextureManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Slice;

/** Fullbright: die Lichtberechnung bekommt statt des Helligkeitsreglers einen Wert weit über dem Maximum. */
@Mixin(LightmapTextureManager.class)
public class LightmapTextureManagerMixin {
	@ModifyExpressionValue(method = "update", require = 0,
		at = @At(value = "INVOKE", target = "Ljava/lang/Double;floatValue()F", ordinal = 0),
		slice = @Slice(from = @At(value = "INVOKE",
			target = "Lnet/minecraft/client/option/GameOptions;getGamma()Lnet/minecraft/client/option/SimpleOption;")))
	private float mclauncher$fullbright(float gamma) {
		return AxoHud.fullbright() ? AxoHud.FULLBRIGHT_GAMMA : gamma;
	}
}
