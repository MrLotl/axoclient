<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import Checkbox from '../../lib/components/Checkbox.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import type { ContentItem, ContentType, ContentVersion, Instance } from '../../lib/types';

  let { instance, type, item, onclose }: { instance: Instance; type: ContentType; item: ContentItem; onclose: () => void } = $props();

  let versions = $state<ContentVersion[]>([]);
  let all = $state(false);
  let loading = $state(true);
  let selected = $state<string | null>(item.versionId);

  async function load() {
    loading = true;
    try {
      versions = await call<ContentVersion[]>('content.versionsFor', { id: instance.id, type, file: item.file, all });
    } catch (e) {
      app.error(e);
    } finally {
      loading = false;
    }
  }

  onMount(load);

  async function apply() {
    if (!selected || selected === item.versionId || !item.projectId) return;
    try {
      await call('content.install', { instanceId: instance.id, type, source: item.source, projectId: item.projectId, versionId: selected });
      onclose();
    } catch (e) {
      app.error(e);
    }
  }
</script>

<Dialog title={t('content.changeVersionTitle', { name: item.name })} subtitle={t('content.currentVersion', { version: item.version ?? '?' })} icon="refresh" width={520} {onclose}>
  <Checkbox bind:checked={all} label={t('content.allMcVersions')} small onchange={load} />
  <div class="list" role="radiogroup" aria-label={t('content.versions')}>
    {#if loading}
      <span class="spinner dark" style="margin: 20px auto"></span>
    {:else}
      {#each versions as v (v.id)}
        <label class="ver" class:on={selected === v.id}>
          <input class="sr" type="radio" name="ver" value={v.id} bind:group={selected} />
          <span class="radio"></span>
          <span class="col grow" style="min-width: 0; gap: 2px">
            <span class="name truncate">{v.versionNumber}</span>
            <span class="meta truncate">{v.gameVersions.slice(0, 4).join(', ')} · {new Date(v.published).toLocaleDateString('de-DE')}</span>
          </span>
          {#if v.channel !== 'release'}<span class="chip">{v.channel === 'beta' ? 'Beta' : 'Alpha'}</span>{/if}
          {#if v.id === item.versionId}<span class="chip accent">{t('content.installed')}</span>{/if}
        </label>
      {:else}
        <p class="hint" style="padding: 20px; text-align: center">{t('content.noVersions')}</p>
      {/each}
    {/if}
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={!selected || selected === item.versionId} onclick={apply}>{t('content.useVersion')}</button>
  {/snippet}
</Dialog>

<style>
  .list {
    max-height: 380px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .ver {
    position: relative;
    padding: 10px 12px;
    border-radius: 10px;
    display: flex;
    align-items: center;
    gap: 10px;
    cursor: pointer;
  }
  .ver:hover {
    background: var(--panel-3);
  }
  .ver.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .radio {
    width: 14px;
    height: 14px;
    border-radius: 7px;
    box-shadow: inset 0 0 0 1.5px #6a6a6a;
    flex-shrink: 0;
  }
  .ver.on .radio {
    background: var(--accent);
    box-shadow: inset 0 0 0 3px var(--panel);
  }
  .name {
    font-size: 13.5px;
    font-weight: 600;
  }
  .meta {
    font-size: 11.5px;
    color: var(--muted-2);
  }
</style>
