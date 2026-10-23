<script lang="ts">
  import { onMount } from 'svelte';
  import { fadeSlide, fadeLoad, stagger } from '../lib/util/motion';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import Segmented from '../lib/components/Segmented.svelte';
  import Select from '../lib/components/Select.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import { ago, compactNumber } from '../lib/util/format';
  import type { ContentProject, ContentVersion, LoaderType, SearchPage, SearchSort } from '../lib/types';

  let source = $state(app.ui?.contentSource.modpacks ?? 'modrinth');
  let view = $state<'grid' | 'list'>(app.ui?.views.modpacks ?? 'grid');
  let sort = $state<SearchSort>(app.ui?.sort.modpacks ?? 'popular');
  let loader = $state<LoaderType | 'all'>('all');
  let mc = $state<string>('all');
  let query = $state('');
  let page = $state(0);
  let result = $state<SearchPage | null>(null);
  // Jede neue Ergebnisseite kommt wieder gestaffelt herein
  const delay = $derived.by(() => {
    void result;
    return stagger(25, 12);
  });
  let loading = $state(false);
  let picking = $state<ContentProject | null>(null);
  let versions = $state<ContentVersion[]>([]);
  let chosen = $state<string | null>(null);
  let timer: ReturnType<typeof setTimeout> | undefined;

  const mcOptions = ['all', '1.21.4', '1.21.1', '1.20.1', '1.19.2', '1.18.2', '1.16.5', '1.12.2'];

  async function search(reset = true) {
    if (reset) page = 0;
    loading = true;
    try {
      result = await call<SearchPage>('content.search', {
        source,
        kind: 'modpack',
        text: query,
        minecraft: mc === 'all' ? null : mc,
        loader: loader === 'all' ? null : loader,
        category: null,
        sort: query.trim() && sort === 'popular' ? 'relevance' : sort,
        page,
        pageSize: 24
      });
    } catch (e) {
      app.error(e);
    } finally {
      loading = false;
    }
  }

  onMount(() => search());

  async function openVersions(p: ContentProject) {
    picking = p;
    versions = [];
    chosen = null;
    try {
      versions = await call<ContentVersion[]>('modpacks.versions', { source, id: p.id });
      chosen = versions[0]?.id ?? null;
    } catch (e) {
      app.error(e);
    }
  }

  async function install(p: ContentProject, versionId: string | null) {
    picking = null;
    try {
      await call('modpacks.install', { source, projectId: p.id, versionId });
      app.toast(t('modpacks.installStarted', { name: p.title }), 'success');
    } catch (e) {
      app.error(e);
    }
  }

  const installingJob = $derived(app.jobFor('modpack.install'));
  const pages = $derived(result ? Math.ceil(Math.min(result.total, 2400) / 24) : 0);
</script>

<div class="page">
  <div class="toolbar">
    <button class="icon-btn" aria-label={t('common.back')} onclick={() => app.back()}><Icon name="chevronLeft" /></button>
    <h1>{t('modpacks.title')}</h1>
    <Segmented
      bind:value={source}
      label={t('content.source')}
      options={[{ value: 'modrinth', label: 'Modrinth' }, { value: 'curseforge', label: app.curseForge ? 'CurseForge' : `CurseForge · ${t('common.comingSoon')}` }]}
      onchange={(v) => {
        if (v === 'curseforge' && !app.curseForge) {
          source = 'modrinth';
          app.toast(t('content.curseForgeSoon'));
          return;
        }
        search();
      }} />
    <div class="grow"></div>
    <Segmented bind:value={view} label={t('common.view')} options={[{ value: 'grid', icon: 'grid', aria: t('common.gridView') }, { value: 'list', icon: 'list', aria: t('common.listView') }]} onchange={(v) => app.updateUiState({ modpacks: v })} />
  </div>

  <div class="filters">
    <label class="field search">
      <span style="color: #8a8a8a; display: flex"><Icon name="search" /></span>
      <input
        type="search"
        bind:value={query}
        oninput={() => {
          clearTimeout(timer);
          timer = setTimeout(() => search(), 300);
        }}
        placeholder={t('modpacks.search')}
        aria-label={t('modpacks.search')} />
    </label>
    <Segmented
      bind:value={loader}
      label={t('versions.loader')}
      options={[
        { value: 'all', label: t('common.all') },
        { value: 'fabric', label: 'Fabric' },
        { value: 'forge', label: 'Forge' },
        { value: 'neoforge', label: 'NeoForge' },
        { value: 'quilt', label: 'Quilt' }
      ]}
      onchange={() => search()} />
    <Select bind:value={mc} label={t('versions.minecraft')} width="160px" options={mcOptions.map((v) => ({ value: v, label: v === 'all' ? t('modpacks.allVersions') : v }))} onchange={() => search()} />
    <Select
      bind:value={sort}
      label={t('content.sort')}
      width="190px"
      options={[
        { value: 'popular', label: t('content.sorts.popular') },
        { value: 'updated', label: t('content.sorts.updated') },
        { value: 'newest', label: t('content.sorts.newest') },
        { value: 'relevance', label: t('content.sorts.relevance') }
      ]}
      onchange={() => {
        app.updateUiState({ sortModpacks: sort });
        search();
      }} />
  </div>

  <div class={view === 'grid' ? 'grid' : 'list'} class:loading>
    {#if result}
      {#each result.items as p, i (p.id)}
        <article class={view === 'grid' ? 'card' : 'row-card'} in:fadeSlide|global={{ delay: delay(i), y: 10 }}>
          <div class="thumb">
            {#if p.imageUrl}<img class="banner" src={p.imageUrl} alt="" loading="lazy" use:fadeLoad={p.imageUrl} />{:else if p.iconUrl}<img class="banner blur" src={p.iconUrl} alt="" loading="lazy" use:fadeLoad={p.iconUrl} />{/if}
            {#if p.iconUrl}<img class="icon" src={p.iconUrl} alt="" loading="lazy" use:fadeLoad={p.iconUrl} />{/if}
          </div>
          <div class="info">
            <h2 class="truncate">{p.title}</h2>
            <span class="by">{t('content.by', { author: p.author })}</span>
            <p class="desc">{p.description}</p>
            <div class="meta">
              <span><Icon name="download" size={12} />{compactNumber(p.downloads)}</span>
              {#if p.updated}<span><Icon name="clock" size={12} />{ago(p.updated)}</span>{/if}
              {#each p.loaders.slice(0, 2) as l (l)}<span class="chip">{t(`loader.${l}`)}</span>{/each}
              {#if p.gameVersions.length}<span class="chip">{p.gameVersions[p.gameVersions.length - 1]}</span>{/if}
            </div>
          </div>
          <div class="actions">
            <button class="btn btn-second sm" disabled={!!installingJob} onclick={() => install(p, null)}><Icon name="download" size={13} stroke={1.8} />{t('modpacks.install')}</button>
            <button class="icon-btn" aria-label={t('modpacks.chooseVersion')} title={t('modpacks.chooseVersion')} onclick={() => openVersions(p)}><Icon name="list" size={14} /></button>
            <button class="icon-btn" aria-label={t('modpacks.onSource')} title={t('modpacks.onSource')} onclick={() => call('shell.openUrl', { url: p.webUrl })}><Icon name="external" size={14} /></button>
          </div>
        </article>
      {:else}
        <p class="none">{t('modpacks.none')}</p>
      {/each}
    {/if}
  </div>
  {#if pages > 1}
    <div class="pager">
      <button class="icon-btn" disabled={page === 0} aria-label={t('content.prevPage')} onclick={() => { page--; search(false); }}><Icon name="chevronLeft" /></button>
      <span>{t('content.page', { page: page + 1, pages })}</span>
      <button class="icon-btn" disabled={page + 1 >= pages} aria-label={t('content.nextPage')} onclick={() => { page++; search(false); }}><Icon name="chevronRight" /></button>
    </div>
  {/if}
</div>

{#if picking}
  <Dialog title={t('modpacks.chooseVersionTitle', { name: picking.title })} icon="list" width={520} onclose={() => (picking = null)}>
    <div class="versions">
      {#each versions as v, i (v.id)}
        <label class="ver" class:on={chosen === v.id}>
          <input class="sr" type="radio" name="mpv" value={v.id} bind:group={chosen} />
          <span class="col grow" style="gap: 2px; min-width: 0">
            <span class="v-name truncate">{v.versionNumber}</span>
            <span class="hint">{v.gameVersions.join(', ')} · {v.loaders.map((l) => t(`loader.${l}`)).join(', ')} · {new Date(v.published).toLocaleDateString('de-DE')}</span>
          </span>
          {#if i === 0}<span class="chip accent">{t('content.newest')}</span>{/if}
          {#if v.channel !== 'release'}<span class="chip">{v.channel}</span>{/if}
        </label>
      {:else}
        <span class="spinner dark" style="margin: 20px auto"></span>
      {/each}
    </div>
    {#snippet footer()}
      <button class="btn btn-ghost" onclick={() => (picking = null)}>{t('common.cancel')}</button>
      <button class="btn btn-grad" disabled={!chosen} onclick={() => install(picking!, chosen)}>{t('modpacks.install')}</button>
    {/snippet}
  </Dialog>
{/if}

<style>
  .page {
    height: 100%;
    padding: 24px 28px;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .toolbar,
  .filters {
    display: flex;
    align-items: center;
    gap: 12px;
  }
  h1 {
    font-size: 20px;
    font-weight: 600;
  }
  .search {
    flex-grow: 1;
    height: 38px;
    background: var(--panel);
    box-shadow: none;
  }
  .grid {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    /* Karten haben overflow: hidden – ohne max-content würden die Zeilen bei vielen Treffern zusammengedrückt */
    grid-auto-rows: max-content;
    gap: 16px;
    align-content: start;
    transition: opacity 0.15s ease;
  }
  .list {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .loading {
    opacity: 0.5;
  }
  .card {
    border-radius: 12px;
    background: var(--panel);
    overflow: hidden;
    display: flex;
    flex-direction: column;
  }
  .row-card {
    padding: 10px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    align-items: center;
    gap: 14px;
  }
  .thumb {
    position: relative;
    aspect-ratio: 16 / 7;
    background: #1c1c1c;
    overflow: hidden;
  }
  .row-card .thumb {
    width: 120px;
    aspect-ratio: 16 / 9;
    border-radius: 8px;
    flex-shrink: 0;
  }
  .banner {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: cover;
  }
  .banner.blur {
    filter: blur(22px) saturate(1.3);
    opacity: 0.5;
    transform: scale(1.4);
  }
  .icon {
    position: absolute;
    left: 12px;
    bottom: 10px;
    width: 52px;
    height: 52px;
    border-radius: 12px;
    object-fit: cover;
    box-shadow: 0 6px 16px rgba(0, 0, 0, 0.4);
  }
  .row-card .icon {
    left: 50%;
    top: 50%;
    bottom: auto;
    transform: translate(-50%, -50%);
    width: 40px;
    height: 40px;
  }
  .info {
    padding: 12px 14px 4px;
    display: flex;
    flex-direction: column;
    gap: 4px;
    flex-grow: 1;
    min-width: 0;
  }
  .row-card .info {
    padding: 0;
  }
  h2 {
    font-size: 15px;
    font-weight: 600;
  }
  .by {
    font-size: 12px;
    color: var(--muted-2);
  }
  .desc {
    font-size: 12.5px;
    color: var(--muted);
    line-height: 1.45;
    display: -webkit-box;
    -webkit-line-clamp: 2;
    line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }
  .meta {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 8px;
    margin-top: 4px;
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .meta > span:not(.chip) {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .actions {
    padding: 10px 14px 14px;
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .row-card .actions {
    padding: 0;
  }
  .actions .btn {
    flex-grow: 1;
  }
  .row-card .actions .btn {
    flex-grow: 0;
  }
  .none {
    padding: 40px 0;
    text-align: center;
    color: var(--muted-2);
  }
  .pager {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 12px;
    font-size: 12.5px;
    color: var(--muted);
  }
  .versions {
    max-height: 380px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .ver {
    position: relative;
    padding: 10px 12px;
    border-radius: 10px;
    display: flex;
    align-items: center;
    gap: 10px;
    cursor: pointer;
  }
  .ver:hover {
    background: var(--panel-3);
  }
  .ver.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .v-name {
    font-size: 13.5px;
    font-weight: 600;
  }
</style>
