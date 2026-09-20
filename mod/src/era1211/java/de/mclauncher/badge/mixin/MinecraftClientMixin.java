package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyReturnValue;
import net.minecraft.client.MinecraftClient;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Fenstertitel: "AxoClient 1.21.11" statt "Minecraft 1.21.11". */
@Mixin(MinecraftClient.class)
public class MinecraftClientMixin {
	@ModifyReturnValue(method = "getWindowTitle", at = @At("RETURN"))
	private String mclauncher$title(String title) {
		return title.startsWith("Minecraft") ? "AxoClient" + title.substring("Minecraft".length()) : title;
	}
}
