package de.axoclient.mod.ui;

import com.mojang.blaze3d.platform.NativeImage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.renderer.texture.DynamicTexture;
import net.minecraft.resources.Identifier;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * Die Linien-Symbole aus dem Mockup (SVG mit runden Linienenden). Jedes Symbol wird einmal je Bildschirmgröße in eine
 * weiße Textur gerastert und beim Zeichnen eingefärbt – so bleibt es bei jeder GUI-Skalierung scharf.
 */
public final class Icons {
	/** Ein Symbol: Linienzüge, gefüllte Flächen und Kreise in einem Quadrat der Kantenlänge {@code box}. */
	public static final class Icon {
		final String name;
		final float box;
		final float stroke;
		final List<float[]> lines = new ArrayList<>();
		final List<float[]> fills = new ArrayList<>();
		/** {cx, cy, r, filled ? 1 : 0} */
		final List<float[]> circles = new ArrayList<>();

		Icon(String name, float box, float stroke) {
			this.name = name;
			this.box = box;
			this.stroke = stroke;
		}

		Icon path(String d) {
			parse(d, lines, fills, false);
			return this;
		}

		Icon fill(String d) {
			parse(d, lines, fills, true);
			return this;
		}

		Icon circle(float cx, float cy, float r) {
			circles.add(new float[] { cx, cy, r, 0 });
			return this;
		}

		Icon dot(float cx, float cy, float r) {
			circles.add(new float[] { cx, cy, r, 1 });
			return this;
		}

		/** Kreisbogen im Uhrzeigersinn (0° = rechts, 90° = unten). */
		Icon arc(float cx, float cy, float r, float fromDeg, float toDeg) {
			int steps = Math.max(6, (int) Math.abs(toDeg - fromDeg) / 8);
			float[] points = new float[(steps + 1) * 2];
			for (int i = 0; i <= steps; i++) {
				double a = Math.toRadians(fromDeg + (toDeg - fromDeg) * i / steps);
				points[i * 2] = cx + (float) (Math.cos(a) * r);
				points[i * 2 + 1] = cy + (float) (Math.sin(a) * r);
			}
			lines.add(points);
			return this;
		}

		/** Quadratische Kurve als Linienzug. */
		Icon quad(float x0, float y0, float cx, float cy, float x1, float y1) {
			int steps = 14;
			float[] points = new float[(steps + 1) * 2];
			for (int i = 0; i <= steps; i++) {
				float t = i / (float) steps;
				float u = 1 - t;
				points[i * 2] = u * u * x0 + 2 * u * t * cx + t * t * x1;
				points[i * 2 + 1] = u * u * y0 + 2 * u * t * cy + t * t * y1;
			}
			lines.add(points);
			return this;
		}
	}

	public static final Icon CLOSE = new Icon("close", 16, 1.7F).path("M4 4l8 8M12 4l-8 8");
	public static final Icon PLAY = new Icon("play", 16, 1.5F).fill("M5 3.5v9l7-4.5z");
	public static final Icon SEND = new Icon("send", 16, 1.7F).path("M14 2L7 9").path("M14 2l-4.5 12-2.5-5-5-2.5z");
	public static final Icon CHECK = new Icon("check", 16, 2.0F).path("M3.5 8.5l3 3 6-7");
	public static final Icon CHECK_BOLD = new Icon("check_bold", 16, 2.6F).path("M3.5 8.5l3 3 6-7");
	public static final Icon PLUS = new Icon("plus", 16, 1.7F).path("M8 3v10M3 8h10");
	public static final Icon CHEVRON_DOWN = new Icon("chevron_down", 16, 1.6F).path("M4 6l4 4 4-4");
	public static final Icon CHEVRON_LEFT = new Icon("chevron_left", 16, 1.6F).path("M10 4L6 8l4 4");
	public static final Icon CHEVRON_RIGHT = new Icon("chevron_right", 16, 1.6F).path("M6 4l4 4-4 4");
	public static final Icon ARROW_RIGHT = new Icon("arrow_right", 16, 1.7F).path("M3 8h10M9.5 4.5L13 8l-3.5 3.5");
	public static final Icon ARROW_DOWN = new Icon("arrow_down", 16, 1.7F).path("M8 3v10M4.5 9.5L8 13l3.5-3.5");
	public static final Icon ARROW_UP = new Icon("arrow_up", 16, 1.7F).path("M8 13V3M4.5 6.5L8 3l3.5 3.5");
	public static final Icon RETRY = new Icon("retry", 16, 1.6F).arc(8, 8, 5.5F, 0, 315).path("M13.5 2.5v3h-3");
	public static final Icon OFFLINE = new Icon("offline", 16, 1.6F).path("M2 2l12 12")
		.arc(8, 9.5F, 4.2F, 196, 330).path("M3.4 12.5h7.6").arc(3.6F, 11, 1.5F, 90, 250).arc(12.4F, 10.8F, 1.6F, 290, 420);
	public static final Icon EYE = new Icon("eye", 16, 1.5F).quad(1.5F, 8, 8, -1, 14.5F, 8).quad(1.5F, 8, 8, 17, 14.5F, 8).circle(8, 8, 2);
	public static final Icon EYE_OFF = new Icon("eye_off", 16, 1.5F).path("M2 2l12 12").quad(3.9F, 5.2F, 8, 1, 14.5F, 8)
		.quad(12.4F, 11, 8, 15.5F, 1.5F, 8);
	public static final Icon MOVE = new Icon("move", 16, 1.6F).path("M8 1.5v13M1.5 8h13")
		.path("M6 3.5L8 1.5l2 2M6 12.5l2 2 2-2M3.5 6L1.5 8l2 2M12.5 6l2 2-2 2");
	public static final Icon GRID = new Icon("grid", 16, 1.5F).path("M3.5 2h9M14 3.5v9M12.5 14h-9M2 12.5v-9")
		.arc(3.5F, 3.5F, 1.5F, 180, 270).arc(12.5F, 3.5F, 1.5F, 270, 360).arc(12.5F, 12.5F, 1.5F, 0, 90).arc(3.5F, 12.5F, 1.5F, 90, 180)
		.path("M6 2v12M10 2v12M2 6h12M2 10h12");
	public static final Icon WARN = new Icon("warn", 16, 2.2F).path("M8 4v5M8 11.8v.2");
	public static final Icon CUBE = new Icon("cube", 16, 1.5F).path("M8 1.8l5.5 3.1v6.2L8 14.2l-5.5-3.1V4.9z").path("M2.5 4.9L8 8l5.5-3.1M8 8v6.2");
	public static final Icon NO_CAPE = new Icon("no_cape", 16, 1.4F).circle(8, 8, 5.5F).path("M4.2 11.8l7.6-7.6");
	public static final Icon IMPORT = new Icon("import", 16, 1.6F).path("M8 2v8M4.5 6.5L8 10l3.5-3.5M2.5 11v2.5h11V11");
	public static final Icon SETTINGS = new Icon("settings", 16, 1.5F).circle(8, 8, 2.2F).circle(8, 8, 5.2F)
		.path("M8 1.5v1.3M8 13.2v1.3M1.5 8h1.3M13.2 8h1.3M3.4 3.4l.9.9M11.7 11.7l.9.9M3.4 12.6l.9-.9M11.7 4.3l.9-.9");
	public static final Icon TRASH = new Icon("trash", 16, 1.5F).path("M2.5 4h11M6 4V2.5h4V4M4 4l.8 9.5h6.4L12 4M6.8 6.5v4.5M9.2 6.5v4.5");
	public static final Icon STAR = new Icon("star", 16, 1.4F).path("M8 1.8l1.9 3.9 4.3.6-3.1 3 .7 4.3L8 11.6l-3.8 2 .7-4.3-3.1-3 4.3-.6z");

	private static final String LEGS = "M12 14l-3 6M12 14l3 6";

	/** Strichmännchen der eingebauten Emotes (Mockup, 24er Raster). */
	public static final Map<String, Icon> EMOTES = new HashMap<>();
	/** Für eigene Emotes ohne eigenes Bild. */
	public static final Icon EMOTE_CUSTOM = figure("custom", 12, 5, "M12 7.5V14M12 9l-4 2M12 9l4 2" + LEGS + "M17 3.5l.6 1.2 1.3.2-.9.9.2 1.3-1.2-.6-1.2.6.2-1.3-.9-.9 1.3-.2z");

	static {
		EMOTES.put("wave", figure("wave", 12, 5, "M12 7.5V14M12 9l-3.5 3.5M12 9l3.5-3.5 1-2.5" + LEGS));
		EMOTES.put("bow", figure("bow", 16, 8, "M12 14l2.5-4.5M13.6 11l2.4 2.5" + LEGS));
		EMOTES.put("clap", figure("clap", 12, 5, "M12 7.5V14M12 9l-2 2.2 2 1.3M12 9l2 2.2-2 1.3" + LEGS));
		EMOTES.put("cheer", figure("cheer", 12, 5, "M12 7.5V14M12 9L8.5 4M12 9l3.5-5" + LEGS));
		EMOTES.put("dance", figure("dance", 12, 5, "M12 7.5V14M12 9L8 6.5M12 9l4 3M12 14l-4 3.5M12 14l2 6"));
		EMOTES.put("sit", figure("sit", 12, 8, "M12 10.5V16M12 16h5v4M12 12.5l3 1.5"));
		EMOTES.put("point", figure("point", 12, 5, "M12 7.5V14M12 9l6-1M12 9l-3 3.5" + LEGS));
		EMOTES.put("salute", figure("salute", 12, 5, "M12 7.5V14M12 9l2.2-2.8-3.4-1.6M12 9l-3 4" + LEGS));
		EMOTES.put("facepalm", figure("facepalm", 12, 5, "M12 7.5V14M12 9l2.2-1.8-1.4-2M12 9l-3 4" + LEGS));
		EMOTES.put("nod", figure("nod", 12, 5.5F, "M12 8V14M12 9.5l-2.5 3.5M12 9.5l2.5 3.5M8.5 3.5l1 1M15.5 3.5l-1 1" + LEGS));
	}

	private static Icon figure(String name, float hx, float hy, String d) {
		return new Icon("emote_" + name, 24, 1.5F).circle(hx, hy, 2.2F).path(d);
	}

	public static Icon emote(String id) {
		Icon icon = EMOTES.get(id);
		return icon != null ? icon : EMOTE_CUSTOM;
	}

	private static final Map<String, Identifier> TEXTURES = new HashMap<>();

	private Icons() {}

	/** Weiße Textur des Symbols mit genau {@code pixels} Bildschirmpunkten Kantenlänge (auf dem Render-Thread). */
	static Identifier texture(Icon icon, int pixels) {
		String key = icon.name + "_" + pixels;
		Identifier known = TEXTURES.get(key);
		if (known != null)
			return known;
		NativeImage image = new NativeImage(pixels, pixels, true);
		float scale = pixels / icon.box;
		float half = icon.stroke * scale / 2.0F;
		for (int y = 0; y < pixels; y++) {
			for (int x = 0; x < pixels; x++) {
				float px = (x + 0.5F) / scale;
				float py = (y + 0.5F) / scale;
				float coverage = 0;
				for (float[] line : icon.lines)
					coverage = Math.max(coverage, clamp(half - distance(line, px, py) * scale + 0.5F));
				for (float[] polygon : icon.fills) {
					float edge = distance(polygon, px, py) * scale;
					coverage = Math.max(coverage, inside(polygon, px, py) ? 1 : clamp(0.5F - edge + half));
				}
				for (float[] c : icon.circles) {
					float d = (float) Math.hypot(px - c[0], py - c[1]);
					coverage = Math.max(coverage, c[3] > 0 ? clamp((c[2] - d) * scale + 0.5F)
						: clamp(half - Math.abs(d - c[2]) * scale + 0.5F));
				}
				int alpha = Math.round(coverage * 255);
				image.setPixel(x, y, (alpha << 24) | 0xFFFFFF);
			}
		}
		Identifier id = Identifier.fromNamespaceAndPath("axoclient", "ui/icon/" + key);
		Minecraft.getInstance().getTextureManager().register(id, new DynamicTexture(() -> "AxoClient " + key, image));
		TEXTURES.put(key, id);
		return id;
	}

	private static float clamp(float v) {
		return Math.max(0, Math.min(1, v));
	}

	/** Kürzester Abstand zum Linienzug (Punkte abwechselnd x, y). */
	private static float distance(float[] p, float x, float y) {
		float best = Float.MAX_VALUE;
		if (p.length == 2)
			return (float) Math.hypot(x - p[0], y - p[1]);
		for (int i = 0; i + 3 < p.length; i += 2) {
			float ax = p[i], ay = p[i + 1], dx = p[i + 2] - ax, dy = p[i + 3] - ay;
			float t = Math.max(0, Math.min(1, ((x - ax) * dx + (y - ay) * dy) / Math.max(1e-6F, dx * dx + dy * dy)));
			best = Math.min(best, (float) Math.hypot(x - (ax + t * dx), y - (ay + t * dy)));
		}
		return best;
	}

	private static boolean inside(float[] p, float x, float y) {
		boolean in = false;
		int n = p.length / 2;
		for (int i = 0, j = n - 1; i < n; j = i++) {
			float xi = p[i * 2], yi = p[i * 2 + 1], xj = p[j * 2], yj = p[j * 2 + 1];
			if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi)
				in = !in;
		}
		return in;
	}

	/** Liest M/L/H/V/Z (groß = absolut, klein = relativ) in Linienzüge; geschlossene Pfade auf Wunsch als Fläche. */
	private static void parse(String d, List<float[]> lines, List<float[]> fills, boolean fill) {
		List<Float> current = new ArrayList<>();
		float x = 0, y = 0, startX = 0, startY = 0;
		char command = 'M';
		int i = 0;
		while (i < d.length()) {
			char c = d.charAt(i);
			if (Character.isWhitespace(c) || c == ',') {
				i++;
				continue;
			}
			if (Character.isLetter(c)) {
				command = c;
				i++;
				if (c == 'Z' || c == 'z') {
					current.add(startX);
					current.add(startY);
					x = startX;
					y = startY;
					flush(current, fill ? fills : lines);
				}
				continue;
			}
			int[] end = { i };
			float a = number(d, end);
			i = end[0];
			switch (command) {
				case 'M', 'm' -> {
					float b = nextNumber(d, end);
					i = end[0];
					flush(current, lines);
					x = command == 'm' ? x + a : a;
					y = command == 'm' ? y + b : b;
					startX = x;
					startY = y;
					current.add(x);
					current.add(y);
					command = command == 'm' ? 'l' : 'L';
				}
				case 'L', 'l' -> {
					float b = nextNumber(d, end);
					i = end[0];
					x = command == 'l' ? x + a : a;
					y = command == 'l' ? y + b : b;
					current.add(x);
					current.add(y);
				}
				case 'H', 'h' -> {
					x = command == 'h' ? x + a : a;
					current.add(x);
					current.add(y);
				}
				case 'V', 'v' -> {
					y = command == 'v' ? y + a : a;
					current.add(x);
					current.add(y);
				}
				default -> {
				}
			}
		}
		flush(current, lines);
	}

	private static void flush(List<Float> points, List<float[]> target) {
		if (points.size() >= 2) {
			float[] array = new float[points.size()];
			for (int i = 0; i < array.length; i++)
				array[i] = points.get(i);
			target.add(array);
		}
		points.clear();
	}

	private static float nextNumber(String d, int[] at) {
		int i = at[0];
		while (i < d.length() && (Character.isWhitespace(d.charAt(i)) || d.charAt(i) == ','))
			i++;
		at[0] = i;
		return number(d, at);
	}

	private static float number(String d, int[] at) {
		int i = at[0];
		int start = i;
		if (i < d.length() && (d.charAt(i) == '-' || d.charAt(i) == '+'))
			i++;
		boolean dot = false;
		while (i < d.length()) {
			char c = d.charAt(i);
			if (Character.isDigit(c))
				i++;
			else if (c == '.' && !dot) {
				dot = true;
				i++;
			} else
				break;
		}
		at[0] = i;
		return i > start ? Float.parseFloat(d.substring(start, i)) : 0;
	}
}
