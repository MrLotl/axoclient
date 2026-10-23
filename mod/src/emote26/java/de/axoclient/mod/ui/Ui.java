package de.axoclient.mod.ui;

import java.util.ArrayList;
import java.util.List;
import java.util.function.IntConsumer;

/**
 * Bedienung im "Sofort-Modus": Beim Zeichnen meldet jedes Element seine Klickfläche an; ein Klick löst die oberste
 * Fläche unter der Maus aus. So stimmen Bild und Bedienung immer überein, ohne das Layout doppelt zu rechnen.
 */
public final class Ui {
	private record Hit(int x, int y, int w, int h, Runnable action, IntConsumer drag, int[] clip, boolean silent) {}

	public Gfx g;
	public int mx;
	public int my;
	/** Ob gerade ein Dialog darüber liegt: dann reagiert nichts darunter. */
	public boolean inert;

	private List<Hit> building = new ArrayList<>();
	private List<Hit> hits = new ArrayList<>();
	private final List<int[]> clips = new ArrayList<>();
	private IntConsumer dragging;

	/** Neues Bild beginnen. */
	public void begin(Gfx g, int mx, int my) {
		this.g = g;
		this.mx = mx;
		this.my = my;
		building = new ArrayList<>();
		clips.clear();
	}

	/** Bild fertig: die gemeldeten Flächen gelten ab jetzt für Klicks. */
	public void finish() {
		hits = building;
	}

	/** Ob die Maus über dem Rechteck steht (und nicht abgeschnitten oder verdeckt ist). */
	public boolean hover(int x, int y, int w, int h) {
		if (inert || !Gfx.inside(mx, my, x, y, w, h))
			return false;
		int[] clip = clips.isEmpty() ? null : clips.get(clips.size() - 1);
		return clip == null || Gfx.inside(mx, my, clip[0], clip[1], clip[2], clip[3]);
	}

	/** Klickfläche anmelden. */
	public void hit(int x, int y, int w, int h, Runnable action) {
		add(x, y, w, h, action, null, false);
	}

	/** Klickfläche ohne Klick-Klang (z. B. wenn die Aktion selbst etwas Passenderes abspielt). */
	public void quiet(int x, int y, int w, int h, Runnable action) {
		add(x, y, w, h, action, null, true);
	}

	private void add(int x, int y, int w, int h, Runnable action, IntConsumer drag, boolean silent) {
		if (!inert)
			building.add(new Hit(x, y, w, h, action, drag, clips.isEmpty() ? null : clips.get(clips.size() - 1), silent));
	}

	/** Fläche zum Ziehen (Regler): bekommt bei Klick und beim Ziehen die Maus-x in Design-Pixeln. */
	public void drag(int x, int y, int w, int h, IntConsumer onX) {
		add(x, y, w, h, null, onX, false);
	}

	/** Eine Fläche, die Klicks schluckt (z. B. das Fenster selbst, damit ein Klick daneben schließt). */
	public void block(int x, int y, int w, int h) {
		quiet(x, y, w, h, () -> {});
	}

	public void pushClip(int x, int y, int w, int h) {
		clips.add(new int[] { x, y, w, h });
		g.clip(x, y, w, h);
	}

	public void popClip() {
		clips.remove(clips.size() - 1);
		g.unclip();
	}

	/** Klick bei (x, y): true, wenn eine Fläche getroffen wurde. */
	public boolean click(int x, int y) {
		for (int i = hits.size() - 1; i >= 0; i--) {
			Hit hit = hits.get(i);
			if (!Gfx.inside(x, y, hit.x(), hit.y(), hit.w(), hit.h()))
				continue;
			if (hit.clip() != null && !Gfx.inside(x, y, hit.clip()[0], hit.clip()[1], hit.clip()[2], hit.clip()[3]))
				continue;
			if (!hit.silent())
				Sounds.click();
			if (hit.drag() != null) {
				dragging = hit.drag();
				dragging.accept(x);
			} else
				hit.action().run();
			return true;
		}
		return false;
	}

	public boolean dragTo(int x) {
		if (dragging == null)
			return false;
		dragging.accept(x);
		return true;
	}

	public void release() {
		dragging = null;
	}
}
