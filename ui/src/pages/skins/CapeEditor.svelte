<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import Select from '../../lib/components/Select.svelte';
  import SkinViewer from '../../lib/components/SkinViewer.svelte';
  import Segmented from '../../lib/components/Segmented.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import { buildCapeTexture, canvasToBase64, CAPE_SCALES } from '../../lib/util/cape';
  import type { CapeView } from '../../lib/types';

  let { cape = null, onclose, onsaved }: { cape?: CapeView | null; onclose: () => void; onsaved: () => void } = $props();

  type Tool = 'pen' | 'eraser' | 'fill' | 'picker' | 'move';
  /** Importiertes Bild als Ebene unter dem Gemalten; Lage und Größe in Cape-Pixeln. */
  type Layer = { id: number; url: string; img: HTMLImageElement; x: number; y: number; w: number; h: number; size: number };
  // Gemalte Ebene: Farbe, '' = durchsichtig, ERASED = radiert (deckt auch Bilder darunter ab)
  const ERASED = '-';
  let scale = $state(2);
  let tool = $state<Tool>('pen');
  let color = $state('#db2777');
  let name = $state(cape?.name ?? '');
  let pixels = $state<string[]>([]);
  let layers = $state<Layer[]>([]);
  let selected = $state<number | null>(null);
  let imagePixels = $state<string[]>([]);
  let previewUrl = $state<string | null>(null);
  let previewMode = $state<'cape' | 'elytra'>('cape');
  let busy = $state(false);
  let zoom = $state(1);
  let painting = false;
  let dragging: { id: number; dx: number; dy: number } | null = null;
  let nextLayerId = 1;

  const palette = ['#ffffff', '#c4c4c4', '#6b6b6b', '#1f1f1f', '#000000', '#db2777', '#ec4899', '#f9a8d4', '#7c3aed', '#8b5cf6', '#4f46e5', '#2563eb', '#0d9488', '#16a34a', '#facc15', '#ea580c', '#dc2626', '#92400e'];

  const cols = $derived(10 * scale);
  const rows = $derived(16 * scale);
  const baseCell = $derived(Math.max(2, Math.floor(420 / rows)));
  const cell = $derived(Math.max(2, Math.round(baseCell * zoom)));
  const ZOOM_MIN = 1;
  const ZOOM_MAX = 8;
  let board: HTMLCanvasElement | undefined = $state();
  let viewport: HTMLDivElement | undefined = $state();

  /** Was am Ende im Cape steht: Gemaltes vor Bildern. */
  function composite(i: number): string {
    const p = pixels[i];
    if (p === ERASED) return '';
    return p || imagePixels[i] || '';
  }

  // Bilder auf das Cape-Raster rechnen, sobald sich eines bewegt oder die Auflösung ändert
  $effect(() => {
    const c = document.createElement('canvas');
    c.width = cols;
    c.height = rows;
    const ctx = c.getContext('2d')!;
    for (const l of layers) ctx.drawImage(l.img, l.x, l.y, l.w, l.h);
    const data = ctx.getImageData(0, 0, cols, rows).data;
    const result = Array<string>(cols * rows);
    for (let i = 0; i < cols * rows; i++)
      result[i] = data[i * 4 + 3] < 40 ? '' : '#' + [0, 1, 2].map((o) => data[i * 4 + o].toString(16).padStart(2, '0')).join('');
    imagePixels = result;
  });

  // Leinwand neu zeichnen, wenn sich Pixel, Bilder, Auflösung oder Zoom ändern
  $effect(() => {
    if (!board) return;
    const ctx = board.getContext('2d')!;
    ctx.clearRect(0, 0, board.width, board.height);
    void imagePixels;
    for (let i = 0; i < pixels.length; i++) {
      const x = (i % cols) * cell;
      const y = Math.floor(i / cols) * cell;
      ctx.fillStyle = composite(i) || (((i % cols) + Math.floor(i / cols)) % 2 ? '#2b2b2b' : '#252525');
      ctx.fillRect(x, y, cell, cell);
    }
    const sel = layers.find((l) => l.id === selected);
    if (sel && tool === 'move') {
      ctx.save();
      ctx.strokeStyle = '#f9a8d4';
      ctx.lineWidth = 2;
      ctx.setLineDash([6, 4]);
      ctx.strokeRect(sel.x * cell + 1, sel.y * cell + 1, sel.w * cell - 2, sel.h * cell - 2);
      ctx.restore();
    }
    if (cell >= 8) {
      ctx.strokeStyle = 'rgba(0,0,0,0.25)';
      ctx.lineWidth = 1;
      for (let x = 0; x <= cols; x++) {
        ctx.beginPath();
        ctx.moveTo(x * cell + 0.5, 0);
        ctx.lineTo(x * cell + 0.5, rows * cell);
        ctx.stroke();
      }
      for (let y = 0; y <= rows; y++) {
        ctx.beginPath();
        ctx.moveTo(0, y * cell + 0.5);
        ctx.lineTo(cols * cell, y * cell + 0.5);
        ctx.stroke();
      }
    }
  });

  /** Mausposition in Cape-Pixeln (mit Nachkommastellen). */
  function gridPos(e: MouseEvent) {
    const rect = board!.getBoundingClientRect();
    return { x: ((e.clientX - rect.left) / rect.width) * cols, y: ((e.clientY - rect.top) / rect.height) * rows };
  }

  function paintAt(e: MouseEvent) {
    if (!board) return;
    const p = gridPos(e);
    const x = Math.floor(p.x);
    const y = Math.floor(p.y);
    if (x < 0 || y < 0 || x >= cols || y >= rows) return;
    paint(y * cols + x);
  }

  function onBoardDown(e: MouseEvent) {
    e.preventDefault();
    if (tool === 'move') {
      const p = gridPos(e);
      // Oberstes Bild unter dem Mauszeiger
      const hit = [...layers].reverse().find((l) => p.x >= l.x && p.x <= l.x + l.w && p.y >= l.y && p.y <= l.y + l.h);
      selected = hit?.id ?? null;
      if (hit) dragging = { id: hit.id, dx: p.x - hit.x, dy: p.y - hit.y };
      return;
    }
    painting = true;
    paintAt(e);
  }

  function onBoardMove(e: MouseEvent) {
    if (dragging) {
      const p = gridPos(e);
      const l = layers.find((x) => x.id === dragging!.id);
      if (l) {
        // Auf ganze Cape-Pixel einrasten, damit nichts verschwimmt
        l.x = Math.round(p.x - dragging.dx);
        l.y = Math.round(p.y - dragging.dy);
      }
    } else if (painting && (tool === 'pen' || tool === 'eraser')) paintAt(e);
  }

  function endStroke() {
    if (painting || dragging) renderPreview();
    painting = false;
    dragging = null;
  }

  function setZoom(next: number, around?: { x: number; y: number }) {
    const z = Math.min(ZOOM_MAX, Math.max(ZOOM_MIN, Math.round(next * 4) / 4));
    if (z === zoom || !viewport) {
      zoom = z;
      return;
    }
    // Punkt unter der Maus bleibt stehen
    const rect = viewport.getBoundingClientRect();
    const ax = around ? around.x - rect.left : rect.width / 2;
    const ay = around ? around.y - rect.top : rect.height / 2;
    const fx = (viewport.scrollLeft + ax) / (cols * cell || 1);
    const fy = (viewport.scrollTop + ay) / (rows * cell || 1);
    zoom = z;
    requestAnimationFrame(() => {
      if (!viewport) return;
      viewport.scrollLeft = fx * cols * cell - ax;
      viewport.scrollTop = fy * rows * cell - ay;
    });
  }

  function onWheel(e: WheelEvent) {
    if (!e.ctrlKey) return;
    e.preventDefault();
    setZoom(zoom * (e.deltaY < 0 ? 1.25 : 0.8), { x: e.clientX, y: e.clientY });
  }

  function blank(c: number, r: number) {
    return Array(c * r).fill('');
  }

  onMount(async () => {
    if (cape) {
      try {
        const state = await call<{ width: number; height: number; pixels: string[] | null }>('capes.editState', { id: cape.id });
        if (state.pixels && state.pixels.length === state.width * state.height) {
          scale = Math.max(1, state.width / 10);
          pixels = state.pixels;
          renderPreview();
          return;
        }
      } catch {
        /* neu anfangen */
      }
    }
    pixels = blank(10 * scale, 16 * scale);
    renderPreview();
  });

  /** Auflösung wechseln: Bild wird umgerechnet (nächster Nachbar). */
  function rescale(next: number) {
    const oldCols = 10 * scale;
    const oldRows = 16 * scale;
    const nc = 10 * next;
    const nr = 16 * next;
    const result = blank(nc, nr);
    for (let y = 0; y < nr; y++)
      for (let x = 0; x < nc; x++) result[y * nc + x] = pixels[Math.floor((y * oldRows) / nr) * oldCols + Math.floor((x * oldCols) / nc)] ?? '';
    pixels = result;
    const f = next / scale;
    for (const l of layers) {
      l.x *= f;
      l.y *= f;
      l.w *= f;
      l.h *= f;
    }
    scale = next;
    renderPreview();
  }

  function paint(i: number) {
    if (tool === 'pen') pixels[i] = color;
    else if (tool === 'eraser') pixels[i] = imagePixels[i] ? ERASED : '';
    else if (tool === 'picker') {
      if (composite(i)) color = composite(i);
      tool = 'pen';
    } else if (tool === 'fill') fill(i);
  }

  function fill(start: number) {
    const target = composite(start);
    if (target === color) return;
    const stack = [start];
    const seen = new Set<number>();
    while (stack.length) {
      const k = stack.pop()!;
      if (seen.has(k) || composite(k) !== target) continue;
      seen.add(k);
      pixels[k] = color;
      const x = k % cols;
      const y = Math.floor(k / cols);
      if (x > 0) stack.push(k - 1);
      if (x < cols - 1) stack.push(k + 1);
      if (y > 0) stack.push(k - cols);
      if (y < rows - 1) stack.push(k + cols);
    }
  }

  function frontCanvas(): HTMLCanvasElement {
    const c = document.createElement('canvas');
    c.width = cols;
    c.height = rows;
    const ctx = c.getContext('2d')!;
    for (let i = 0; i < pixels.length; i++) {
      const p = composite(i);
      if (!p) continue;
      ctx.fillStyle = p;
      ctx.fillRect(i % cols, Math.floor(i / cols), 1, 1);
    }
    return c;
  }

  // Vorschau auch nach dem Verschieben oder Skalieren eines Bilds
  $effect(() => {
    void imagePixels;
    renderPreview();
  });

  let previewTimer: ReturnType<typeof setTimeout> | undefined;
  function renderPreview() {
    clearTimeout(previewTimer);
    previewTimer = setTimeout(() => {
      previewUrl = buildCapeTexture(frontCanvas(), scale).toDataURL('image/png');
    }, 120);
  }

  /** Bilder importieren (mehrere möglich): jedes wird eine eigene Ebene, eingepasst und mittig. */
  function importImage() {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = 'image/png,image/jpeg';
    input.multiple = true;
    input.onchange = () => {
      for (const file of Array.from(input.files ?? [])) {
        const url = URL.createObjectURL(file);
        const img = new Image();
        img.onload = () => {
          const ratio = Math.min(cols / img.width, rows / img.height);
          const w = img.width * ratio;
          const h = img.height * ratio;
          const layer: Layer = { id: nextLayerId++, url, img, x: Math.round((cols - w) / 2), y: Math.round((rows - h) / 2), w, h, size: 1 };
          layers = [...layers, layer];
          selected = layer.id;
          tool = 'move';
        };
        img.src = url;
      }
    };
    input.click();
  }

  /** Größe des gewählten Bilds ändern, die Mitte bleibt stehen. */
  function resizeLayer(l: Layer, size: number) {
    const cx = l.x + l.w / 2;
    const cy = l.y + l.h / 2;
    l.w = (l.w / l.size) * size;
    l.h = (l.h / l.size) * size;
    l.size = size;
    l.x = Math.round(cx - l.w / 2);
    l.y = Math.round(cy - l.h / 2);
  }

  function removeLayer(id: number) {
    const l = layers.find((x) => x.id === id);
    if (l) URL.revokeObjectURL(l.url);
    layers = layers.filter((x) => x.id !== id);
    if (selected === id) selected = null;
    // Radiertes, unter dem kein Bild mehr liegt, ist wieder einfach durchsichtig
    const covered = coveredByImages();
    pixels = pixels.map((p, i) => (p === ERASED && !covered[i] ? '' : p));
  }

  function coveredByImages(): boolean[] {
    const c = document.createElement('canvas');
    c.width = cols;
    c.height = rows;
    const ctx = c.getContext('2d')!;
    for (const l of layers) ctx.drawImage(l.img, l.x, l.y, l.w, l.h);
    const data = ctx.getImageData(0, 0, cols, rows).data;
    return Array.from({ length: cols * rows }, (_, i) => data[i * 4 + 3] >= 40);
  }

  const selectedLayer = $derived(layers.find((l) => l.id === selected) ?? null);

  function onKey(e: KeyboardEvent) {
    if ((e.key === 'Delete' || e.key === 'Backspace') && selected !== null && tool === 'move' && !(e.target instanceof HTMLInputElement)) {
      e.preventDefault();
      removeLayer(selected);
    }
  }

  async function save() {
    busy = true;
    try {
      const texture = buildCapeTexture(frontCanvas(), scale);
      await call('capes.save', {
        id: cape?.id ?? null,
        name: name.trim(),
        base64: canvasToBase64(texture),
        source: 'editor',
        // Bilder werden mit dem Gemalten zusammengeführt gespeichert
        edit: { formatVersion: 1, width: cols, height: rows, import: null, pixels: pixels.map((_, i) => composite(i)) }
      });
      onsaved();
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }

  const tools: { id: Tool; icon: string; label: string }[] = [
    { id: 'pen', icon: 'pencil', label: 'capes.tools.pen' },
    { id: 'eraser', icon: 'x', label: 'capes.tools.eraser' },
    { id: 'fill', icon: 'palette', label: 'capes.tools.fill' },
    { id: 'picker', icon: 'eye', label: 'capes.tools.picker' },
    { id: 'move', icon: 'move', label: 'capes.tools.move' }
  ];
</script>

<svelte:window onmouseup={endStroke} onkeydown={onKey} />

<Dialog title={cape ? t('capes.editorEditTitle') : t('capes.editorTitle')} subtitle={t('capes.editorHint')} icon="pencil" width={980} {onclose}>
  <div class="layout">
    <div class="tools">
      <span class="section-label">{t('capes.tool')}</span>
      <div class="tool-grid">
        {#each tools as tl (tl.id)}
          <button class="tool" class:on={tool === tl.id} aria-pressed={tool === tl.id} aria-label={t(tl.label)} title={t(tl.label)} onclick={() => (tool = tl.id)}><Icon name={tl.icon} /></button>
        {/each}
      </div>
      <span class="section-label">{t('capes.color')}</span>
      <div class="palette">
        {#each palette as p (p)}
          <button class="swatch" class:on={color === p} style="background: {p}" aria-label={t('capes.colorNamed', { color: p })} onclick={() => { color = p; if (tool !== 'fill') tool = 'pen'; }}></button>
        {/each}
        <label class="swatch custom" style="background: {color}" title={t('capes.customColor')}><input type="color" bind:value={color} class="sr" /></label>
      </div>
      <span class="section-label">{t('capes.resolution')}</span>
      <Select
        value={scale}
        label={t('capes.resolution')}
        options={CAPE_SCALES.map((s) => ({ value: s, label: `${10 * s} × ${16 * s}` + (s === 1 ? ' · Standard' : s === 2 ? ' · HD' : '') }))}
        onchange={(v) => rescale(v)} />
      <span class="hint">{t('capes.resolutionHint')}</span>
      <span class="section-label">{t('capes.images')}</span>
      {#each layers as l (l.id)}
        <div class="layer" class:on={selected === l.id}>
          <button class="layer-pick" onclick={() => { selected = l.id; tool = 'move'; }} aria-pressed={selected === l.id}>
            <img src={l.url} alt="" />
            <span class="truncate">{t('capes.imageNamed', { n: layers.indexOf(l) + 1 })}</span>
          </button>
          <button class="layer-x" aria-label={t('capes.imageRemove')} title={t('capes.imageRemove')} onclick={() => removeLayer(l.id)}><Icon name="x" size={11} /></button>
        </div>
      {/each}
      {#if selectedLayer}
        <label class="label">
          <span>{t('capes.imageSize', { pct: Math.round(selectedLayer.size * 100) })}</span>
          <input class="range" type="range" min="0.1" max="3" step="0.05" value={selectedLayer.size} oninput={(e) => resizeLayer(selectedLayer!, parseFloat(e.currentTarget.value))} />
        </label>
      {/if}
      <button class="btn btn-second sm" onclick={importImage}><Icon name="image" size={13} />{t('capes.importImage')}</button>
      <button class="btn btn-flat sm" onclick={() => { pixels = blank(cols, rows); layers.forEach((l) => URL.revokeObjectURL(l.url)); layers = []; selected = null; renderPreview(); }}>{t('capes.clear')}</button>
    </div>

    <div class="canvas-wrap">
      <div class="canvas" bind:this={viewport} onwheel={onWheel}>
        <canvas
          bind:this={board}
          class:move={tool === 'move'}
          width={cols * cell}
          height={rows * cell}
          aria-label={t('capes.canvasLabel')}
          onmousedown={onBoardDown}
          onmousemove={onBoardMove}
          onmouseleave={() => (painting = false)}></canvas>
      </div>
      <div class="zoom">
        <button class="icon-btn small" aria-label={t('capes.zoomOut')} title={t('capes.zoomOut')} disabled={zoom <= ZOOM_MIN} onclick={() => setZoom(zoom / 1.25)}><Icon name="minus" size={14} /></button>
        <button class="zoom-value" title={t('capes.zoomReset')} onclick={() => setZoom(1)}>{Math.round(zoom * 100)} %</button>
        <button class="icon-btn small" aria-label={t('capes.zoomIn')} title={t('capes.zoomIn')} disabled={zoom >= ZOOM_MAX} onclick={() => setZoom(zoom * 1.25)}><Icon name="plus" size={14} /></button>
        <span class="hint">{t('capes.zoomHint')}</span>
      </div>
    </div>

    <div class="preview">
      <span class="section-label">{t('capes.preview')}</span>
      {#if app.activeAccount}
        <SkinViewer skin={`/axo/head/${app.activeAccount.uuid.replaceAll('-', '')}`} cape={previewUrl} elytra={previewMode === 'elytra'} width={200} height={300} facing="back" label={t('capes.previewLabel')} />
      {/if}
      <Segmented bind:value={previewMode} label={t('capes.previewMode')} options={[{ value: 'cape', label: t('capes.cape') }, { value: 'elytra', label: 'Elytra' }]} />
      <label class="label">
        <span>{t('skins.name')}</span>
        <span class="field"><input bind:value={name} maxlength="40" placeholder={t('capes.namePlaceholder')} /></span>
      </label>
      <p class="hint">{t('capes.reviewNote')}</p>
    </div>
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={busy || !name.trim() || pixels.every((_, i) => !composite(i))} onclick={save}>{t('capes.save')}</button>
  {/snippet}
</Dialog>

<style>
  .layout {
    display: flex;
    gap: 20px;
    align-items: flex-start;
  }
  .tools {
    width: 200px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .tool-grid {
    display: grid;
    grid-template-columns: repeat(5, 1fr);
    gap: 6px;
  }
  .tool {
    height: 36px;
    border: 0;
    border-radius: 8px;
    background: var(--panel-3);
    color: var(--muted);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .tool.on {
    background: var(--accent-soft);
    color: var(--accent-text);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .palette {
    display: grid;
    grid-template-columns: repeat(6, 1fr);
    gap: 6px;
  }
  .swatch {
    position: relative;
    aspect-ratio: 1;
    border: 0;
    border-radius: 6px;
    cursor: pointer;
    box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.1);
  }
  .swatch.on {
    box-shadow: 0 0 0 2px var(--panel), 0 0 0 4px var(--accent-text);
  }
  .canvas-wrap {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  /* Feste Größe; beim Hineinzoomen wird gescrollt */
  .canvas {
    height: 460px;
    overflow: auto;
    display: flex;
    padding: 12px;
    border-radius: 12px;
    background: #1c1c1c;
  }
  .canvas canvas {
    /* mittig, beim Hineinzoomen trotzdem bis an jeden Rand scrollbar */
    margin: auto;
    flex-shrink: 0;
    cursor: crosshair;
    image-rendering: pixelated;
    border-radius: 4px;
  }
  .canvas canvas.move {
    cursor: move;
  }
  .zoom {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .zoom .icon-btn.small {
    width: 28px;
    height: 28px;
    color: var(--muted);
  }
  .zoom-value {
    min-width: 58px;
    height: 28px;
    border: 0;
    border-radius: 7px;
    background: var(--panel-3);
    color: var(--text);
    font-size: 12px;
    font-weight: 600;
    font-variant-numeric: tabular-nums;
    cursor: pointer;
  }
  .zoom .hint {
    margin-left: auto;
  }
  .layer {
    display: flex;
    align-items: center;
    gap: 4px;
    border-radius: 8px;
    background: var(--panel-3);
  }
  .layer.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .layer-pick {
    flex-grow: 1;
    min-width: 0;
    padding: 5px 6px;
    border: 0;
    background: none;
    color: var(--text);
    font-size: 12.5px;
    display: flex;
    align-items: center;
    gap: 8px;
    cursor: pointer;
    text-align: left;
  }
  .layer-pick img {
    width: 26px;
    height: 26px;
    border-radius: 5px;
    object-fit: cover;
    flex-shrink: 0;
  }
  .layer-x {
    width: 24px;
    height: 24px;
    margin-right: 4px;
    border: 0;
    border-radius: 6px;
    background: transparent;
    color: var(--muted);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .layer-x:hover {
    background: var(--red);
    color: #fff;
  }
  .range {
    width: 100%;
    accent-color: var(--accent-text);
  }
  .preview {
    width: 220px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    gap: 10px;
    align-items: stretch;
  }
</style>
