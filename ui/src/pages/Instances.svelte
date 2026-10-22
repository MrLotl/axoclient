<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import Segmented from '../lib/components/Segmented.svelte';
  import ImageBox from '../lib/components/ImageBox.svelte';
  import PlayButton from '../lib/components/PlayButton.svelte';
  import { versionLabel } from '../lib/util/format';
  import { upload } from '../lib/util/files';

  let view = $state<'grid' | 'list'>(app.ui?.views.instances ?? 'grid');
  let query = $state('');
  let dragging = $state(false);

  const list = $derived(
    app.instances.filter((i) => !query.trim() || i.name.toLowerCase().includes(query.trim().toLowerCase()))
  );

  // Import-Dialog: andere Launcher, .mrpack, Backups, ZIP-Exporte und Ordner
  function importFile() {
    app.dialog = { kind: 'import' };
  }

  // .mrpack oder Backup per Drag & Drop
  async function ondrop(e: DragEvent) {
    e.preventDefault();
    dragging = false;
    const file = e.dataTransfer?.files[0];
    if (!file || !/\.(mrpack|zip)$/i.test(file.name)) return;
    app.dialog = { kind: 'import', data: { file: await upload(file) } };
  }
</script>

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  class="page"
  ondragover={(e) => {
    e.preventDefault();
    dragging = true;
  }}
  ondragleave={() => (dragging = false)}
  {ondrop}>
  <div class="toolbar">
    <h1>{t('instances.title')}</h1>
    <span class="count-pill">{app.instances.length}</span>
    <Segmented
      bind:value={view}
      label={t('common.view')}
      options={[
        { value: 'grid', icon: 'grid', aria: t('common.gridView') },
        { value: 'list', icon: 'list', aria: t('common.listView') }
      ]}
      onchange={(v) => app.updateUiState({ instances: v })} />
    <span class="field search">
      <span style="color: #7a7a7a; display: flex"><Icon name="search" /></span>
      <input bind:value={query} placeholder={t('instances.search')} aria-label={t('instances.search')} />
    </span>
    <div class="grow"></div>
    <button class="btn btn-tool" onclick={() => app.navigate({ page: 'modpacks' })}><Icon name="download" />{t('instances.modpacks')}</button>
    <button class="btn btn-tool" onclick={importFile}><Icon name="import" />{t('instances.import')}</button>
    <button class="btn btn-tool" onclick={() => app.navigate({ page: 'instanceCreate' })}><Icon name="plus" stroke={1.8} />{t('instances.new')}</button>
  </div>

  {#if app.instances.length === 0}
    <div class="empty">
      <img src="/img/logo.svg" alt="" width="72" height="72" />
      <h2>{t('instances.emptyTitle')}</h2>
      <p>{t('instances.emptyText')}</p>
      <div class="row">
        <button class="btn btn-second" onclick={() => app.navigate({ page: 'instanceCreate' })}><Icon name="plus" />{t('instances.new')}</button>
        <button class="btn btn-second" onclick={() => app.navigate({ page: 'modpacks' })}><Icon name="download" />{t('instances.browseModpacks')}</button>
      </div>
    </div>
  {:else if view === 'grid'}
    <div class="grid">
      {#each list as i (i.id)}
        <article class="card">
          <button class="thumb" aria-label={t('instances.open', { name: i.name })} onclick={() => app.navigate({ page: 'instance', id: i.id })}>
            {#if i.hasIcon}
              <img class="blur" src={i.iconUrl} alt="" aria-hidden="true" />
              <img class="cover" src={i.iconUrl} alt="" />
            {:else}
              <span class="blur-fallback"></span>
              <span class="cover-fallback"><ImageBox name={i.name} width={96} radius={18} /></span>
            {/if}
            <span class="version">{versionLabel(i.minecraftVersion, i.loader)}</span>
            {#if i.running}<span class="live">{t('common.running')}</span>{/if}
          </button>
          <h2 class="truncate">{i.name}</h2>
          <PlayButton instanceId={i.id} height={36} compact />
        </article>
      {/each}
    </div>
  {:else}
    <div class="list">
      {#each list as i (i.id)}
        <article class="row-card">
          <button class="row-open" onclick={() => app.navigate({ page: 'instance', id: i.id })} aria-label={t('instances.open', { name: i.name })}>
            <ImageBox url={i.hasIcon ? i.iconUrl : null} name={i.name} width={54} />
            <span class="row-text">
              <span class="row-name truncate">{i.name}</span>
              <span class="row-ver">{versionLabel(i.minecraftVersion, i.loader)}{i.modCount ? ` · ${t('instances.mods', { count: i.modCount })}` : ''}</span>
            </span>
          </button>
          <div style="width: 140px"><PlayButton instanceId={i.id} height={36} compact /></div>
        </article>
      {/each}
    </div>
  {/if}

  {#if dragging}
    <div class="drop"><Icon name="import" size={28} /><span>{t('instances.dropHint')}</span></div>
  {/if}
</div>

<style>
  .page {
    position: relative;
    height: 100%;
    padding: 24px 28px;
    display: flex;
    flex-direction: column;
    gap: 20px;
  }
  .toolbar {
    display: flex;
    align-items: center;
    gap: 12px;
  }
  h1 {
    font-size: 20px;
    font-weight: 600;
  }
  .search {
    height: 36px;
    width: 220px;
    background: var(--panel);
    box-shadow: none;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
    grid-auto-rows: max-content;
    gap: 16px;
    align-content: start;
    overflow-y: auto;
    min-height: 0;
    padding-bottom: 8px;
  }
  .card {
    padding: 10px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 10px;
    transition: background-color 0.12s ease;
  }
  .card:hover {
    background: #2f2f2f;
  }
  .thumb {
    position: relative;
    display: block;
    aspect-ratio: 16 / 9;
    border-radius: 8px;
    overflow: hidden;
    background: #1a1a1a;
    border: 0;
    padding: 0;
    cursor: pointer;
  }
  .blur {
    position: absolute;
    inset: -30px;
    width: calc(100% + 60px);
    height: calc(100% + 60px);
    object-fit: cover;
    filter: blur(24px);
    opacity: 0.55;
  }
  .blur-fallback {
    position: absolute;
    inset: 0;
    background: var(--accent-soft-strong);
  }
  .cover,
  .cover-fallback {
    position: absolute;
    top: 16px;
    bottom: 34px;
    left: 50%;
    transform: translateX(-50%);
    aspect-ratio: 1 / 1;
    height: calc(100% - 50px);
    border-radius: 16px;
    object-fit: cover;
    box-shadow: 0 6px 16px rgba(0, 0, 0, 0.35);
    display: flex;
  }
  .cover-fallback :global(span) {
    width: 100% !important;
    height: 100% !important;
  }
  .version,
  .live {
    position: absolute;
    bottom: 8px;
    padding: 4px 8px;
    border-radius: 6px;
    background: rgba(0, 0, 0, 0.5);
    backdrop-filter: blur(6px);
    color: #f2f2f2;
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.2px;
  }
  .version {
    left: 8px;
  }
  .live {
    right: 8px;
    color: var(--green);
  }
  h2 {
    margin: 0 2px;
    font-size: 15px;
    font-weight: 600;
  }
  .list {
    display: flex;
    flex-direction: column;
    gap: 8px;
    overflow-y: auto;
    min-height: 0;
  }
  .row-card {
    padding: 8px 10px 8px 8px;
    border-radius: 10px;
    background: var(--panel);
    display: flex;
    align-items: center;
    gap: 14px;
  }
  .row-card:hover {
    background: #2f2f2f;
  }
  .row-open {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: 14px;
    border: 0;
    background: transparent;
    padding: 0;
    cursor: pointer;
    text-align: left;
    color: var(--text);
  }
  .row-text {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .row-name {
    font-size: 15px;
    font-weight: 600;
  }
  .row-ver {
    font-size: 12px;
    color: var(--muted);
  }
  .empty {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 12px;
    text-align: center;
  }
  .empty h2 {
    font-size: 18px;
  }
  .empty p {
    max-width: 420px;
    color: var(--muted);
    line-height: 1.5;
  }
  .drop {
    position: absolute;
    inset: 12px;
    border-radius: 16px;
    border: 2px dashed var(--accent-text);
    background: rgba(20, 20, 20, 0.85);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
    color: var(--accent-text);
    font-weight: 600;
    pointer-events: none;
  }
</style>
