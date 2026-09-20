package de.mclauncher.badge;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.numbers.NumberFormat;
import net.minecraft.network.chat.numbers.StyledFormat;
import net.minecraft.world.scores.DisplaySlot;
import net.minecraft.world.scores.Objective;
import net.minecraft.world.scores.PlayerScoreEntry;
import net.minecraft.world.scores.PlayerTeam;
import net.minecraft.world.scores.Scoreboard;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Optional;

/** Die Seitenleiste (Scoreboard) des Servers, gezeichnet von uns, damit sie sich frei verschieben lässt. */
final class AxoBoard {
	private static final int LINE = 9;
	private static final int MAX_ROWS = 15;
	private static final Comparator<PlayerScoreEntry> ORDER = Comparator.comparingInt(PlayerScoreEntry::value)
		.reversed().thenComparing(PlayerScoreEntry::owner, String.CASE_INSENSITIVE_ORDER);

	private record Row(Component name, Component score, int scoreWidth) {}

	private static boolean active;
	private static Component title = Component.empty();
	private static List<Row> rows = List.of();
	private static int width;
	private static int height;
	private static long stamp;

	private AxoBoard() {}

	/** Die Seitenleiste der Teamfarbe des Spielers; die Farbe heißt je nach Version anders, der Name ist gleich. */
	private static Objective teamObjective(Scoreboard scoreboard, PlayerTeam team) {
		if (team == null)
			return null;
		Object color = team.getColor();
		if (color instanceof Optional<?> optional)
			color = optional.orElse(null);
		if (!(color instanceof Enum<?> named))
			return null;
		try {
			return scoreboard.getDisplayObjective(DisplaySlot.valueOf("TEAM_" + named.name()));
		} catch (IllegalArgumentException e) {
			return null;
		}
	}

	private static void refresh() {
		long now = System.currentTimeMillis();
		if (now - stamp < 25)
			return;
		stamp = now;
		active = false;
		Minecraft client = Minecraft.getInstance();
		if (client.level == null || client.player == null)
			return;
		Scoreboard scoreboard = client.level.getScoreboard();
		Objective objective = teamObjective(scoreboard, scoreboard.getPlayersTeam(client.player.getScoreboardName()));
		if (objective == null)
			objective = scoreboard.getDisplayObjective(DisplaySlot.SIDEBAR);
		if (objective == null)
			return;

		Font font = client.font;
		NumberFormat format = objective.numberFormatOrDefault(StyledFormat.SIDEBAR_DEFAULT);
		title = objective.getDisplayName();
		int gap = font.width(": ");
		int widest = font.width(title);
		ArrayList<Row> list = new ArrayList<>();
		List<PlayerScoreEntry> entries = scoreboard.listPlayerScores(objective).stream()
			.filter(entry -> !entry.owner().startsWith("#")).sorted(ORDER).limit(MAX_ROWS).toList();
		for (PlayerScoreEntry entry : entries) {
			Component name = PlayerTeam.formatNameForTeam(scoreboard.getPlayersTeam(entry.owner()), entry.ownerName());
			Component score = entry.formatValue(format);
			int scoreWidth = font.width(score);
			list.add(new Row(name, score, scoreWidth));
			widest = Math.max(widest, font.width(name) + (scoreWidth > 0 ? gap + scoreWidth : 0));
		}
		rows = list;
		width = widest;
		height = (list.size() + 1) * LINE;
		active = true;
	}

	/** {Breite, Höhe} der Seitenleiste oder null, wenn gerade keine da ist. */
	static int[] size() {
		refresh();
		return active ? new int[] { width, height } : null;
	}

	static void draw(GuiGraphicsExtractor graphics, int x, int y) {
		refresh();
		if (!active)
			return;
		Font font = Minecraft.getInstance().font;
		graphics.text(font, title, x + (width - font.width(title)) / 2, y + 1, 0xFFFFFFFF, false);
		int row = y + LINE;
		for (Row entry : rows) {
			graphics.text(font, entry.name(), x, row + 1, 0xFFFFFFFF, false);
			if (entry.scoreWidth() > 0)
				graphics.text(font, entry.score(), x + width - entry.scoreWidth(), row + 1, 0xFFFFFFFF, false);
			row += LINE;
		}
	}
}
