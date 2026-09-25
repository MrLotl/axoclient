package de.mclauncher.badge.mixin;

import de.mclauncher.badge.AxoHud;
import net.minecraft.client.renderer.LightmapRenderStateExtractor;
import net.minecraft.client.renderer.state.LightmapRenderState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/**
 * Fullbright: Helligkeit weit über das Maximum des Reglers und volle Nachtsicht als Grundlicht, damit auch
 * Stellen ganz ohne Licht hell werden.
 */
@Mixin(LightmapRenderStateExtractor.class)
public class LightmapMixin {
	@Inject(method = "extract", at = @At("RETURN"), require = 0)
	private void mclauncher$fullbright(LightmapRenderState state, float partialTick, CallbackInfo callback) {
		if (!state.needsUpdate || !AxoHud.fullbright())
			return;
		state.brightness = Math.max(state.brightness, AxoHud.FULLBRIGHT_GAMMA);
		state.nightVisionEffectIntensity = 1.0F;
	}
}
