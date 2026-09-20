package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyReturnValue;
import de.mclauncher.badge.AxoHud;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Fenstertitel ("AxoClient" statt "Minecraft") und die Taste für das Anzeigen-Menü. */
@Mixin(Minecraft.class)
public class MinecraftMixin {
	@ModifyReturnValue(method = "createTitle", at = @At("RETURN"))
	private String mclauncher$title(String title) {
		return title.startsWith("Minecraft") ? "AxoClient" + title.substring("Minecraft".length()) : title;
	}

	@Inject(method = "tick", at = @At("TAIL"), require = 0)
	private void mclauncher$hudKey(CallbackInfo callback) {
		AxoHud.tick();
	}
}
