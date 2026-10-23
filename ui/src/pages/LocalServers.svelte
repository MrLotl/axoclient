<script lang="ts">
  import { onMount } from 'svelte';
  import { flip } from 'svelte/animate';
  import { fadeSlide, pop, stagger, reducedMotion, DUR } from '../lib/util/motion';

  const delay = stagger(30, 12, 700);
  const flipMs = reducedMotion() ? 0 : DUR.slow;
  import { app } from '../lib/stores/app.svelte';
  import { call, on } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import ServerDialog from './servers/ServerDialog.svelte';
  import ImageBox from '../lib/components/ImageBox.svelte';
  import { duration, megabytes } from '../lib/util/format';
  import { copyText } from '../lib/util/clipboard';
  import type { ServerImportDraft, ServerView } from '../lib/types';

  let servers = $state<ServerView[]>([]);
  let editing = $state<ServerView | null | 'new'>(null);
  let importDraft = $state<ServerImportDraft | null>(null);
  let copied = $state<string | null>(null);
  let tick = $state(0);

  async function load() {
    try {
      servers = await call<ServerView[]>('localServers.list');
    } catch (e) {
      app.error(e);
    }
  }

  onMount(() => {
    load();
    const off = on('servers.changed', load);
    // Laufzeit und RAM regelmäßig auffrischen
    const timer = setInterval(() => {
      tick++;
      if (servers.some((s) => s.running)) load();
    }, 5000);
    return () => {
      off();
      clearInterval(timer);
    };
  });

  const running = $derived(servers.filter((s) => s.running).length);
  const startJob = (id: string) => app.jobFor('server.start', id);

  async function toggle(s: ServerView) {
    try {
      if (s.running) await call('localServers.stop', { id: s.id });
      else {
        if (!s.eulaAccepted && s.software !== 'velocity') {
          editing = s;
          app.toast(t('servers.eulaMissing'), 'error');
          return;
        }
        await call('localServers.start', { id: s.id });
      }
    } catch (e) {
      app.error(e);
    }
  }

  async function importServer() {
    try {
      const draft = await call<ServerImportDraft | null>('localServers.inspectFolder');
      if (draft) {
        importDraft = draft;
        editing = 'new';
      }
    } catch (e) {
      app.error(e);
    }
  }

  // Bild direkt auf Icon oder Banner einer Karte ziehen; ImageBox schneidet zu
  async function setImage(id: string, banner: boolean, base64: string | null) {
    try {
      await call('localServers.setImage', { id, banner, base64 });
      await load();
    } catch (e) {
      app.error(e);
    }
  }

  function create() {
    importDraft = null;
    editing = 'new';
  }

  async function copy(address: string) {
    await copyText(address);
    copied = address;
    setTimeout(() => (copied = null), 1500);
  }

  function remove(s: ServerView) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('localServers.deleteTitle', { name: s.name }),
        text: t('localServers.deleteText'),
        confirm: t('common.delete'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            await call('localServers.delete', { id: s.id });
            await load();
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }

  function uptime(s: ServerView) {
    tick;
    return s.startedAt ? duration((Date.now() - Date.parse(s.startedAt)) / 1000) : '–';
  }

  function statusOf(s: ServerView) {
    if (s.running) return { text: t('common.running'), cls: 'on' };
    if (startJob(s.id)) return { text: t('servers.starting'), cls: 'busy' };
    return { text: t('common.stopped'), cls: '' };
  }

  const softwareName: Record<string, string> = { vanilla: 'Vanilla', paper: 'Paper', fabric: 'Fabric', velocity: 'Velocity' };
</script>

<div class="page">
  <div class="toolbar">
    <h1>{t('localServers.title')}</h1>
    <span class="count-pill">{servers.length}</span>
    <span class="running" class:on={running > 0}><span class="dot"></span>{running === 0 ? t('localServers.noneRunning') : t('localServers.running', { count: running })}</span>
    <div class="grow"></div>
    <button class="btn btn-tool" style="width: 36px; padding: 0" aria-label={t('localServers.openRoot')} title={t('localServers.openRoot')} onclick={() => call('shell.openFolder', { kind: 'servers' })}><Icon name="folder" /></button>
    <button class="btn btn-tool" onclick={importServer}><Icon name="import" />{t('localServers.import.button')}</button>
    <button class="btn btn-tool" onclick={create}><Icon name="plus" stroke={1.8} />{t('localServers.create')}</button>
  </div>

  <div class="grid scroll">
    {#each servers as s, i (s.id)}
      {@const st = statusOf(s)}
      {@const job = startJob(s.id)}
      <article class="server" class:running={s.running} animate:flip={{ duration: flipMs }} in:fadeSlide|global={{ delay: delay(i), y: 10 }} out:pop={{ scale: 0.95, duration: DUR.med }}>
        <div class="cover">
          <span class="cover-img">
            <ImageBox url={s.bannerUrl} width="100%" height="100%" radius={0} droppable aspect={4} outputWidth={1200}
              cropTitle={t('localServers.banner')} onchange={(b64) => setImage(s.id, true, b64)} />
          </span>
          <span class="icon">
            <ImageBox url={s.iconUrl} fallbackIcon="servers" width={64} radius={14} pixelated droppable outputWidth={64}
              cropTitle={t('localServers.icon')} onchange={(b64) => setImage(s.id, false, b64)} />
          </span>
        </div>

        <div class="body">
          <div class="head">
            <div class="col grow" style="gap: 2px; min-width: 0">
              <h2 class="truncate">{s.name}<span class="ver"> · {softwareName[s.software]} {s.version}</span></h2>
              {#if s.modCount !== null}<span class="sub truncate">{t('instances.mods', { count: s.modCount })}</span>{/if}
            </div>
            <span class="status {st.cls}"><span class="dot"></span>{st.text}</span>
          </div>

          <div class="tiles">
            <button class="tile addr" onclick={() => copy(`localhost:${s.port}`)} title={t('localServers.copyAddress', { address: `localhost:${s.port}` })}>
              <span class="tile-head"><Icon name="globe" size={12} />{t('localServers.tiles.address')}<Icon name={copied === `localhost:${s.port}` ? 'check' : 'copy'} size={11} /></span>
              <span class="tile-value mono">localhost:{s.port}</span>
            </button>
            <div class="tile">
              <span class="tile-head"><Icon name="user" size={12} />{t('localServers.tiles.players')}</span>
              <span class="tile-value">{s.running ? `${s.onlinePlayers} / ${s.maxPlayers}` : `– / ${s.maxPlayers}`}</span>
            </div>
            <div class="tile">
              <span class="tile-head"><Icon name="memory" size={12} />{t('localServers.tiles.ramShort')}</span>
              <span class="tile-value">{s.running && s.memoryUsedMb ? `${megabytes(s.memoryUsedMb)} / ${megabytes(s.memoryMb)}` : megabytes(s.memoryMb)}</span>
              <div class="ram"><span style="width: {s.running && s.memoryUsedMb ? Math.min(100, (s.memoryUsedMb / s.memoryMb) * 100) : 0}%"></span></div>
            </div>
            <div class="tile">
              <span class="tile-head"><Icon name="clock" size={12} />{s.running || !s.totalUptimeSeconds ? t('localServers.tiles.uptime') : t('localServers.tiles.uptimeTotal')}</span>
              <span class="tile-value">{s.running ? uptime(s) : s.totalUptimeSeconds ? duration(s.totalUptimeSeconds) : '–'}</span>
            </div>
          </div>

          {#if job}
            <div class="progress">
              <span class="truncate">{job.status ? tr(job.status) : t('servers.starting')}</span>
              <div class="bar"><span style="width: {job.progress === null ? 30 : Math.max(3, job.progress * 100)}%" class:indeterminate={job.progress === null}></span></div>
            </div>
          {:else if s.publicAddress}
            <button class="addr-line" onclick={() => copy(s.publicAddress!)}><Icon name={copied === s.publicAddress ? 'check' : 'globe'} size={12} />{t('localServers.internet', { address: s.publicAddress })}</button>
          {:else if s.internetProblem}
            <span class="problem"><Icon name="warning" size={12} />{tr(s.internetProblem)}</span>
          {/if}
          {#if s.players.length}<div class="players">{#each s.players as p (p)}<span class="chip">{p}</span>{/each}</div>{/if}

          <div class="actions">
            <button class="btn {s.running ? 'btn-ghost' : 'btn-second'} sm" disabled={!!job} onclick={() => toggle(s)}>
              {#if job}<span class="spinner dark"></span>{:else}<Icon name={s.running ? 'stop' : 'play'} size={12} />{/if}
              {s.running ? t('common.stop') : t('common.start')}
            </button>
            {#if s.running && s.software !== 'velocity'}
              <button class="btn btn-flat sm" onclick={() => app.joinAddress(`localhost:${s.port}`)}><Icon name="play" size={12} />{t('common.join')}</button>
            {/if}
            <div class="grow"></div>
            <button class="icon-btn small" aria-label={t('localServers.logs', { name: s.name })} title={t('localServers.logsShort')} onclick={() => app.navigate({ page: 'console', id: `server:${s.id}` })}><Icon name="terminal" size={14} /></button>
            <button class="icon-btn small" aria-label={t('localServers.folder', { name: s.name })} title={t('common.openFolder')} onclick={() => call('shell.openFolder', { kind: 'server', id: s.id })}><Icon name="folder" size={14} /></button>
            <button class="icon-btn small" aria-label={t('localServers.edit', { name: s.name })} title={t('common.edit')} onclick={() => (editing = s)}><Icon name="settings" size={14} /></button>
            <button class="icon-btn small trash" aria-label={t('localServers.delete', { name: s.name })} disabled={s.running} onclick={() => remove(s)}><Icon name="trash" size={14} /></button>
          </div>
        </div>
      </article>
    {/each}

    <button class="add" onclick={create}>
      <span class="add-icon"><Icon name="plus" size={20} /></span>
      <strong>{servers.length ? t('localServers.addTitle') : t('localServers.emptyTitle')}</strong>
      <span class="hint">{servers.length ? t('localServers.addText') : t('localServers.emptyText')}</span>
    </button>
  </div>
</div>

{#if editing}
  <ServerDialog
    server={editing === 'new' ? null : editing}
    draft={editing === 'new' ? importDraft : null}
    onclose={() => {
      editing = null;
      importDraft = null;
    }}
    onsaved={load} />
{/if}


<style>
  .page {
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
  .running {
    display: flex;
    align-items: center;
    gap: 7px;
    font-size: 13px;
    color: var(--muted-2);
  }
  .running .dot,
  .status .dot {
    width: 7px;
    height: 7px;
    border-radius: 4px;
    background: #555;
  }
  .running.on .dot,
  .status.on .dot {
    background: var(--green);
  }
  .scroll {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    align-content: start;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(560px, 1fr));
    grid-auto-rows: max-content;
    gap: 14px;
  }
  .server {
    border-radius: 14px;
    background: var(--panel);
    overflow: hidden;
    display: flex;
    flex-direction: column;
  }
  .server.running {
    box-shadow: inset 0 0 0 1px rgba(63, 185, 80, 0.3);
  }

  /* Banner oben (oder Akzentverlauf), Icon ragt hinein */
  .cover {
    position: relative;
    flex-shrink: 0;
    height: 116px;
    margin-bottom: 26px;
    background:
      radial-gradient(120% 140% at 0% 0%, color-mix(in srgb, var(--accent-from) 18%, transparent), transparent 60%),
      radial-gradient(120% 140% at 100% 100%, color-mix(in srgb, var(--accent-to) 14%, transparent), transparent 60%),
      #2a2a2a;
  }
  .cover-img {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
  }
  .icon {
    position: absolute;
    left: 18px;
    bottom: -26px;
    width: 64px;
    height: 64px;
    border-radius: 14px;
    overflow: hidden;
    background: #333;
    color: var(--muted-2);
    display: flex;
    align-items: center;
    justify-content: center;
    box-shadow: 0 0 0 4px var(--panel), 0 8px 18px rgba(0, 0, 0, 0.35);
  }

  .body {
    flex-grow: 1;
    min-width: 0;
    padding: 0 18px 16px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .grid .head {
    padding-left: 76px;
    margin-top: -22px;
  }
  .head {
    display: flex;
    align-items: center;
    gap: 12px;
    min-height: 44px;
  }
  h2 {
    font-size: 16px;
    font-weight: 600;
  }
  .ver {
    font-size: 13px;
    font-weight: 500;
    color: var(--muted-2);
  }
  .sub {
    font-size: 12.5px;
    color: var(--muted-2);
  }
  .status {
    height: 24px;
    padding: 0 10px;
    border-radius: 12px;
    background: #333;
    color: var(--muted);
    font-size: 11.5px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
  }
  .status.on {
    background: var(--green-bg);
    color: var(--green);
  }
  .status.busy {
    background: var(--accent-soft);
    color: var(--accent-text);
  }
  .status.busy .dot {
    background: var(--accent-text);
    animation: axo-pulse 1s ease-in-out infinite;
  }

  .tiles {
    display: grid;
    /* Adresse ist der längste Wert */
    grid-template-columns: minmax(0, 1.4fr) repeat(3, minmax(0, 1fr));
    gap: 8px;
  }
  /* Kleine Karten für Adresse, Spieler, RAM und Laufzeit */
  .tile {
    min-height: 70px;
    padding: 11px 13px;
    border: 0;
    border-radius: 10px;
    background: #2a2a2a;
    box-shadow: inset 0 0 0 1px rgba(255, 255, 255, 0.04);
    color: var(--text);
    display: flex;
    flex-direction: column;
    justify-content: space-between;
    gap: 7px;
    text-align: left;
    min-width: 0;
  }
  .tile.addr {
    cursor: pointer;
    transition: background-color 0.12s ease;
  }
  .tile.addr:hover {
    background: #313131;
  }
  .tile-head {
    font-size: 11.5px;
    font-weight: 500;
    color: var(--muted-2);
    display: flex;
    align-items: center;
    gap: 6px;
    white-space: nowrap;
    overflow: hidden;
  }
  .tile.addr .tile-head :global(svg:last-child) {
    margin-left: auto;
    opacity: 0.7;
  }
  .tile-value {
    font-size: 15px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 6px;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .mono {
    font-family: var(--mono);
    font-size: 13px;
  }
  .ram {
    height: 5px;
    border-radius: 3px;
    background: #3a3a3a;
    overflow: hidden;
  }
  .ram span {
    display: block;
    height: 100%;
    background: var(--accent);
    transition: width 0.4s ease;
  }

  .progress {
    display: flex;
    flex-direction: column;
    gap: 6px;
    font-size: 12px;
    color: var(--accent-text);
  }
  .bar {
    height: 4px;
    border-radius: 2px;
    background: #3a3a3a;
    overflow: hidden;
  }
  .bar span {
    display: block;
    height: 100%;
    background: var(--accent);
    transition: width 0.25s ease;
  }
  .bar span.indeterminate {
    animation: axo-indeterminate 1.2s ease-in-out infinite;
  }
  .addr-line {
    align-self: flex-start;
    padding: 0;
    border: 0;
    background: none;
    color: var(--green);
    font-size: 12px;
    display: flex;
    align-items: center;
    gap: 6px;
    cursor: pointer;
  }
  .problem {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
    color: var(--yellow);
  }
  .players {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
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

  .add {
    min-height: 200px;
    padding: 24px;
    border: 0;
    border-radius: 14px;
    background: transparent;
    box-shadow: inset 0 0 0 1.5px #343434;
    color: var(--text);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 8px;
    text-align: center;
    cursor: pointer;
    transition: box-shadow 0.15s ease, background-color 0.15s ease;
  }
  .add:hover {
    background: rgba(255, 255, 255, 0.02);
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .add-icon {
    width: 40px;
    height: 40px;
    border-radius: 10px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .add strong {
    font-size: 14px;
  }
  .add .hint {
    max-width: 340px;
  }
</style>
