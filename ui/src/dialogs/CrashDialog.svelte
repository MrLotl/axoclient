<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';
  import type { CrashEvent, LogLine } from '../lib/types';
  import { copyText } from '../lib/util/clipboard';

  let { crash, onclose }: { crash: CrashEvent; onclose: () => void } = $props();

  const a = $derived(crash.analysis);
  let phase = $state<'idle' | 'fixing' | 'done'>('idle');
  let showLog = $state(false);
  let log = $state<string>('');
  let copied = $state(false);

  async function loadLog() {
    showLog = !showLog;
    if (showLog && !log) {
      const lines = await call<LogLine[]>('console.lines', { key: `instance:${crash.instanceId}`, max: 300 });
      log = lines.map((l) => l.text).join('\n');
    }
  }

  async function copy() {
    try {
      const text = await call<string>('console.copy', { key: `instance:${crash.instanceId}`, max: 2000 });
      await copyText(text);
      copied = true;
      setTimeout(() => (copied = false), 2000);
    } catch (e) {
      app.error(e);
    }
  }

  async function run(action: string, file?: string | null) {
    if (action === 'updateDriver') {
      call('shell.openUrl', { url: 'https://www.google.com/search?q=Grafiktreiber+aktualisieren' });
      return;
    }
    phase = 'fixing';
    try {
      await call('crash.action', { id: crash.instanceId, action, file: file ?? a.configFile ?? a.suspects[0]?.file ?? null });
      phase = 'done';
    } catch (e) {
      phase = 'idle';
      app.error(e);
    }
  }

  const needsFile = (action: string) => action === 'disableMod' || action === 'updateMod';
</script>

<Dialog title={t('crash.title')} subtitle={crash.instanceName} icon="warning" tone="danger" role="alertdialog" width={580} {onclose}>
  {#if phase === 'done'}
    <div class="done">
      <span class="ok"><Icon name="check" size={16} stroke={2.2} /></span>
      <span class="col"><span class="strong">{t('crash.fixed')}</span><span class="muted">{t('crash.fixedText')}</span></span>
    </div>
  {:else}
    <div class="cause">
      <span class="cause-icon"><Icon name={a.knownCause ? 'wrench' : 'info'} size={18} /></span>
      <div class="col">
        <span class="section-label">{a.knownCause ? t('crash.likelyCause') : t('crash.unknownCause')}</span>
        <span class="strong">{tr(a.cause)}</span>
        {#if a.suspects.length > 0}
          <span class="muted">{t('crash.suspects')}: {a.suspects.map((s) => s.name).join(', ')}</span>
        {:else if a.recentlyAdded.length > 0}
          <span class="muted">{t('crash.recentMods')}: {a.recentlyAdded.map((s) => s.name).join(', ')}</span>
        {/if}
      </div>
    </div>
    {#if a.actions.length > 0}
      <div class="actions">
        {#each a.actions as action (action)}
          {#if needsFile(action)}
            {#each a.suspects.length ? a.suspects : a.recentlyAdded as s (s.file)}
              <button class="btn btn-second sm" disabled={phase === 'fixing'} onclick={() => run(action, s.file)}>
                {t(`crash.actions.${action}`)}: {s.name}
              </button>
            {/each}
          {:else}
            <button class="btn btn-second sm" disabled={phase === 'fixing'} onclick={() => run(action)}>{t(`crash.actions.${action}`)}</button>
          {/if}
        {/each}
      </div>
    {/if}
  {/if}

  <button class="link" aria-expanded={showLog} onclick={loadLog}>
    <span class="chev" class:open={showLog}><Icon name="chevronRight" size={12} stroke={1.8} /></span>
    {showLog ? t('crash.hideLog') : t('crash.showLog')}
  </button>
  {#if showLog}<pre class="log">{log}</pre>{/if}

  {#snippet footer()}
    <button class="btn btn-flat" onclick={copy}><Icon name="copy" size={14} />{copied ? t('crash.logCopied') : t('crash.copyLog')}</button>
    <div class="grow"></div>
    <button class="btn btn-ghost" onclick={onclose}>{t('common.close')}</button>
    {#if phase === 'fixing'}
      <button class="btn btn-grad" disabled><span class="spinner"></span>{t('crash.fixing')}</button>
    {:else}
      <button class="btn btn-grad" onclick={() => { onclose(); app.launch(crash.instanceId); }}>
        <Icon name="play" size={13} />{t('crash.playAgain')}
      </button>
    {/if}
  {/snippet}
</Dialog>

<style>
  .cause,
  .done {
    padding: 12px;
    border-radius: 12px;
    background: #242424;
    box-shadow: inset 0 0 0 1px #343434;
    display: flex;
    align-items: center;
    gap: 12px;
  }
  .done {
    background: rgba(63, 185, 80, 0.08);
    box-shadow: inset 0 0 0 1px rgba(63, 185, 80, 0.25);
  }
  .ok {
    width: 32px;
    height: 32px;
    border-radius: 16px;
    background: rgba(63, 185, 80, 0.18);
    color: var(--green);
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .cause-icon {
    width: 40px;
    height: 40px;
    border-radius: 9px;
    background: rgba(244, 112, 103, 0.12);
    color: #ff8a80;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .col {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .strong {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-strong);
    line-height: 1.4;
  }
  .muted {
    font-size: 12px;
    color: var(--muted);
  }
  .actions {
    display: flex;
    flex-wrap: wrap;
    gap: 8px;
  }
  .link {
    align-self: flex-start;
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--muted);
    font-size: 12.5px;
    font-weight: 500;
    display: flex;
    align-items: center;
    gap: 6px;
    cursor: pointer;
  }
  .link:hover {
    color: var(--accent-text);
  }
  .chev {
    display: flex;
    transition: transform 0.15s ease;
  }
  .chev.open {
    transform: rotate(90deg);
  }
  .log {
    margin: 0;
    max-height: 180px;
    overflow: auto;
    padding: 12px;
    border-radius: 10px;
    background: #1a1a1a;
    box-shadow: inset 0 0 0 1px #303030;
    color: #bdbdbd;
    font-family: var(--mono);
    font-size: 11.5px;
    line-height: 1.55;
    white-space: pre;
    user-select: text;
  }
</style>
