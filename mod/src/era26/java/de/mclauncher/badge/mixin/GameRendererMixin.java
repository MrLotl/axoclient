package de.mclauncher.badge.mixin;

import de.mclauncher.badge.AxoHud;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.GameRenderer;
import net.minecraft.world.entity.LivingEntity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/**
 * Fullbright mit Shadern: Shader werten die Lightmap meist nicht aus, wohl aber die Nachtsicht-Stärke, die Iris
 * über diese Methode abfragt. Für den eigenen Spieler gilt sie deshalb bei Fullbright als volle Nachtsicht.
 * Die Methode heißt in 26.1 getNightVisionScale, ab 26.2 nightVisionScale.
 */
@Mixin(GameRenderer.class)
public class GameRendererMixin {
	@Inject(method = {"getNightVisionScale", "nightVisionScale"}, at = @At("HEAD"), cancellable = true, require = 0)
	private static void mclauncher$fullbrightNightVision(LivingEntity entity, float partialTick, CallbackInfoReturnable<Float> callback) {
		Minecraft minecraft = Minecraft.getInstance();
		if (AxoHud.fullbright() && (entity == minecraft.player || entity == minecraft.getCameraEntity()))
			callback.setReturnValue(1.0F);
	}
}
