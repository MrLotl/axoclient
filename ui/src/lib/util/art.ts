/**
 * Hintergründe für Instanzen: das echte Titelbild-Panorama ihrer Minecraft-Version (aus den Spieldateien),
 * sonst ein ruhiger Farbverlauf. Aus der ID entsteht immer derselbe Verlauf.
 */

function hash(seed: string): number {
  let a = 2166136261;
  for (const ch of seed) a = Math.imul(a ^ ch.charCodeAt(0), 16777619);
  return a >>> 0;
}

/** Dunkler Farbverlauf als CSS-Wert, gleichbleibend pro Instanz. */
export function gradient(seed: string): string {
  const h = hash(seed);
  const hue = h % 360;
  const hue2 = (hue + 35 + ((h >> 9) % 50)) % 360;
  return (
    `radial-gradient(120% 90% at 78% 18%, hsl(${hue} 55% 42% / 0.55), transparent 60%), ` +
    `linear-gradient(135deg, hsl(${hue} 38% 20%) 0%, hsl(${hue2} 42% 11%) 100%)`
  );
}

/** Adresse des Panoramas einer Minecraft-Version (Blickrichtung 0–3), oder null bei unbrauchbarem Versionsnamen. */
export function panoramaUrl(minecraft: string | null | undefined, view = 0): string | null {
  if (!minecraft || !/^[A-Za-z0-9._-]{1,64}$/.test(minecraft) || minecraft.includes('..')) return null;
  const base = `/axo/panorama/${encodeURIComponent(minecraft)}`;
  return view > 0 && view <= 3 ? `${base}/${view}` : base;
}

/**
 * Hintergrund für eine Instanz als CSS-Wert: oben das Panorama, darunter der Farbverlauf.
 * Fehlt das Panorama (Version noch nie gestartet), zeigt der Browser einfach den Verlauf.
 */
export function artLayers(seed: string, minecraft: string | null | undefined, panorama = true, view = 0): string {
  const url = panorama ? panoramaUrl(minecraft, view) : null;
  return url ? `url('${url}'), ${gradient(seed)}` : gradient(seed);
}

type BackdropSource = {
  id: string;
  artSeed?: string;
  minecraftVersion: string;
  bannerUrl: string | null;
  gradientOnly?: boolean;
  panoramaView?: number;
};

/**
 * Hintergrund einer Instanz als CSS-Wert, überall gleich (Bühne, Regal, Kacheln):
 * eigenes Hintergrundbild > Panorama der Version (gewählte Blickrichtung) > Farbverlauf.
 */
export function backdrop(i: BackdropSource, panorama = true): string {
  const seed = i.artSeed ?? i.id;
  if (i.bannerUrl) return `url('${i.bannerUrl}'), ${gradient(seed)}`;
  return artLayers(seed, i.minecraftVersion, panorama && !i.gradientOnly, i.panoramaView ?? 0);
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
  let a = hash('grain');
  const rnd = () => {
    a = (Math.imul(a ^ (a >>> 15), 2246822507) + 0x6d2b79f5) >>> 0;
    return a / 4294967296;
  };
  const img = x.createImageData(160, 160);
  for (let i = 0; i < img.data.length; i += 4) {
    const v = Math.floor(rnd() * 255);
    img.data[i] = img.data[i + 1] = img.data[i + 2] = v;
    img.data[i + 3] = 128 + Math.floor(rnd() * 127);
  }
  x.putImageData(img, 0, 0);
  grainUrl = cv.toDataURL();
  return grainUrl;
}
