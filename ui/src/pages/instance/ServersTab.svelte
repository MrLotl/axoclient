<script lang="ts">
  import { onMount } from 'svelte';
  import { fadeSlide, fadeOut, stagger, DUR } from '../../lib/util/motion';

  const delay = stagger(30, 12, 700);
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import PingBars from '../../lib/components/PingBars.svelte';
  import type { ServerEntry, ServerStatus } from '../../lib/types';

  let { instanceId, count = $bindable(null) }: { instanceId: string; count?: number | null } = $props();
  let servers = $state<ServerEntry[]>([]);
  let status = $state<Record<string, ServerStatus | null>>({});
  let query = $state('');

  async function load() {
    try {
      servers = await call<ServerEntry[]>('serverlist.list', { id: instanceId });
      count = servers.length;
      for (const s of servers) ping(s.address);
    } catch (e) {
      app.error(e);
    }
  }

  async function ping(address: string) {
    status[address] = null;
    try {
      status[address] = await call<ServerStatus>('server.ping', { address });
    } catch {
      status[address] = { online: false, players: 0, maxPlayers: 0, motd: '', latencyMs: null, favicon: null, version: null };
    }
  }

  onMount(load);

  const shown = $derived(servers.filter((s) => !query.trim() || (s.name + s.address).toLowerCase().includes(query.trim().toLowerCase())));

  function edit(server: ServerEntry | null) {
    app.dialog = { kind: 'serverEdit', data: { instanceId, server, onSaved: load } };
  }

  function remove(s: ServerEntry) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('servers.removeTitle', { name: s.name }),
        confirm: t('common.remove'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            servers = await call<ServerEntry[]>('serverlist.remove', { id: instanceId, index: s.index });
            count = servers.length;
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }

  // Umsortieren per Drag & Drop: Die gezogene Zeile folgt der Maus, die anderen rutschen per Transform zur Seite.
  // Die Reihenfolge ändert sich erst beim Loslassen – dadurch springt beim Ziehen nichts im DOM herum.
  // Beim Filtern aus, weil dann Positionen in der sichtbaren Liste nicht denen in servers.dat entsprechen.
  let listEl: HTMLDivElement | undefined = $state();
  let drag = $state<{ from: number; target: number; startY: number; dy: number; active: boolean; mids: number[]; stride: number } | null>(null);
  let settling = $state(false);

  function dragStart(e: PointerEvent, s: ServerEntry) {
    if (query || e.button !== 0 || !listEl || (e.target as HTMLElement).closest('button, a, input')) return;
    const rows = [...listEl.querySelectorAll<HTMLElement>('.row')];
    const rects = rows.map((r) => r.getBoundingClientRect());
    const from = servers.indexOf(s);
    const gap = rects.length > 1 ? rects[1].top - rects[0].bottom : 8;
    drag = { from, target: from, startY: e.clientY, dy: 0, active: false, mids: rects.map((r) => r.top + r.height / 2), stride: rects[from].height + gap };
  }

  function dragMove(e: PointerEvent) {
    if (!drag) return;
    const dy = e.clientY - drag.startY;
    if (!drag.active) {
      if (Math.abs(dy) < 5) return;
      drag.active = true;
    }
    e.preventDefault();
    drag.dy = dy;
    const center = drag.mids[drag.from] + dy;
    let target = 0;
    drag.mids.forEach((mid, i) => {
      if (i !== drag!.from && center > mid) target++;
    });
    drag.target = target;
  }

  function offsetOf(i: number): number {
    if (!drag?.active) return 0;
    if (i === drag.from) return drag.dy;
    if (drag.from < i && i <= drag.target) return -drag.stride;
    if (drag.target <= i && i < drag.from) return drag.stride;
    return 0;
  }

  async function dragEnd() {
    if (!drag) return;
    const { from, target, active } = drag;
    // Reihenfolge lokal setzen und im selben Frame alle Verschiebungen ohne Übergang auflösen
    settling = true;
    if (active && target !== from) {
      const next = [...servers];
      const [item] = next.splice(from, 1);
      next.splice(target, 0, item);
      servers = next;
    }
    drag = null;
    requestAnimationFrame(() => requestAnimationFrame(() => (settling = false)));
    if (!active || target === from) return;
    try {
      servers = await call<ServerEntry[]>('serverlist.move', { id: instanceId, from, to: target });
    } catch (e) {
      app.error(e);
      load();
    }
  }

  async function moveBy(s: ServerEntry, delta: number) {
    const from = servers.indexOf(s);
    const to = from + delta;
    if (query || to < 0 || to >= servers.length) return;
    try {
      servers = await call<ServerEntry[]>('serverlist.move', { id: instanceId, from, to });
    } catch (e) {
      app.error(e);
    }
  }
</script>

<svelte:window onpointermove={dragMove} onpointerup={dragEnd} onpointercancel={dragEnd} />

<div class="tab">
  <div class="toolbar">
    <label class="field search">
      <span style="color: #8a8a8a; display: flex"><Icon name="search" /></span>
      <input type="search" bind:value={query} placeholder={t('servers.search')} aria-label={t('servers.search')} />
    </label>
    <span class="summary">{t('servers.count', { count: servers.length })}</span>
    <div class="grow"></div>
    <button class="btn btn-tool" onclick={() => load()}><Icon name="refresh" />{t('servers.refresh')}</button>
    <button class="btn btn-tool" onclick={() => edit(null)}><Icon name="plus" stroke={1.8} />{t('servers.add')}</button>
  </div>
  <div class="list" bind:this={listEl} class:dragging={drag?.active} class:settling>
    {#each shown as s, i (s.index)}
      {@const st = status[s.address]}
      <!-- svelte-ignore a11y_no_static_element_interactions -->
      <div
        class="row"
        in:fadeSlide|global={{ delay: delay(i), y: -6 }}
        out:fadeOut={{ duration: DUR.med }}
        class:lifted={drag?.active && drag.from === i}
        class:sortable={!query}
        style="transform: translateY({offsetOf(i)}px)"
        onpointerdown={(e) => dragStart(e, s)}>
        {#if !query}
          <button
            class="grip"
            aria-label={t('servers.reorder', { name: s.name })}
            title={t('servers.reorderHint')}
            onkeydown={(e) => {
              if (e.key === 'ArrowUp') { e.preventDefault(); moveBy(s, -1); }
              if (e.key === 'ArrowDown') { e.preventDefault(); moveBy(s, 1); }
            }}><Icon name="grip" size={14} /></button>
        {/if}
        {#if st?.favicon || s.iconUrl}<img class="icon" src={st?.favicon ?? s.iconUrl} alt="" />{:else}<span class="icon blank"><Icon name="servers" size={18} /></span>{/if}
        <div class="text">
          <span class="name truncate">{s.name}</span>
          <span class="motd truncate">{st?.online ? st.motd || s.address : s.address}</span>
        </div>
        {#if st?.online}<span class="players">{t('servers.players', { online: st.players, max: st.maxPlayers })}</span>{/if}
        <PingBars ms={st?.latencyMs ?? null} offline={st !== undefined && st !== null && !st.online} />
        <div class="actions">
          <button class="icon-btn small" aria-label={t('common.send')} title={t('common.send')} onclick={() => (app.dialog = { kind: 'share', data: { mode: 'server', server: { name: s.name, address: s.address } } })}><Icon name="send" size={14} /></button>
          <button class="icon-btn small" aria-label={t('common.edit')} title={t('common.edit')} onclick={() => edit(s)}><Icon name="edit" size={14} /></button>
          <button class="icon-btn small trash" aria-label={t('servers.removeNamed', { name: s.name })} onclick={() => remove(s)}><Icon name="trash" size={14} /></button>
          <button class="btn btn-second sm" onclick={() => app.launch(instanceId, { server: s.address })}><Icon name="play" size={12} />{t('common.join')}</button>
        </div>
      </div>
    {:else}
      <div class="empty"><Icon name="servers" size={24} /><span>{t('servers.empty')}</span></div>
    {/each}
  </div>
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
  .list {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    overflow-x: hidden;
    /* Platz für den Schatten der gezogenen Zeile, sonst schneidet der Scrollbereich ihn ab */
    margin: -8px -10px;
    padding: 8px 10px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .row {
    padding: 10px 12px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    align-items: center;
    gap: 14px;
  }
  .row.sortable {
    cursor: grab;
  }
  .list.dragging,
  .list.dragging .row {
    cursor: grabbing;
    user-select: none;
  }
  .row.lifted {
    position: relative;
    z-index: 2;
    background: #333;
    box-shadow: 0 8px 22px rgba(0, 0, 0, 0.4), inset 0 0 0 1px var(--accent-ring);
  }
  .row {
    transition: background-color 0.15s ease, box-shadow 0.15s ease, transform 0.2s cubic-bezier(0.2, 0.8, 0.2, 1);
  }
  /* Gezogene Zeile klebt ohne Verzögerung an der Maus; beim Ablegen springt alles ohne Übergang an den neuen Platz */
  .row.lifted,
  .list.settling .row {
    transition: background-color 0.15s ease, box-shadow 0.15s ease;
  }
  .grip {
    width: 18px;
    height: 32px;
    margin: 0 -6px 0 -4px;
    padding: 0;
    border: 0;
    border-radius: 6px;
    background: transparent;
    color: #5f5f5f;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: grab;
    flex-shrink: 0;
  }
  .row:hover .grip,
  .grip:focus-visible {
    color: var(--muted);
  }
  .icon {
    width: 48px;
    height: 48px;
    border-radius: 8px;
    image-rendering: pixelated;
    flex-shrink: 0;
  }
  .blank {
    display: flex;
    align-items: center;
    justify-content: center;
    background: #3a3a3a;
    color: var(--muted-2);
  }
  .text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .name {
    font-size: 14.5px;
    font-weight: 600;
  }
  .motd {
    font-size: 12.5px;
    color: var(--muted);
  }
  .players {
    font-size: 12px;
    color: var(--muted-2);
    white-space: nowrap;
  }
  .actions {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .icon-btn.small {
    width: 30px;
    height: 30px;
    color: var(--muted);
  }
  .empty {
    flex-grow: 1;
    min-height: 200px;
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
