<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import Segmented from '../../lib/components/Segmented.svelte';
  import { ago, bytes } from '../../lib/util/format';
  import { upload } from '../../lib/util/files';
  import type { BackupInfo, JobInfo, WorldInfo } from '../../lib/types';

  let { instanceId }: { instanceId: string } = $props();
  let worlds = $state<WorldInfo[]>([]);
  let backups = $state<BackupInfo[]>([]);
  let loaded = $state(false);
  let query = $state('');
  let view = $state<'grid' | 'list'>('grid');
  let showBackups = $state(false);

  async function load() {
    try {
      worlds = await call<WorldInfo[]>('worlds.list', { id: instanceId });
      backups = await call<BackupInfo[]>('worlds.backups', { id: instanceId });
      loaded = true;
    } catch (e) {
      app.error(e);
    }
  }

  onMount(() => {
    load();
    return on<JobInfo>('job.changed', (j) => {
      if ((j.kind === 'world.backup' || j.kind === 'world.restore') && j.subject === instanceId && j.state === 'succeeded') load();
    });
  });

  const shown = $derived(worlds.filter((w) => !query.trim() || w.name.toLowerCase().includes(query.trim().toLowerCase())));
  const modeLabel = (w: WorldInfo) => (w.hardcore ? t('worlds.modes.hardcore') : t(`worlds.modes.${w.gameMode}`));

  async function backup(w: WorldInfo) {
    try {
      await call('worlds.backup', { id: instanceId, folder: w.folder });
    } catch (e) {
      app.error(e);
    }
  }

  function remove(w: WorldInfo) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('worlds.deleteTitle', { name: w.name }),
        text: t('worlds.deleteText'),
        confirm: t('common.delete'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            await call('worlds.delete', { id: instanceId, folder: w.folder });
            await load();
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }

  async function importWorlds(refs: string[]) {
    if (refs.length === 0) return;
    try {
      const names = await call<string[]>('worlds.import', { id: instanceId, fileRefs: refs });
      app.toast(names.length === 1 ? t('worlds.imported', { name: names[0] }) : t('worlds.importedMany', { count: names.length }), 'success');
      await load();
    } catch (e) {
      app.error(e);
    }
  }

  async function pickImport() {
    try {
      const files = await call<{ id: string }[]>('dialog.openFile', { purpose: 'world', multi: true });
      await importWorlds(files.map((f) => f.id));
    } catch (e) {
      app.error(e);
    }
  }

  let dragging = $state(false);
  async function ondrop(e: DragEvent) {
    e.preventDefault();
    dragging = false;
    const files = Array.from(e.dataTransfer?.files ?? []).filter((f) => /\.zip$/i.test(f.name));
    if (files.length === 0) return;
    try {
      const refs = [];
      for (const f of files) refs.push((await upload(f)).id);
      await importWorlds(refs);
    } catch (err) {
      app.error(err);
    }
  }

  const totalSize = $derived(worlds.reduce((sum, w) => sum + w.sizeBytes, 0));

  async function restore(b: BackupInfo) {
    try {
      const name = await call<string>('worlds.restore', { id: instanceId, file: b.file });
      app.toast(t('worlds.restored', { name }), 'success');
      await load();
    } catch (e) {
      app.error(e);
    }
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="tab"
  ondragover={(e) => {
    e.preventDefault();
    dragging = true;
  }}
  ondragleave={() => (dragging = false)}
  {ondrop}>
  <div class="toolbar">
    <label class="field search">
      <span style="color: #8a8a8a; display: flex"><Icon name="search" /></span>
      <input type="search" bind:value={query} placeholder={t('worlds.search')} aria-label={t('worlds.search')} />
    </label>
    <Segmented bind:value={view} label={t('common.view')} options={[{ value: 'grid', icon: 'grid', aria: t('common.gridView') }, { value: 'list', icon: 'list', aria: t('common.listView') }]} />
    <span class="summary">{t('worlds.count', { count: worlds.length })} · {bytes(totalSize)}</span>
    <div class="grow"></div>
    <button class="btn btn-tool square" aria-label={t('worlds.openFolder')} title={t('worlds.openFolder')} onclick={() => call('shell.openFolder', { kind: 'instanceSub', id: instanceId, sub: 'saves' })}><Icon name="folder" /></button>
    <button class="btn btn-tool square" class:pressed={showBackups} onclick={() => (showBackups = !showBackups)} aria-pressed={showBackups} aria-label={t('worlds.backups', { count: backups.length })} title={t('worlds.backups', { count: backups.length })}><Icon name="archive" /></button>
    <button class="btn btn-tool" onclick={pickImport}><Icon name="import" stroke={1.8} />{t('worlds.import')}</button>
  </div>

  {#if showBackups}
    <div class="backups panel">
      {#each backups as b (b.file)}
        <div class="b-row">
          <Icon name="archive" size={14} />
          <span class="grow truncate">{b.world}</span>
          <span class="hint">{new Date(b.createdAt).toLocaleString('de-DE', { dateStyle: 'medium', timeStyle: 'short' })} · {bytes(b.sizeBytes)}</span>
          <button class="btn btn-second sm" onclick={() => restore(b)}>{t('worlds.restore')}</button>
        </div>
      {:else}
        <p class="hint">{t('worlds.noBackups')}</p>
      {/each}
    </div>
  {/if}

  {#if loaded && worlds.length === 0}
    <div class="empty"><Icon name="world" size={24} /><span>{t('worlds.empty')}</span></div>
  {:else}
    <div class={view === 'grid' ? 'grid' : 'list'}>
      {#each shown as w (w.folder)}
        <article class={view === 'grid' ? 'card' : 'row-card'}>
          <div class="shot">
            {#if w.iconUrl}<img src={w.iconUrl} alt="" />{:else}<Icon name="map" size={view === 'grid' ? 22 : 18} />{/if}
            {#if view === 'grid'}<span class="mode" class:hardcore={w.hardcore}>{modeLabel(w)}</span>{/if}
          </div>
          <div class="w-text">
            <h3 class="truncate">{w.name}</h3>
            <span class="sub truncate">{view === 'list' ? `${modeLabel(w)} · ` : ''}{ago(w.lastPlayed)} · {bytes(w.sizeBytes)}{w.cheats ? ` · ${t('worlds.cheats')}` : ''}</span>
          </div>
          <div class="w-actions">
            <button class="icon-btn small" aria-label={t('worlds.backupNamed', { name: w.name })} title={t('worlds.backup')} onclick={() => backup(w)}><Icon name="archive" size={14} /></button>
            <button class="icon-btn small" aria-label={t('worlds.openFolder')} title={t('common.openFolder')} onclick={() => call('shell.openFolder', { kind: 'world', id: instanceId, sub: w.folder })}><Icon name="folder" size={14} /></button>
            <button class="icon-btn small" aria-label={t('worlds.deleteNamed', { name: w.name })} title={t('common.delete')} onclick={() => remove(w)}><Icon name="trash" size={14} /></button>
            <div class="grow"></div>
            <button class="btn btn-second sm play" onclick={() => app.launch(instanceId, { world: w.folder })}><Icon name="play" size={12} />{t('common.play')}</button>
          </div>
        </article>
      {/each}
    </div>
  {/if}

  {#if dragging}
    <div class="drop"><Icon name="import" size={28} /><span>{t('worlds.dropHint')}</span></div>
  {/if}
</div>

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
  .backups {
    padding: 10px 12px;
    display: flex;
    flex-direction: column;
    gap: 6px;
    max-height: 200px;
    overflow-y: auto;
  }
  .b-row {
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 13px;
    color: var(--muted);
  }
  .grid {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
    grid-auto-rows: max-content;
    gap: 16px;
    align-content: start;
  }
  .list {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .card {
    padding: 8px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 10px;
    transition: background-color 0.12s ease;
  }
  .row-card {
    padding: 6px 12px 6px 6px;
    border-radius: 10px;
    background: var(--panel);
    display: flex;
    align-items: center;
    gap: 14px;
    transition: background-color 0.12s ease;
  }
  .card:hover,
  .row-card:hover {
    background: #2f2f2f;
  }
  /* Vorschaubild der Welt (icon.png), ohne Bild das Karten-Symbol */
  .shot {
    position: relative;
    flex-shrink: 0;
    border-radius: 6px;
    overflow: hidden;
    background: #1c1c1c;
    color: var(--muted-2);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .shot img {
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .card .shot {
    aspect-ratio: 16 / 9;
  }
  .row-card .shot {
    width: 96px;
    height: 54px;
  }
  .mode {
    position: absolute;
    left: 8px;
    bottom: 8px;
    height: 20px;
    padding: 0 7px;
    border-radius: 5px;
    background: rgba(76, 141, 255, 0.85);
    color: #fff;
    font-size: 11px;
    font-weight: 600;
    line-height: 20px;
  }
  .mode.hardcore {
    background: rgba(244, 112, 103, 0.9);
  }
  .w-text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .card .w-text {
    padding: 0 4px;
  }
  h3 {
    margin: 0;
    font-size: 14.5px;
    font-weight: 600;
  }
  .sub {
    font-size: 12px;
    color: var(--muted-2);
  }
  .w-actions {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .card .w-actions {
    padding: 0 4px 4px;
  }
  .row-card .w-actions .grow {
    width: 6px;
    flex-grow: 0;
  }
  .row-card .play {
    min-width: 110px;
    justify-content: center;
  }
  .icon-btn.small {
    width: 30px;
    height: 30px;
    border-radius: 7px;
    background: #353535;
    color: var(--muted);
  }
  .icon-btn.small:hover {
    background: #3e3e3e;
    color: var(--text);
  }
  .square {
    width: 36px;
    padding: 0;
  }
  .pressed {
    background: var(--accent-soft-strong);
    color: var(--accent-text);
  }
  .tab {
    position: relative;
  }
  .drop {
    position: absolute;
    inset: 0;
    border-radius: 16px;
    border: 2px dashed var(--accent-text);
    background: rgba(20, 20, 20, 0.88);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
    color: var(--accent-text);
    font-weight: 600;
    pointer-events: none;
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
</style>
