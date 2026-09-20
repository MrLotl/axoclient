package de.mclauncher.badge;

import de.mclauncher.badge.hud.HudEditor;
import net.minecraft.client.gui.Click;
import net.minecraft.client.input.CharInput;
import net.minecraft.client.input.KeyInput;
import net.minecraft.client.input.KeyInput;
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
	public boolean mouseClicked(Click click, boolean doubled) {
		if (click.button() == 1) {
			editor.rightClick(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) click.x(), (int) click.y());
			return true;
		}
		if (click.button() == 0 && editor.mouseDown(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) click.x(), (int) click.y())) {
			close();
			return true;
		}
		return click.button() == 0 || super.mouseClicked(click, doubled);
	}

	@Override
	public boolean mouseDragged(Click click, double offsetX, double offsetY) {
		if (click.button() == 0)
			editor.mouseDrag(new AxoHud.Painter(null), AxoHud.texts(), width, height,
				(int) click.x(), (int) click.y());
		return click.button() == 0 || super.mouseDragged(click, offsetX, offsetY);
	}

	@Override
	public boolean mouseReleased(Click click) {
		if (click.button() == 0)
			editor.mouseUp();
		return click.button() == 0 || super.mouseReleased(click);
	}

	@Override
	public boolean charTyped(CharInput input) {
		return editor.charTyped(input.codepoint()) || super.charTyped(input);
	}

	@Override
	public boolean keyPressed(KeyInput input) {
		return editor.keyPressed(input.key()) || super.keyPressed(input);
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
