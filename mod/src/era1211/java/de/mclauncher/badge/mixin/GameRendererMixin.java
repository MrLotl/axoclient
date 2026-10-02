package de.mclauncher.badge.mixin;

import de.mclauncher.badge.AxoHud;
import net.minecraft.client.MinecraftClient;
import net.minecraft.client.render.GameRenderer;
import net.minecraft.entity.LivingEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Fullbright mit Shadern: Shader werten die Lightmap meist nicht aus, wohl aber die Nachtsicht-Stärke, die Iris
 * über diese Methode abfragt. Für den eigenen Spieler gilt sie deshalb bei Fullbright als volle Nachtsicht.
 */
@Mixin(GameRenderer.class)
public class GameRendererMixin {
	@Inject(method = "getNightVisionStrength", at = @At("HEAD"), cancellable = true, require = 0)
	private static void mclauncher$fullbrightNightVision(LivingEntity entity, float tickDelta, CallbackInfoReturnable<Float> callback) {
		MinecraftClient client = MinecraftClient.getInstance();
		if (AxoHud.fullbright() && (entity == client.player || entity == client.getCameraEntity()))
			callback.setReturnValue(1.0F);
	}
}
