package de.axoclient.mod.mixin;

import com.llamalad7.mixinextras.injector.ModifyReturnValue;
import de.axoclient.mod.AxoPlatformImpl;
import de.axoclient.mod.BadgeService;
import de.axoclient.mod.CapeTextures;
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
	private PlayerSkin axoclient$axoCape(PlayerSkin skin) {
		if (AxoPlatformImpl.isSelf(getProfile().id())) {
			// Im AxoClient-Menü gewechselt: sofort zeigen, ohne auf ein neues Laden des Profils zu warten
			if (AxoPlatformImpl.ownSkin != null)
				skin = new PlayerSkin(AxoPlatformImpl.ownSkin, skin.cape(), skin.elytra(), AxoPlatformImpl.ownModel, skin.secure());
			if (AxoPlatformImpl.ownCapeChanged)
				skin = new PlayerSkin(skin.body(), AxoPlatformImpl.ownCape, AxoPlatformImpl.ownCape, skin.model(), skin.secure());
		}
		ClientAsset.Texture cape = CapeTextures.get(BadgeService.capeOf(getProfile().id()));
		return cape == null ? skin : new PlayerSkin(skin.body(), cape, cape, skin.model(), skin.secure());
	}
}
