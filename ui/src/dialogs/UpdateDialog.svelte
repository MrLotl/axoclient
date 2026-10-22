<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import Checkbox from '../lib/components/Checkbox.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';
  import { bytes } from '../lib/util/format';

  let { onclose }: { onclose: () => void } = $props();
  const update = $derived(app.update);
  const job = $derived(app.jobFor('launcher.update'));
  let auto = $state(app.settings?.updates.auto ?? false);

  // Release-Notes: nur Aufzählungspunkte als Text (fremdes Markdown wird nie als HTML angezeigt)
  const notes = $derived(
    (update?.notes ?? '')
      .split('\n')
      .map((l) => l.trim())
      .filter((l) => /^[-*] /.test(l))
      .map((l) => l.slice(2).replace(/\*\*|__|`/g, ''))
      .slice(0, 8)
  );

  async function install() {
    await app.saveSettings((s) => (s.updates.auto = auto));
    try {
      await call('update.apply');
    } catch (e) {
      app.error(e);
    }
  }

  async function later() {
    if (auto !== app.settings?.updates.auto) await app.saveSettings((s) => (s.updates.auto = auto));
    onclose();
  }
</script>

{#if update}
  <Dialog title={t('updates.title')} width={480} padded={false} onclose={later}>
    {#snippet header()}
      <div class="hero">
        <img src="/img/logo.svg" alt="" width="52" height="52" />
        <div class="titles">
          <h2>{t('updates.title')}</h2>
          <div class="versions">
            <span class="v old">v{app.version.split('-')[0]}</span>
            <Icon name="arrowRight" size={14} stroke={1.6} />
            <span class="v new">v{update.version}</span>
            <span class="size">· {bytes(update.sizeBytes)}</span>
          </div>
        </div>
        <button class="icon-btn flat" aria-label={t('common.close')} onclick={later}><Icon name="x" size={14} /></button>
      </div>
    {/snippet}
    <div class="body">
      {#if notes.length > 0}
        <span class="section-label">{t('updates.whatsNew')}</span>
        <ul>
          {#each notes as note, i (i)}<li><span class="bullet"></span>{note}</li>{/each}
        </ul>
      {/if}
      <button class="link" onclick={() => call('shell.openUrl', { url: 'https://github.com/MrLotl/AxoClient/releases' })}>{t('updates.allChanges')}</button>
      {#if job}
        <div class="progress">
          <div class="row"><span class="grow">{job.status ? tr(job.status) : t('updates.installing')}</span><span>{Math.round((job.progress ?? 0) * 100)} %</span></div>
          <div class="bar"><span style="width: {(job.progress ?? 0) * 100}%"></span></div>
        </div>
      {/if}
    </div>
    {#snippet footer()}
      <div class="grow"><Checkbox bind:checked={auto} label={t('updates.auto')} small /></div>
      <button class="btn btn-ghost" onclick={later}>{t('common.later')}</button>
      <button class="btn btn-grad" disabled={!!job} onclick={install}><Icon name="download" size={14} stroke={1.8} />{t('updates.installNow')}</button>
    {/snippet}
  </Dialog>
{/if}

<style>
  .hero {
    padding: 22px 22px 18px;
    background: radial-gradient(120% 140% at 0% 0%, color-mix(in srgb, var(--accent-from) 22%, transparent) 0%, color-mix(in srgb, var(--accent-to) 12%, transparent) 45%, transparent 75%);
    display: flex;
    align-items: flex-start;
    gap: 14px;
  }
  .titles {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  h2 {
    font-size: 18px;
    font-weight: 700;
    color: var(--text-strong);
  }
  .versions {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 12px;
    font-weight: 600;
    color: var(--muted-2);
  }
  .v {
    height: 22px;
    padding: 0 8px;
    border-radius: 6px;
    line-height: 22px;
  }
  .old {
    background: #333;
    color: var(--muted);
  }
  .new {
    background: var(--accent);
    color: #fff;
  }
  .size {
    font-weight: 500;
  }
  .body {
    padding: 4px 22px 18px;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  ul {
    margin: 0;
    padding: 0;
    list-style: none;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  li {
    display: flex;
    gap: 10px;
    font-size: 13px;
    color: var(--text-2);
    line-height: 1.45;
  }
  .bullet {
    width: 6px;
    height: 6px;
    margin-top: 7px;
    border-radius: 3px;
    background: var(--accent);
    flex-shrink: 0;
  }
  .link {
    align-self: flex-start;
    padding: 0;
    border: 0;
    background: transparent;
    font-size: 12.5px;
    font-weight: 500;
    color: var(--accent-text);
    cursor: pointer;
  }
  .progress {
    display: flex;
    flex-direction: column;
    gap: 6px;
    font-size: 12px;
    font-weight: 600;
    color: var(--accent-text);
  }
  .bar {
    height: 6px;
    border-radius: 3px;
    background: #333;
    overflow: hidden;
  }
  .bar span {
    display: block;
    height: 100%;
    background: var(--accent);
    transition: width 0.2s ease;
  }
</style>
