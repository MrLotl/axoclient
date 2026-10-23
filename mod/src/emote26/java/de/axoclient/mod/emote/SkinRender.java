package de.axoclient.mod.emote;

import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.DataInputStream;
import java.io.IOException;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.TreeSet;
import java.util.zip.InflaterInputStream;

/**
 * Kleines Pixelbild eines Skins in der markantesten Pose eines Emotes (ohne Minecraft, reines Java). Der Launcher hat
 * dieselbe Rechnung in TypeScript ({@code ui/src/lib/util/emotePreview.ts}), damit die Vorschau überall gleich aussieht.
 * <p>
 * Koordinaten wie im Bedrock-Format der Emotes: y nach oben, der Spieler schaut nach +z (zur Kamera), Winkel in Grad,
 * Drehreihenfolge erst z, dann y, dann x. Kopf und Arme hängen am Oberkörper, der sich um die Hüfte dreht.
 */
public final class SkinRender {
	private SkinRender() {}

	// ---------- Pose aus der Animation ----------

	/** Drehung (Grad) und Verschiebung (Pixel) je Körperteil. */
	public static final class Pose {
		public final Map<String, float[]> rotation = new HashMap<>();
		public final Map<String, float[]> position = new HashMap<>();

		float[] rot(String part) {
			return rotation.getOrDefault(part, new float[3]);
		}

		float[] pos(String part) {
			return position.getOrDefault(part, new float[3]);
		}
	}

	/** Körperteil-Namen der Animation auf head, body, right_arm, left_arm, right_leg, left_leg abbilden. */
	static String part(String bone) {
		String b = bone.toLowerCase(java.util.Locale.ROOT).replace("_", "").replace(" ", "");
		return switch (b) {
			case "head" -> "head";
			case "body", "torso" -> "body";
			case "rightarm" -> "right_arm";
			case "leftarm" -> "left_arm";
			case "rightleg" -> "right_leg";
			case "leftleg" -> "left_leg";
			default -> null;
		};
	}

	/**
	 * Die markanteste Pose eines Emotes (Bedrock-/GeckoLib-Format): der Schlüsselbild-Zeitpunkt mit der stärksten
	 * Auslenkung. Unbekannte Formate ergeben die Grundhaltung.
	 */
	public static Pose pose(String json, String animation) {
		Pose pose = new Pose();
		try {
			JsonObject root = JsonParser.parseString(json).getAsJsonObject();
			JsonObject anims = root.has("animations") && root.get("animations").isJsonObject() ? root.getAsJsonObject("animations") : null;
			if (anims == null || anims.size() == 0)
				return pose;
			JsonElement picked = animation != null && anims.has(animation) ? anims.get(animation) : anims.entrySet().iterator().next().getValue();
			if (!picked.isJsonObject() || !picked.getAsJsonObject().has("bones"))
				return pose;
			JsonObject bones = picked.getAsJsonObject().getAsJsonObject("bones");
			// Alle Spuren lesen
			Map<String, TreeMapTrack> rot = new HashMap<>(), pos = new HashMap<>();
			TreeSet<Float> times = new TreeSet<>();
			for (Map.Entry<String, JsonElement> bone : bones.entrySet()) {
				String part = part(bone.getKey());
				if (part == null || !bone.getValue().isJsonObject())
					continue;
				JsonObject channels = bone.getValue().getAsJsonObject();
				TreeMapTrack r = track(channels.get("rotation"), times);
				TreeMapTrack p = track(channels.get("position"), times);
				if (r != null)
					rot.put(part, r);
				if (p != null)
					pos.put(part, p);
			}
			// Zeitpunkt mit der stärksten Auslenkung
			float best = 0, bestScore = -1;
			for (float t : times) {
				float score = 0;
				for (TreeMapTrack r : rot.values())
					for (float v : r.at(t))
						score += Math.abs(v);
				for (TreeMapTrack p : pos.values())
					for (float v : p.at(t))
						score += Math.abs(v) * 4;
				if (score > bestScore + 0.01F) {
					bestScore = score;
					best = t;
				}
			}
			for (Map.Entry<String, TreeMapTrack> e : rot.entrySet())
				pose.rotation.put(e.getKey(), e.getValue().at(best));
			for (Map.Entry<String, TreeMapTrack> e : pos.entrySet())
				pose.position.put(e.getKey(), e.getValue().at(best));
		} catch (RuntimeException ignored) {
			// unlesbar: Grundhaltung
		}
		return pose;
	}

	/** Schlüsselbilder einer Spur (Zeit → x, y, z), linear dazwischen. */
	static final class TreeMapTrack {
		final List<Float> times = new ArrayList<>();
		final List<float[]> values = new ArrayList<>();

		float[] at(float t) {
			if (times.isEmpty())
				return new float[3];
			if (t <= times.get(0))
				return values.get(0).clone();
			for (int i = 1; i < times.size(); i++)
				if (t <= times.get(i)) {
					float a = times.get(i - 1), b = times.get(i);
					float k = b > a ? (t - a) / (b - a) : 1;
					float[] va = values.get(i - 1), vb = values.get(i);
					return new float[] { va[0] + (vb[0] - va[0]) * k, va[1] + (vb[1] - va[1]) * k, va[2] + (vb[2] - va[2]) * k };
				}
			return values.get(values.size() - 1).clone();
		}
	}

	private static TreeMapTrack track(JsonElement channel, TreeSet<Float> times) {
		if (channel == null)
			return null;
		TreeMapTrack track = new TreeMapTrack();
		if (channel.isJsonArray()) {
			track.times.add(0F);
			track.values.add(vector(channel));
			times.add(0F);
			return track;
		}
		if (!channel.isJsonObject())
			return null;
		JsonObject keys = channel.getAsJsonObject();
		if (keys.has("vector")) {
			track.times.add(0F);
			track.values.add(vector(keys.get("vector")));
			times.add(0F);
			return track;
		}
		java.util.TreeMap<Float, float[]> sorted = new java.util.TreeMap<>();
		for (Map.Entry<String, JsonElement> key : keys.entrySet()) {
			float t;
			try {
				t = Float.parseFloat(key.getKey());
			} catch (NumberFormatException e) {
				continue;
			}
			JsonElement v = key.getValue();
			if (v.isJsonObject()) {
				JsonObject o = v.getAsJsonObject();
				v = o.has("vector") ? o.get("vector") : o.has("post") ? o.get("post") : o.has("pre") ? o.get("pre") : null;
				if (v != null && v.isJsonObject() && v.getAsJsonObject().has("vector"))
					v = v.getAsJsonObject().get("vector");
			}
			sorted.put(t, vector(v));
		}
		for (Map.Entry<Float, float[]> e : sorted.entrySet()) {
			track.times.add(e.getKey());
			track.values.add(e.getValue());
			times.add(e.getKey());
		}
		return track.times.isEmpty() ? null : track;
	}

	private static float[] vector(JsonElement v) {
		float[] out = new float[3];
		if (v == null || !v.isJsonArray())
			return out;
		JsonArray a = v.getAsJsonArray();
		for (int i = 0; i < 3 && i < a.size(); i++) {
			try {
				out[i] = a.get(i).getAsFloat();
			} catch (RuntimeException e) {
				out[i] = 0; // Ausdrücke (Molang) kennt die Vorschau nicht
			}
		}
		return out;
	}

	// ---------- Skin lesen ----------

	/** Liest ein PNG (8 Bit je Kanal, nicht verschachtelt) als ARGB; null, wenn es kein Skin-Bild ist. */
	public static int[][] decodePng(byte[] png) {
		try {
			DataInputStream in = new DataInputStream(new ByteArrayInputStream(png));
			long signature = in.readLong();
			if (signature != 0x89504E470D0A1A0AL)
				return null;
			int width = 0, height = 0, depth = 0, type = 0, interlace = 0;
			byte[] palette = null, trans = null;
			ByteArrayOutputStream data = new ByteArrayOutputStream();
			while (true) {
				int length = in.readInt();
				byte[] kind = new byte[4];
				in.readFully(kind);
				byte[] chunk = new byte[length];
				in.readFully(chunk);
				in.readInt(); // CRC
				String name = new String(kind, java.nio.charset.StandardCharsets.US_ASCII);
				if (name.equals("IHDR")) {
					DataInputStream h = new DataInputStream(new ByteArrayInputStream(chunk));
					width = h.readInt();
					height = h.readInt();
					depth = h.readUnsignedByte();
					type = h.readUnsignedByte();
					h.readUnsignedByte();
					h.readUnsignedByte();
					interlace = h.readUnsignedByte();
				} else if (name.equals("PLTE"))
					palette = chunk;
				else if (name.equals("tRNS"))
					trans = chunk;
				else if (name.equals("IDAT"))
					data.write(chunk);
				else if (name.equals("IEND"))
					break;
			}
			if (depth != 8 || interlace != 0 || width <= 0 || height <= 0 || width > 1024 || height > 1024)
				return null;
			int channels = switch (type) {
				case 0 -> 1;
				case 2 -> 3;
				case 3 -> 1;
				case 4 -> 2;
				case 6 -> 4;
				default -> -1;
			};
			if (channels < 0)
				return null;
			byte[] raw = new InflaterInputStream(new ByteArrayInputStream(data.toByteArray())).readAllBytes();
			int stride = width * channels;
			int[][] out = new int[height][width];
			byte[] prev = new byte[stride], cur = new byte[stride];
			int at = 0;
			for (int y = 0; y < height; y++) {
				int filter = raw[at++] & 0xFF;
				System.arraycopy(raw, at, cur, 0, stride);
				at += stride;
				for (int i = 0; i < stride; i++) {
					int a = i >= channels ? cur[i - channels] & 0xFF : 0;
					int b = prev[i] & 0xFF;
					int c = i >= channels ? prev[i - channels] & 0xFF : 0;
					int x = cur[i] & 0xFF;
					int v = switch (filter) {
						case 1 -> x + a;
						case 2 -> x + b;
						case 3 -> x + ((a + b) >> 1);
						case 4 -> {
							int p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c);
							yield x + (pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
						}
						default -> x;
					};
					cur[i] = (byte) v;
				}
				for (int x = 0; x < width; x++) {
					int o = x * channels;
					int r, g, bl, al = 255;
					switch (type) {
						case 0 -> r = g = bl = cur[o] & 0xFF;
						case 4 -> {
							r = g = bl = cur[o] & 0xFF;
							al = cur[o + 1] & 0xFF;
						}
						case 2 -> {
							r = cur[o] & 0xFF;
							g = cur[o + 1] & 0xFF;
							bl = cur[o + 2] & 0xFF;
						}
						case 3 -> {
							int index = cur[o] & 0xFF;
							if (palette == null || index * 3 + 2 >= palette.length)
								return null;
							r = palette[index * 3] & 0xFF;
							g = palette[index * 3 + 1] & 0xFF;
							bl = palette[index * 3 + 2] & 0xFF;
							al = trans != null && index < trans.length ? trans[index] & 0xFF : 255;
						}
						default -> {
							r = cur[o] & 0xFF;
							g = cur[o + 1] & 0xFF;
							bl = cur[o + 2] & 0xFF;
							al = cur[o + 3] & 0xFF;
						}
					}
					out[y][x] = (al << 24) | (r << 16) | (g << 8) | bl;
				}
				byte[] swap = prev;
				prev = cur;
				cur = swap;
			}
			return out;
		} catch (IOException | RuntimeException e) {
			return null;
		}
	}

	// ---------- Zeichnen ----------

	/** Ein Quader des Modells: Lage (Pixel), Größe, Textur-Ursprung, Aufblähung (zweite Ebene). */
	private record Box(String part, float x, float y, float z, int w, int h, int d, int u, int v, float grow, boolean mirror) {}

	private static List<Box> model(boolean slim, boolean legacy) {
		int aw = slim ? 3 : 4;
		List<Box> boxes = new ArrayList<>();
		boxes.add(new Box("head", -4, 24, -4, 8, 8, 8, 0, 0, 0, false));
		boxes.add(new Box("body", -4, 12, -2, 8, 12, 4, 16, 16, 0, false));
		boxes.add(new Box("right_arm", -4 - aw, 12, -2, aw, 12, 4, 40, 16, 0, false));
		boxes.add(new Box("left_arm", 4, 12, -2, aw, 12, 4, legacy ? 40 : 32, legacy ? 16 : 48, 0, legacy));
		boxes.add(new Box("right_leg", -4, 0, -2, 4, 12, 4, 0, 16, 0, false));
		boxes.add(new Box("left_leg", 0, 0, -2, 4, 12, 4, legacy ? 0 : 16, legacy ? 16 : 48, 0, legacy));
		boxes.add(new Box("head", -4, 24, -4, 8, 8, 8, 32, 0, 0.5F, false));
		if (!legacy) {
			boxes.add(new Box("body", -4, 12, -2, 8, 12, 4, 16, 32, 0.25F, false));
			boxes.add(new Box("right_arm", -4 - aw, 12, -2, aw, 12, 4, 40, 32, 0.25F, false));
			boxes.add(new Box("left_arm", 4, 12, -2, aw, 12, 4, 48, 48, 0.25F, false));
			boxes.add(new Box("right_leg", -4, 0, -2, 4, 12, 4, 0, 32, 0.25F, false));
			boxes.add(new Box("left_leg", 0, 0, -2, 4, 12, 4, 0, 48, 0.25F, false));
		}
		return boxes;
	}

	/** Schmale Arme (Alex): rechts neben dem rechten Arm (x 54–55) ist die Textur leer. */
	static boolean slim(int[][] skin) {
		if (skin.length < 64)
			return false;
		for (int y = 20; y < 32; y++)
			if ((skin[y][54] >>> 24) != 0 || (skin[y][55] >>> 24) != 0)
				return false;
		return true;
	}

	/** Drehpunkt eines Körperteils (Pixel). */
	private static float[] pivot(String part, boolean slim) {
		float shoulder = slim ? 4.5F : 5;
		return switch (part) {
			case "head" -> new float[] { 0, 24, 0 };
			case "body" -> new float[] { 0, 12, 0 };
			case "right_arm" -> new float[] { -shoulder, 22, 0 };
			case "left_arm" -> new float[] { shoulder, 22, 0 };
			case "right_leg" -> new float[] { -2, 12, 0 };
			case "left_leg" -> new float[] { 2, 12, 0 };
			default -> new float[3];
		};
	}

	/** 3 × 4-Matrix (Zeilen) für "um p drehen (z, y, x), dann um t verschieben". */
	private static float[] transform(float[] rotDeg, float[] p, float[] t) {
		double rx = Math.toRadians(rotDeg[0]), ry = Math.toRadians(rotDeg[1]), rz = Math.toRadians(rotDeg[2]);
		double cx = Math.cos(rx), sx = Math.sin(rx), cy = Math.cos(ry), sy = Math.sin(ry), cz = Math.cos(rz), sz = Math.sin(rz);
		// R = Rx · Ry · Rz
		double[] m = {
			cy * cz, -cy * sz, sy,
			sx * sy * cz + cx * sz, -sx * sy * sz + cx * cz, -sx * cy,
			-cx * sy * cz + sx * sz, cx * sy * sz + sx * cz, cx * cy };
		float[] out = new float[12];
		for (int r = 0; r < 3; r++) {
			out[r * 4] = (float) m[r * 3];
			out[r * 4 + 1] = (float) m[r * 3 + 1];
			out[r * 4 + 2] = (float) m[r * 3 + 2];
			out[r * 4 + 3] = (float) (p[r] + t[r] - (m[r * 3] * p[0] + m[r * 3 + 1] * p[1] + m[r * 3 + 2] * p[2]));
		}
		return out;
	}

	/** a · b (erst b, dann a). */
	private static float[] combine(float[] a, float[] b) {
		float[] out = new float[12];
		for (int r = 0; r < 3; r++) {
			for (int c = 0; c < 3; c++)
				out[r * 4 + c] = a[r * 4] * b[c] + a[r * 4 + 1] * b[4 + c] + a[r * 4 + 2] * b[8 + c];
			out[r * 4 + 3] = a[r * 4] * b[3] + a[r * 4 + 1] * b[7] + a[r * 4 + 2] * b[11] + a[r * 4 + 3];
		}
		return out;
	}

	private static float[] apply(float[] m, float x, float y, float z) {
		return new float[] { m[0] * x + m[1] * y + m[2] * z + m[3], m[4] * x + m[5] * y + m[6] * z + m[7], m[8] * x + m[9] * y + m[10] * z + m[11] };
	}

	/**
	 * Zeichnet den Skin (ARGB, 64 × 64 oder 64 × 32) in der Pose als size × size-Bild (ARGB, zeilenweise), schräg von
	 * vorn, Pixel scharf.
	 */
	public static int[] render(int[][] skin, Pose pose, int size) {
		boolean legacy = skin.length < 64;
		boolean slim = !legacy && slim(skin);
		// Teile bewegen: Kopf und Arme hängen am Oberkörper
		Map<String, float[]> local = new HashMap<>();
		for (String part : new String[] { "head", "body", "right_arm", "left_arm", "right_leg", "left_leg" })
			local.put(part, transform(pose.rot(part), pivot(part, slim), pose.pos(part)));
		float[] body = local.get("body");
		Map<String, float[]> world = new HashMap<>(local);
		for (String part : new String[] { "head", "right_arm", "left_arm" })
			world.put(part, combine(body, local.get(part)));
		// Kamera: schräg von vorn rechts (die meisten Emotes nutzen den rechten Arm) und etwas von oben
		float[] view = transform(new float[] { 10, 25, 0 }, new float[3], new float[3]);

		List<float[]> quads = new ArrayList<>(); // je Pixel der Textur: 4 Ecken (x, y, z) und Farbe
		List<Integer> colors = new ArrayList<>();
		for (Box box : model(slim, legacy))
			addBox(box, combine(view, world.get(box.part())), skin, quads, colors);
		if (quads.isEmpty())
			return new int[size * size];

		// Fester Ausschnitt für alle Posen (gleich große Figuren): Höhe 42 Pixel um die Körpermitte
		float scale = (size - 4) / 42.0F;
		float ox = size / 2.0F, oy = size / 2.0F + 15 * scale;

		int[] out = new int[size * size];
		float[] depth = new float[size * size];
		java.util.Arrays.fill(depth, -Float.MAX_VALUE);
		for (int i = 0; i < quads.size(); i++) {
			float[] q = quads.get(i);
			float[] s = new float[12];
			for (int k = 0; k < 4; k++) {
				s[k * 3] = ox + q[k * 3] * scale;
				s[k * 3 + 1] = oy - q[k * 3 + 1] * scale;
				s[k * 3 + 2] = q[k * 3 + 2];
			}
			int c = colors.get(i);
			triangle(out, depth, size, s, 0, 1, 2, c);
			triangle(out, depth, size, s, 0, 2, 3, c);
		}
		return out;
	}

	/** Die sichtbaren Seiten eines Quaders als Pixel-Vierecke (Farbe schon schattiert). */
	private static void addBox(Box b, float[] m, int[][] skin, List<float[]> quads, List<Integer> colors) {
		float g = b.grow();
		float x0 = b.x() - g, y0 = b.y() - g, z0 = b.z() - g, x1 = b.x() + b.w() + g, y1 = b.y() + b.h() + g, z1 = b.z() + b.d() + g;
		int w = b.w(), h = b.h(), d = b.d(), u = b.u(), v = b.v();
		// Seite: Textur-Ursprung, Breite/Höhe in Pixeln, Ecke und Achsen im Raum
		addFace(b, m, skin, quads, colors, u + d, v + d, w, h, new float[] { x0, y1, z1 }, new float[] { x1 - x0, 0, 0 }, new float[] { 0, y0 - y1, 0 }); // vorn
		addFace(b, m, skin, quads, colors, u + 2 * d + w, v + d, w, h, new float[] { x1, y1, z0 }, new float[] { x0 - x1, 0, 0 }, new float[] { 0, y0 - y1, 0 }); // hinten
		addFace(b, m, skin, quads, colors, u, v + d, d, h, new float[] { x0, y1, z0 }, new float[] { 0, 0, z1 - z0 }, new float[] { 0, y0 - y1, 0 }); // rechte Seite (−x)
		addFace(b, m, skin, quads, colors, u + d + w, v + d, d, h, new float[] { x1, y1, z1 }, new float[] { 0, 0, z0 - z1 }, new float[] { 0, y0 - y1, 0 }); // linke Seite (+x)
		addFace(b, m, skin, quads, colors, u + d, v, w, d, new float[] { x0, y1, z0 }, new float[] { x1 - x0, 0, 0 }, new float[] { 0, 0, z1 - z0 }); // oben
		addFace(b, m, skin, quads, colors, u + d + w, v, w, d, new float[] { x0, y0, z1 }, new float[] { x1 - x0, 0, 0 }, new float[] { 0, 0, z0 - z1 }); // unten
	}

	private static void addFace(Box b, float[] m, int[][] skin, List<float[]> quads, List<Integer> colors, int tu, int tv, int tw, int th,
								float[] origin, float[] across, float[] down) {
		// Normale nach dem Drehen (für das Licht; verdeckte Seiten erledigt der Tiefenpuffer)
		float[] o = apply(m, origin[0], origin[1], origin[2]);
		float[] a = sub(apply(m, origin[0] + across[0], origin[1] + across[1], origin[2] + across[2]), o);
		float[] dn = sub(apply(m, origin[0] + down[0], origin[1] + down[1], origin[2] + down[2]), o);
		float nx = a[1] * dn[2] - a[2] * dn[1], ny = a[2] * dn[0] - a[0] * dn[2], nz = a[0] * dn[1] - a[1] * dn[0];
		float len = (float) Math.sqrt(nx * nx + ny * ny + nz * nz);
		if (len < 1e-6F)
			return;
		nx /= len;
		ny /= len;
		nz /= len;
		// Licht von vorn oben
		float light = 0.55F + 0.45F * Math.max(0, nx * 0.25F + ny * 0.55F + nz * 0.8F);
		for (int j = 0; j < th; j++)
			for (int i = 0; i < tw; i++) {
				int sx = b.mirror() ? tu + tw - 1 - i : tu + i, sy = tv + j;
				if (sy >= skin.length || sx >= skin[sy].length)
					continue;
				int c = skin[sy][sx];
				if (b.grow() > 0 && (c >>> 24) < 128)
					continue; // zweite Ebene: nur gesetzte Pixel
				c |= 0xFF000000;
				float fi0 = i / (float) tw, fi1 = (i + 1) / (float) tw, fj0 = j / (float) th, fj1 = (j + 1) / (float) th;
				float[] q = new float[12];
				float[][] corners = { { fi0, fj0 }, { fi1, fj0 }, { fi1, fj1 }, { fi0, fj1 } };
				for (int k = 0; k < 4; k++) {
					float px = o[0] + a[0] * corners[k][0] + dn[0] * corners[k][1];
					float py = o[1] + a[1] * corners[k][0] + dn[1] * corners[k][1];
					float pz = o[2] + a[2] * corners[k][0] + dn[2] * corners[k][1];
					q[k * 3] = px;
					q[k * 3 + 1] = py;
					q[k * 3 + 2] = pz;
				}
				quads.add(q);
				int r = Math.min(255, Math.round(((c >> 16) & 255) * light));
				int gg = Math.min(255, Math.round(((c >> 8) & 255) * light));
				int bb = Math.min(255, Math.round((c & 255) * light));
				colors.add(0xFF000000 | (r << 16) | (gg << 8) | bb);
			}
	}

	private static float[] sub(float[] a, float[] b) {
		return new float[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
	}

	/** Dreieck mit Tiefentest (größeres z = näher an der Kamera), Pixelmitten-Regel. */
	private static void triangle(int[] out, float[] depth, int size, float[] s, int i0, int i1, int i2, int color) {
		float ax = s[i0 * 3], ay = s[i0 * 3 + 1], az = s[i0 * 3 + 2];
		float bx = s[i1 * 3], by = s[i1 * 3 + 1], bz = s[i1 * 3 + 2];
		float cx = s[i2 * 3], cy = s[i2 * 3 + 1], cz = s[i2 * 3 + 2];
		float area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
		if (Math.abs(area) < 1e-6F)
			return;
		int minX = Math.max(0, (int) Math.floor(Math.min(ax, Math.min(bx, cx))));
		int maxX = Math.min(size - 1, (int) Math.ceil(Math.max(ax, Math.max(bx, cx))));
		int minY = Math.max(0, (int) Math.floor(Math.min(ay, Math.min(by, cy))));
		int maxY = Math.min(size - 1, (int) Math.ceil(Math.max(ay, Math.max(by, cy))));
		for (int y = minY; y <= maxY; y++)
			for (int x = minX; x <= maxX; x++) {
				float px = x + 0.5F, py = y + 0.5F;
				float w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
				float w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
				float w2 = 1 - w0 - w1;
				if (w0 < -1e-4F || w1 < -1e-4F || w2 < -1e-4F)
					continue;
				float z = w0 * az + w1 * bz + w2 * cz;
				int at = y * size + x;
				if (z > depth[at]) {
					depth[at] = z;
					out[at] = color;
				}
			}
	}
}
