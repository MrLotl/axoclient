<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import DropOverlay from '../lib/components/DropOverlay.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import Segmented from '../lib/components/Segmented.svelte';
  import Select from '../lib/components/Select.svelte';
  import { loaderName, ago, duration } from '../lib/util/format';
  import { backdrop } from '../lib/util/art';
  import { orderInstances, moveId } from '../lib/util/order';
  import { upload } from '../lib/util/files';
  import { flip } from 'svelte/animate';
  import { fadeOnly } from '../lib/util/motion';
  import { fadeSlide, reducedMotion, pop, DUR } from '../lib/util/motion';
  import type { InstanceSort, InstanceSummary } from '../lib/types';

  const flipMs = reducedMotion() ? 0 : 220;

  let view = $state<'grid' | 'list'>(app.ui?.views.instances ?? 'grid');
  let size = $state(app.ui?.views.instancesSize ?? 0.5);
  let sort = $state<InstanceSort>(app.ui?.sort.instances ?? 'recent');
  let filter = $state<string>('all');
  let query = $state('');
  let menu = $state<'new' | 'view' | null>(null);

  const favorites = $derived(new Set(app.ui?.favorites ?? []));
  // Nur Loader als Filter anbieten, die es in den eigenen Instanzen gibt
  const loaders = $derived([...new Set(app.instances.map((i) => i.loader))].sort());

  // Ziehen zum Sortieren: solange gezogen wird, gilt diese Reihenfolge (die Karten weichen per flip aus)
  let dragId = $state<string | null>(null);
  let liveOrder = $state<string[] | null>(null);
  // Nur die ungefilterte Liste lässt sich umsortieren, sonst wären Positionen nicht eindeutig
  const canDrag = $derived(filter === 'all' && !query.trim());

  const ordered = $derived(orderInstances(app.instances, app.ui, sort));
  const list = $derived.by(() => {
    const q = query.trim().toLowerCase();
    let base = ordered;
    if (liveOrder) {
      const pos = new Map(liveOrder.map((id, i) => [id, i]));
      base = [...ordered].sort((a, b) => (pos.get(a.id) ?? 0) - (pos.get(b.id) ?? 0));
    }
    return base
      .filter((i) => filter === 'all' || (filter === 'favorites' ? favorites.has(i.id) : i.loader === filter))
      .filter((i) => !q || (i.name + ' ' + i.minecraftVersion + ' ' + loaderName(i.loader)).toLowerCase().includes(q));
  });

  // Die zuletzt gespielten Instanzen stehen als größere Kacheln über der Liste (nur ungefiltert, Kachelansicht)
  const recent = $derived(
    view === 'grid' && canDrag && app.instances.length >= 4
      ? app.instances
          .filter((i) => i.lastPlayed)
          .sort((a, b) => String(b.lastPlayed).localeCompare(String(a.lastPlayed)))
          .slice(0, 2)
      : []
  );

  // Kachelbreite bzw. Zeilenhöhe folgen dem Regler (0 = klein, 1 = groß)
  const tileMin = $derived(Math.round(190 + size * 190));
  const rowHeight = $derived(Math.round(48 + size * 44));

  let saveTimer: ReturnType<typeof setTimeout> | undefined;
  function onSize() {
    clearTimeout(saveTimer);
    saveTimer = setTimeout(() => app.updateUiState({ instancesSize: size }), 300);
  }

  function setSort(value: InstanceSort) {
    sort = value;
    if (app.ui) app.ui.sort.instances = value;
    app.updateUiState({ sortInstances: value });
  }

  function toggleFavorite(id: string) {
    if (app.ui) {
      // Sofort umschalten, die Antwort von C# bestätigt nur
      const next = new Set(app.ui.favorites ?? []);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      app.ui.favorites = [...next];
    }
    app.updateUiState({ favorite: id });
  }

  function ondragstart(e: DragEvent, id: string) {
    if (!canDrag) return;
    dragId = id;
    liveOrder = list.map((i) => i.id);
    if (e.dataTransfer) {
      e.dataTransfer.effectAllowed = 'move';
      e.dataTransfer.setData('text/plain', id);
    }
  }

  function ondragover(e: DragEvent, target: string) {
    if (!dragId || !liveOrder) return;
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'move';
    if (target === dragId) return;
    const box = (e.currentTarget as HTMLElement).getBoundingClientRect();
    const after = view === 'grid' ? e.clientX > box.left + box.width / 2 : e.clientY > box.top + box.height / 2;
    const next = moveId(liveOrder, dragId, target, after);
    if (next.join() !== liveOrder.join()) liveOrder = next;
  }

  function finishDrag() {
    if (!dragId || !liveOrder) return;
    const order = liveOrder;
    const changed = order.join() !== ordered.map((i) => i.id).join();
    if (changed) {
      if (app.ui) {
        app.ui.instanceOrder = order;
        app.ui.sort.instances = 'custom';
      }
      sort = 'custom';
      app.updateUiState({ sortInstances: 'custom', order });
    }
    dragId = null;
    liveOrder = null;
  }

  function art(i: InstanceSummary) {
    return backdrop(i, app.settings?.appearance.versionPanorama ?? true);
  }

  function badge(i: InstanceSummary) {
    if (app.jobFor('launch', i.id)) return t('instances.starting');
    if (i.running || app.running.includes(i.id)) return t('common.running');
    return i.lastPlayed ? ago(i.lastPlayed) : t('instances.never');
  }

  async function playOrStop(i: InstanceSummary) {
    if (app.running.includes(i.id)) {
      try {
        await call('instances.stop', { id: i.id });
      } catch (e) {
        app.error(e);
      }
      return;
    }
    if (app.ui) app.ui.lastInstance = i.id;
    app.launch(i.id);
  }

  function open(id: string) {
    if (!dragId) app.navigate({ page: 'instance', id });
  }

  function importFile() {
    app.dialog = { kind: 'import' };
  }

  async function ondrop(files: File[]) {
    app.dialog = { kind: 'import', data: { file: await upload(files[0]!) } };
  }

  const columns: { key: InstanceSort | null; label: string }[] = [
    { key: 'name', label: 'instances.col.name' },
    { key: null, label: 'instances.col.version' },
    { key: null, label: 'instances.col.loader' },
    { key: 'mods', label: 'instances.col.mods' },
    { key: 'playtime', label: 'instances.col.playtime' },
    { key: 'recent', label: 'instances.col.last' }
  ];
</script>

{#snippet tileBody(i: InstanceSummary)}
  <span class="badge" class:live={app.running.includes(i.id)}>{badge(i)}</span>
  <button
    class="fav"
    class:on={favorites.has(i.id)}
    aria-pressed={favorites.has(i.id)}
    aria-label={favorites.has(i.id) ? t('instances.unfavorite', { name: i.name }) : t('instances.favorite', { name: i.name })}
    onclick={(e) => { e.stopPropagation(); toggleFavorite(i.id); }}>
    <Icon name={favorites.has(i.id) ? 'starFill' : 'star'} size={15} />
  </button>
  <span class="info">
    {#if i.hasIcon}<img class="ico" src={i.iconUrl} alt="" draggable="false" />{/if}
    <span class="txt">
      <b class="name">{i.name}</b>
      <span class="tags">
        <span class="tag">{loaderName(i.loader)}</span>
        <span class="tag">{i.minecraftVersion}</span>
        {#if i.modCount}<small>{t('instances.mods', { count: i.modCount })}{i.totalSeconds ? ' · ' + duration(i.totalSeconds) : ''}</small>{/if}
      </span>
    </span>
    <button
      class="pl"
      class:stop={app.running.includes(i.id)}
      aria-label={app.running.includes(i.id) ? t('home.stopGame') : t('common.play')}
      onclick={(e) => { e.stopPropagation(); playOrStop(i); }}>
      <Icon name={app.running.includes(i.id) ? 'stop' : 'play'} size={15} />
    </button>
  </span>
{/snippet}

<svelte:window onmousedown={(e) => { if (menu && !(e.target as HTMLElement).closest('.pw')) menu = null; }} />

<div class="page">
  {#if app.instances.length === 0}
    <div class="empty" in:fadeSlide={{ y: 0, duration: DUR.slow }}>
      <img src="/img/logo.svg" alt="" width="72" height="72" />
      <h2 class="display">{t('instances.emptyTitle')}</h2>
      <p>{t('instances.emptyText')}</p>
      <div class="row">
        <button class="btn btn-grad" onclick={() => app.navigate({ page: 'instanceCreate' })}><Icon name="plus" />{t('instances.new')}</button>
        <button class="btn btn-second" onclick={() => app.navigate({ page: 'modpacks' })}><Icon name="download" />{t('instances.browseModpacks')}</button>
      </div>
    </div>
  {:else}
    <div class="toolbar">
      <label class="find">
        <Icon name="search" size={14} />
        <input bind:value={query} placeholder={t('instances.search')} aria-label={t('instances.search')} />
      </label>
      <div class="filters" role="group" aria-label={t('common.view')}>
        <button class:on={filter === 'all'} onclick={() => (filter = 'all')}>{t('instances.all')} {app.instances.length}</button>
        <button class:on={filter === 'favorites'} onclick={() => (filter = 'favorites')}>{t('instances.favorites')}</button>
        {#each loaders as l (l)}
          <button class:on={filter === l} onclick={() => (filter = l)}>{loaderName(l)}</button>
        {/each}
      </div>
      <div class="grow"></div>
      <Select
        value={sort}
        label={t('instances.sortLabel')}
        width="150px"
        options={(['custom', 'recent', 'name', 'playtime', 'mods'] as const).map((k) => ({ value: k, label: t(`instances.sort.${k}`) }))}
        onchange={(v) => setSort(v as InstanceSort)} />
      <div class="pw">
        <button class="btn btn-tool" aria-haspopup="menu" aria-expanded={menu === 'view'} aria-label={t('common.view')} onclick={() => (menu = menu === 'view' ? null : 'view')}>
          <Icon name={view === 'grid' ? 'grid' : 'list'} size={16} /><Icon name="chevronDown" size={14} />
        </button>
        {#if menu === 'view'}
          <div class="menu popup" in:pop={{ origin: 'top right', scale: 0.96, y: -6, duration: DUR.med }}>
            <Segmented
              bind:value={view}
              label={t('common.view')}
              options={[
                { value: 'grid', icon: 'grid', aria: t('common.gridView') },
                { value: 'list', icon: 'list', aria: t('common.listView') }
              ]}
              onchange={(v) => app.updateUiState({ instances: v })} />
            <label class="sizer" title={view === 'grid' ? t('instances.gridSize') : t('instances.listSize')}>
              <i class="a"></i>
              <input type="range" min="0" max="1" step="0.01" bind:value={size} oninput={onSize} aria-label={view === 'grid' ? t('instances.gridSize') : t('instances.listSize')} />
              <i class="b"></i>
            </label>
          </div>
        {/if}
      </div>
      <div class="pw">
        <button class="btn btn-grad" aria-haspopup="menu" aria-expanded={menu === 'new'} onclick={() => (menu = menu === 'new' ? null : 'new')}>
          <Icon name="plus" stroke={2} />{t('instances.new')}<Icon name="chevronDown" size={14} />
        </button>
        {#if menu === 'new'}
          <div class="menu popup items" role="menu" in:pop={{ origin: 'top right', scale: 0.96, y: -6, duration: DUR.med }}>
            <button role="menuitem" onclick={() => { menu = null; app.navigate({ page: 'instanceCreate' }); }}><Icon name="plus" size={15} />{t('instances.createNew')}</button>
            <button role="menuitem" onclick={() => { menu = null; importFile(); }}><Icon name="import" size={15} />{t('instances.import')}</button>
            <button role="menuitem" onclick={() => { menu = null; app.navigate({ page: 'modpacks' }); }}><Icon name="download" size={15} />{t('instances.browseModpacks')}</button>
          </div>
        {/if}
      </div>
    </div>

    {#if recent.length}
      <h2 class="sec">{t('instances.recentTitle')}</h2>
      <div class="hero" in:fadeOnly={{ duration: 160 }}>
        {#each recent as i (i.id)}
          <div
            class="card"
            role="button"
            tabindex="0"
            aria-label={t('instances.open', { name: i.name })}
            style="background-image: {art(i)}"
            onclick={() => open(i.id)}
            onkeydown={(e) => e.key === 'Enter' && open(i.id)}>
            {@render tileBody(i)}
          </div>
        {/each}
      </div>
      <h2 class="sec">{t('instances.allTitle')}</h2>
    {/if}

    {#if list.length === 0}
      <p class="none">{query.trim() ? t('instances.noMatch', { query: query.trim() }) : filter === 'favorites' ? t('instances.noFavorites') : t('instances.noLoader')}</p>
    {:else if view === 'grid'}
      <div class="grid" style="--min: {tileMin}px; --k: {size}" in:fadeOnly={{ duration: 160 }}>
        {#each list as i (i.id)}
          <div
            class="card"
            class:dragging={dragId === i.id}
            class:draggable={canDrag}
            role="button"
            tabindex="0"
            draggable={canDrag}
            aria-label={t('instances.open', { name: i.name })}
            style="background-image: {art(i)}"
            onclick={() => open(i.id)}
            onkeydown={(e) => e.key === 'Enter' && open(i.id)}
            ondragstart={(e) => ondragstart(e, i.id)}
            ondragover={(e) => ondragover(e, i.id)}
            ondrop={(e) => { e.preventDefault(); finishDrag(); }}
            ondragend={finishDrag}
            animate:flip={{ duration: flipMs }}>
            {@render tileBody(i)}
          </div>
        {/each}
      </div>
    {:else}
      <section class="list" style="--rh: {rowHeight}px; --k: {size}" in:fadeOnly={{ duration: 160 }}>
        <div class="lrow hd">
          <span></span><span></span>
          {#each columns as c (c.label)}
            {#if c.key}
              <button class:on={sort === c.key} onclick={() => setSort(c.key!)}>{t(c.label)}{sort === c.key ? ' ↓' : ''}</button>
            {:else}
              <span>{t(c.label)}</span>
            {/if}
          {/each}
          <span></span>
        </div>
        <div class="rows">
          {#each list as i (i.id)}
            <div
              class="lrow"
              class:dragging={dragId === i.id}
              class:draggable={canDrag}
              role="button"
              tabindex="0"
              draggable={canDrag}
              aria-label={t('instances.open', { name: i.name })}
              onclick={() => open(i.id)}
              onkeydown={(e) => e.key === 'Enter' && open(i.id)}
              ondragstart={(e) => ondragstart(e, i.id)}
              ondragover={(e) => ondragover(e, i.id)}
              ondrop={(e) => { e.preventDefault(); finishDrag(); }}
              ondragend={finishDrag}
              animate:flip={{ duration: flipMs }}>
              <button
                class="fav"
                class:on={favorites.has(i.id)}
                aria-pressed={favorites.has(i.id)}
                aria-label={favorites.has(i.id) ? t('instances.unfavorite', { name: i.name }) : t('instances.favorite', { name: i.name })}
                onclick={(e) => { e.stopPropagation(); toggleFavorite(i.id); }}>
                <Icon name={favorites.has(i.id) ? 'starFill' : 'star'} size={15} />
              </button>
              <span class="thumb" style="background-image: {art(i)}">
                {#if i.hasIcon}<img src={i.iconUrl} alt="" draggable="false" />{/if}
              </span>
              <span class="nm"><b class="truncate">{i.name}</b><small class="truncate">{badge(i)}</small></span>
              <span class="c m">{i.minecraftVersion}</span>
              <span class="c">{loaderName(i.loader)}</span>
              <span class="c m">{i.modCount || '–'}</span>
              <span class="c m">{i.totalSeconds ? duration(i.totalSeconds) : '–'}</span>
              <span class="c">{i.lastPlayed ? ago(i.lastPlayed) : t('instances.never')}</span>
              <span class="ac">
                <button
                  class="icon-btn"
                  class:stop={app.running.includes(i.id)}
                  aria-label={app.running.includes(i.id) ? t('home.stopGame') : t('common.play')}
                  onclick={(e) => { e.stopPropagation(); playOrStop(i); }}>
                  <Icon name={app.running.includes(i.id) ? 'stop' : 'play'} size={14} />
                </button>
              </span>
            </div>
          {/each}
        </div>
      </section>
    {/if}
  {/if}

  <DropOverlay icon="import" hint={t('instances.dropHint')} accept={(f) => /\.(mrpack|zip)$/i.test(f.name)} {ondrop} />
</div>

<style>
  .page {
    position: relative;
    height: 100%;
    padding: 10px 32px 20px;
    display: flex;
    flex-direction: column;
    gap: 18px;
  }
  .sec {
    margin: 0 0 -8px;
    font-size: 11.5px;
    font-weight: 600;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: var(--muted);
  }
  .hero {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 16px;
  }
  .hero .card {
    aspect-ratio: 21 / 9;
  }
  .pw {
    position: relative;
    flex-shrink: 0;
  }
  .pw .menu {
    position: absolute;
    top: calc(100% + 6px);
    right: 0;
    z-index: 20;
    min-width: 190px;
    padding: 10px;
    display: grid;
    gap: 10px;
  }
  .pw .menu.items {
    padding: 6px;
    gap: 2px;
  }
  .pw .menu.items button {
    display: flex;
    align-items: center;
    gap: 10px;
    height: 36px;
    padding: 0 10px;
    border: 0;
    border-radius: var(--radius-s);
    background: transparent;
    color: var(--text);
    font-size: 13.5px;
    font-weight: 600;
    text-align: left;
    cursor: pointer;
  }
  .pw .menu.items button:hover {
    background: rgba(255, 255, 255, 0.08);
  }
  .tags {
    display: flex;
    align-items: center;
    gap: 6px;
    min-width: 0;
    overflow: hidden;
    white-space: nowrap;
  }
  .tag {
    font-size: 11px;
    font-weight: 600;
    padding: 2px 7px;
    border-radius: 6px;
    background: rgba(255, 255, 255, 0.14);
    color: var(--text);
  }
  .toolbar {
    display: flex;
    align-items: center;
    gap: 14px;
    white-space: nowrap;
  }
  .find {
    height: 36px;
    width: 200px;
    flex-shrink: 0;
    padding: 0 12px;
    border-radius: var(--radius-m);
    background: rgba(255, 255, 255, 0.07);
    color: var(--muted);
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .find:focus-within {
    background: rgba(255, 255, 255, 0.11);
    box-shadow: inset 0 0 0 1px var(--line-2);
  }
  .find input {
    flex: 1;
    min-width: 0;
    border: 0;
    outline: none;
    background: transparent;
    color: var(--text-strong);
    font: 500 13.5px var(--font);
  }
  .filters {
    display: flex;
    gap: 14px;
    min-width: 0;
    overflow: hidden;
  }
  .filters button {
    padding: 0;
    border: 0;
    background: transparent;
    font-family: var(--display);
    font-weight: 600;
    font-size: 14px;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--faint);
    cursor: pointer;
  }
  .filters button:hover {
    color: var(--text);
  }
  .filters button.on {
    color: var(--text-strong);
  }
  .sizer {
    display: flex;
    align-items: center;
    gap: 8px;
    color: var(--muted);
    flex-shrink: 0;
  }
  .sizer i {
    display: block;
    border: 1.5px solid currentColor;
    border-radius: 2px;
  }
  .sizer i.a {
    width: 8px;
    height: 8px;
  }
  .sizer i.b {
    width: 14px;
    height: 14px;
  }
  .sizer input {
    width: 76px;
    accent-color: var(--accent-light);
  }
  .none {
    color: var(--muted);
    padding: 40px 0;
    text-align: center;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(var(--min), 1fr));
    grid-auto-rows: max-content;
    gap: calc(12px + var(--k) * 10px);
    align-content: start;
    overflow-y: auto;
    overflow-x: hidden;
    min-height: 0;
    padding: 6px 6px 10px;
    margin: -6px -6px 0;
  }
  .card {
    position: relative;
    aspect-ratio: 16 / 9;
    border-radius: var(--radius-m);
    overflow: hidden;
    background-size: cover;
    background-position: center;
    box-shadow: 0 10px 26px rgba(0, 0, 0, 0.5);
    cursor: pointer;
    transition: transform var(--dur-med) var(--ease-out), box-shadow var(--dur-med) ease;
  }
  .card.draggable {
    cursor: grab;
  }
  .card.dragging,
  .lrow.dragging {
    opacity: 0.45;
  }
  .card::after {
    content: '';
    position: absolute;
    inset: 0;
    background: linear-gradient(0deg, rgba(8, 9, 11, 0.92) 0%, rgba(8, 9, 11, 0.2) 55%, transparent 75%);
  }
  .card:hover {
    transform: translateY(-4px);
    box-shadow: 0 0 0 2px var(--text-strong), 0 16px 30px rgba(0, 0, 0, 0.6);
  }
  .badge {
    position: absolute;
    top: 10px;
    left: 10px;
    z-index: 1;
    font-size: 11.5px;
    font-weight: 700;
    color: var(--text-strong);
    background: rgba(10, 11, 13, 0.65);
    padding: 3px 9px;
    border-radius: var(--radius-s);
    backdrop-filter: blur(6px);
  }
  .badge.live {
    background: var(--green);
    color: #0b130e;
  }
  .fav {
    width: 30px;
    height: 30px;
    padding: 0;
    border: 0;
    border-radius: 50%;
    display: grid;
    place-items: center;
    background: transparent;
    color: var(--faint);
    cursor: pointer;
    flex-shrink: 0;
  }
  .card .fav {
    position: absolute;
    top: 8px;
    right: 8px;
    z-index: 2;
    background: rgba(10, 11, 13, 0.55);
    color: rgba(255, 255, 255, 0.85);
    opacity: 0;
    transition: opacity var(--dur-fast) ease;
  }
  .card:hover .fav,
  .card:focus-within .fav,
  .fav.on {
    opacity: 1;
  }
  .fav:hover {
    color: var(--text-strong);
  }
  .fav.on {
    color: var(--yellow) !important;
  }
  .info {
    position: absolute;
    left: 14px;
    right: 14px;
    bottom: 12px;
    z-index: 1;
    display: flex;
    align-items: flex-end;
    gap: 10px;
  }
  .txt {
    flex: 1;
    min-width: 0;
    display: grid;
    gap: 3px;
  }
  .name {
    font-family: var(--display);
    font-weight: 700;
    font-size: calc(17px + var(--k) * 11px);
    line-height: 1;
    text-transform: uppercase;
    color: var(--text-strong);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .txt small {
    font-size: 12px;
    color: var(--muted);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .pl {
    width: 38px;
    height: 38px;
    padding: 0;
    border: 0;
    border-radius: 50%;
    display: grid;
    place-items: center;
    background: var(--text-strong);
    color: #0d0d0f;
    cursor: pointer;
    flex-shrink: 0;
    opacity: 0;
    transform: scale(0.9);
    transition: opacity var(--dur-fast) ease, transform var(--dur-fast) ease;
  }
  .pl.stop {
    background: var(--green);
    opacity: 1;
    transform: none;
  }
  .card:hover .pl,
  .card:focus-within .pl {
    opacity: 1;
    transform: none;
  }
  .list {
    min-height: 0;
    display: flex;
    flex-direction: column;
    border-radius: var(--radius-l);
    background: var(--glass);
    border: 1px solid var(--line);
    overflow: hidden;
  }
  .rows {
    overflow-y: auto;
    min-height: 0;
  }
  .lrow {
    --th: calc(48px + var(--k) * 80px);
    display: grid;
    grid-template-columns: 30px var(--th) minmax(0, 1fr) 70px 90px 56px 90px 110px 44px;
    gap: 14px;
    align-items: center;
    height: var(--rh);
    padding: 0 14px;
    border-bottom: 1px solid var(--line);
    cursor: pointer;
  }
  .rows .lrow:last-child {
    border-bottom: 0;
  }
  .lrow:not(.hd):hover {
    background: rgba(255, 255, 255, 0.05);
  }
  .lrow.hd {
    height: 40px;
    cursor: default;
    font-family: var(--display);
    font-weight: 600;
    font-size: 13px;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--faint);
    white-space: nowrap;
  }
  .lrow.hd button {
    padding: 0;
    border: 0;
    background: transparent;
    text-align: left;
    color: inherit;
    font: inherit;
    letter-spacing: inherit;
    text-transform: inherit;
    cursor: pointer;
  }
  .lrow.hd button:hover,
  .lrow.hd button.on {
    color: var(--text-strong);
  }
  .thumb {
    position: relative;
    display: block;
    width: var(--th);
    aspect-ratio: 16 / 9;
    max-width: 100%;
    border-radius: 4px;
    background-size: cover;
    background-position: center;
  }
  .thumb img {
    position: absolute;
    left: 4px;
    bottom: 4px;
    width: 40%;
    max-width: 30px;
    aspect-ratio: 1;
    border-radius: 4px;
    object-fit: cover;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.5);
  }
  .lrow.draggable {
    cursor: grab;
  }
  .ico {
    width: calc(30px + var(--k) * 14px);
    height: calc(30px + var(--k) * 14px);
    border-radius: var(--radius-s);
    object-fit: cover;
    flex-shrink: 0;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.5);
  }
  .nm {
    min-width: 0;
    display: grid;
    gap: 2px;
  }
  .nm b {
    font-weight: 600;
    font-size: calc(14px + var(--k) * 10px);
    color: var(--text-strong);
  }
  .nm small {
    font-size: 12px;
    color: var(--muted);
  }
  .c {
    font-size: 13px;
    color: var(--muted);
    white-space: nowrap;
  }
  .c.m {
    font-family: var(--mono);
    font-size: 12.5px;
  }
  .ac {
    display: flex;
    justify-content: flex-end;
  }
  .ac .icon-btn.stop {
    background: var(--green);
    color: #0b130e;
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
    font-size: 32px;
  }
  .empty p {
    max-width: 420px;
    color: var(--muted);
    line-height: 1.5;
  }
  @media (max-width: 1240px) {
    .filters {
      display: none;
    }
  }
</style>
