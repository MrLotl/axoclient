/**
 * Baut aus der Vorderseite eines Capes (10×16 · Faktor) die vollständige Cape-Textur (64×32 · Faktor):
 * Vorderseite, Rückseite, Ränder und Elytra. So reicht es, im Editor nur die sichtbare Seite zu malen.
 */
export function buildCapeTexture(front: HTMLCanvasElement, scale: number): HTMLCanvasElement {
  const out = document.createElement('canvas');
  out.width = 64 * scale;
  out.height = 32 * scale;
  const ctx = out.getContext('2d')!;
  ctx.imageSmoothingEnabled = false;
  const s = scale;
  const fw = 10 * s;
  const fh = 16 * s;

  // Außenseite (sichtbar) und Innenseite (gespiegelt)
  ctx.drawImage(front, 0, 0, fw, fh, 1 * s, 1 * s, fw, fh);
  ctx.save();
  ctx.translate(12 * s + fw, 1 * s);
  ctx.scale(-1, 1);
  ctx.drawImage(front, 0, 0, fw, fh, 0, 0, fw, fh);
  ctx.restore();

  // Ränder aus den äußeren Pixelreihen strecken
  ctx.drawImage(front, 0, 0, 1 * s, fh, 0, 1 * s, 1 * s, fh);
  ctx.drawImage(front, fw - s, 0, 1 * s, fh, 11 * s, 1 * s, 1 * s, fh);
  ctx.drawImage(front, 0, 0, fw, 1 * s, 1 * s, 0, fw, 1 * s);
  ctx.drawImage(front, 0, fh - s, fw, 1 * s, 11 * s, 0, fw, 1 * s);

  // Elytra: beide Flügelseiten (je 10×20) aus der Vorderseite
  ctx.drawImage(front, 0, 0, fw, fh, 24 * s, 2 * s, 10 * s, 20 * s);
  ctx.drawImage(front, 0, 0, fw, fh, 36 * s, 2 * s, 10 * s, 20 * s);
  ctx.drawImage(front, 0, 0, fw, 1 * s, 24 * s, 0, 10 * s, 2 * s);
  ctx.drawImage(front, 0, 0, 1 * s, fh, 22 * s, 2 * s, 2 * s, 20 * s);
  ctx.drawImage(front, fw - s, 0, 1 * s, fh, 34 * s, 2 * s, 2 * s, 20 * s);
  cutElytraShape(ctx, s);
  return out;
}

/**
 * Das Elytra-Modell ist ein flacher Quader – die Flügelform entsteht wie bei Minecraft nur durch transparente Pixel.
 * Pro Zeile (0–19) wie viele Pixel von der Außenkante weg durchsichtig sind: oben volle Breite (Schulter),
 * nach unten zur Spitze hin immer schmaler.
 */
export const ELYTRA_CUT = Array.from({ length: 20 }, (_, row) =>
  row === 0 ? 1 : row < 3 ? 0 : Math.min(8, Math.round(Math.pow((row - 2) / 17, 1.35) * 8))
);

function cutElytraShape(ctx: CanvasRenderingContext2D, s: number) {
  for (let row = 0; row < 20; row++) {
    const cut = ELYTRA_CUT[row];
    if (cut === 0) continue;
    const y = (2 + row) * s;
    // Vorderseite (24–33): Außenkante links; Rückseite (36–45) ist gespiegelt
    ctx.clearRect(24 * s, y, cut * s, s);
    ctx.clearRect((46 - cut) * s, y, cut * s, s);
    // Seitenfläche an der schrägen Kante (22–23) nur, wo der Flügel bis zur Kante reicht
    ctx.clearRect(22 * s, y, 2 * s, s);
  }
  // Unterseite (34–43, Zeilen 0–1) nur so breit wie die Spitze
  const tip = ELYTRA_CUT[19];
  ctx.clearRect(34 * s, 0, tip * s, 2 * s);
}

export function canvasToBase64(canvas: HTMLCanvasElement): string {
  return canvas.toDataURL('image/png').split(',')[1];
}

/** Erlaubte Editor-Auflösungen der Vorderseite (Faktor 1 = 10×16). */
export const CAPE_SCALES = [1, 2, 4, 8, 16] as const;
