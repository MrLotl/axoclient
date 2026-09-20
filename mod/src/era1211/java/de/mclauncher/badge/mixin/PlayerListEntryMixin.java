package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyReturnValue;
import de.mclauncher.badge.BadgeService;
import de.mclauncher.badge.CapeTextures;
import net.minecraft.client.network.PlayerListEntry;
import net.minecraft.client.util.SkinTextures;
import net.minecraft.util.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

/** Spieler mit AxoClient-Umhang: Umhang (und Elytra) durch den gewählten AxoClient-Umhang ersetzen. */
@Mixin(PlayerListEntry.class)
public abstract class PlayerListEntryMixin {
	@Shadow
	public abstract com.mojang.authlib.GameProfile getProfile();

	@ModifyReturnValue(method = "getSkinTextures", at = @At("RETURN"))
	private SkinTextures mclauncher$axoCape(SkinTextures skin) {
		Identifier cape = CapeTextures.get(BadgeService.capeOf(getProfile().getId()));
		return cape == null
			? skin
			: new SkinTextures(skin.texture(), skin.textureUrl(), cape, cape, skin.model(), skin.secure());
	}
}
