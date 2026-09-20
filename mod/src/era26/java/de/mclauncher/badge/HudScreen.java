package de.mclauncher.badge;

import de.mclauncher.badge.hud.HudEditor;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

/** Auswahlmenü für die Anzeigen: rechts ein- und ausschalten, im Bild mit der Maus verschieben. */
public class HudScreen extends Screen {
	private final HudEditor editor = new HudEditor(AxoHud.config());

	public HudScreen() {
		super(Component.literal("AxoClient-Anzeigen"));
	}

	@Override
	public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float delta) {
		super.extractRenderState(graphics, mouseX, mouseY, delta);
		editor.render(new AxoHud.Painter(graphics), AxoHud.texts(), width, height, mouseX, mouseY);
	}

	@Override
	public boolean mouseClicked(MouseButtonEvent event, boolean doubled) {
		if (event.button() == 1) {
			editor.rightClick(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) event.x(), (int) event.y());
			return true;
		}
		if (event.button() == 0 && editor.mouseDown(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) event.x(), (int) event.y())) {
			onClose();
			return true;
		}
		return event.button() == 0 || super.mouseClicked(event, doubled);
	}

	@Override
	public boolean mouseDragged(MouseButtonEvent event, double offsetX, double offsetY) {
		if (event.button() == 0)
			editor.mouseDrag(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) event.x(), (int) event.y());
		return event.button() == 0 || super.mouseDragged(event, offsetX, offsetY);
	}

	@Override
	public boolean mouseReleased(MouseButtonEvent event) {
		if (event.button() == 0)
			editor.mouseUp();
		return event.button() == 0 || super.mouseReleased(event);
	}

	@Override
	public boolean charTyped(CharacterEvent event) {
		return editor.charTyped(event.codepoint()) || super.charTyped(event);
	}

	@Override
	public boolean keyPressed(KeyEvent event) {
		return editor.keyPressed(event.key()) || super.keyPressed(event);
	}

	@Override
	public void onClose() {
		editor.close();
		super.onClose();
	}

	/** Das Spiel soll weiterlaufen, damit FPS und Ping sich beim Einstellen aktualisieren. */
	@Override
	public boolean isPauseScreen() {
		return false;
	}
}
