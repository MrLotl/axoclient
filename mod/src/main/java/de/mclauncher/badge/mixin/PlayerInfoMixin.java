package de.mclauncher.badge.mixin;

import com.llamalad7.mixinextras.injector.ModifyReturnValue;
import de.mclauncher.badge.BadgeService;
import de.mclauncher.badge.CapeTextures;
import net.minecraft.client.multiplayer.PlayerInfo;
import net.minecraft.core.ClientAsset;
import net.minecraft.world.entity.player.PlayerSkin;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

/** Spieler mit AxoClient-Umhang: Umhang (und Elytra) durch den gewählten AxoClient-Umhang ersetzen. */
@Mixin(PlayerInfo.class)
public abstract class PlayerInfoMixin {
	@Shadow
	public abstract com.mojang.authlib.GameProfile getProfile();

	@ModifyReturnValue(method = "getSkin", at = @At("RETURN"))
	private PlayerSkin mclauncher$axoCape(PlayerSkin skin) {
		ClientAsset.Texture cape = CapeTextures.get(BadgeService.capeOf(getProfile().id()));
		return cape == null ? skin : new PlayerSkin(skin.body(), cape, cape, skin.model(), skin.secure());
	}
}
