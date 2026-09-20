package de.mclauncher.badge.hud;

import java.util.List;

/**
 * Das Einrasten beim Verschieben: erst aufs Raster, dann an die Kanten der anderen Anzeigen und
 * an den Bildrand. So lassen sich Anzeigen bündig aneinanderhängen oder sauber ausrichten.
 */
public final class HudSnap {
	/** Die gefundene Stelle und – falls eingerastet wurde – die Linien zum Anzeigen. */
	public static final class Result {
		public int x;
		public int y;
		/** Stelle der Hilfslinie oder -1, wenn nicht eingerastet wurde. */
		public int guideX = -1;
		public int guideY = -1;
	}

	/** Ein möglicher Fangpunkt: die Position selbst und die Kante, an der sie hängt. */
	private record Candidate(int value, int guide) {}

	private HudSnap() {}

	public static Result apply(HudConfig config, int x, int y, int width, int height,
							   List<int[]> others, int screenWidth, int screenHeight) {
		Result result = new Result();
		result.x = config.grid ? round(x, config.gridSize) : x;
		result.y = config.grid ? round(y, config.gridSize) : y;

		if (config.snap) {
			Candidate horizontal = best(result.x, candidates(others, width, screenWidth, true));
			if (horizontal != null) {
				result.x = horizontal.value();
				result.guideX = horizontal.guide();
			}
			Candidate vertical = best(result.y, candidates(others, height, screenHeight, false));
			if (vertical != null) {
				result.y = vertical.value();
				result.guideY = vertical.guide();
			}
		}

		result.x = HudOverlay.clamp(result.x, width, screenWidth);
		result.y = HudOverlay.clamp(result.y, height, screenHeight);
		return result;
	}

	/** Zeichnet die Hilfslinien, an denen das Stück gerade hängt. */
	public static void drawGuides(HudPainter painter, int guideX, int guideY, int screenWidth, int screenHeight) {
		if (guideX >= 0)
			painter.fill(guideX, 0, 1, screenHeight, HudSkin.GUIDE_LINE);
		if (guideY >= 0)
			painter.fill(0, guideY, screenWidth, 1, HudSkin.GUIDE_LINE);
	}

	/** Das Raster hinter den Anzeigen, damit man beim Ziehen sieht, woran es sich orientiert. */
	public static void drawGrid(HudConfig config, HudPainter painter, int screenWidth, int screenHeight) {
		if (!config.grid)
			return;
		int size = HudConfig.clampGrid(config.gridSize);
		for (int x = size; x < screenWidth; x += size)
			painter.fill(x, 0, 1, screenHeight, HudSkin.GRID_LINE);
		for (int y = size; y < screenHeight; y += size)
			painter.fill(0, y, screenWidth, 1, HudSkin.GRID_LINE);
	}

	/**
	 * Alle Fangpunkte einer Achse: bündige Kanten mit den anderen Anzeigen, direktes Anhängen
	 * davor und dahinter, dazu die beiden Bildränder.
	 */
	private static Candidate[] candidates(List<int[]> others, int size, int available, boolean horizontal) {
		Candidate[] list = new Candidate[others.size() * 4 + 2];
		int next = 0;
		for (int[] other : others) {
			int start = horizontal ? other[0] : other[1];
			int length = horizontal ? other[2] : other[3];
			int end = start + length;
			list[next++] = new Candidate(start, start);                // vordere Kanten bündig
			list[next++] = new Candidate(end - size, end);              // hintere Kanten bündig
			list[next++] = new Candidate(end, end);                     // direkt dahinter anhängen
			list[next++] = new Candidate(start - size, start);          // direkt davor anhängen
		}
		list[next++] = new Candidate(0, 0);
		list[next] = new Candidate(available - size, available - 1);
		return list;
	}

	private static Candidate best(int value, Candidate[] candidates) {
		Candidate best = null;
		int bestDistance = HudConfig.SNAP_DISTANCE + 1;
		for (Candidate candidate : candidates) {
			int distance = Math.abs(candidate.value() - value);
			if (distance < bestDistance) {
				bestDistance = distance;
				best = candidate;
			}
		}
		return best;
	}

	private static int round(int value, int size) {
		int step = HudConfig.clampGrid(size);
		return Math.round(value / (float) step) * step;
	}
}
