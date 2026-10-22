<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call, on } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import LogView from '../lib/components/LogView.svelte';
  import Select from '../lib/components/Select.svelte';
  import { copyText } from '../lib/util/clipboard';
  import type { LogLine, ServerView } from '../lib/types';

  interface Source {
    key: string;
    kind: 'instance' | 'server';
    id: string;
    running: boolean;
    startedAt: string;
  }

  let sources = $state<Source[]>([]);
  let servers = $state<ServerView[]>([]);
  let selected = $state<string | null>(app.route.id ?? null);
  let lines = $state<LogLine[]>([]);
  let search = $state('');
  let levels = $state<string[]>(['info', 'warn', 'error']);
  let command = $state('');
  let history: string[] = [];
  let historyIndex = -1;
  let copied = $state(false);

  function clearView() {
    if (!selected) return;
    app.consoleClearedAt[selected] = lines.at(-1)?.seq ?? 0;
    lines = [];
  }

  async function loadSources() {
    try {
      sources = await call<Source[]>('console.sources');
      servers = await call<ServerView[]>('localServers.list');
      if (!selected || !sources.some((s) => s.key === selected)) selected = sources.find((s) => s.running)?.key ?? sources[0]?.key ?? null;
    } catch (e) {
      app.error(e);
    }
  }

  async function loadLines() {
    if (!selected) {
      lines = [];
      return;
    }
    try {
      const cleared = app.consoleClearedAt[selected] ?? -1;
      lines = (await call<LogLine[]>('console.lines', { key: selected, max: 5000 })).filter((l) => l.seq > cleared);
    } catch (e) {
      app.error(e);
    }
  }

  onMount(() => {
    loadSources();
    const offs = [
      on<{ key: string; line: LogLine }[]>('console.lines', (batch) => {
        const mine = batch.filter((b) => b.key === selected).map((b) => b.line);
        if (mine.length) lines = [...lines, ...mine].slice(-20000);
        if (batch.some((b) => !sources.some((s) => s.key === b.key))) loadSources();
      }),
      on('game.started', loadSources),
      on('game.exited', loadSources),
      on('servers.changed', loadSources)
    ];
    return () => offs.forEach((o) => o());
  });

  $effect(() => {
    selected;
    loadLines();
  });

  const current = $derived(sources.find((s) => s.key === selected));
  const isServer = $derived(current?.kind === 'server');

  function label(s: Source): string {
    if (s.kind === 'server') return servers.find((x) => x.id === s.id)?.name ?? s.id;
    return app.instances.find((i) => i.id === s.id)?.name ?? s.id;
  }

  async function send() {
    if (!command.trim() || !current || current.kind !== 'server') return;
    try {
      await call('console.command', { id: current.id, command });
      history = [command, ...history].slice(0, 50);
      historyIndex = -1;
      command = '';
    } catch (e) {
      app.error(e);
    }
  }

  function onkey(e: KeyboardEvent) {
    if (e.key === 'Enter') send();
    else if (e.key === 'ArrowUp' && history.length) {
      historyIndex = Math.min(history.length - 1, historyIndex + 1);
      command = history[historyIndex];
      e.preventDefault();
    } else if (e.key === 'ArrowDown') {
      historyIndex = Math.max(-1, historyIndex - 1);
      command = historyIndex >= 0 ? history[historyIndex] : '';
      e.preventDefault();
    }
  }

  async function copyLog() {
    if (!selected) return;
    try {
      await copyText(await call<string>('console.copy', { key: selected, max: 5000 }));
      copied = true;
      setTimeout(() => (copied = false), 1800);
    } catch (e) {
      app.error(e);
    }
  }

  function toggleLevel(level: string) {
    levels = levels.includes(level) ? levels.filter((l) => l !== level) : [...levels, level];
  }
</script>

<div class="page">
  <div class="toolbar">
    <h1>{t('console.title')}</h1>
    {#if sources.length}
      <Select
        bind:value={selected}
        label={t('console.source')}
        width="300px"
        options={sources.map((s) => ({ value: s.key, label: `${label(s)}`, hint: s.running ? t('common.running') : t('console.ended'), badge: s.kind === 'server' ? 'Server' : undefined }))} />
    {/if}
    <label class="field search">
      <span style="color: #8a8a8a; display: flex"><Icon name="search" /></span>
      <input type="search" bind:value={search} placeholder={t('console.search')} aria-label={t('console.search')} />
    </label>
    <div class="levels" role="group" aria-label={t('console.filter')}>
      {#each ['info', 'warn', 'error', 'debug'] as l (l)}
        <button class="lvl {l}" aria-pressed={levels.includes(l)} onclick={() => toggleLevel(l)}>{l.toUpperCase()}</button>
      {/each}
    </div>
    <div class="grow"></div>
    <button class="btn btn-tool" style="width: 36px; padding: 0" disabled={!selected || lines.length === 0} aria-label={t('console.clear')} title={t('console.clear')} onclick={clearView}><Icon name="trash" /></button>
    <button class="btn btn-tool" onclick={copyLog} disabled={!selected}><Icon name={copied ? 'check' : 'copy'} />{copied ? t('crash.logCopied') : t('crash.copyLog')}</button>
    {#if current?.kind === 'instance'}
      <button class="btn btn-tool" style="width: 36px; padding: 0" aria-label={t('console.logsFolder')} title={t('console.logsFolder')} onclick={() => call('shell.openFolder', { kind: 'instanceSub', id: current!.id, sub: 'logs' })}><Icon name="folder" /></button>
    {/if}
  </div>

  {#if sources.length === 0}
    <div class="empty">
      <span class="empty-icon"><Icon name="console" size={24} /></span>
      <h2>{t('console.emptyTitle')}</h2>
      <p>{t('console.emptyText')}</p>
    </div>
  {:else}
    <div class="log-wrap">
      <LogView {lines} {search} {levels} />
    </div>
    {#if isServer}
      <div class="command field" class:disabled={!current?.running}>
        <span class="prompt">&gt;</span>
        <input bind:value={command} onkeydown={onkey} disabled={!current?.running} placeholder={current?.running ? t('console.commandPlaceholder') : t('servers.notRunning')} spellcheck="false" aria-label={t('console.command')} />
        <button class="btn btn-second sm" disabled={!current?.running || !command.trim()} onclick={send}>{t('common.send')}</button>
      </div>
    {/if}
  {/if}
</div>

<style>
  .page {
    height: 100%;
    padding: 24px 28px;
    display: flex;
    flex-direction: column;
    gap: 16px;
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
    width: 240px;
    height: 36px;
    background: var(--panel);
    box-shadow: none;
  }
  .levels {
    padding: 3px;
    border-radius: 10px;
    background: var(--panel);
    display: flex;
    gap: 2px;
  }
  .lvl {
    height: 28px;
    padding: 0 10px;
    border: 0;
    border-radius: 7px;
    background: transparent;
    color: #6b6b6b;
    font-size: 11px;
    font-weight: 700;
    cursor: pointer;
    font-family: var(--mono);
  }
  .lvl[aria-pressed='true'] {
    background: #333;
    color: #5f9cff;
  }
  .lvl.warn[aria-pressed='true'] {
    color: var(--yellow);
  }
  .lvl.error[aria-pressed='true'] {
    color: var(--red);
  }
  .lvl.debug[aria-pressed='true'] {
    color: var(--muted);
  }
  .log-wrap {
    position: relative;
    flex-grow: 1;
    min-height: 0;
    display: flex;
  }
  .command {
    height: 44px;
    background: #191919;
    font-family: var(--mono);
  }
  .command input {
    font-family: var(--mono);
    font-size: 13px;
  }
  .command.disabled {
    opacity: 0.6;
  }
  .prompt {
    color: var(--accent-text);
    font-weight: 700;
  }
  .empty {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
    text-align: center;
  }
  .empty-icon {
    width: 52px;
    height: 52px;
    border-radius: 14px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .empty h2 {
    font-size: 17px;
  }
  .empty p {
    max-width: 420px;
    color: var(--muted);
    font-size: 13px;
    line-height: 1.5;
  }
</style>
