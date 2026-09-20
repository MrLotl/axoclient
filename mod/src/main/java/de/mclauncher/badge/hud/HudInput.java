package de.mclauncher.badge.hud;

import java.util.ArrayDeque;

/**
 * Zustand der Tasten und Mausknöpfe für die Tastenanzeige und den Klickzähler. Jede Minecraft-Version meldet
 * hier jedes Bild, was gedrückt ist; die Klicks je Sekunde werden aus den Übergängen "losgelassen → gedrückt"
 * der letzten Sekunde gezählt.
 */
public final class HudInput {
	public static final int FORWARD = 0;
	public static final int LEFT = 1;
	public static final int BACK = 2;
	public static final int RIGHT = 3;
	public static final int JUMP = 4;
	public static final int ATTACK = 5;
	public static final int USE = 6;

	private static final long WINDOW_MILLIS = 1000L;
	private static final boolean[] DOWN = new boolean[7];
	private static final ArrayDeque<Long> LEFT_CLICKS = new ArrayDeque<>();
	private static final ArrayDeque<Long> RIGHT_CLICKS = new ArrayDeque<>();

	private HudInput() {}

	/** Jedes Bild aufrufen (auch wenn nichts gedrückt ist). */
	public static synchronized void sample(boolean forward, boolean left, boolean back, boolean right, boolean jump,
										   boolean attack, boolean use) {
		long now = System.currentTimeMillis();
		if (attack && !DOWN[ATTACK])
			LEFT_CLICKS.addLast(now);
		if (use && !DOWN[USE])
			RIGHT_CLICKS.addLast(now);
		DOWN[FORWARD] = forward;
		DOWN[LEFT] = left;
		DOWN[BACK] = back;
		DOWN[RIGHT] = right;
		DOWN[JUMP] = jump;
		DOWN[ATTACK] = attack;
		DOWN[USE] = use;
	}

	public static synchronized boolean isDown(int key) {
		return key >= 0 && key < DOWN.length && DOWN[key];
	}

	/** Klicks der letzten Sekunde: ATTACK (links) oder USE (rechts). */
	public static synchronized int cps(int button) {
		ArrayDeque<Long> clicks = button == USE ? RIGHT_CLICKS : LEFT_CLICKS;
		long limit = System.currentTimeMillis() - WINDOW_MILLIS;
		while (!clicks.isEmpty() && clicks.peekFirst() < limit)
			clicks.pollFirst();
		return clicks.size();
	}
}
