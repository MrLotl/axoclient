<script lang="ts">
  import type { Snippet } from 'svelte';
  import Icon from './Icon.svelte';
  import ImageCropDialog from './ImageCropDialog.svelte';
  import { dropFiles, isImage } from '../util/drop';
  import { pickImage } from '../util/files';
  import { t } from '../i18n';

  /**
   * Die eine Bildfläche für Instanz- und Server-Bilder (Icons und Banner).
   * - Ohne Bild: Anfangsbuchstaben von `name`, sonst `fallbackIcon` oder `placeholder`.
   * - `editable`: Klick oder Stift beim Hovern wählt ein neues Bild, Ablegen per Drag & Drop geht auch.
   *   Danach wird im Dialog zugeschnitten und `onchange` bekommt das Ergebnis (null = entfernen).
   * - `droppable`: nur Drag & Drop, ohne Hover-Stift (für Übersichten).
   * - `editButton`: 'overlay' legt den Stift übers Bild (Icons), 'corner' setzt ihn oben rechts (Banner).
   */
  type Size = number | string;
  let {
    url = null,
    name = '',
    fallbackIcon = null,
    placeholder = null,
    width = 36,
    height = width,
    radius = 8,
    pixelated = false,
    blur = false,
    dashed = false,
    editable = false,
    droppable = false,
    removable = false,
    editButton = 'overlay',
    aspect = 1,
    outputWidth = 256,
    cropTitle = '',
    editLabel = t('common.edit'),
    removeLabel = t('common.remove'),
    onchange,
    children
  }: {
    url?: string | null;
    name?: string;
    fallbackIcon?: string | null;
    placeholder?: string | null;
    width?: Size;
    height?: Size;
    radius?: number;
    pixelated?: boolean;
    blur?: boolean;
    dashed?: boolean;
    editable?: boolean;
    droppable?: boolean;
    removable?: boolean;
    editButton?: 'overlay' | 'corner';
    aspect?: number;
    outputWidth?: number;
    cropTitle?: string;
    editLabel?: string;
    removeLabel?: string;
    onchange?: (base64: string | null, url: string | null) => void;
    children?: Snippet;
  } = $props();

  const css = (v: Size) => (typeof v === 'number' ? `${v}px` : v);
  const px = $derived(typeof width === 'number' ? width : 64);

  // Kaputte Bilder (z. B. Server ohne Icon) zeigen den Platzhalter, bis sich die Adresse ändert
  let failedUrl = $state<string | null>(null);
  const shown = $derived(!!url && failedUrl !== url);
  const initials = $derived(
    name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0]?.toUpperCase())
      .join('')
  );

  let cropping = $state<string | null>(null);

  function startCrop(file: Blob | string) {
    if (cropping) URL.revokeObjectURL(cropping);
    cropping = typeof file === 'string' ? file : URL.createObjectURL(file);
  }

  async function choose() {
    const image = await pickImage();
    if (image) startCrop(image.url);
  }

  function cropped(base64: string) {
    if (cropping) URL.revokeObjectURL(cropping);
    cropping = null;
    const mime = aspect >= 2 ? 'image/jpeg' : 'image/png';
    onchange?.(base64, `data:${mime};base64,${base64}`);
  }
</script>

<div
  class="box"
  class:editable
  class:empty={!shown && !name}
  class:dashed={dashed && !shown}
  style="width: {css(width)}; height: {css(height)}; border-radius: {radius}px"
  use:dropFiles={{ accept: isImage, ondrop: (f) => startCrop(f[0]!), disabled: !(editable || droppable) || !onchange }}>
  {#if shown}
    <img src={url} alt="" class:pixelated class:blur onerror={() => (failedUrl = url)} />
  {:else if name}
    <span class="initials">{initials}</span>
  {:else if fallbackIcon}
    <span class="fallback"><Icon name={fallbackIcon} size={Math.max(14, Math.round(px * 0.18))} /></span>
  {:else if placeholder}
    <span class="fallback text"><Icon name="image" size={14} />{placeholder}</span>
  {/if}

  {@render children?.()}

  {#if editable}
    {#if editButton === 'overlay'}
      <button class="overlay" aria-label={editLabel} title={editLabel} onclick={choose}>
        <Icon name="pencil" size={Math.min(18, Math.max(12, Math.round(px * 0.25)))} />
      </button>
      {#if removable && shown}
        <button class="remove" aria-label={removeLabel} title={removeLabel} onclick={() => onchange?.(null, null)}><Icon name="x" size={11} /></button>
      {/if}
    {:else}
      <div class="corner">
        <button class="mini" aria-label={editLabel} title={editLabel} onclick={choose}><Icon name="pencil" size={14} /></button>
        {#if removable && shown}
          <button class="mini" aria-label={removeLabel} title={removeLabel} onclick={() => onchange?.(null, null)}><Icon name="x" size={13} /></button>
        {/if}
      </div>
    {/if}
  {/if}
</div>

{#if cropping}
  <ImageCropDialog
    src={cropping}
    {aspect}
    {outputWidth}
    round={aspect === 1}
    title={cropTitle}
    onconfirm={cropped}
    oncancel={() => {
      URL.revokeObjectURL(cropping!);
      cropping = null;
    }} />
{/if}

<style>
  .box {
    position: relative;
    flex-shrink: 0;
    overflow: hidden;
    isolation: isolate;
    container-type: size;
  }
  img {
    display: block;
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  img.pixelated {
    image-rendering: pixelated;
  }
  /* Ohne Banner: Instanzbild weich gezeichnet als Hintergrund */
  img.blur {
    position: absolute;
    inset: -40px;
    width: calc(100% + 80px);
    height: calc(100% + 80px);
    filter: blur(30px) saturate(1.2);
    opacity: 0.8;
  }
  .initials {
    font-size: 38cqmin;
    width: 100%;
    height: 100%;
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--accent);
    color: #fff;
    font-weight: 700;
    letter-spacing: -0.5px;
  }
  .fallback {
    width: 100%;
    height: 100%;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    background: var(--panel-2);
    color: var(--muted-2);
  }
  .fallback.text {
    font-size: 12px;
  }
  .dashed {
    outline: 1.5px dashed #4a4a4a;
    outline-offset: -1.5px;
  }
  .editable.dashed:hover {
    outline-color: #5a5a5a;
  }
  .overlay {
    position: absolute;
    inset: 0;
    z-index: 2;
    padding: 0;
    border: 0;
    border-radius: inherit;
    background: rgba(0, 0, 0, 0.5);
    color: #fff;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    opacity: 0;
    transition: opacity 0.15s ease;
  }
  /* Leere Felder brauchen keinen dunklen Schleier, der Stift reicht */
  .empty > .overlay {
    background: rgba(0, 0, 0, 0.25);
  }
  .box:hover > .overlay,
  .overlay:focus-visible {
    opacity: 1;
  }
  .remove {
    position: absolute;
    z-index: 3;
    top: 6px;
    right: 6px;
    width: 22px;
    height: 22px;
    padding: 0;
    border: 0;
    border-radius: 11px;
    background: rgba(0, 0, 0, 0.6);
    color: #fff;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    opacity: 0;
    transition: opacity 0.15s ease, background-color 0.15s ease;
  }
  .remove:hover {
    background: var(--red);
  }
  .box:hover > .remove,
  .remove:focus-visible {
    opacity: 1;
  }
  .corner {
    position: absolute;
    z-index: 3;
    top: 14px;
    right: 14px;
    display: flex;
    gap: 6px;
    opacity: 0;
    transition: opacity 0.15s ease;
  }
  .box:hover > .corner,
  .corner:focus-within {
    opacity: 1;
  }
  .mini {
    width: 34px;
    height: 34px;
    padding: 0;
    border: 0;
    border-radius: 9px;
    background: rgba(0, 0, 0, 0.45);
    backdrop-filter: blur(8px);
    color: #f2f2f2;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .mini:hover {
    background: rgba(0, 0, 0, 0.65);
  }
</style>
