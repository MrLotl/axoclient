package de.mclauncher.badge;

import com.mojang.blaze3d.platform.InputConstants;
import de.mclauncher.badge.hud.HudEditor;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.CharacterEvent;
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
		if (event.button() == InputConstants.MOUSE_BUTTON_RIGHT) {
			editor.rightClick(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) event.x(), (int) event.y());
			return true;
		}
		if (isLeft(event) && editor.mouseDown(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) event.x(), (int) event.y())) {
			onClose();
			return true;
		}
		return isLeft(event) || super.mouseClicked(event, doubled);
	}

	@Override
	public boolean mouseDragged(MouseButtonEvent event, double offsetX, double offsetY) {
		if (isLeft(event))
			editor.mouseDrag(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) event.x(), (int) event.y());
		return isLeft(event) || super.mouseDragged(event, offsetX, offsetY);
	}

	@Override
	public boolean mouseReleased(MouseButtonEvent event) {
		if (isLeft(event))
			editor.mouseUp();
		return isLeft(event) || super.mouseReleased(event);
	}

	/** Ab 26.3 zählt Minecraft die Maustasten wie SDL ab 1 (links = 1, rechts = 3), davor ab 0. */
	private static boolean isLeft(MouseButtonEvent event) {
		return event.button() == InputConstants.MOUSE_BUTTON_LEFT;
	}

	@Override
	public boolean mouseScrolled(double mouseX, double mouseY, double horizontalAmount, double verticalAmount) {
		editor.mouseScrolled(width, height, (int) mouseX, (int) mouseY, verticalAmount);
		return true;
	}

	@Override
	public boolean charTyped(CharacterEvent event) {
		return editor.charTyped(event.codepoint()) || super.charTyped(event);
	}

	@Override
	public boolean keyPressed(KeyEvent event) {
		return editor.keyPressed(glfwKey(event.key())) || super.keyPressed(event);
	}

	/** Der Editor rechnet mit GLFW-Nummern; ab 26.3 kommen SDL-Nummern, daher über die Konstanten der Version übersetzen. */
	private static int glfwKey(int key) {
		if (key == InputConstants.KEY_ESCAPE)
			return 256;
		if (key == InputConstants.KEY_RETURN)
			return 257;
		if (key == InputConstants.KEY_BACKSPACE)
			return 259;
		if (key == InputConstants.KEY_NUMPADENTER)
			return 335;
		int[] digits = {InputConstants.KEY_0, InputConstants.KEY_1, InputConstants.KEY_2, InputConstants.KEY_3,
			InputConstants.KEY_4, InputConstants.KEY_5, InputConstants.KEY_6, InputConstants.KEY_7, InputConstants.KEY_8,
			InputConstants.KEY_9};
		for (int i = 0; i < digits.length; i++)
			if (key == digits[i])
				return '0' + i;
		int[] letters = {InputConstants.KEY_A, InputConstants.KEY_B, InputConstants.KEY_C, InputConstants.KEY_D,
			InputConstants.KEY_E, InputConstants.KEY_F};
		for (int i = 0; i < letters.length; i++)
			if (key == letters[i])
				return 'A' + i;
		return -1;
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
