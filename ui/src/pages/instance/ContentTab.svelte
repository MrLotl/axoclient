<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import DropOverlay from '../../lib/components/DropOverlay.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import Segmented from '../../lib/components/Segmented.svelte';
  import Toggle from '../../lib/components/Toggle.svelte';
  import { bytes } from '../../lib/util/format';
  import { upload } from '../../lib/util/files';
  import AddContentDialog from './AddContentDialog.svelte';
  import VersionDialog from './VersionDialog.svelte';
  import UpdatesDialog from './UpdatesDialog.svelte';
  import RepairDialog from './RepairDialog.svelte';
  import type { ContentItem, ContentType, ContentUpdate, Instance, IssueView } from '../../lib/types';

  let { instance, type, count = $bindable(null) }: { instance: Instance; type: ContentType; count?: number | null } = $props();

  let items = $state<ContentItem[]>([]);
  let loaded = $state(false);
  let query = $state('');
  let view = $state<'grid' | 'list'>('grid');
  let updates = $state<ContentUpdate[]>([]);
  let issues = $state<IssueView[]>([]);
  let dialog = $state<null | 'add' | 'updates' | 'repair' | { version: ContentItem }>(null);

  // Größe der Kacheln bzw. Zeilen (0 = klein, 1 = groß), je Ansicht gemerkt
  function storedSize(v: 'grid' | 'list') {
    try {
      const n = parseFloat(localStorage.getItem(`content.size.${v}`) ?? '');
      return Number.isFinite(n) ? Math.min(1, Math.max(0, n)) : 0.5;
    } catch {
      return 0.5;
    }
  }
  let sizes = $state({ grid: storedSize('grid'), list: storedSize('list') });
  function setSize(value: number) {
    sizes[view] = value;
    try {
      localStorage.setItem(`content.size.${view}`, String(value));
    } catch {
      // ohne Speicher gilt die Größe nur bis zum Schließen
    }
  }
  const size = $derived(sizes[view]);

  const folder = $derived(type === 'mod' ? 'mods' : type === 'resourcePack' ? 'resourcepacks' : 'shaderpacks');
  const key = $derived(type === 'mod' ? 'mods' : type === 'resourcePack' ? 'packs' : 'shaders');
  const updateFiles = $derived(new Set(updates.map((u) => u.file)));
  // Mod-Schutz gilt nur für Mods
  const modLock = $derived(type === 'mod' ? (instance.modLock?.level ?? 'off') : 'off');
  const addBlocked = $derived(modLock === 'full');

  async function load() {
    try {
      items = await call<ContentItem[]>('content.list', { id: instance.id, type });
      count = items.filter((i) => i.source !== 'axoclient').length;
      loaded = true;
    } catch (e) {
      app.error(e);
    }
  }

  async function scan() {
    try {
      updates = (await call<ContentUpdate[]>('content.updates', { id: instance.id })).filter((u) => u.type === type);
    } catch {
      updates = [];
    }
    if (type === 'mod') {
      try {
        issues = await call<IssueView[]>('instances.diagnose', { id: instance.id });
      } catch {
        issues = [];
      }
    }
  }

  onMount(() => {
    load();
    scan();
    const off = on<{ instanceId: string }>('content.changed', (d) => {
      if (d.instanceId === instance.id) {
        load();
        scan();
      }
    });
    return off;
  });

  const shown = $derived(
    items.filter((i) => !query.trim() || i.name.toLowerCase().includes(query.trim().toLowerCase()) || i.file.toLowerCase().includes(query.trim().toLowerCase()))
  );
  const disabledCount = $derived(items.filter((i) => !i.enabled).length);

  async function toggle(item: ContentItem) {
    try {
      items = await call<ContentItem[]>('content.setEnabled', { id: instance.id, type, file: item.file, enabled: !item.enabled });
    } catch (e) {
      app.error(e);
    }
  }

  function remove(item: ContentItem) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('content.removeTitle', { name: item.name }),
        text: t('content.removeText'),
        confirm: t('common.remove'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            items = await call<ContentItem[]>('content.remove', { id: instance.id, type, files: [item.file] });
            count = items.filter((i) => i.source !== 'axoclient').length;
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }

  function share(item: ContentItem) {
    if (!item.projectId || !item.versionId || (item.source !== 'modrinth' && item.source !== 'curseforge')) {
      app.toast(t('content.cannotShareLocal'), 'error');
      return;
    }
    app.dialog = {
      kind: 'share',
      data: {
        mode: 'mod',
        mod: { title: item.name, type: type === 'mod' ? 'mod' : type === 'resourcePack' ? 'resourcepack' : 'shaderpack', source: item.source, projectId: item.projectId, versionId: item.versionId }
      }
    };
  }

  async function ondrop(files: File[]) {
    if (addBlocked) {
      app.toast(t('content.modLock.full'), 'error');
      return;
    }
    try {
      const refs = [];
      for (const f of files) refs.push((await upload(f)).id);
      items = await call<ContentItem[]>('content.addFiles', { id: instance.id, type, fileRefs: refs });
      app.toast(t('content.added', { count: files.length }), 'success');
    } catch (err) {
      app.error(err);
    }
  }

  const sourceLabel = (i: ContentItem) =>
    i.source === 'axoclient' ? 'AxoClient' : i.source === 'local' ? t('content.localOwn') : i.source === 'modrinth' ? 'Modrinth' : i.source === 'curseforge' ? 'CurseForge' : t('content.fromFriend');
</script>

<div class="tab">
  <div class="toolbar">
    <label class="field search">
      <span style="color: #8a8a8a; display: flex"><Icon name="search" /></span>
      <input type="search" bind:value={query} placeholder={t(`content.${key}.search`)} aria-label={t(`content.${key}.search`)} />
    </label>
    <Segmented bind:value={view} label={t('common.view')} options={[{ value: 'grid', icon: 'grid', aria: t('common.gridView') }, { value: 'list', icon: 'list', aria: t('common.listView') }]} />
    <input
      class="size"
      type="range"
      min="0"
      max="1"
      step="0.01"
      value={size}
      oninput={(e) => setSize(parseFloat(e.currentTarget.value))}
      aria-label={t('content.size')}
      title={t('content.size')}
      style="background: linear-gradient(90deg, var(--accent-from), var(--accent-to)) 0 / {size * 100}% 100% no-repeat, #3a3a3a" />
    <span class="summary truncate">
      {t(`content.${key}.count`, { count: items.filter((i) => i.source !== 'axoclient').length })}{disabledCount ? ` · ${t('content.disabledCount', { count: disabledCount })}` : ''}
    </span>
    <div class="grow"></div>
    {#if modLock !== 'off'}
      <span class="lock-chip" title={t(`modLock.levels.${modLock}.text`)}><Icon name="lock" size={12} />{t(`modLock.levels.${modLock}.name`)}</span>
    {/if}
    <button class="btn btn-tool" style="width: 36px; padding: 0" aria-label={t('content.openFolder')} title={t('content.openFolder')} onclick={() => call('shell.openFolder', { kind: 'instanceSub', id: instance.id, sub: folder })}>
      <Icon name="folder" />
    </button>
    {#if type === 'mod'}
      <button class="btn btn-tool" onclick={() => (dialog = 'repair')}>
        <Icon name="wrench" />{t('content.repair')}
        {#if issues.length}<span class="warn-count">{issues.length}</span>{/if}
      </button>
    {/if}
    <button class="btn btn-tool" onclick={() => (dialog = 'updates')}>
      <Icon name="refresh" />{updates.length ? t('content.updatesCount', { count: updates.length }) : t('content.checkUpdates')}
    </button>
    <button class="btn btn-tool" disabled={addBlocked} title={addBlocked ? t('content.modLock.full') : undefined} onclick={() => (dialog = 'add')}><Icon name="plus" stroke={1.8} />{t(`content.${key}.add`)}</button>
  </div>

  {#if loaded && items.length === 0}
    <div class="empty">
      <span class="empty-icon"><Icon name={type === 'mod' ? 'puzzle' : type === 'resourcePack' ? 'image' : 'sun'} size={22} /></span>
      <h2>{t(`content.${key}.emptyTitle`)}</h2>
      <p>{t(`content.${key}.emptyText`)}</p>
      <button class="btn btn-second" disabled={addBlocked} onclick={() => (dialog = 'add')}><Icon name="plus" />{t(`content.${key}.add`)}</button>
    </div>
  {:else if view === 'grid'}
    <div class="grid" style="--k: {size}">
      {#each shown as m (m.file)}
        <article class="card" class:off={!m.enabled}>
          {#if !m.locked && !m.modLocked}
            <button class="remove" aria-label={t('content.removeNamed', { name: m.name })} title={t('common.remove')} onclick={() => remove(m)}><Icon name="x" size={12} /></button>
          {/if}
          <div class="thumb">
            {#if m.iconUrl}
              <img class="blur" src={m.iconUrl} alt="" aria-hidden="true" />
              <img class="icon" src={m.iconUrl} alt="" />
            {:else}
              <span class="icon no-icon"><Icon name={m.locked ? 'sparkle' : type === 'mod' ? 'puzzle' : 'image'} size={26} /></span>
            {/if}
            {#if updateFiles.has(m.file)}<span class="badge green">{t('content.updateAvailable')}</span>{/if}
          </div>
          <div class="meta">
            <h3 class="truncate">{m.name}</h3>
            <span class="sub truncate">{m.version ?? ''}{m.version ? ' · ' : ''}{sourceLabel(m)}</span>
          </div>
          <div class="actions">
            {#if m.modLocked}
              <button class="icon-btn small" aria-label={t('content.shareNamed', { name: m.name })} title={t('common.send')} onclick={() => share(m)}><Icon name="send" size={14} /></button>
              <div class="grow"></div>
              <span class="locked" title={t('modLock.rowHint')}><Icon name="lock" size={12} />{t('modLock.locked')}</span>
            {:else if !m.locked}
              <button class="icon-btn small" aria-label={t('content.shareNamed', { name: m.name })} title={t('common.send')} onclick={() => share(m)}><Icon name="send" size={14} /></button>
              {#if m.source === 'modrinth' || m.source === 'curseforge'}
                <button class="icon-btn small" aria-label={t('content.changeVersion')} title={t('content.changeVersion')} onclick={() => (dialog = { version: m })}><Icon name="swap" size={14} /></button>
              {/if}
              <div class="grow"></div>
              <Toggle checked={m.enabled} label={t('content.enabledNamed', { name: m.name })} onchange={() => toggle(m)} />
            {:else}
              <span class="locked"><Icon name="lock" size={12} />{t('content.axoLocked')}</span>
            {/if}
          </div>
        </article>
      {/each}
    </div>
  {:else}
    <div class="list" style="--k: {size}">
      {#each shown as m (m.file)}
        <div class="row" class:off={!m.enabled}>
          {#if m.iconUrl}<img class="row-icon" src={m.iconUrl} alt="" />{:else}<span class="row-icon no-icon"><Icon name={m.locked ? 'sparkle' : type === 'mod' ? 'puzzle' : 'image'} size={18} /></span>{/if}
          <div class="row-text">
            <span class="row-title">
              <span class="row-name truncate">{m.name}</span>
              {#if m.version}<span class="row-ver truncate" title={m.version}>{m.version}</span>{/if}
              {#if updateFiles.has(m.file)}<span class="badge green static">{t('content.update')}</span>{/if}
            </span>
            <span class="sub truncate">{sourceLabel(m)} · {m.file} · {bytes(m.size)}</span>
          </div>
          <!-- Feste Plätze, damit Schalter und Knöpfe in jeder Zeile untereinander stehen -->
          <div class="row-actions">
            {#if m.modLocked}
              <span class="locked" title={t('modLock.rowHint')}><Icon name="lock" size={12} />{t('modLock.locked')}</span>
            {:else if !m.locked}
              {#if m.source === 'modrinth' || m.source === 'curseforge'}
                <button class="icon-btn small" aria-label={t('content.changeVersion')} title={t('content.changeVersion')} onclick={() => (dialog = { version: m })}><Icon name="swap" size={14} /></button>
              {:else}
                <span class="slot"></span>
              {/if}
              <Toggle checked={m.enabled} label={t('content.enabledNamed', { name: m.name })} onchange={() => toggle(m)} />
            {:else}
              <span class="locked"><Icon name="lock" size={12} />{t('content.axoLocked')}</span>
            {/if}
          </div>
          {#if !m.locked && !m.modLocked}
            <button class="remove" aria-label={t('content.removeNamed', { name: m.name })} title={t('common.remove')} onclick={() => remove(m)}><Icon name="x" size={12} /></button>
          {/if}
        </div>
      {/each}
    </div>
  {/if}

  <DropOverlay hint={t(`content.${key}.dropHint`)} inset={0} accept={(f) => (type === 'mod' ? /\.jar$/i : /\.zip$/i).test(f.name)} {ondrop} />
</div>

{#if dialog === 'add'}
  <AddContentDialog {instance} {type} installed={items} onclose={() => (dialog = null)} />
{:else if dialog === 'updates'}
  <UpdatesDialog instanceId={instance.id} {updates} onclose={() => (dialog = null)} onrescan={scan} />
{:else if dialog === 'repair'}
  <RepairDialog instanceId={instance.id} {issues} onclose={() => (dialog = null)} onrescan={scan} />
{:else if dialog && typeof dialog === 'object'}
  <VersionDialog {instance} {type} item={dialog.version} onclose={() => (dialog = null)} />
{/if}

<style>
  .tab {
    position: relative;
    flex-grow: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .toolbar {
    flex-shrink: 0;
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
    min-width: 0;
    font-size: 13px;
    color: var(--muted-2);
  }
  .warn-count {
    min-width: 18px;
    height: 18px;
    padding: 0 5px;
    border-radius: 9px;
    background: rgba(227, 179, 65, 0.18);
    color: var(--yellow);
    font-size: 11px;
    font-weight: 700;
    line-height: 18px;
    text-align: center;
  }
  .grid {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    margin-right: -8px;
    padding-right: 8px;
    display: grid;
    /* Regler: 150 px bis 560 px breite Kacheln */
    grid-template-columns: repeat(auto-fill, minmax(calc(150px + var(--k) * 410px), 1fr));
    grid-auto-rows: max-content;
    gap: 16px;
    align-content: start;
  }
  .card {
    position: relative;
    padding: 10px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 10px;
    transition: background-color 0.12s ease, opacity 0.12s ease;
  }
  .card:hover {
    background: #2f2f2f;
  }
  .card.off,
  .row.off {
    opacity: 0.55;
  }
  .thumb {
    position: relative;
    aspect-ratio: 16 / 9;
    border-radius: 8px;
    overflow: hidden;
    background: #1c1c1c;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .blur {
    position: absolute;
    inset: -20%;
    width: 140%;
    height: 140%;
    object-fit: cover;
    filter: blur(22px) saturate(1.3);
    opacity: 0.45;
  }
  .icon {
    position: relative;
    height: calc(100% - 36px);
    aspect-ratio: 1 / 1;
    border-radius: 18px;
    object-fit: cover;
  }
  .no-icon {
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
  }
  .badge {
    position: absolute;
    top: 8px;
    left: 8px;
    height: 22px;
    padding: 0 8px;
    border-radius: 11px;
    backdrop-filter: blur(6px);
    font-size: 11px;
    font-weight: 600;
    line-height: 22px;
  }
  .badge.static {
    position: static;
  }
  .badge.green {
    background: rgba(63, 185, 80, 0.2);
    color: #6fdc80;
  }
  .meta {
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding: 0 2px;
  }
  h3 {
    font-size: calc(13px + var(--k) * 5px);
    font-weight: 600;
    margin: 0;
  }
  .sub {
    font-size: 12px;
    color: var(--muted-2);
  }
  .actions {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 0 2px;
    min-height: 30px;
  }
  .icon-btn.small {
    width: 30px;
    height: 30px;
    color: var(--muted);
  }
  .locked {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .lock-chip {
    height: 28px;
    padding: 0 10px;
    border-radius: 14px;
    background: var(--accent-soft);
    color: var(--accent-text);
    font-size: 12px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 6px;
    white-space: nowrap;
    cursor: help;
  }
  .list {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    /* Platz für das X, das beim Hovern über die Ecke der Zeile ragt */
    margin: -8px -10px 0 0;
    padding: 8px 10px 0 0;
    display: flex;
    flex-direction: column;
    gap: calc(6px + var(--k) * 4px);
  }
  .row {
    position: relative;
    flex-shrink: 0;
    /* Regler: Symbol 32 px bis 96 px, Schrift wächst mit */
    padding: calc(6px + var(--k) * 6px) calc(10px + var(--k) * 4px);
    border-radius: 10px;
    background: var(--panel);
    display: flex;
    align-items: center;
    gap: calc(10px + var(--k) * 6px);
    transition: background-color 0.12s ease, opacity 0.12s ease;
  }
  .row:hover {
    background: #2f2f2f;
  }
  .row-icon {
    width: calc(32px + var(--k) * 64px);
    height: calc(32px + var(--k) * 64px);
    border-radius: calc(7px + var(--k) * 5px);
    object-fit: cover;
    flex-shrink: 0;
  }
  .row-text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: calc(2px + var(--k) * 3px);
  }
  .row-title {
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .row-name {
    flex-shrink: 0;
    max-width: 60%;
    font-size: calc(13.5px + var(--k) * 3.5px);
    font-weight: 600;
  }
  .row-ver {
    min-width: 0;
    font-size: calc(12px + var(--k) * 1.5px);
    color: var(--muted);
  }
  .row .sub {
    font-size: calc(11.5px + var(--k) * 1.5px);
  }
  .row-actions {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 8px;
  }
  .slot {
    width: 30px;
  }
  /* Entfernen: kleines X oben rechts, nur beim Hovern */
  .remove {
    position: absolute;
    z-index: 1;
    width: 22px;
    height: 22px;
    border-radius: 11px;
    border: none;
    padding: 0;
    background: #3a3a3a;
    color: var(--text);
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.45);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    opacity: 0;
    transform: scale(0.85);
    transition: opacity 0.12s ease, transform 0.12s ease, background-color 0.12s ease;
  }
  .row .remove {
    top: -7px;
    right: -7px;
  }
  .card .remove {
    top: 16px;
    right: 16px;
  }
  .row:hover .remove,
  .card:hover .remove,
  .remove:focus-visible {
    opacity: 1;
    transform: none;
  }
  .remove:hover {
    background: var(--red);
    color: #fff;
  }
  .size {
    -webkit-appearance: none;
    appearance: none;
    width: 110px;
    height: 4px;
    border-radius: 2px;
    outline: none;
    cursor: pointer;
    flex-shrink: 0;
  }
  .size::-webkit-slider-thumb {
    -webkit-appearance: none;
    width: 14px;
    height: 14px;
    border-radius: 7px;
    background: #fff;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.4);
  }
  .empty {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
    text-align: center;
    border-radius: 12px;
    box-shadow: inset 0 0 0 1.5px #2f2f2f;
  }
  .empty-icon {
    width: 48px;
    height: 48px;
    border-radius: 12px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .empty h2 {
    font-size: 16px;
  }
  .empty p {
    max-width: 420px;
    font-size: 13px;
    color: var(--muted);
    line-height: 1.5;
  }
</style>
