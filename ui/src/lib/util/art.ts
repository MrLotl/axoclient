/**
 * Kleine Pixel-Landschaften als Hintergrund der Bühne und als Ersatzbild, solange eine Instanz kein eigenes Bild hat.
 * Aus dem Namen bzw. der ID wird immer dieselbe Landschaft erzeugt.
 */

type Biome = {
  sky: [string, string, string, string];
  grass: string;
  dirt: string;
  stone: string;
  leaf: string;
  trunk: string;
  water: string;
  sun: string;
  trees: number;
  clouds?: boolean;
  night?: boolean;
  island?: boolean;
  cactus?: boolean;
  ore?: string;
};

const BIOMES: Record<string, Biome> = {
  forest: { sky: ['#78aef5', '#90bff7', '#a9cff9', '#c3defb'], grass: '#5ea53a', dirt: '#7a5534', stone: '#707077', leaf: '#3c7b2a', trunk: '#6b4a2b', water: '#3f76e4', sun: '#fff3a8', trees: 0.12, clouds: true },
  cave: { sky: ['#1f2d3c', '#29394c', '#33465c', '#3e546c'], grass: '#6b8a58', dirt: '#5d4a3a', stone: '#585a62', leaf: '#2f5a3a', trunk: '#4d3a28', water: '#2b4f80', sun: '#e8ecf2', trees: 0.07, ore: '#d9883a', night: true },
  sky: { sky: ['#5fa8f0', '#7cb9f3', '#9acbf6', '#b9dcf9'], grass: '#62b03c', dirt: '#7a5534', stone: '#76767c', leaf: '#3f8a2c', trunk: '#6b4a2b', water: '#3f76e4', sun: '#fff3a8', trees: 0.14, island: true, clouds: true },
  snow: { sky: ['#9fbbe2', '#b4cbea', '#c8daf1', '#dde8f7'], grass: '#eef3f8', dirt: '#7a5534', stone: '#7d8088', leaf: '#2c5a3b', trunk: '#4f3a26', water: '#4a7ccf', sun: '#ffffff', trees: 0.1, clouds: true },
  desert: { sky: ['#f0ad62', '#f3bf7d', '#f6d199', '#f9e3b8'], grass: '#e7cf8c', dirt: '#dcbf78', stone: '#b99b5f', leaf: '#5e8a3a', trunk: '#5e8a3a', water: '#3f76e4', sun: '#fff8d8', trees: 0.05, cactus: true },
  sunset: { sky: ['#3b2b5c', '#7a3d6b', '#cf616a', '#f49a6a'], grass: '#4d8839', dirt: '#6b4a2e', stone: '#5e5a66', leaf: '#2f6a2c', trunk: '#4f3622', water: '#2d4a8a', sun: '#ffd28a', trees: 0.09 }
};
const ORDER = ['forest', 'sunset', 'cave', 'snow', 'sky', 'desert'];

function rng(seed: string) {
  let a = 2166136261;
  for (const ch of seed) a = Math.imul(a ^ ch.charCodeAt(0), 16777619);
  a >>>= 0;
  return () => {
    a |= 0;
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function shade(hex: string, f: number) {
  const n = parseInt(hex.slice(1), 16);
  const c = (v: number) => Math.max(0, Math.min(255, Math.round(v * f)));
  return `rgb(${c((n >> 16) & 255)},${c((n >> 8) & 255)},${c(n & 255)})`;
}

const cache = new Map<string, string>();

/** Landschaft als Daten-URL (96 × 54 Pixel, mit image-rendering: pixelated anzeigen). */
export function landscape(seed: string): string {
  const hit = cache.get(seed);
  if (hit) return hit;
  if (typeof document === 'undefined') return '';
  const R = rng(seed);
  const B = BIOMES[ORDER[Math.floor(R() * ORDER.length)]!]!;
  const W = 96;
  const H = 54;
  const cv = document.createElement('canvas');
  cv.width = W;
  cv.height = H;
  const x = cv.getContext('2d');
  if (!x) return '';
  const px = (X: number, Y: number, col: string, v = 0.07) => {
    x.fillStyle = shade(col, 1 + (R() - 0.5) * v * 2);
    x.fillRect(X, Y, 1, 1);
  };
  for (let y = 0; y < H; y++) {
    x.fillStyle = B.sky[Math.min(3, Math.floor(y / (H * 0.15)))]!;
    x.fillRect(0, y, W, 1);
  }
  if (B.night)
    for (let i = 0; i < 40; i++) {
      x.fillStyle = `rgba(255,255,255,${0.4 + R() * 0.5})`;
      x.fillRect(Math.floor(R() * W), Math.floor(R() * H * 0.45), 1, 1);
    }
  x.fillStyle = B.sun;
  x.fillRect(Math.floor(W * 0.74), 5, 8, 8);
  if (B.clouds)
    for (let i = 0; i < 4; i++) {
      x.fillStyle = 'rgba(255,255,255,.78)';
      const cx = Math.floor(R() * W);
      const cy = 4 + Math.floor(R() * 12);
      const cw = 8 + Math.floor(R() * 10);
      x.fillRect(cx, cy, cw, 2);
      x.fillRect(cx + 2, cy - 1, cw - 5, 1);
    }
  const s1 = R() * 10;
  const s2 = R() * 10;
  const sea = Math.floor(H * 0.7);
  const L = Math.floor(W * 0.16);
  const Rr = Math.floor(W * 0.84);
  const ground = (X: number) =>
    B.island ? Math.round(H * 0.5 + Math.sin(X * 0.2 + s2) * 1.5) : Math.round(H * 0.6 + Math.sin(X * 0.07 + s1) * 4 + Math.sin(X * 0.19 + s2) * 2);
  for (let X = 0; X < W; X++) {
    const g = ground(X);
    if (B.island) {
      if (X < L || X > Rr) continue;
      const bottom = g + 3 + Math.floor(Math.min(X - L, Rr - X) * 0.45);
      for (let Y = g; Y <= Math.min(bottom, H - 1); Y++) px(X, Y, Y === g ? B.grass : Y < g + 3 ? B.dirt : R() < 0.06 ? '#3a3a3e' : B.stone);
      continue;
    }
    if (g > sea && !B.night) for (let Y = sea; Y < g; Y++) px(X, Y, B.water, 0.04);
    for (let Y = g; Y < H; Y++) {
      let col = Y === g ? (g > sea ? '#dcca8a' : B.grass) : Y < g + 4 ? B.dirt : B.stone;
      if (Y > g + 5 && R() < 0.03) col = B.ore ?? '#2d2d31';
      px(X, Y, col);
    }
  }
  for (let X = 3; X < W - 3; X++) {
    if (R() > B.trees) continue;
    const g = ground(X);
    if (B.island ? X < L + 3 || X > Rr - 3 : g >= sea) continue;
    if (B.cactus) {
      for (let k = 1; k <= 5; k++) px(X, g - k, B.trunk);
      X += 6;
      continue;
    }
    for (let k = 1; k <= 4; k++) px(X, g - k, B.trunk);
    for (let dx = -2; dx <= 2; dx++) for (let dy = 4; dy <= 7; dy++) if (!(Math.abs(dx) === 2 && (dy === 7 || R() < 0.3))) px(X + dx, g - dy, B.leaf, 0.12);
    X += 4;
  }
  const url = cv.toDataURL();
  cache.set(seed, url);
  return url;
}

/**
 * Hintergrund für eine Instanz als CSS-Wert: zuerst das Titelbild-Panorama ihrer Minecraft-Version
 * (aus den Spieldateien), darunter die Pixel-Landschaft. Fehlt das Panorama, zeigt der Browser
 * einfach die untere Ebene.
 */
export function artLayers(seed: string, minecraft: string | null | undefined, panorama = true): string {
  const land = `url('${landscape(seed)}')`;
  if (!panorama || !minecraft || !/^[A-Za-z0-9._-]{1,64}$/.test(minecraft) || minecraft.includes('..')) return land;
  return `url('/axo/panorama/${encodeURIComponent(minecraft)}'), ${land}`;
}

let grainUrl = '';
/** Feines Filmkorn (160 × 160, wird gekachelt). */
export function grain(): string {
  if (grainUrl || typeof document === 'undefined') return grainUrl;
  const cv = document.createElement('canvas');
  cv.width = 160;
  cv.height = 160;
  const x = cv.getContext('2d');
  if (!x) return '';
  const R = rng('grain');
  const img = x.createImageData(160, 160);
  for (let i = 0; i < img.data.length; i += 4) {
    const v = Math.floor(R() * 255);
    img.data[i] = img.data[i + 1] = img.data[i + 2] = v;
    img.data[i + 3] = 128 + Math.floor(R() * 127);
  }
  x.putImageData(img, 0, 0);
  grainUrl = cv.toDataURL();
  return grainUrl;
}
