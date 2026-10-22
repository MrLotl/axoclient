<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import type { ContentUpdate } from '../../lib/types';

  let { instanceId, updates, onclose, onrescan }: { instanceId: string; updates: ContentUpdate[]; onclose: () => void; onrescan: () => Promise<void> | void } = $props();
  let scanning = $state(true);

  onMount(async () => {
    await onrescan();
    scanning = false;
  });

  const job = $derived(app.jobFor('content.update', instanceId));

  async function updateAll(list: ContentUpdate[]) {
    try {
      await call('content.applyUpdates', { id: instanceId, updates: list });
      onclose();
    } catch (e) {
      app.error(e);
    }
  }
</script>

<Dialog title={t('content.updatesTitle')} icon="refresh" width={560} {onclose}>
  {#if scanning}
    <div class="row" style="justify-content: center; padding: 30px 0"><span class="spinner dark"></span><span class="hint">{t('content.searchingUpdates')}</span></div>
  {:else if updates.length === 0}
    <div class="ok">
      <span class="ok-icon"><Icon name="check" size={18} stroke={2.2} /></span>
      <span class="col"><strong>{t('content.allUpToDate')}</strong><span class="hint">{t('content.allUpToDateText')}</span></span>
    </div>
  {:else}
    <div class="list">
      {#each updates as u (u.file)}
        <div class="item">
          {#if u.iconUrl}<img src={u.iconUrl} alt="" />{:else}<span class="blank"><Icon name="puzzle" /></span>{/if}
          <span class="col grow" style="min-width: 0; gap: 2px">
            <span class="name truncate">{u.name}</span>
            <span class="vers"><span class="old">{u.currentVersion ?? '?'}</span><Icon name="arrowRight" size={12} /><span class="new">{u.newVersion}</span></span>
          </span>
          <button class="btn btn-second sm" disabled={!!job} onclick={() => updateAll([u])}>{t('content.update')}</button>
        </div>
      {/each}
    </div>
  {/if}
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.close')}</button>
    {#if updates.length > 0}
      <button class="btn btn-grad" disabled={!!job} onclick={() => updateAll(updates)}><Icon name="download" size={14} stroke={1.8} />{t('content.updateAll')}</button>
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
    max-height: 400px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .item {
    padding: 8px 10px;
    border-radius: 10px;
    background: var(--panel-2);
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .item img,
  .blank {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    object-fit: cover;
    flex-shrink: 0;
  }
  .blank {
    display: flex;
    align-items: center;
    justify-content: center;
    background: #333;
    color: var(--muted-2);
  }
  .name {
    font-size: 13.5px;
    font-weight: 600;
  }
  .vers {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .new {
    color: #6fdc80;
    font-weight: 600;
  }
</style>
