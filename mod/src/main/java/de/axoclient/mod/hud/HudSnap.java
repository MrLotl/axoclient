package de.axoclient.mod.hud;

import java.util.List;

/**
 * Einrasten beim Verschieben. Zuerst an anderen Widgets und am Bildrand: bündige Kanten, gleiche Mitte, direkt daneben
 * andocken (mit kleinem Abstand) und die Bildmitte. Nur wenn dort nichts in der Nähe ist, aufs Raster. So lassen sich
 * Widgets sauber aneinanderhängen und ausrichten, auch bei eingeschaltetem Raster.
 * <p>
 * Alle Rechtecke sind die Außenkanten (samt Hintergrundfläche), damit Hintergründe bündig aneinanderstoßen.
 */
public final class HudSnap {
	/** Abstand zwischen angedockten Widgets. */
	public static final int GAP = 2;

	/** Die gefundene Stelle und – falls eingerastet wurde – die Linien zum Anzeigen. */
	public static final class Result {
		public int x;
		public int y;
		/** Stelle der Hilfslinie oder -1, wenn nicht eingerastet wurde. */
		public int guideX = -1;
		public int guideY = -1;
	}

	/** Ein möglicher Fangpunkt: die Position selbst und die Linie, an der er hängt. */
	private record Candidate(int value, int guide) {}

	private HudSnap() {}

	/**
	 * Wohin das Stück rastet. x, y, width und height sind seine Außenkanten dort, wo die Maus es gerade hätte; others
	 * die Außenkanten der übrigen Stücke als {x, y, w, h}.
	 */
	public static Result apply(HudConfig config, int x, int y, int width, int height,
							   List<int[]> others, int screenWidth, int screenHeight) {
		Result result = new Result();
		result.x = x;
		result.y = y;
		Candidate horizontal = config.snap ? best(x, candidates(others, x, y, width, height, screenWidth, true)) : null;
		Candidate vertical = config.snap ? best(y, candidates(others, x, y, width, height, screenHeight, false)) : null;
		if (horizontal != null) {
			result.x = horizontal.value();
			result.guideX = horizontal.guide();
		} else if (config.grid)
			result.x = round(x, config.gridSize);
		if (vertical != null) {
			result.y = vertical.value();
			result.guideY = vertical.guide();
		} else if (config.grid)
			result.y = round(y, config.gridSize);
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

	/** Alle Fangpunkte einer Achse (horizontal = x). */
	private static Candidate[] candidates(List<int[]> others, int x, int y, int width, int height, int available, boolean horizontal) {
		int size = horizontal ? width : height;
		// Für "daneben andocken" muss das andere Stück auf der Querachse in der Nähe sein
		int crossStart = horizontal ? y : x, crossEnd = crossStart + (horizontal ? height : width);
		Candidate[] list = new Candidate[others.size() * 5 + 3];
		int next = 0;
		for (int[] other : others) {
			int start = horizontal ? other[0] : other[1];
			int length = horizontal ? other[2] : other[3];
			int end = start + length;
			list[next++] = new Candidate(start, start);                          // vordere Kanten bündig
			list[next++] = new Candidate(end - size, end - 1);                   // hintere Kanten bündig
			list[next++] = new Candidate(start + (length - size) / 2, start + length / 2); // Mitten bündig
			int otherCrossStart = horizontal ? other[1] : other[0];
			int otherCrossEnd = otherCrossStart + (horizontal ? other[3] : other[2]);
			boolean near = crossStart < otherCrossEnd + HudConfig.SNAP_DISTANCE * 2 && otherCrossStart < crossEnd + HudConfig.SNAP_DISTANCE * 2;
			if (near) {
				list[next++] = new Candidate(end + GAP, end + GAP / 2);              // direkt dahinter
				list[next++] = new Candidate(start - GAP - size, start - GAP / 2);   // direkt davor
			}
		}
		list[next++] = new Candidate(0, 0);
		list[next++] = new Candidate(available - size, available - 1);
		list[next++] = new Candidate((available - size) / 2, available / 2);
		return java.util.Arrays.copyOf(list, next);
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
