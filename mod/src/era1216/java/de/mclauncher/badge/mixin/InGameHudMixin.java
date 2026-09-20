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
	@Inject(method = "render", at = @At("TAIL"), require = 0)
	private void mclauncher$axoHud(DrawContext context, RenderTickCounter tick, CallbackInfo callback) {
		AxoHud.render(context);
	}
}
