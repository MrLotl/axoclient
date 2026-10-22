<script lang="ts">
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import PingBars from '../../lib/components/PingBars.svelte';
  import type { ServerEntry, ServerStatus } from '../../lib/types';
  import { fade } from 'svelte/transition';

  let { instanceId }: { instanceId: string | null } = $props();

  let servers = $state<ServerEntry[]>([]);
  let status = $state<Record<string, ServerStatus | null>>({});

  // Alte Liste bleibt stehen, bis die neue da ist – sonst klappt das Feld kurz zusammen
  let loadedFor = $state<string | null>(null);
  let loadToken = 0;
  let switching = $state(false);
  let switchTimer: ReturnType<typeof setTimeout> | undefined;
  async function load(id: string | null) {
    const token = ++loadToken;
    if (!id) {
      servers = [];
      status = {};
      loadedFor = null;
      return;
    }
    try {
      const next = await call<ServerEntry[]>('serverlist.list', { id });
      if (token !== loadToken) return;
      // Animieren nur beim Wechsel der Instanz, nicht beim Öffnen der Seite oder Neuladen
      switching = loadedFor !== null && loadedFor !== id;
      clearTimeout(switchTimer);
      if (switching) switchTimer = setTimeout(() => (switching = false), 400);
      status = {};
      servers = next;
      loadedFor = id;
      for (const s of servers) ping(s.address);
    } catch (e) {
      app.error(e);
    }
  }

  // Höhe der Liste gleitet beim Instanzwechsel auf den neuen Inhalt
  let inner: HTMLElement | undefined = $state();
  let listHeight = $state<number | null>(null);
  $effect(() => {
    if (!inner) return;
    const ro = new ResizeObserver(() => (listHeight = inner!.offsetHeight));
    ro.observe(inner);
    return () => ro.disconnect();
  });

  async function ping(address: string) {
    status[address] = null;
    try {
      status[address] = await call<ServerStatus>('server.ping', { address });
    } catch {
      status[address] = { online: false, players: 0, maxPlayers: 0, motd: '', latencyMs: null, favicon: null, version: null };
    }
  }

  $effect(() => {
    load(instanceId);
  });

  export function reload() {
    load(instanceId);
  }
</script>

<section class="panel servers" aria-label={t('servers.title')}>
  <div class="head-row">
    <h2>{t('servers.title')}</h2>
    <button
      class="icon-btn"
      aria-label={t('servers.add')}
      aria-haspopup="dialog"
      disabled={!instanceId}
      onclick={() => (app.dialog = { kind: 'serverEdit', data: { instanceId, onSaved: reload } })}>
      <Icon name="plus" />
    </button>
  </div>
  <div class="list" class:switching style:height={listHeight !== null ? `${listHeight}px` : undefined}>
    <div class="list-inner" bind:this={inner}>
    {#each servers as s (`${loadedFor}:${s.index}`)}
      {@const st = status[s.address]}
      <div class="row" in:fade={{ duration: switching ? 180 : 0 }}>
        {#if st?.favicon || s.iconUrl}
          <img class="icon" src={st?.favicon ?? s.iconUrl} alt="" />
        {:else}
          <span class="icon placeholder"><Icon name="servers" size={18} /></span>
        {/if}
        <div class="text">
          <span class="name truncate">{s.name}</span>
          <span class="meta truncate">{s.address}{st?.online ? ` · ${t('servers.players', { online: st.players.toLocaleString('de-DE'), max: st.maxPlayers.toLocaleString('de-DE') })}` : ''}</span>
        </div>
        <PingBars ms={st?.latencyMs ?? null} offline={st !== undefined && st !== null && !st.online} />
        <button class="icon-btn" aria-label={t('servers.joinNamed', { name: s.name })} title={t('common.join')} onclick={() => instanceId && app.launch(instanceId, { server: s.address })}>
          <Icon name="play" size={14} />
        </button>
      </div>
    {:else}
      <p class="empty" in:fade={{ duration: switching ? 180 : 0 }}>{t('servers.emptyHome')}</p>
    {/each}
    </div>
  </div>
</section>

<style>
  .servers {
    padding: 16px;
    display: flex;
    flex-direction: column;
    gap: 14px;
    flex: 0 1000 auto;
    min-height: 160px;
    max-height: 45%;
  }
  .head-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  h2 {
    font-size: 16px;
    font-weight: 600;
  }
  .list {
    min-height: 0;
    overflow-y: auto;
    margin-right: -6px;
    padding-right: 6px;
  }
  .list.switching {
    transition: height 0.3s cubic-bezier(0.22, 1, 0.36, 1);
  }
  .list-inner {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .row {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 10px;
    border-radius: 8px;
    background: var(--panel-3);
  }
  .icon {
    width: 40px;
    height: 40px;
    border-radius: 6px;
    flex-shrink: 0;
    image-rendering: pixelated;
  }
  .placeholder {
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
    font-size: 14px;
    font-weight: 500;
  }
  .meta {
    font-size: 12px;
    color: var(--muted);
    font-variant-numeric: tabular-nums;
  }
  .empty {
    margin: 8px 0;
    font-size: 13px;
    color: var(--muted-2);
    text-align: center;
  }
</style>
