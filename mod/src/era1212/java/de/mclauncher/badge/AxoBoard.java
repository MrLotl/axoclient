package de.mclauncher.badge;

import net.minecraft.client.MinecraftClient;
import net.minecraft.client.font.TextRenderer;
import net.minecraft.client.gui.DrawContext;
import net.minecraft.scoreboard.Scoreboard;
import net.minecraft.scoreboard.ScoreboardDisplaySlot;
import net.minecraft.scoreboard.ScoreboardEntry;
import net.minecraft.scoreboard.ScoreboardObjective;
import net.minecraft.scoreboard.Team;
import net.minecraft.scoreboard.number.NumberFormat;
import net.minecraft.scoreboard.number.StyledNumberFormat;
import net.minecraft.text.Text;

import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;

/** Die Seitenleiste (Scoreboard) des Servers, gezeichnet von uns, damit sie sich frei verschieben lässt. */
final class AxoBoard {
	private static final int LINE = 9;
	private static final int MAX_ROWS = 15;
	private static final Comparator<ScoreboardEntry> ORDER = Comparator.comparingInt(ScoreboardEntry::value)
		.reversed().thenComparing(ScoreboardEntry::owner, String.CASE_INSENSITIVE_ORDER);

	private record Row(Text name, Text score, int scoreWidth) {}

	private static boolean active;
	private static Text title = Text.empty();
	private static List<Row> rows = List.of();
	private static int width;
	private static int height;
	private static long stamp;

	private AxoBoard() {}

	private static void refresh() {
		long now = System.currentTimeMillis();
		if (now - stamp < 25)
			return;
		stamp = now;
		active = false;
		MinecraftClient client = MinecraftClient.getInstance();
		if (client.world == null || client.player == null)
			return;
		Scoreboard scoreboard = client.world.getScoreboard();
		ScoreboardObjective objective = null;
		Team team = scoreboard.getScoreHolderTeam(client.player.getNameForScoreboard());
		if (team != null && team.getColor() != null) {
			ScoreboardDisplaySlot slot = ScoreboardDisplaySlot.fromFormatting(team.getColor());
			if (slot != null)
				objective = scoreboard.getObjectiveForSlot(slot);
		}
		if (objective == null)
			objective = scoreboard.getObjectiveForSlot(ScoreboardDisplaySlot.SIDEBAR);
		if (objective == null)
			return;

		TextRenderer font = client.textRenderer;
		NumberFormat format = objective.getNumberFormatOr(StyledNumberFormat.RED);
		title = objective.getDisplayName();
		int gap = font.getWidth(": ");
		int widest = font.getWidth(title);
		ArrayList<Row> list = new ArrayList<>();
		List<ScoreboardEntry> entries = scoreboard.getScoreboardEntries(objective).stream()
			.filter(entry -> !entry.owner().startsWith("#")).sorted(ORDER).limit(MAX_ROWS).toList();
		for (ScoreboardEntry entry : entries) {
			Text name = Team.decorateName(scoreboard.getScoreHolderTeam(entry.owner()), entry.name());
			Text score = entry.formatted(format);
			int scoreWidth = font.getWidth(score);
			list.add(new Row(name, score, scoreWidth));
			widest = Math.max(widest, font.getWidth(name) + (scoreWidth > 0 ? gap + scoreWidth : 0));
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

	static void draw(DrawContext context, int x, int y) {
		refresh();
		if (!active)
			return;
		TextRenderer font = MinecraftClient.getInstance().textRenderer;
		context.drawText(font, title, x + (width - font.getWidth(title)) / 2, y + 1, 0xFFFFFFFF, false);
		int row = y + LINE;
		for (Row entry : rows) {
			context.drawText(font, entry.name(), x, row + 1, 0xFFFFFFFF, false);
			if (entry.scoreWidth() > 0)
				context.drawText(font, entry.score(), x + width - entry.scoreWidth(), row + 1, 0xFFFFFFFF, false);
			row += LINE;
		}
	}
}
