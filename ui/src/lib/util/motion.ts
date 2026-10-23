// Gemeinsame Übergänge für die ganze Oberfläche. Svelte spielt Übergänge über die Web Animations API ab,
// die Regel für reduzierte Bewegung in app.css greift dort nicht – deshalb prüft jeder Übergang sie selbst.
import { cubicOut, backOut } from 'svelte/easing';
import type { TransitionConfig } from 'svelte/transition';

export const DUR = { fast: 120, med: 200, slow: 300 } as const;

const media = typeof window !== 'undefined' ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;
export function reducedMotion() {
  return media?.matches ?? false;
}

// Bestehende Transformation des Elements beibehalten, damit Übergänge Positionierungen nicht überschreiben
function baseTransform(node: Element) {
  const t = getComputedStyle(node).transform;
  return t === 'none' ? '' : t;
}

type Opts = { delay?: number; duration?: number; x?: number; y?: number; scale?: number; origin?: string };

/** Einblenden mit leichtem Versatz – für Seiten, Tabs und Listeneinträge.
 *  In scrollbaren Listen nur negative y-Werte (von oben herein): ein Versatz nach unten ragt kurz
 *  über den Rand, Windows blendet dann einen Scrollbalken ein und alle Karten ändern kurz ihre Breite. */
export function fadeSlide(node: Element, { delay = 0, duration = DUR.med, x = 0, y = 8 }: Opts = {}): TransitionConfig {
  if (reducedMotion()) return { duration: 0 };
  const base = baseTransform(node);
  const opacity = +getComputedStyle(node).opacity;
  return {
    delay,
    duration,
    easing: cubicOut,
    css: (t, u) => `opacity: ${t * opacity}; transform: ${base} translate(${u * x}px, ${u * y}px);`
  };
}

/** Nur Ausblenden, für alles, was Platz für Neues macht */
export function fadeOut(node: Element, { delay = 0, duration = DUR.fast }: Opts = {}): TransitionConfig {
  if (reducedMotion()) return { duration: 0 };
  const opacity = +getComputedStyle(node).opacity;
  return { delay, duration, easing: cubicOut, css: (t) => `opacity: ${t * opacity}` };
}

/** Aufploppen mit leichter Skalierung – Dialoge, Menüs, Badges */
export function pop(node: Element, { delay = 0, duration = DUR.fast + 40, scale = 0.96, y = 0, origin }: Opts = {}): TransitionConfig {
  if (reducedMotion()) return { duration: 0 };
  const base = baseTransform(node);
  const opacity = +getComputedStyle(node).opacity;
  if (origin) (node as HTMLElement).style.transformOrigin = origin;
  return {
    delay,
    duration,
    easing: cubicOut,
    css: (t, u) => `opacity: ${t * opacity}; transform: ${base} translateY(${u * y}px) scale(${scale + (1 - scale) * t});`
  };
}

/** Kleines Federn beim Erscheinen von Zählern und Badges */
export function bounce(node: Element, { delay = 0, duration = 260 }: Opts = {}): TransitionConfig {
  if (reducedMotion()) return { duration: 0 };
  const base = baseTransform(node);
  return { delay, duration, easing: backOut, css: (t) => `opacity: ${Math.min(1, t * 2)}; transform: ${base} scale(${0.4 + 0.6 * t});` };
}

/** Gestaffelte Verzögerung: nur kurz nach dem Erzeugen, später hinzugefügte Einträge kommen sofort */
export function stagger(step = 30, max = 10, span = 400) {
  const start = performance.now();
  return (i: number) => (performance.now() - start < span ? Math.min(i, max) * step : 0);
}

/** Bild erst zeigen, wenn es geladen ist – statt es zeilenweise aufploppen zu lassen.
 *  Schon geladene Bilder (Cache) bleiben unangetastet, damit nichts unnötig flackert. */
export function fadeLoad(node: HTMLImageElement, _src?: string | null) {
  const ready = () => node.complete && node.naturalWidth > 0;
  const done = () => {
    if (node.classList.contains('fade-load')) node.classList.add('loaded');
  };
  const arm = () => {
    node.classList.remove('loaded');
    if (ready()) node.classList.remove('fade-load');
    else node.classList.add('fade-load');
  };
  arm();
  node.addEventListener('load', done);
  node.addEventListener('error', done);
  return {
    update() {
      queueMicrotask(arm);
    },
    destroy() {
      node.removeEventListener('load', done);
      node.removeEventListener('error', done);
    }
  };
}

/** Auf- und Zuklappen: Höhe und Abstände laufen weich mit, der Inhalt blendet dabei ein bzw. aus.
 *  Für Zeilen, die je nach Schalter erscheinen – die Karte darum wächst und schrumpft dann ohne Sprung. */
export function expand(node: Element, { delay = 0, duration = DUR.slow - 40 }: Opts = {}): TransitionConfig {
  if (reducedMotion()) return { duration: 0 };
  const style = getComputedStyle(node);
  const opacity = +style.opacity;
  const height = parseFloat(style.height);
  const num = (v: string) => parseFloat(v) || 0;
  const [pt, pb, mt, mb, bt, bb] = [style.paddingTop, style.paddingBottom, style.marginTop, style.marginBottom, style.borderTopWidth, style.borderBottomWidth].map(num);
  // Im Flex-/Grid-Container zählt auch der Abstand zum Vorgänger: der läuft mit, sonst springt es am Ende
  const parent = node.parentElement ? getComputedStyle(node.parentElement) : null;
  const gap = parent && /flex|grid/.test(parent.display) && node.previousElementSibling ? num(parent.rowGap) : 0;
  return {
    delay,
    duration,
    easing: cubicOut,
    css: (t) =>
      `overflow: hidden; opacity: ${Math.min(t * 1.4, 1) * opacity}; height: ${t * height}px;` +
      `padding-top: ${t * pt}px; padding-bottom: ${t * pb}px; margin-top: ${t * mt - (1 - t) * gap}px; margin-bottom: ${t * mb}px;` +
      `border-top-width: ${t * bt}px; border-bottom-width: ${t * bb}px; min-height: 0;`
  };
}
