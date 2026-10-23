import { call } from '../bridge/bridge';

/**
 * Vorschaubild eines Emotes: der eigene Skin in der markantesten Pose, als scharfes Pixelbild. Dieselbe Rechnung wie
 * im Spiel (mod/src/emote26/java/de/axoclient/mod/emote/SkinRender.java), damit die Vorschau überall gleich aussieht.
 *
 * Koordinaten wie im Bedrock-Format der Emotes: y nach oben, der Spieler schaut nach +z (zur Kamera), Winkel in Grad,
 * Drehreihenfolge erst z, dann y, dann x. Kopf und Arme hängen am Oberkörper, der sich um die Hüfte dreht.
 */

type Vec = [number, number, number];
type Mat = number[]; // 3 × 4, zeilenweise

export interface Pose {
  rotation: Record<string, Vec>;
  position: Record<string, Vec>;
}

/** Skin als ARGB-Zeilen (wie im Spiel). */
export type Skin = number[][];

const PARTS = ['head', 'body', 'right_arm', 'left_arm', 'right_leg', 'left_leg'] as const;

function part(bone: string): string | null {
  switch (bone.toLowerCase().replace(/[_ ]/g, '')) {
    case 'head': return 'head';
    case 'body':
    case 'torso': return 'body';
    case 'rightarm': return 'right_arm';
    case 'leftarm': return 'left_arm';
    case 'rightleg': return 'right_leg';
    case 'leftleg': return 'left_leg';
    default: return null;
  }
}

// ---------- Pose aus der Animation ----------

class Track {
  times: number[] = [];
  values: Vec[] = [];

  at(t: number): Vec {
    if (!this.times.length) return [0, 0, 0];
    if (t <= this.times[0]) return [...this.values[0]] as Vec;
    for (let i = 1; i < this.times.length; i++)
      if (t <= this.times[i]) {
        const a = this.times[i - 1], b = this.times[i];
        const k = b > a ? (t - a) / (b - a) : 1;
        const va = this.values[i - 1], vb = this.values[i];
        return [va[0] + (vb[0] - va[0]) * k, va[1] + (vb[1] - va[1]) * k, va[2] + (vb[2] - va[2]) * k];
      }
    return [...this.values[this.values.length - 1]] as Vec;
  }
}

function vector(v: unknown): Vec {
  const out: Vec = [0, 0, 0];
  if (!Array.isArray(v)) return out;
  for (let i = 0; i < 3 && i < v.length; i++) {
    const n = typeof v[i] === 'number' ? v[i] : typeof v[i] === 'string' ? Number(v[i]) : NaN;
    out[i] = Number.isFinite(n) ? n : 0; // Ausdrücke (Molang) kennt die Vorschau nicht
  }
  return out;
}

function track(channel: unknown, times: Set<number>): Track | null {
  if (channel == null) return null;
  const tr = new Track();
  if (Array.isArray(channel)) {
    tr.times.push(0);
    tr.values.push(vector(channel));
    times.add(0);
    return tr;
  }
  if (typeof channel !== 'object') return null;
  const keys = channel as Record<string, unknown>;
  if ('vector' in keys) {
    tr.times.push(0);
    tr.values.push(vector(keys.vector));
    times.add(0);
    return tr;
  }
  const sorted: [number, Vec][] = [];
  for (const [key, raw] of Object.entries(keys)) {
    const t = Number.parseFloat(key);
    if (!Number.isFinite(t)) continue;
    let v: unknown = raw;
    if (v && typeof v === 'object' && !Array.isArray(v)) {
      const o = v as Record<string, unknown>;
      v = 'vector' in o ? o.vector : 'post' in o ? o.post : 'pre' in o ? o.pre : null;
      if (v && typeof v === 'object' && !Array.isArray(v) && 'vector' in (v as object)) v = (v as Record<string, unknown>).vector;
    }
    sorted.push([t, vector(v)]);
  }
  sorted.sort((a, b) => a[0] - b[0]);
  for (const [t, v] of sorted) {
    tr.times.push(t);
    tr.values.push(v);
    times.add(t);
  }
  return tr.times.length ? tr : null;
}

/** Die markanteste Pose (Schlüsselbild mit der stärksten Auslenkung); unbekannte Formate ergeben die Grundhaltung. */
export function pose(json: string, animation: string | null): Pose {
  const result: Pose = { rotation: {}, position: {} };
  try {
    const root = JSON.parse(json);
    const anims = root && typeof root.animations === 'object' ? (root.animations as Record<string, any>) : null;
    if (!anims || !Object.keys(anims).length) return result;
    const picked = animation != null && animation in anims ? anims[animation] : anims[Object.keys(anims)[0]];
    if (!picked || typeof picked !== 'object' || !picked.bones || typeof picked.bones !== 'object') return result;
    const rot: Record<string, Track> = {}, pos: Record<string, Track> = {};
    const times = new Set<number>();
    for (const [bone, channels] of Object.entries(picked.bones as Record<string, any>)) {
      const p = part(bone);
      if (!p || !channels || typeof channels !== 'object') continue;
      const r = track(channels.rotation, times);
      const q = track(channels.position, times);
      if (r) rot[p] = r;
      if (q) pos[p] = q;
    }
    let best = 0, bestScore = -1;
    for (const t of [...times].sort((a, b) => a - b)) {
      let score = 0;
      for (const r of Object.values(rot)) for (const v of r.at(t)) score += Math.abs(v);
      for (const q of Object.values(pos)) for (const v of q.at(t)) score += Math.abs(v) * 4;
      if (score > bestScore + 0.01) {
        bestScore = score;
        best = t;
      }
    }
    for (const [k, r] of Object.entries(rot)) result.rotation[k] = r.at(best);
    for (const [k, q] of Object.entries(pos)) result.position[k] = q.at(best);
  } catch {
    // unlesbar: Grundhaltung
  }
  return result;
}

// ---------- Zeichnen ----------

interface Box { part: string; x: number; y: number; z: number; w: number; h: number; d: number; u: number; v: number; grow: number; mirror: boolean }

function model(slim: boolean, legacy: boolean): Box[] {
  const aw = slim ? 3 : 4;
  const b = (part: string, x: number, y: number, z: number, w: number, h: number, d: number, u: number, v: number, grow = 0, mirror = false): Box =>
    ({ part, x, y, z, w, h, d, u, v, grow, mirror });
  const boxes = [
    b('head', -4, 24, -4, 8, 8, 8, 0, 0),
    b('body', -4, 12, -2, 8, 12, 4, 16, 16),
    b('right_arm', -4 - aw, 12, -2, aw, 12, 4, 40, 16),
    b('left_arm', 4, 12, -2, aw, 12, 4, legacy ? 40 : 32, legacy ? 16 : 48, 0, legacy),
    b('right_leg', -4, 0, -2, 4, 12, 4, 0, 16),
    b('left_leg', 0, 0, -2, 4, 12, 4, legacy ? 0 : 16, legacy ? 16 : 48, 0, legacy),
    b('head', -4, 24, -4, 8, 8, 8, 32, 0, 0.5)
  ];
  if (!legacy)
    boxes.push(
      b('body', -4, 12, -2, 8, 12, 4, 16, 32, 0.25),
      b('right_arm', -4 - aw, 12, -2, aw, 12, 4, 40, 32, 0.25),
      b('left_arm', 4, 12, -2, aw, 12, 4, 48, 48, 0.25),
      b('right_leg', -4, 0, -2, 4, 12, 4, 0, 32, 0.25),
      b('left_leg', 0, 0, -2, 4, 12, 4, 0, 48, 0.25)
    );
  return boxes;
}

/** Schmale Arme (Alex): rechts neben dem rechten Arm (x 54–55) ist die Textur leer. */
function isSlim(skin: Skin): boolean {
  if (skin.length < 64) return false;
  for (let y = 20; y < 32; y++) if (skin[y][54] >>> 24 !== 0 || skin[y][55] >>> 24 !== 0) return false;
  return true;
}

function pivot(p: string, slim: boolean): Vec {
  const shoulder = slim ? 4.5 : 5;
  switch (p) {
    case 'head': return [0, 24, 0];
    case 'body': return [0, 12, 0];
    case 'right_arm': return [-shoulder, 22, 0];
    case 'left_arm': return [shoulder, 22, 0];
    case 'right_leg': return [-2, 12, 0];
    case 'left_leg': return [2, 12, 0];
    default: return [0, 0, 0];
  }
}

const f = Math.fround;

/** Matrix für "um p drehen (z, y, x), dann um t verschieben". */
function transform(rotDeg: Vec, p: Vec, t: Vec): Mat {
  const rx = (rotDeg[0] * Math.PI) / 180, ry = (rotDeg[1] * Math.PI) / 180, rz = (rotDeg[2] * Math.PI) / 180;
  const cx = Math.cos(rx), sx = Math.sin(rx), cy = Math.cos(ry), sy = Math.sin(ry), cz = Math.cos(rz), sz = Math.sin(rz);
  const m = [
    cy * cz, -cy * sz, sy,
    sx * sy * cz + cx * sz, -sx * sy * sz + cx * cz, -sx * cy,
    -cx * sy * cz + sx * sz, cx * sy * sz + sx * cz, cx * cy
  ];
  const out: Mat = new Array(12);
  for (let r = 0; r < 3; r++) {
    out[r * 4] = f(m[r * 3]);
    out[r * 4 + 1] = f(m[r * 3 + 1]);
    out[r * 4 + 2] = f(m[r * 3 + 2]);
    out[r * 4 + 3] = f(p[r] + t[r] - (m[r * 3] * p[0] + m[r * 3 + 1] * p[1] + m[r * 3 + 2] * p[2]));
  }
  return out;
}

/** a · b (erst b, dann a). */
function combine(a: Mat, b: Mat): Mat {
  const out: Mat = new Array(12);
  for (let r = 0; r < 3; r++) {
    for (let c = 0; c < 3; c++) out[r * 4 + c] = f(a[r * 4] * b[c] + a[r * 4 + 1] * b[4 + c] + a[r * 4 + 2] * b[8 + c]);
    out[r * 4 + 3] = f(a[r * 4] * b[3] + a[r * 4 + 1] * b[7] + a[r * 4 + 2] * b[11] + a[r * 4 + 3]);
  }
  return out;
}

function apply(m: Mat, x: number, y: number, z: number): Vec {
  return [m[0] * x + m[1] * y + m[2] * z + m[3], m[4] * x + m[5] * y + m[6] * z + m[7], m[8] * x + m[9] * y + m[10] * z + m[11]];
}

/** Zeichnet den Skin in der Pose als size × size-Bild (ARGB, zeilenweise), schräg von vorn, Pixel scharf. */
export function render(skin: Skin, p: Pose, size: number): Int32Array {
  const legacy = skin.length < 64;
  const slim = !legacy && isSlim(skin);
  const local: Record<string, Mat> = {};
  for (const name of PARTS) local[name] = transform(p.rotation[name] ?? [0, 0, 0], pivot(name, slim), p.position[name] ?? [0, 0, 0]);
  const world: Record<string, Mat> = { ...local };
  for (const name of ['head', 'right_arm', 'left_arm']) world[name] = combine(local.body, local[name]);
  const view = transform([10, 25, 0], [0, 0, 0], [0, 0, 0]);

  const quads: number[][] = [];
  const colors: number[] = [];
  for (const box of model(slim, legacy)) addBox(box, combine(view, world[box.part]), skin, quads, colors);
  const out = new Int32Array(size * size);
  if (!quads.length) return out;

  const scale = (size - 4) / 42;
  const ox = size / 2, oy = size / 2 + 15 * scale;
  const depth = new Float64Array(size * size).fill(-Infinity);
  for (let i = 0; i < quads.length; i++) {
    const q = quads[i];
    const s = new Array(12);
    for (let k = 0; k < 4; k++) {
      s[k * 3] = ox + q[k * 3] * scale;
      s[k * 3 + 1] = oy - q[k * 3 + 1] * scale;
      s[k * 3 + 2] = q[k * 3 + 2];
    }
    triangle(out, depth, size, s, 0, 1, 2, colors[i]);
    triangle(out, depth, size, s, 0, 2, 3, colors[i]);
  }
  return out;
}

function addBox(b: Box, m: Mat, skin: Skin, quads: number[][], colors: number[]) {
  const g = b.grow;
  const x0 = b.x - g, y0 = b.y - g, z0 = b.z - g, x1 = b.x + b.w + g, y1 = b.y + b.h + g, z1 = b.z + b.d + g;
  const { w, h, d, u, v } = b;
  addFace(b, m, skin, quads, colors, u + d, v + d, w, h, [x0, y1, z1], [x1 - x0, 0, 0], [0, y0 - y1, 0]); // vorn
  addFace(b, m, skin, quads, colors, u + 2 * d + w, v + d, w, h, [x1, y1, z0], [x0 - x1, 0, 0], [0, y0 - y1, 0]); // hinten
  addFace(b, m, skin, quads, colors, u, v + d, d, h, [x0, y1, z0], [0, 0, z1 - z0], [0, y0 - y1, 0]); // rechte Seite (−x)
  addFace(b, m, skin, quads, colors, u + d + w, v + d, d, h, [x1, y1, z1], [0, 0, z0 - z1], [0, y0 - y1, 0]); // linke Seite (+x)
  addFace(b, m, skin, quads, colors, u + d, v, w, d, [x0, y1, z0], [x1 - x0, 0, 0], [0, 0, z1 - z0]); // oben
  addFace(b, m, skin, quads, colors, u + d + w, v, w, d, [x0, y0, z1], [x1 - x0, 0, 0], [0, 0, z0 - z1]); // unten
}

function addFace(b: Box, m: Mat, skin: Skin, quads: number[][], colors: number[], tu: number, tv: number, tw: number, th: number,
  origin: Vec, across: Vec, down: Vec) {
  const o = apply(m, origin[0], origin[1], origin[2]);
  const a1 = apply(m, origin[0] + across[0], origin[1] + across[1], origin[2] + across[2]);
  const d1 = apply(m, origin[0] + down[0], origin[1] + down[1], origin[2] + down[2]);
  const a: Vec = [a1[0] - o[0], a1[1] - o[1], a1[2] - o[2]];
  const dn: Vec = [d1[0] - o[0], d1[1] - o[1], d1[2] - o[2]];
  let nx = a[1] * dn[2] - a[2] * dn[1], ny = a[2] * dn[0] - a[0] * dn[2], nz = a[0] * dn[1] - a[1] * dn[0];
  const len = Math.sqrt(nx * nx + ny * ny + nz * nz);
  if (len < 1e-6) return;
  nx /= len;
  ny /= len;
  nz /= len;
  // Licht von vorn oben
  const light = 0.55 + 0.45 * Math.max(0, nx * 0.25 + ny * 0.55 + nz * 0.8);
  for (let j = 0; j < th; j++)
    for (let i = 0; i < tw; i++) {
      const sx = b.mirror ? tu + tw - 1 - i : tu + i, sy = tv + j;
      if (sy >= skin.length || sx >= skin[sy].length) continue;
      let c = skin[sy][sx];
      if (b.grow > 0 && c >>> 24 < 128) continue; // zweite Ebene: nur gesetzte Pixel
      c = (c | 0xff000000) >>> 0;
      const corners = [[i / tw, j / th], [(i + 1) / tw, j / th], [(i + 1) / tw, (j + 1) / th], [i / tw, (j + 1) / th]];
      const q = new Array(12);
      for (let k = 0; k < 4; k++) {
        q[k * 3] = o[0] + a[0] * corners[k][0] + dn[0] * corners[k][1];
        q[k * 3 + 1] = o[1] + a[1] * corners[k][0] + dn[1] * corners[k][1];
        q[k * 3 + 2] = o[2] + a[2] * corners[k][0] + dn[2] * corners[k][1];
      }
      quads.push(q);
      const r = Math.min(255, Math.round(((c >>> 16) & 255) * light));
      const gg = Math.min(255, Math.round(((c >>> 8) & 255) * light));
      const bb = Math.min(255, Math.round((c & 255) * light));
      colors.push((0xff000000 | (r << 16) | (gg << 8) | bb) | 0);
    }
}

/** Dreieck mit Tiefentest (größeres z = näher an der Kamera), Pixelmitten-Regel. */
function triangle(out: Int32Array, depth: Float64Array, size: number, s: number[], i0: number, i1: number, i2: number, color: number) {
  const ax = s[i0 * 3], ay = s[i0 * 3 + 1], az = s[i0 * 3 + 2];
  const bx = s[i1 * 3], by = s[i1 * 3 + 1], bz = s[i1 * 3 + 2];
  const cx = s[i2 * 3], cy = s[i2 * 3 + 1], cz = s[i2 * 3 + 2];
  const area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
  if (Math.abs(area) < 1e-6) return;
  const minX = Math.max(0, Math.floor(Math.min(ax, bx, cx)));
  const maxX = Math.min(size - 1, Math.ceil(Math.max(ax, bx, cx)));
  const minY = Math.max(0, Math.floor(Math.min(ay, by, cy)));
  const maxY = Math.min(size - 1, Math.ceil(Math.max(ay, by, cy)));
  for (let y = minY; y <= maxY; y++)
    for (let x = minX; x <= maxX; x++) {
      const px = x + 0.5, py = y + 0.5;
      const w0 = ((bx - px) * (cy - py) - (by - py) * (cx - px)) / area;
      const w1 = ((cx - px) * (ay - py) - (cy - py) * (ax - px)) / area;
      const w2 = 1 - w0 - w1;
      if (w0 < -1e-4 || w1 < -1e-4 || w2 < -1e-4) continue;
      const z = w0 * az + w1 * bz + w2 * cz;
      const at = y * size + x;
      if (z > depth[at]) {
        depth[at] = z;
        out[at] = color;
      }
    }
}

// ---------- Im Launcher: Bild laden, rechnen, als Bild-URL zurückgeben ----------

const skins = new Map<string, Promise<Skin | null>>();
const anims = new Map<string, Promise<{ json: string; animation: string | null } | null>>();
const images = new Map<string, Promise<string | null>>();

function loadSkin(url: string): Promise<Skin | null> {
  let known = skins.get(url);
  if (!known) {
    known = new Promise<Skin | null>((resolve) => {
      const img = new Image();
      img.onload = () => {
        try {
          const canvas = document.createElement('canvas');
          canvas.width = img.naturalWidth;
          canvas.height = img.naturalHeight;
          const ctx = canvas.getContext('2d');
          if (!ctx || canvas.width !== 64 || (canvas.height !== 64 && canvas.height !== 32)) return resolve(null);
          ctx.drawImage(img, 0, 0);
          const data = ctx.getImageData(0, 0, canvas.width, canvas.height).data;
          const skin: Skin = [];
          for (let y = 0; y < canvas.height; y++) {
            const row: number[] = [];
            for (let x = 0; x < canvas.width; x++) {
              const i = (y * canvas.width + x) * 4;
              row.push(((data[i + 3] << 24) | (data[i] << 16) | (data[i + 1] << 8) | data[i + 2]) >>> 0);
            }
            skin.push(row);
          }
          resolve(skin);
        } catch {
          resolve(null);
        }
      };
      img.onerror = () => resolve(null);
      img.src = url;
    });
    skins.set(url, known);
  }
  return known;
}

function loadAnimation(id: string) {
  let known = anims.get(id);
  if (!known) {
    known = call<{ json: string; animation: string | null } | null>('emotes.animation', { id }).catch(() => null);
    anims.set(id, known);
  }
  return known;
}

/** Bild-URL (PNG) der Vorschau in size × size Bildpunkten, oder null (kein Skin, keine Daten). */
export function emotePreview(skinUrl: string, id: string, size: number): Promise<string | null> {
  const key = `${skinUrl}|${id}|${size}`;
  let known = images.get(key);
  if (!known) {
    known = (async () => {
      const [skin, anim] = await Promise.all([loadSkin(skinUrl), loadAnimation(id)]);
      if (!skin || !anim) return null;
      const pixels = render(skin, pose(anim.json, anim.animation), size);
      const canvas = document.createElement('canvas');
      canvas.width = canvas.height = size;
      const ctx = canvas.getContext('2d');
      if (!ctx) return null;
      const image = ctx.createImageData(size, size);
      for (let i = 0; i < pixels.length; i++) {
        const c = pixels[i];
        image.data[i * 4] = (c >>> 16) & 255;
        image.data[i * 4 + 1] = (c >>> 8) & 255;
        image.data[i * 4 + 2] = c & 255;
        image.data[i * 4 + 3] = (c >>> 24) & 255;
      }
      ctx.putImageData(image, 0, 0);
      return canvas.toDataURL('image/png');
    })();
    images.set(key, known);
  }
  return known;
}

/** Nach einem Skin- oder Emote-Wechsel neu rechnen. */
export function clearEmotePreviews() {
  skins.clear();
  anims.clear();
  images.clear();
}
