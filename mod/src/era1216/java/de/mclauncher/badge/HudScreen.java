package de.mclauncher.badge;

import de.mclauncher.badge.hud.HudEditor;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.client.gui.screen.Screen;
import net.minecraft.text.Text;

/** Auswahlmenü für die Anzeigen: rechts ein- und ausschalten, im Bild mit der Maus verschieben. */
public class HudScreen extends Screen {
	private final HudEditor editor = new HudEditor(AxoHud.config());

	public HudScreen() {
		super(Text.literal("AxoClient-Anzeigen"));
	}

	@Override
	public void render(DrawContext context, int mouseX, int mouseY, float delta) {
		super.render(context, mouseX, mouseY, delta);
		editor.render(new AxoHud.Painter(context), AxoHud.texts(), width, height, mouseX, mouseY);
	}

	@Override
	public boolean mouseClicked(double mouseX, double mouseY, int button) {
		if (button == 1) {
			editor.rightClick(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) mouseX, (int) mouseY);
			return true;
		}
		if (button == 0 && editor.mouseDown(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) mouseX, (int) mouseY)) {
			close();
			return true;
		}
		return button == 0 || super.mouseClicked(mouseX, mouseY, button);
	}

	@Override
	public boolean mouseDragged(double mouseX, double mouseY, int button, double deltaX, double deltaY) {
		if (button == 0)
			editor.mouseDrag(new AxoHud.Painter(null), AxoHud.texts(), width, height, (int) mouseX, (int) mouseY);
		return button == 0 || super.mouseDragged(mouseX, mouseY, button, deltaX, deltaY);
	}

	@Override
	public boolean mouseReleased(double mouseX, double mouseY, int button) {
		if (button == 0)
			editor.mouseUp();
		return button == 0 || super.mouseReleased(mouseX, mouseY, button);
	}

	@Override
	public boolean keyPressed(int keyCode, int scanCode, int modifiers) {
		return editor.keyPressed(keyCode) || super.keyPressed(keyCode, scanCode, modifiers);
	}

	@Override
	public void close() {
		editor.close();
		super.close();
	}

	/** Das Spiel soll weiterlaufen, damit FPS und Ping sich beim Einstellen aktualisieren. */
	@Override
	public boolean shouldPause() {
		return false;
	}
}
