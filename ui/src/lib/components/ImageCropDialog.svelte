<script lang="ts">
  import Dialog from './Dialog.svelte';
  import Icon from './Icon.svelte';
  import { t } from '../i18n';

  /**
   * Bild zuschneiden: Ausschnitt per Ziehen verschieben, mit Regler oder Mausrad zoomen.
   * Das Ergebnis hat genau das Seitenverhältnis, in dem das Bild später angezeigt wird.
   */
  let {
    src,
    aspect,
    outputWidth,
    title,
    round = false,
    onconfirm,
    oncancel
  }: {
    src: string;
    aspect: number;
    outputWidth: number;
    title: string;
    round?: boolean;
    onconfirm: (base64: string) => void;
    oncancel: () => void;
  } = $props();

  const frameW = $derived(aspect >= 2 ? 600 : 300);
  const frameH = $derived(Math.round(frameW / aspect));

  let img: HTMLImageElement | undefined = $state();
  let natural = $state<{ w: number; h: number } | null>(null);
  let zoom = $state(1);
  let x = $state(0);
  let y = $state(0);
  let busy = $state(false);
  let pan: { px: number; py: number; x: number; y: number } | null = null;

  // Grundmaßstab: Bild deckt den Rahmen gerade vollständig ab
  const base = $derived(natural ? Math.max(frameW / natural.w, frameH / natural.h) : 1);
  const scale = $derived(base * zoom);
  const drawW = $derived(natural ? natural.w * scale : 0);
  const drawH = $derived(natural ? natural.h * scale : 0);

  function clamp() {
    x = Math.min(0, Math.max(frameW - drawW, x));
    y = Math.min(0, Math.max(frameH - drawH, y));
  }

  function onload() {
    if (!img) return;
    natural = { w: img.naturalWidth, h: img.naturalHeight };
    zoom = 1;
    // mittig starten
    x = (frameW - natural.w * base) / 2;
    y = (frameH - natural.h * base) / 2;
  }

  /** Zoomt um einen Punkt im Rahmen, damit die Stelle unter der Maus stehen bleibt. */
  function setZoom(next: number, cx = frameW / 2, cy = frameH / 2) {
    next = Math.min(5, Math.max(1, next));
    const ratio = next / zoom;
    x = cx - (cx - x) * ratio;
    y = cy - (cy - y) * ratio;
    zoom = next;
    clamp();
  }

  function down(e: PointerEvent) {
    if (e.button !== 0) return;
    pan = { px: e.clientX, py: e.clientY, x, y };
    (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
  }

  function move(e: PointerEvent) {
    if (!pan) return;
    x = pan.x + e.clientX - pan.px;
    y = pan.y + e.clientY - pan.py;
    clamp();
  }

  function wheel(e: WheelEvent) {
    e.preventDefault();
    const r = (e.currentTarget as HTMLElement).getBoundingClientRect();
    setZoom(zoom * (e.deltaY < 0 ? 1.1 : 1 / 1.1), e.clientX - r.left, e.clientY - r.top);
  }

  function key(e: KeyboardEvent) {
    const step = e.shiftKey ? 20 : 6;
    const moves: Record<string, [number, number]> = { ArrowLeft: [step, 0], ArrowRight: [-step, 0], ArrowUp: [0, step], ArrowDown: [0, -step] };
    if (moves[e.key]) {
      e.preventDefault();
      x += moves[e.key][0];
      y += moves[e.key][1];
      clamp();
    } else if (e.key === '+' || e.key === '=') setZoom(zoom * 1.1);
    else if (e.key === '-') setZoom(zoom / 1.1);
  }

  async function confirm() {
    if (!img || !natural) return;
    busy = true;
    const factor = outputWidth / frameW;
    const canvas = document.createElement('canvas');
    canvas.width = outputWidth;
    canvas.height = Math.round(outputWidth / aspect);
    const ctx = canvas.getContext('2d')!;
    ctx.imageSmoothingQuality = 'high';
    ctx.drawImage(img, x * factor, y * factor, drawW * factor, drawH * factor);
    // Breite Banner als JPEG (Fotos, klein), Icons als PNG (Transparenz, Pixelart)
    const type = aspect >= 2 ? 'image/jpeg' : 'image/png';
    const dataUrl = canvas.toDataURL(type, 0.9);
    busy = false;
    onconfirm(dataUrl.slice(dataUrl.indexOf(',') + 1));
  }
</script>

<Dialog {title} subtitle={t('crop.hint')} icon="image" width={frameW + 48} onclose={oncancel}>
  <div class="crop">
    <!-- svelte-ignore a11y_no_noninteractive_tabindex -->
    <div
      class="frame"
      class:round
      style="width: {frameW}px; height: {frameH}px"
      role="application"
      aria-label={t('crop.area')}
      tabindex="0"
      onpointerdown={down}
      onpointermove={move}
      onpointerup={() => (pan = null)}
      onpointercancel={() => (pan = null)}
      onwheel={wheel}
      onkeydown={key}>
      <img bind:this={img} {src} alt="" {onload} draggable="false" style="width: {drawW}px; height: {drawH}px; transform: translate({x}px, {y}px)" />
    </div>
    <div class="zoom">
      <Icon name="image" size={12} />
      <input type="range" min="1" max="5" step="0.01" value={zoom} aria-label={t('crop.zoom')} oninput={(e) => setZoom(+e.currentTarget.value)} />
      <Icon name="image" size={18} />
    </div>
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={oncancel}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={!natural || busy} onclick={confirm}>{t('crop.apply')}</button>
  {/snippet}
</Dialog>

<style>
  .crop {
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 14px;
  }
  .frame {
    position: relative;
    overflow: hidden;
    border-radius: 12px;
    background: repeating-conic-gradient(#2a2a2a 0% 25%, #303030 0% 50%) 0 0 / 16px 16px;
    box-shadow: inset 0 0 0 1px #3a3a3a;
    cursor: grab;
    touch-action: none;
  }
  .frame:active {
    cursor: grabbing;
  }
  .frame.round {
    border-radius: 22%;
  }
  .frame img {
    position: absolute;
    left: 0;
    top: 0;
    max-width: none;
    user-select: none;
    pointer-events: none;
  }
  .zoom {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 10px;
    color: var(--muted-2);
  }
  .zoom input {
    flex-grow: 1;
    accent-color: var(--accent-from);
  }
</style>
