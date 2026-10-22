<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Dialog from '../lib/components/Dialog.svelte';
  import Checkbox from '../lib/components/Checkbox.svelte';
  import Icon from '../lib/components/Icon.svelte';

  type Mod = { name: string; source: string; projectId: string; versionId: string | null; meta: string };

  let {
    instanceId,
    friend,
    address,
    mods,
    onclose
  }: { instanceId: string; friend: string | null; address: string; title?: string; mods: Mod[]; onclose: () => void } = $props();

  // Alle fehlenden Mods sind schon ausgewählt
  let chosen = $state<Record<string, boolean>>(Object.fromEntries(mods.map((m) => [m.projectId, true])));
  let busy = $state(false);

  const instance = $derived(app.instances.find((i) => i.id === instanceId));
  const count = $derived(mods.filter((m) => chosen[m.projectId]).length);

  async function install() {
    busy = true;
    try {
      await call('ingame.installMissing', {
        instanceId,
        address,
        mods: mods.filter((m) => chosen[m.projectId]).map((m) => ({ source: m.source, projectId: m.projectId, versionId: m.versionId }))
      });
      app.toast(t('ingame.missingStarted'), 'success');
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog title={t('ingame.missingTitle', { count: mods.length })} subtitle={t('ingame.missingSubtitle', { name: friend ?? '', address })} icon="download" width={480} {onclose}>
  <p class="text">{t('ingame.missingText', { instance: instance?.name ?? '', name: friend ?? '' })}</p>
  <div class="list">
    {#each mods as m (m.projectId)}
      <div class="row">
        <Checkbox bind:checked={chosen[m.projectId]} />
        <span class="icon"><Icon name="puzzle" size={14} /></span>
        <span class="name truncate">{m.name}</span>
        <span class="meta">{m.meta}</span>
      </div>
    {/each}
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={busy || count === 0} onclick={install}>
      {#if busy}<span class="spinner"></span>{/if}{t('ingame.missingInstall', { count })}
    </button>
  {/snippet}
</Dialog>

<style>
  .text {
    margin: 0 0 14px;
    font-size: 13px;
    line-height: 1.5;
    color: var(--muted);
  }
  .list {
    max-height: 320px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .row {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 10px;
    border-radius: 9px;
    background: #242424;
    box-shadow: inset 0 0 0 1px #363636;
  }
  .icon {
    width: 28px;
    height: 28px;
    flex-shrink: 0;
    border-radius: 7px;
    background: #333;
    color: #bdbdbd;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .name {
    flex-grow: 1;
    min-width: 0;
    font-size: 13px;
    font-weight: 500;
  }
  .meta {
    font-size: 12px;
    color: var(--muted-2);
    white-space: nowrap;
  }
</style>
