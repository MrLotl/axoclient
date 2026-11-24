<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { call } from '../bridge/bridge';
  import { t } from '../i18n';
  import Icon from './Icon.svelte';
  import ImageCropDialog from './ImageCropDialog.svelte';
  import { gradient, panoramaUrl } from '../util/art';
  import { pickImage } from '../util/files';
  import { pop, DUR } from '../util/motion';
  import type { InstanceSummary } from '../types';

  /**
   * Hintergrund einer Instanz wählen: Panorama der Version (vier Blickrichtungen), Farbverlauf oder eigenes Bild.
   * Das eigene Bild wird im Format 16:9 zugeschnitten und als Hintergrund der Instanz gespeichert.
   */
  let { instance, onclose, onchanged }: { instance: InstanceSummary; onclose: () => void; onchanged: () => void } = $props();

  let busy = $state(false);
  let cropping = $state<string | null>(null);

  const seed = $derived(instance.artSeed ?? instance.id);
  const current = $derived(instance.bannerUrl ? 'custom' : instance.gradientOnly ? 'gradient' : `view${instance.panoramaView ?? 0}`);

  async function setBackdrop(gradientOnly: boolean, view: number) {
    busy = true;
    try {
      await call('instances.setBackdrop', { id: instance.id, gradientOnly, panoramaView: view });
      onchanged();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }

  async function chooseImage() {
    const image = await pickImage();
    if (image) cropping = image.url;
  }

  async function saveImage(base64: string) {
    if (cropping) URL.revokeObjectURL(cropping);
    cropping = null;
    busy = true;
    try {
      await call('instances.setImage', { id: instance.id, banner: true, base64, fileRef: null });
      onchanged();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<svelte:window onkeydown={(e) => { if (e.key === 'Escape' && !cropping) onclose(); }} />

<div class="picker menu" role="dialog" aria-label={t('backdrop.title')} in:pop={{ origin: 'top right', scale: 0.96, y: -6, duration: DUR.med }}>
  <div class="head">
    <b>{t('backdrop.title')}</b>
    <button class="icon-btn flat" aria-label={t('common.close')} onclick={onclose}><Icon name="x" size={14} /></button>
  </div>

  <span class="label">{t('backdrop.panorama')}</span>
  <div class="views">
    {#each [0, 1, 2, 3] as v (v)}
      <button
        class="tile"
        class:on={current === `view${v}`}
        disabled={busy}
        aria-pressed={current === `view${v}`}
        aria-label={t('backdrop.view', { n: v + 1 })}
        style="background-image: url('{panoramaUrl(instance.minecraftVersion, v)}'), {gradient(seed)}"
        onclick={() => setBackdrop(false, v)}>
        <span class="tag">{t('backdrop.view', { n: v + 1 })}</span>
      </button>
    {/each}
  </div>
  <p class="hint">{t('backdrop.panoramaHint', { version: instance.minecraftVersion })}</p>

  <div class="two">
    <button class="tile wide" class:on={current === 'gradient'} disabled={busy} aria-pressed={current === 'gradient'} style="background-image: {gradient(seed)}" onclick={() => setBackdrop(true, instance.panoramaView ?? 0)}>
      <span class="tag">{t('backdrop.gradient')}</span>
    </button>
    <button
      class="tile wide custom"
      class:on={current === 'custom'}
      disabled={busy}
      aria-pressed={current === 'custom'}
      style={instance.bannerUrl ? `background-image: url('${instance.bannerUrl}')` : ''}
      onclick={chooseImage}>
      {#if !instance.bannerUrl}<Icon name="image" size={18} />{/if}
      <span class="tag">{instance.bannerUrl ? t('backdrop.replace') : t('backdrop.choose')}</span>
    </button>
  </div>
</div>

{#if cropping}
  <ImageCropDialog
    src={cropping}
    aspect={16 / 9}
    outputWidth={1920}
    title={t('backdrop.cropTitle')}
    onconfirm={saveImage}
    oncancel={() => {
      if (cropping) URL.revokeObjectURL(cropping);
      cropping = null;
    }} />
{/if}

<style>
  .picker {
    position: absolute;
    top: calc(100% + 8px);
    right: 0;
    z-index: 40;
    width: 380px;
    padding: 12px;
    gap: 8px;
  }
  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 0 4px 4px;
  }
  .head b {
    font-family: var(--display);
    font-size: 16px;
    font-weight: 700;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--text-strong);
  }
  .label {
    padding-left: 4px;
    font-size: 12px;
    font-weight: 600;
    color: var(--muted-2);
  }
  .views {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }
  .two {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
    margin-top: 4px;
  }
  .tile {
    position: relative;
    aspect-ratio: 16 / 9;
    max-width: 100%;
    padding: 0;
    border: 0;
    border-radius: var(--radius-m);
    background-color: var(--panel-3);
    background-size: cover;
    background-position: center;
    cursor: pointer;
    display: grid;
    place-items: center;
    color: var(--muted);
    transition: box-shadow var(--dur-fast) ease, transform var(--dur-fast) ease;
  }
  .tile:hover:not(:disabled) {
    transform: translateY(-2px);
  }
  .tile.on {
    box-shadow: 0 0 0 2px var(--text-strong);
  }
  .tile:disabled {
    opacity: 0.6;
    cursor: default;
  }
  .tile.custom:not([style*='url']) {
    border: 1px dashed var(--line-2);
  }
  .tag {
    position: absolute;
    left: 6px;
    bottom: 6px;
    padding: 2px 7px;
    border-radius: var(--radius-s);
    background: rgba(10, 11, 13, 0.7);
    font-size: 11.5px;
    font-weight: 600;
    color: var(--text-strong);
  }
  .hint {
    padding-left: 4px;
    font-size: 11.5px;
    color: var(--faint);
  }
</style>
