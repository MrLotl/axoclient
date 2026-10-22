<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import Segmented from '../../lib/components/Segmented.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import { compactNumber, ago } from '../../lib/util/format';
  import type { ContentItem, ContentProject, ContentType, ContentVersion, Instance, JobInfo, SearchPage, SearchSort } from '../../lib/types';

  let { instance, type, installed, onclose }: { instance: Instance; type: ContentType; installed: ContentItem[]; onclose: () => void } = $props();

  const kind = $derived(type === 'mod' ? 'mod' : type === 'resourcePack' ? 'resourcePack' : 'shaderPack');
  const key = $derived(type === 'mod' ? 'mods' : type === 'resourcePack' ? 'packs' : 'shaders');

  let source = $state(app.ui?.contentSource.addMods ?? 'modrinth');
  let sort = $state<SearchSort>(app.ui?.sort.addMods ?? 'popular');
  let query = $state('');
  let category = $state<string | null>(null);
  let page = $state(0);
  let result = $state<SearchPage | null>(null);
  let loading = $state(false);
  let installing = $state<Record<string, 'busy' | 'done'>>({});
  let versionsFor = $state<string | null>(null);
  let versions = $state<ContentVersion[]>([]);
  let timer: ReturnType<typeof setTimeout> | undefined;

  const installedProjects = $derived(new Set(installed.map((i) => i.projectId).filter(Boolean)));

  const categories: Record<string, string[]> = {
    mod: ['optimization', 'utility', 'adventure', 'technology', 'magic', 'decoration', 'storage', 'worldgen', 'library', 'equipment', 'food', 'mobs'],
    resourcePack: ['vanilla-like', 'realistic', 'simplistic', 'cartoon', 'decoration', 'combat', 'gui', 'tweaks'],
    shaderPack: ['vanilla-like', 'realistic', 'fantasy', 'semi-realistic', 'cartoon', 'potato', 'low', 'medium', 'high']
  };

  async function search(reset = true) {
    if (reset) page = 0;
    loading = true;
    try {
      result = await call<SearchPage>('content.search', {
        source,
        kind,
        text: query,
        minecraft: instance.minecraft.version,
        loader: type === 'mod' ? instance.loader.type : null,
        category,
        sort: query.trim() && sort === 'popular' ? 'relevance' : sort,
        page,
        pageSize: 20
      });
    } catch (e) {
      app.error(e);
      result = { items: [], total: 0 };
    } finally {
      loading = false;
    }
  }

  onMount(() => {
    search();
    // Fertige Installationen markieren
    return on<JobInfo>('job.changed', (job) => {
      if (job.kind !== 'content.install' || job.subject !== instance.id || job.state === 'running') return;
      for (const [id, state] of Object.entries(installing)) {
        if (state !== 'busy') continue;
        if (job.state === 'succeeded') installing[id] = 'done';
        else delete installing[id];
      }
    });
  });

  function onQuery() {
    clearTimeout(timer);
    timer = setTimeout(() => search(), 300);
  }

  async function install(project: ContentProject, versionId: string | null = null) {
    installing[project.id] = 'busy';
    versionsFor = null;
    try {
      await call('content.install', { instanceId: instance.id, type, source, projectId: project.id, versionId });
    } catch (e) {
      delete installing[project.id];
      app.error(e);
    }
  }

  async function openVersions(project: ContentProject) {
    if (versionsFor === project.id) {
      versionsFor = null;
      return;
    }
    versionsFor = project.id;
    versions = [];
    try {
      versions = await call<ContentVersion[]>('content.projectVersions', { source, projectId: project.id, instanceId: instance.id, kind });
    } catch (e) {
      app.error(e);
    }
  }

  const pages = $derived(result ? Math.ceil(result.total / 20) : 0);
</script>

<Dialog title={t(`content.${key}.add`)} width={980} padded={false} {onclose}>
  {#snippet header()}
    <div class="top">
      <h2>{t(`content.${key}.add`)}</h2>
      <span class="for">{t('content.forInstance', { name: instance.name, version: instance.minecraft.version })}</span>
      <div class="grow"></div>
      <Segmented
        bind:value={source}
        label={t('content.source')}
        options={[
          { value: 'modrinth', label: 'Modrinth' },
          { value: 'curseforge', label: app.curseForge ? 'CurseForge' : `CurseForge · ${t('common.comingSoon')}` }
        ]}
        onchange={(v) => {
          if (v === 'curseforge' && !app.curseForge) {
            source = 'modrinth';
            app.toast(t('content.curseForgeSoon'));
            return;
          }
          search();
        }} />
      <button class="icon-btn flat" aria-label={t('common.close')} onclick={onclose}><Icon name="x" size={14} /></button>
    </div>
  {/snippet}
  <div class="body">
    <aside class="filters">
      <span class="section-label">{t('content.categories')}</span>
      <div class="cats" role="radiogroup" aria-label={t('content.categories')}>
        <button class="cat" class:on={category === null} onclick={() => { category = null; search(); }}>{t('common.all')}</button>
        {#each categories[kind] ?? [] as c (c)}
          <button class="cat" class:on={category === c} onclick={() => { category = c; search(); }}>{t(`categories.${c}`)}</button>
        {/each}
      </div>
    </aside>
    <div class="results">
      <div class="search-row">
        <label class="field">
          <span style="color: #8a8a8a; display: flex"><Icon name="search" /></span>
          <input bind:value={query} oninput={onQuery} placeholder={t(`content.${key}.search`)} data-autofocus />
        </label>
        <select class="select" bind:value={sort} onchange={() => { app.updateUiState({ sortAddMods: sort }); search(); }} aria-label={t('content.sort')}>
          <option value="popular">{t('content.sorts.popular')}</option>
          <option value="relevance">{t('content.sorts.relevance')}</option>
          <option value="newest">{t('content.sorts.newest')}</option>
          <option value="updated">{t('content.sorts.updated')}</option>
        </select>
      </div>
      <div class="list" class:loading>
        {#if result}
          {#each result.items as p (p.id)}
            {@const state = installedProjects.has(p.id) ? 'done' : installing[p.id]}
            <article class="item">
              {#if p.iconUrl}<img class="p-icon" src={p.iconUrl} alt="" loading="lazy" />{:else}<span class="p-icon blank"><Icon name="puzzle" /></span>{/if}
              <div class="p-text">
                <div class="p-title"><span class="truncate">{p.title}</span>{#if p.author}<span class="by">{t('content.by', { author: p.author })}</span>{/if}</div>
                <p class="desc">{p.description}</p>
                <div class="p-meta">
                  <span><Icon name="download" size={12} />{compactNumber(p.downloads)}</span>
                  {#if p.updated}<span><Icon name="clock" size={12} />{ago(p.updated)}</span>{/if}
                  {#each p.categories.slice(0, 3) as c (c)}<span class="chip">{t(`categories.${c}`)}</span>{/each}
                  <button class="link" onclick={() => call('shell.openUrl', { url: p.webUrl })}><Icon name="external" size={12} />{t('content.details')}</button>
                </div>
              </div>
              <div class="p-actions">
                {#if state === 'done'}
                  <span class="installed"><Icon name="check" size={13} />{installedProjects.has(p.id) ? t('content.installed') : t('content.added')}</span>
                {:else if state === 'busy'}
                  <button class="btn btn-grad sm" disabled><span class="spinner"></span>{t('content.installingShort')}</button>
                {:else}
                  <div class="split">
                    <button class="btn btn-grad sm" onclick={() => install(p)}><Icon name="download" size={13} stroke={1.8} />{t('content.install')}</button>
                    <button class="btn btn-grad sm more" aria-label={t('content.versions')} aria-expanded={versionsFor === p.id} onclick={() => openVersions(p)}><Icon name="chevronDown" size={13} /></button>
                  </div>
                  {#if versionsFor === p.id}
                    <div class="menu versions" role="menu">
                      {#each versions.slice(0, 12) as v (v.id)}
                        <button class="menu-item" role="menuitem" onclick={() => install(p, v.id)}>
                          <span class="truncate grow">{v.versionNumber}</span>
                          {#if v.channel !== 'release'}<span class="chip">{v.channel === 'beta' ? 'Beta' : 'Alpha'}</span>{/if}
                          {#if v === versions[0]}<span class="chip accent">{t('content.newest')}</span>{/if}
                        </button>
                      {:else}
                        <span class="hint" style="padding: 10px">{t('common.loading')}</span>
                      {/each}
                    </div>
                  {/if}
                {/if}
              </div>
            </article>
          {:else}
            <p class="none">{t('content.nothingFound')}</p>
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
  </div>
</Dialog>

<style>
  .top {
    padding: 18px 20px 14px;
    display: flex;
    align-items: center;
    gap: 12px;
  }
  h2 {
    font-size: 17px;
    font-weight: 600;
    color: var(--text-strong);
  }
  .for {
    font-size: 13px;
    color: var(--muted-2);
  }
  .body {
    height: 600px;
    display: flex;
    border-top: 1px solid var(--line);
  }
  .filters {
    width: 200px;
    flex-shrink: 0;
    padding: 16px;
    border-right: 1px solid var(--line);
    display: flex;
    flex-direction: column;
    gap: 10px;
    overflow-y: auto;
  }
  .cats {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .cat {
    height: 32px;
    padding: 0 10px;
    border: 0;
    border-radius: 8px;
    background: transparent;
    color: var(--muted);
    font-size: 13px;
    text-align: left;
    cursor: pointer;
  }
  .cat:hover {
    background: var(--panel-3);
    color: var(--text);
  }
  .cat.on {
    background: var(--accent-soft);
    color: var(--accent-text);
    font-weight: 600;
  }
  .results {
    flex-grow: 1;
    min-width: 0;
    padding: 16px 16px 12px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .search-row {
    display: flex;
    gap: 10px;
  }
  .search-row .field {
    flex-grow: 1;
    height: 38px;
  }
  .select {
    height: 38px;
    padding: 0 10px;
    border: 0;
    border-radius: 9px;
    background: var(--field);
    color: var(--text);
    box-shadow: inset 0 0 0 1px var(--line-2);
  }
  .list {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 8px;
    transition: opacity 0.15s ease;
  }
  .list.loading {
    opacity: 0.5;
  }
  .item {
    padding: 12px;
    border-radius: 12px;
    background: var(--panel-2);
    display: flex;
    gap: 12px;
    align-items: flex-start;
  }
  .p-icon {
    width: 56px;
    height: 56px;
    border-radius: 12px;
    object-fit: cover;
    flex-shrink: 0;
  }
  .p-icon.blank {
    display: flex;
    align-items: center;
    justify-content: center;
    background: #333;
    color: var(--muted-2);
  }
  .p-text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .p-title {
    display: flex;
    align-items: baseline;
    gap: 8px;
    font-size: 14.5px;
    font-weight: 600;
    color: var(--text-strong);
    min-width: 0;
  }
  .by {
    font-size: 12px;
    font-weight: 400;
    color: var(--muted-2);
    white-space: nowrap;
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
  .p-meta {
    display: flex;
    align-items: center;
    flex-wrap: wrap;
    gap: 10px;
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .p-meta > span:not(.chip) {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .link {
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--muted-2);
    font-size: 11.5px;
    display: flex;
    align-items: center;
    gap: 4px;
    cursor: pointer;
  }
  .link:hover {
    color: var(--accent-text);
  }
  .p-actions {
    position: relative;
    flex-shrink: 0;
  }
  /* Ein durchgehender Verlauf über beide Teile, getrennt nur durch eine feine Linie */
  .split {
    display: flex;
    border-radius: 8px;
    background: var(--accent);
    box-shadow: var(--accent-shadow);
    overflow: hidden;
  }
  .split .btn {
    background: transparent;
    box-shadow: none;
    border-radius: 0;
    filter: none;
  }
  .split .btn:hover {
    background: rgba(255, 255, 255, 0.1);
  }
  .split .more {
    width: 28px;
    padding: 0;
    border-left: 1px solid rgba(255, 255, 255, 0.22);
  }
  .versions {
    position: absolute;
    right: 0;
    top: 38px;
    z-index: 10;
    width: 260px;
    max-height: 320px;
    overflow-y: auto;
  }
  .installed {
    height: 32px;
    padding: 0 12px;
    border-radius: 8px;
    background: var(--green-bg);
    color: var(--green);
    font-size: 12.5px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .none {
    padding: 40px 0;
    text-align: center;
    color: var(--muted-2);
    font-size: 13px;
  }
  .pager {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 12px;
    font-size: 12.5px;
    color: var(--muted);
  }
</style>
