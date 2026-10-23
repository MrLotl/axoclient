<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import { bytes } from '../../lib/util/format';
  import type { ScreenshotInfo } from '../../lib/types';
  import { fade } from 'svelte/transition';
  import { fadeSlide, fadeOut, fadeLoad, pop, stagger, reducedMotion, DUR } from '../../lib/util/motion';

  const delay = stagger(25, 12);

  let { instanceId, count = $bindable(null) }: { instanceId: string; count?: number | null } = $props();
  let shots = $state<ScreenshotInfo[]>([]);
  let loaded = $state(false);
  let query = $state('');
  let preview = $state<ScreenshotInfo | null>(null);
  let renaming = $state(false);
  let newName = $state('');

  async function load() {
    try {
      shots = await call<ScreenshotInfo[]>('screenshots.list', { id: instanceId });
      count = shots.length;
      loaded = true;
    } catch (e) {
      app.error(e);
    }
  }

  onMount(load);

  const shown = $derived(shots.filter((s) => !query.trim() || s.file.toLowerCase().includes(query.trim().toLowerCase())));
  const index = $derived(preview ? shown.findIndex((s) => s.file === preview!.file) : -1);

  function step(delta: number) {
    if (index < 0) return;
    const next = shown[(index + delta + shown.length) % shown.length];
    if (next) preview = next;
    renaming = false;
  }

  async function rename() {
    if (!preview || !newName.trim()) return;
    try {
      shots = await call<ScreenshotInfo[]>('screenshots.rename', { id: instanceId, file: preview.file, name: newName.trim() });
      preview = shots.find((s) => s.file.startsWith(newName.trim())) ?? null;
      renaming = false;
    } catch (e) {
      app.error(e);
    }
  }

  async function remove(s: ScreenshotInfo) {
    try {
      shots = await call<ScreenshotInfo[]>('screenshots.delete', { id: instanceId, file: s.file });
      count = shots.length;
      preview = null;
    } catch (e) {
      app.error(e);
    }
  }

  async function copyImage(s: ScreenshotInfo) {
    try {
      const blob = await (await fetch(s.url)).blob();
      await navigator.clipboard.write([new ClipboardItem({ [blob.type]: blob })]);
      app.toast(t('screenshots.copied'), 'success');
    } catch {
      app.toast(t('screenshots.copyFailed'), 'error');
    }
  }
</script>

<svelte:window
  onkeydown={(e) => {
    if (!preview || renaming) return;
    if (e.key === 'Escape') preview = null;
    if (e.key === 'ArrowRight') step(1);
    if (e.key === 'ArrowLeft') step(-1);
  }} />

<div class="tab">
  <div class="toolbar">
    <label class="field search">
      <span style="color: #8a8a8a; display: flex"><Icon name="search" /></span>
      <input type="search" bind:value={query} placeholder={t('screenshots.search')} aria-label={t('screenshots.search')} />
    </label>
    <span class="summary">{t('screenshots.count', { count: shots.length })}</span>
    <div class="grow"></div>
    <button class="btn btn-tool" style="width: 36px; padding: 0" aria-label={t('screenshots.openFolder')} title={t('common.openFolder')} onclick={() => call('shell.openFolder', { kind: 'instanceSub', id: instanceId, sub: 'screenshots' })}><Icon name="folder" /></button>
  </div>
  {#if loaded && shots.length === 0}
    <div class="empty"><Icon name="camera" size={24} /><span>{t('screenshots.empty')}</span></div>
  {:else}
    <div class="grid">
      {#each shown as s, i (s.file)}
        <button class="shot" onclick={() => (preview = s)} aria-label={t('screenshots.open', { name: s.file })} in:fadeSlide|global={{ delay: delay(i), y: 10 }}>
          <img src={s.url} alt="" loading="lazy" use:fadeLoad={s.url} />
          <span class="date">{new Date(s.takenAt).toLocaleDateString('de-DE')}</span>
        </button>
      {/each}
    </div>
  {/if}
</div>

{#if preview}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="lightbox" role="dialog" tabindex="-1" aria-modal="true" aria-label={t('screenshots.preview')} onmousedown={(e) => e.target === e.currentTarget && (preview = null)} in:fade={{ duration: reducedMotion() ? 0 : DUR.med }} out:fadeOut={{ duration: DUR.med }}>
    <button class="nav prev" aria-label={t('screenshots.prev')} onclick={() => step(-1)}><Icon name="chevronLeft" size={20} /></button>
    {#key preview.url}
      <img src={preview.url} alt={preview.file} in:pop={{ scale: 0.97, duration: DUR.med }} />
    {/key}
    <button class="nav next" aria-label={t('screenshots.next')} onclick={() => step(1)}><Icon name="chevronRight" size={20} /></button>
    <div class="bar">
      {#if renaming}
        <span class="field" style="height: 34px; flex-grow: 1"><input bind:value={newName} aria-label={t('screenshots.newName')} onkeydown={(e) => e.key === 'Enter' && rename()} /></span>
        <button class="btn btn-grad sm" onclick={rename}>{t('common.save')}</button>
        <button class="btn btn-ghost sm" onclick={() => (renaming = false)}>{t('common.cancel')}</button>
      {:else}
        <span class="grow truncate name">{preview.file}</span>
        <span class="hint">{preview.width && preview.height ? `${preview.width}×${preview.height} · ` : ''}{bytes(preview.sizeBytes)}</span>
        <button class="icon-btn" aria-label={t('screenshots.copy')} title={t('screenshots.copy')} onclick={() => copyImage(preview!)}><Icon name="copy" size={14} /></button>
        <button class="icon-btn" aria-label={t('screenshots.rename')} title={t('screenshots.rename')} onclick={() => { renaming = true; newName = preview!.file.replace(/\.(png|jpg)$/i, ''); }}><Icon name="edit" size={14} /></button>
        <button class="icon-btn" aria-label={t('screenshots.reveal')} title={t('screenshots.reveal')} onclick={() => call('screenshots.reveal', { id: instanceId, file: preview!.file })}><Icon name="folder" size={14} /></button>
        <button class="icon-btn trash" aria-label={t('common.delete')} onclick={() => remove(preview!)}><Icon name="trash" size={14} /></button>
        <button class="icon-btn" aria-label={t('screenshots.close')} onclick={() => (preview = null)}><Icon name="x" size={14} /></button>
      {/if}
    </div>
  </div>
{/if}

<style>
  .tab {
    flex-grow: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .toolbar {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .search {
    width: 260px;
    height: 36px;
    border-radius: 8px;
    background: var(--panel);
    box-shadow: none;
  }
  .summary {
    font-size: 13px;
    color: var(--muted-2);
  }
  .grid {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
    grid-auto-rows: max-content;
    gap: 12px;
    align-content: start;
  }
  .shot {
    position: relative;
    aspect-ratio: 16 / 9;
    padding: 0;
    border: 0;
    border-radius: 10px;
    overflow: hidden;
    background: #1a1a1a;
    cursor: zoom-in;
  }
  .shot img {
    width: 100%;
    height: 100%;
    object-fit: cover;
    transition: transform 0.3s var(--ease-out);
  }
  .shot:hover img {
    transform: scale(1.04);
  }
  .date {
    position: absolute;
    left: 8px;
    bottom: 8px;
    padding: 3px 7px;
    border-radius: 6px;
    background: rgba(0, 0, 0, 0.5);
    backdrop-filter: blur(6px);
    color: #f2f2f2;
    font-size: 11px;
    font-weight: 600;
  }
  .empty {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
    color: var(--muted-2);
    border-radius: 12px;
    box-shadow: inset 0 0 0 1.5px #2f2f2f;
  }
  .lightbox {
    position: fixed;
    inset: 0;
    z-index: 55;
    background: rgba(8, 8, 8, 0.9);
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 40px 80px 90px;
  }
  .lightbox img {
    max-width: 100%;
    max-height: 100%;
    border-radius: 10px;
    box-shadow: 0 20px 60px rgba(0, 0, 0, 0.6);
  }
  .nav {
    position: absolute;
    top: 50%;
    transform: translateY(-50%);
    width: 44px;
    height: 44px;
    border: 0;
    border-radius: 22px;
    background: rgba(255, 255, 255, 0.08);
    color: #fff;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .nav:hover {
    background: rgba(255, 255, 255, 0.16);
  }
  .prev {
    left: 20px;
  }
  .next {
    right: 20px;
  }
  .bar {
    position: absolute;
    left: 50%;
    bottom: 22px;
    transform: translateX(-50%);
    width: min(720px, calc(100% - 40px));
    padding: 8px 8px 8px 14px;
    border-radius: 12px;
    background: var(--panel);
    box-shadow: 0 12px 30px rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .name {
    font-size: 13px;
    font-weight: 600;
  }
</style>
