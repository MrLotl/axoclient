<script lang="ts">
  // Ersatz, falls der eigentliche Rückfrage-Dialog beim Anzeigen abstürzt: ohne Schnickschnack, nur die Antworten
  import { onMount } from 'svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import type { LaunchPrompt } from '../lib/types';

  let { prompt }: { prompt: LaunchPrompt } = $props();
  const hasBlocker = $derived(prompt.issues.some((i) => i.severity === 'blocker'));

  onMount(() => {
    call('launch.promptShown', { promptId: prompt.promptId }).catch(() => {});
  });

  async function resolve(choice: 'cancel' | 'startAnyway' | 'fix') {
    try {
      await call('launch.resolve', { promptId: prompt.promptId, choice, actions: {}, silence: [] });
    } catch (e) {
      app.error(e);
    }
    if (app.prompt?.promptId === prompt.promptId) app.prompt = null;
  }
</script>

<div class="backdrop">
  <div class="box" role="alertdialog" aria-modal="true">
    <strong>{hasBlocker ? t('pipeline.dialog.blockerTitle') : t('pipeline.dialog.warningTitle')}</strong>
    <p>{t('pipeline.dialog.fallback', { count: prompt.issues.length })}</p>
    <div class="buttons">
      <button class="btn btn-ghost" onclick={() => resolve('cancel')}>{t('common.cancel')}</button>
      {#if !hasBlocker}
        <button class="btn btn-second" onclick={() => resolve('startAnyway')}>{t('pipeline.dialog.startAnyway')}</button>
      {/if}
      <button class="btn btn-grad" onclick={() => resolve('fix')}>{t('pipeline.dialog.fix')}</button>
    </div>
  </div>
</div>

<style>
  .backdrop {
    position: fixed;
    inset: 0;
    z-index: 1000;
    display: grid;
    place-items: center;
    background: rgba(0, 0, 0, 0.55);
  }
  .box {
    width: min(460px, calc(100vw - 32px));
    padding: 20px;
    border-radius: 14px;
    background: var(--panel);
    box-shadow: inset 0 0 0 1px var(--line-2);
    display: flex;
    flex-direction: column;
    gap: 10px;
    color: var(--text);
  }
  p {
    margin: 0;
    color: var(--muted);
    font-size: 13px;
    line-height: 1.5;
  }
  .buttons {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    margin-top: 6px;
  }
</style>
