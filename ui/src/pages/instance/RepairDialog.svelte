<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t, tr } from '../../lib/i18n';
  import type { IssueView } from '../../lib/types';

  let { instanceId, issues, onclose, onrescan }: { instanceId: string; issues: IssueView[]; onclose: () => void; onrescan: () => Promise<void> | void } = $props();
  let scanning = $state(true);
  let busy = $state(false);
  let fixed = $state<number | null>(null);

  onMount(async () => {
    await onrescan();
    scanning = false;
  });

  async function repairAll() {
    busy = true;
    try {
      fixed = await call<number>('instances.repairAll', { id: instanceId });
      await onrescan();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }

  async function redownloadAll() {
    try {
      await call('content.repair', { id: instanceId });
      onclose();
    } catch (e) {
      app.error(e);
    }
  }
</script>

<Dialog title={t('content.repairTitle')} subtitle={t('content.repairText')} icon="wrench" width={560} {onclose}>
  {#if scanning}
    <div class="row" style="justify-content: center; padding: 30px 0"><span class="spinner dark"></span></div>
  {:else if issues.length === 0}
    <div class="ok">
      <span class="ok-icon"><Icon name="check" size={18} stroke={2.2} /></span>
      <span class="col"><strong>{fixed !== null ? t('content.repaired', { count: fixed }) : t('content.noProblems')}</strong><span class="hint">{t('content.readyToPlay')}</span></span>
    </div>
  {:else}
    <div class="list">
      {#each issues as issue (issue.id)}
        <div class="issue" class:blocker={issue.severity === 'blocker'}>
          <Icon name="warning" size={15} />
          <span class="col grow" style="gap: 3px">
            <span class="msg">{tr(issue.message)}</span>
            {#if issue.actions[0]}<span class="act">{tr((issue.actions.find((a) => a.primary) ?? issue.actions[0]).label)}</span>{/if}
          </span>
        </div>
      {/each}
    </div>
  {/if}
  {#snippet footer()}
    <button class="btn btn-flat" onclick={redownloadAll}>{t('content.verifyAll')}</button>
    <div class="grow"></div>
    <button class="btn btn-ghost" onclick={onclose}>{t('common.close')}</button>
    {#if issues.length > 0}
      <button class="btn btn-grad" disabled={busy} onclick={repairAll}>
        {#if busy}<span class="spinner"></span>{:else}<Icon name="wrench" size={14} />{/if}{t('content.repairAll')}
      </button>
    {/if}
  {/snippet}
</Dialog>

<style>
  .ok {
    padding: 14px;
    border-radius: 12px;
    background: rgba(63, 185, 80, 0.08);
    box-shadow: inset 0 0 0 1px rgba(63, 185, 80, 0.25);
    display: flex;
    align-items: center;
    gap: 12px;
  }
  .ok-icon {
    width: 32px;
    height: 32px;
    border-radius: 16px;
    background: rgba(63, 185, 80, 0.18);
    color: var(--green);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .list {
    display: flex;
    flex-direction: column;
    gap: 6px;
    max-height: 380px;
    overflow-y: auto;
  }
  .issue {
    padding: 10px 12px;
    border-radius: 10px;
    background: #242424;
    box-shadow: inset 0 0 0 1px #343434;
    display: flex;
    gap: 10px;
    color: var(--yellow);
  }
  .issue.blocker {
    color: var(--red);
  }
  .msg {
    font-size: 13px;
    font-weight: 600;
    color: var(--text-strong);
    line-height: 1.4;
  }
  .act {
    font-size: 12px;
    color: var(--accent-text);
  }
</style>
