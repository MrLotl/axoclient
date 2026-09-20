package de.mclauncher.badge.mixin;

import de.mclauncher.badge.AxoHud;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.hud.InGameHud;
import net.minecraft.client.render.RenderTickCounter;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Hängt die eigenen Anzeigen (FPS, Ping, ...) ans Ende der normalen Anzeige. */
@Mixin(InGameHud.class)
public class InGameHudMixin {
	/** Das Scoreboard und die Effekte zeichnen wir selbst, damit man sie verschieben kann. */
	@Inject(method = "renderScoreboardSidebar(Lnet/minecraft/client/gui/DrawContext;Lnet/minecraft/client/render/RenderTickCounter;)V",
		at = @At("HEAD"), cancellable = true, require = 0)
	private void mclauncher$ownScoreboard(DrawContext context, RenderTickCounter tick, CallbackInfo callback) {
		if (AxoHud.takesScoreboard())
			callback.cancel();
	}

	@Inject(method = "renderStatusEffectOverlay", at = @At("HEAD"), cancellable = true, require = 0)
	private void mclauncher$ownEffects(DrawContext context, RenderTickCounter tick, CallbackInfo callback) {
		if (!AxoHud.takesEffects())
			return;
		if (AxoHud.effectsAsText())
			callback.cancel();
		else
			AxoHud.beginEffects(context);
	}

	@Inject(method = "renderStatusEffectOverlay", at = @At("RETURN"), require = 0)
	private void mclauncher$endEffects(DrawContext context, RenderTickCounter tick, CallbackInfo callback) {
		AxoHud.endEffects();
	}

	@Inject(method = "render", at = @At("TAIL"), require = 0)
	private void mclauncher$axoHud(DrawContext context, RenderTickCounter tick, CallbackInfo callback) {
		AxoHud.render(context);
	}
}
