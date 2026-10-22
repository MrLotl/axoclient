package de.axoclient.mod.mixin;

import de.axoclient.mod.BadgeTexture;
import de.axoclient.mod.HudPlatform;
import de.axoclient.mod.HudScreen;
import de.axoclient.mod.MenuHooks;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

import java.util.ArrayList;
import java.util.List;

/**
 * Hängt im Pausenmenü eine kleine Schaltfläche mit dem Axolotl an den Streifen der übrigen
 * kleinen Schaltflächen; sie öffnet das Menü für die Anzeigen.
 */
@Mixin(PauseScreen.class)
public abstract class PauseScreenMixin extends Screen {
	/** Kantenlänge der kleinen Schaltflächen im Pausenmenü und ihr Abstand zueinander. */
	@Unique private static final int SIZE = 20;
	@Unique private static final int GAP = 4;
	/** Größe des Axolotl-Bildes in der Schaltfläche (die Vorlage ist 60x46 groß). */
	@Unique private static final int ICON_WIDTH = 16;
	@Unique private static final int ICON_HEIGHT = 12;

	@Unique private Button axoclient$axo;

	private PauseScreenMixin(Component title) {
		super(title); // Mixin übernimmt keine Konstruktoren; nur damit sich die Klasse übersetzen lässt
	}

	@Inject(method = "init", at = @At("TAIL"))
	private void axoclient$addAxoButton(CallbackInfo callback) {
		if (!((PauseScreen) (Object) this).showsPauseMenu())
			return; // F3+Esc: Menü ohne Schaltflächen
		axoclient$axo = Button
			.builder(Component.empty(), button -> {
				Runnable menu = MenuHooks.openMenu;
				if (menu != null)
					menu.run();
				else
					HudPlatform.openScreen(Minecraft.getInstance(), new HudScreen());
			})
			.bounds(0, 0, SIZE, SIZE)
			.tooltip(Tooltip.create(Component.literal(MenuHooks.openMenu != null ? "AxoClient" : "AxoClient-Anzeigen")))
			.build();
		axoclient$place();
		this.addRenderableWidget(axoclient$axo);
	}

	/** Reiht die Schaltfläche hinten an den Streifen an und rückt ihn wieder in die Mitte. */
	@Unique
	private void axoclient$place() {
		List<AbstractWidget> row = axoclient$iconRow();
		if (row.isEmpty()) {
			// Kein solcher Streifen: oben rechts in die Ecke
			axoclient$axo.setPosition(this.width - SIZE - GAP, GAP);
			return;
		}
		int right = Integer.MIN_VALUE;
		for (AbstractWidget widget : row)
			right = Math.max(right, widget.getX() + SIZE);
		int shift = (SIZE + GAP) / 2;
		for (AbstractWidget widget : row)
			widget.setX(widget.getX() - shift);
		axoclient$axo.setPosition(right - shift + GAP, row.get(0).getY());
	}

	/** Die längste Reihe gleich hoher 20x20-Schaltflächen – das ist der gesuchte Streifen. */
	@Unique
	private List<AbstractWidget> axoclient$iconRow() {
		List<AbstractWidget> best = new ArrayList<>();
		List<AbstractWidget> icons = new ArrayList<>();
		for (GuiEventListener element : this.children())
			if (element instanceof AbstractWidget widget && widget != axoclient$axo
				&& widget.getWidth() == SIZE && widget.getHeight() == SIZE)
				icons.add(widget);
		for (AbstractWidget candidate : icons) {
			List<AbstractWidget> sameRow = new ArrayList<>();
			for (AbstractWidget widget : icons)
				if (widget.getY() == candidate.getY())
					sameRow.add(widget);
			if (sameRow.size() > best.size())
				best = sameRow;
		}
		return best;
	}

	@Inject(method = "extractRenderState", at = @At("TAIL"))
	private void axoclient$drawAxoIcon(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta,
										CallbackInfo callback) {
		if (axoclient$axo == null || !(BadgeTexture.get() instanceof Identifier badge))
			return;
		int x = axoclient$axo.getX() + (SIZE - ICON_WIDTH) / 2;
		int y = axoclient$axo.getY() + (SIZE - ICON_HEIGHT) / 2;
		graphics.blit(RenderPipelines.GUI_TEXTURED, badge, x, y, 0, 0, ICON_WIDTH, ICON_HEIGHT, 60, 46, 60, 46);
	}
}
