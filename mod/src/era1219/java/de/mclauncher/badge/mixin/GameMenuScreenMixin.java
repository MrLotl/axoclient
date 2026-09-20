package de.mclauncher.badge.mixin;

import de.mclauncher.badge.BadgeTexture;
import de.mclauncher.badge.HudScreen;
import net.minecraft.client.gl.RenderPipelines;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.Element;
import net.minecraft.client.gui.screen.GameMenuScreen;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.client.gui.tooltip.Tooltip;
import net.minecraft.client.gui.widget.ButtonWidget;
import net.minecraft.client.gui.widget.ClickableWidget;
import net.minecraft.text.Text;
import net.minecraft.util.Identifier;
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
@Mixin(GameMenuScreen.class)
public abstract class GameMenuScreenMixin extends Screen {
	/** Kantenlänge der kleinen Schaltflächen im Pausenmenü und ihr Abstand zueinander. */
	@Unique private static final int SIZE = 20;
	@Unique private static final int GAP = 4;
	/** Größe des Axolotl-Bildes in der Schaltfläche (die Vorlage ist 60x46 groß). */
	@Unique private static final int ICON_WIDTH = 16;
	@Unique private static final int ICON_HEIGHT = 12;

	@Unique private ButtonWidget mclauncher$axo;

	private GameMenuScreenMixin(Text title) {
		super(title); // Mixin übernimmt keine Konstruktoren; nur damit sich die Klasse übersetzen lässt
	}

	@Inject(method = "init", at = @At("TAIL"))
	private void mclauncher$addAxoButton(CallbackInfo callback) {
		if (!((GameMenuScreen) (Object) this).shouldShowMenu())
			return; // F3+Esc: Menü ohne Schaltflächen
		mclauncher$axo = ButtonWidget
			.builder(Text.empty(), button -> {
				if (this.client != null)
					this.client.setScreen(new HudScreen());
			})
			.dimensions(0, 0, SIZE, SIZE)
			.tooltip(Tooltip.of(Text.literal("AxoClient-Anzeigen")))
			.build();
		mclauncher$place();
		this.addDrawableChild(mclauncher$axo);
	}

	/** Reiht die Schaltfläche hinten an den Streifen an und rückt ihn wieder in die Mitte. */
	@Unique
	private void mclauncher$place() {
		List<ClickableWidget> row = mclauncher$iconRow();
		if (row.isEmpty()) {
			// Kein solcher Streifen (ältere Fassungen des Pausenmenüs): oben rechts in die Ecke
			mclauncher$axo.setPosition(this.width - SIZE - GAP, GAP);
			return;
		}
		int right = Integer.MIN_VALUE;
		for (ClickableWidget widget : row)
			right = Math.max(right, widget.getX() + SIZE);
		int shift = (SIZE + GAP) / 2;
		for (ClickableWidget widget : row)
			widget.setX(widget.getX() - shift);
		mclauncher$axo.setPosition(right - shift + GAP, row.get(0).getY());
	}

	/** Die längste Reihe gleich hoher 20x20-Schaltflächen – das ist der gesuchte Streifen. */
	@Unique
	private List<ClickableWidget> mclauncher$iconRow() {
		List<ClickableWidget> best = new ArrayList<>();
		List<ClickableWidget> icons = new ArrayList<>();
		for (Element element : this.children())
			if (element instanceof ClickableWidget widget && widget != mclauncher$axo
				&& widget.getWidth() == SIZE && widget.getHeight() == SIZE)
				icons.add(widget);
		for (ClickableWidget candidate : icons) {
			List<ClickableWidget> sameRow = new ArrayList<>();
			for (ClickableWidget widget : icons)
				if (widget.getY() == candidate.getY())
					sameRow.add(widget);
			if (sameRow.size() > best.size())
				best = sameRow;
		}
		return best;
	}

	@Inject(method = "render", at = @At("TAIL"))
	private void mclauncher$drawAxoIcon(DrawContext context, int mouseX, int mouseY, float delta,
										CallbackInfo callback) {
		if (mclauncher$axo == null || !(BadgeTexture.get() instanceof Identifier badge))
			return;
		int x = mclauncher$axo.getX() + (SIZE - ICON_WIDTH) / 2;
		int y = mclauncher$axo.getY() + (SIZE - ICON_HEIGHT) / 2;
		context.drawTexture(RenderPipelines.GUI_TEXTURED, badge, x, y, 0.0F, 0.0F,
			ICON_WIDTH, ICON_HEIGHT, 60, 46, 60, 46);
	}
}
