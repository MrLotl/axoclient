package de.mclauncher.badge.mixin;

import de.mclauncher.badge.AxoHud;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Gui;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Hängt die eigenen Anzeigen (FPS, Ping, ...) ans Ende der normalen Anzeige (26.1: noch in Gui). */
@Mixin(Gui.class)
public class HudMixin {
	@Inject(method = "extractRenderState", at = @At("TAIL"), require = 0)
	private void mclauncher$axoHud(GuiGraphicsExtractor graphics, DeltaTracker delta, CallbackInfo callback) {
		if (!Minecraft.getInstance().options.hideGui)
			AxoHud.render(graphics);
	}
}
