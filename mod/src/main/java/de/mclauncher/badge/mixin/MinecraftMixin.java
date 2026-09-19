package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyReturnValue;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;

/** Fenstertitel: "AxoClient 26.2" statt "Minecraft 26.2". */
@Mixin(Minecraft.class)
public class MinecraftMixin {
	@ModifyReturnValue(method = "createTitle", at = @At("RETURN"))
	private String mclauncher$title(String title) {
		return title.startsWith("Minecraft") ? "AxoClient" + title.substring("Minecraft".length()) : title;
	}
}
