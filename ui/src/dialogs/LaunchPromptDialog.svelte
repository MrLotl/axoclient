<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import Checkbox from '../lib/components/Checkbox.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';
  import type { LaunchPrompt } from '../lib/types';

  let { prompt }: { prompt: LaunchPrompt } = $props();

  const hasBlocker = $derived(prompt.issues.some((i) => i.severity === 'blocker'));
  const instanceName = $derived(app.instances.find((i) => i.id === prompt.instanceId)?.name ?? '');
  let chosen = $state<Record<string, string>>({});
  let silence = $state<Record<string, boolean>>({});

  $effect(() => {
    for (const issue of prompt.issues) {
      if (!(issue.id in chosen) && issue.actions.length > 0)
        chosen[issue.id] = (issue.actions.find((a) => a.primary) ?? issue.actions[0]).id;
    }
  });

  async function resolve(choice: 'cancel' | 'startAnyway' | 'fix') {
    try {
      await call('launch.resolve', {
        promptId: prompt.promptId,
        choice,
        actions: $state.snapshot(chosen),
        silence: Object.entries(silence)
          .filter(([, v]) => v)
          .map(([k]) => k)
      });
    } catch (e) {
      app.error(e);
    }
    // Der nächste Dialog kann schon vor der Antwort eingetroffen sein: nur den eigenen schließen
    if (app.prompt?.promptId === prompt.promptId) app.prompt = null;
  }
</script>

<Dialog
  title={hasBlocker ? t('pipeline.dialog.blockerTitle') : t('pipeline.dialog.warningTitle')}
  subtitle={instanceName}
  icon="warning"
  tone={hasBlocker ? 'danger' : 'accent'}
  role="alertdialog"
  width={560}
  onclose={() => resolve('cancel')}>
  <div class="issues">
    {#each prompt.issues as issue (issue.id)}
      <div class="issue" class:blocker={issue.severity === 'blocker'}>
        <span class="sev">{issue.severity === 'blocker' ? t('pipeline.dialog.blocker') : t('pipeline.dialog.warning')}</span>
        <p class="msg">{tr(issue.message)}</p>
        {#if issue.detail}<p class="detail">{tr(issue.detail)}</p>{/if}
        {#if issue.actions.length > 1}
          <div class="actions" role="radiogroup" aria-label={tr(issue.message)}>
            {#each issue.actions as action (action.id)}
              <label class="opt" class:on={chosen[issue.id] === action.id}>
                <input class="sr" type="radio" name={issue.id} value={action.id} bind:group={chosen[issue.id]} />
                <span class="radio"></span>{tr(action.label)}
              </label>
            {/each}
          </div>
        {:else if issue.actions.length === 1}
          <span class="single"><Icon name="wrench" size={13} />{tr(issue.actions[0].label)}</span>
        {/if}
        {#if issue.canSilence}
          <Checkbox bind:checked={silence[issue.id]} label={t('pipeline.dialog.silence')} small />
        {/if}
      </div>
    {/each}
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={() => resolve('cancel')}>{t('common.cancel')}</button>
    {#if !hasBlocker}
      <button class="btn btn-second" onclick={() => resolve('startAnyway')}>{t('pipeline.dialog.startAnyway')}</button>
    {/if}
    <button class="btn btn-grad" onclick={() => resolve('fix')}>
      <Icon name="wrench" size={14} stroke={1.6} />{hasBlocker ? t('pipeline.dialog.fix') : t('pipeline.dialog.fixAndStart')}
    </button>
  {/snippet}
</Dialog>

<style>
  .issues {
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .issue {
    padding: 12px 14px;
    border-radius: 12px;
    background: #242424;
    box-shadow: inset 0 0 0 1px #343434;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .issue.blocker {
    box-shadow: inset 0 0 0 1px rgba(244, 112, 103, 0.3);
  }
  .sev {
    align-self: flex-start;
    height: 20px;
    padding: 0 7px;
    border-radius: 6px;
    background: rgba(227, 179, 65, 0.14);
    color: var(--yellow);
    font-size: 10.5px;
    font-weight: 700;
    line-height: 20px;
    text-transform: uppercase;
    letter-spacing: 0.4px;
  }
  .blocker .sev {
    background: rgba(244, 112, 103, 0.14);
    color: var(--red);
  }
  .msg {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-strong);
    line-height: 1.45;
  }
  .detail {
    font-size: 12px;
    color: var(--muted);
  }
  .actions {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
  }
  .opt {
    position: relative;
    height: 30px;
    padding: 0 10px;
    border-radius: 8px;
    background: #2e2e2e;
    color: var(--text-2);
    font-size: 12.5px;
    display: flex;
    align-items: center;
    gap: 8px;
    cursor: pointer;
  }
  .opt.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
    color: var(--accent-text);
  }
  .radio {
    width: 12px;
    height: 12px;
    border-radius: 6px;
    box-shadow: inset 0 0 0 1.5px #6a6a6a;
  }
  .opt.on .radio {
    background: var(--accent);
    box-shadow: none;
  }
  .single {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12.5px;
    color: var(--accent-text);
  }
</style>
