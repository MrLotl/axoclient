package de.mclauncher.badge.mixin;

import de.mclauncher.badge.BadgeTexture;
import de.mclauncher.badge.HudPlatform;
import de.mclauncher.badge.HudScreen;
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

	@Unique private Button mclauncher$axo;

	private PauseScreenMixin(Component title) {
		super(title); // Mixin übernimmt keine Konstruktoren; nur damit sich die Klasse übersetzen lässt
	}

	@Inject(method = "init", at = @At("TAIL"))
	private void mclauncher$addAxoButton(CallbackInfo callback) {
		if (!((PauseScreen) (Object) this).showsPauseMenu())
			return; // F3+Esc: Menü ohne Schaltflächen
		mclauncher$axo = Button
			.builder(Component.empty(), button -> HudPlatform.openScreen(Minecraft.getInstance(), new HudScreen()))
			.bounds(0, 0, SIZE, SIZE)
			.tooltip(Tooltip.create(Component.literal("AxoClient-Anzeigen")))
			.build();
		mclauncher$place();
		this.addRenderableWidget(mclauncher$axo);
	}

	/** Reiht die Schaltfläche hinten an den Streifen an und rückt ihn wieder in die Mitte. */
	@Unique
	private void mclauncher$place() {
		List<AbstractWidget> row = mclauncher$iconRow();
		if (row.isEmpty()) {
			// Kein solcher Streifen: oben rechts in die Ecke
			mclauncher$axo.setPosition(this.width - SIZE - GAP, GAP);
			return;
		}
		int right = Integer.MIN_VALUE;
		for (AbstractWidget widget : row)
			right = Math.max(right, widget.getX() + SIZE);
		int shift = (SIZE + GAP) / 2;
		for (AbstractWidget widget : row)
			widget.setX(widget.getX() - shift);
		mclauncher$axo.setPosition(right - shift + GAP, row.get(0).getY());
	}

	/** Die längste Reihe gleich hoher 20x20-Schaltflächen – das ist der gesuchte Streifen. */
	@Unique
	private List<AbstractWidget> mclauncher$iconRow() {
		List<AbstractWidget> best = new ArrayList<>();
		List<AbstractWidget> icons = new ArrayList<>();
		for (GuiEventListener element : this.children())
			if (element instanceof AbstractWidget widget && widget != mclauncher$axo
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
	private void mclauncher$drawAxoIcon(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta,
										CallbackInfo callback) {
		if (mclauncher$axo == null || !(BadgeTexture.get() instanceof Identifier badge))
			return;
		int x = mclauncher$axo.getX() + (SIZE - ICON_WIDTH) / 2;
		int y = mclauncher$axo.getY() + (SIZE - ICON_HEIGHT) / 2;
		graphics.blit(RenderPipelines.GUI_TEXTURED, badge, x, y, 0, 0, ICON_WIDTH, ICON_HEIGHT, 60, 46, 60, 46);
	}
}
