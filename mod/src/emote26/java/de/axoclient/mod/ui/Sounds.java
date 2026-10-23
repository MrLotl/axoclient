package de.axoclient.mod.ui;

import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.lang.reflect.Modifier;

/**
 * Klänge des Menüs – einfach die von Minecraft selbst (der Knopf-Klick aus den Spielmenüs). Über Reflection
 * angesprochen, damit eine geänderte Schnittstelle in einer neuen Version höchstens den Klang kostet, nie das Spiel.
 */
public final class Sounds {
	private static boolean looked;
	private static Object click;
	private static Method forUi;
	private static Method soundManager;
	private static Method play;
	private static long last;

	private Sounds() {}

	/** Der Klick der Minecraft-Knöpfe. */
	public static void click() {
		play(1.0F);
	}

	private static void play(float pitch) {
		long now = System.currentTimeMillis();
		if (now - last < 40) // mehrere Auslöser im selben Klick nur einmal hören
			return;
		last = now;
		try {
			if (!looked)
				lookUp();
			if (play == null)
				return;
			Object minecraft = Class.forName("net.minecraft.client.Minecraft").getMethod("getInstance").invoke(null);
			Object manager = soundManager.invoke(minecraft);
			Object instance = forUi.invoke(null, click, pitch);
			if (manager != null && instance != null)
				play.invoke(manager, instance);
		} catch (Throwable t) {
			play = null;
		}
	}

	private static void lookUp() throws Exception {
		looked = true;
		Field field = Class.forName("net.minecraft.sounds.SoundEvents").getField("UI_BUTTON_CLICK");
		click = field.get(null);
		Class<?> simple = Class.forName("net.minecraft.client.resources.sounds.SimpleSoundInstance");
		for (Method m : simple.getMethods())
			if (m.getName().equals("forUI") && Modifier.isStatic(m.getModifiers()) && m.getParameterCount() == 2
				&& m.getParameterTypes()[0].isInstance(click) && m.getParameterTypes()[1] == float.class) {
				forUi = m;
				break;
			}
		soundManager = Class.forName("net.minecraft.client.Minecraft").getMethod("getSoundManager");
		for (Method m : soundManager.getReturnType().getMethods())
			if (m.getName().equals("play") && m.getParameterCount() == 1 && forUi != null
				&& m.getParameterTypes()[0].isAssignableFrom(forUi.getReturnType())) {
				play = m;
				break;
			}
		if (forUi == null)
			play = null;
	}
}
