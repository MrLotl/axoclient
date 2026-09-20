package de.mclauncher.badge.mixin;

import de.mclauncher.badge.AxoHud;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.Hud;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Hängt die eigenen Anzeigen (FPS, Ping, ...) ans Ende der normalen Anzeige. */
@Mixin(Hud.class)
public class HudMixin {
	/** Das Scoreboard und die Effekte zeichnen wir selbst, damit man sie verschieben kann. */
	@Inject(method = "extractScoreboardSidebar", at = @At("HEAD"), cancellable = true, require = 0)
	private void mclauncher$ownScoreboard(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo callback) {
		if (AxoHud.takesScoreboard())
			callback.cancel();
	}

	@Inject(method = "extractEffects", at = @At("HEAD"), cancellable = true, require = 0)
	private void mclauncher$ownEffects(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo callback) {
		if (!AxoHud.takesEffects())
			return;
		if (AxoHud.effectsAsText())
			callback.cancel();
		else
			AxoHud.beginEffects(graphics);
	}

	@Inject(method = "extractEffects", at = @At("RETURN"), require = 0)
	private void mclauncher$endEffects(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo callback) {
		AxoHud.endEffects();
	}

	@Inject(method = "extractRenderState", at = @At("TAIL"), require = 0)
	private void mclauncher$axoHud(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo callback) {
		if (!((Hud) (Object) this).isHidden())
			AxoHud.render(graphics);
	}
}
