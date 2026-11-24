<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import Toggle from '../../lib/components/Toggle.svelte';
  import AddContentDialog from './AddContentDialog.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import { bytes } from '../../lib/util/format';
  import type { DatapackItem, Instance, JobInfo, WorldInfo } from '../../lib/types';

  let { instance, world, onclose }: { instance: Instance; world: WorldInfo; onclose: () => void } = $props();

  let items = $state<DatapackItem[]>([]);
  let loaded = $state(false);
  // Suche läuft im großen Hinzufügen-Dialog; solange ist diese Liste ausgeblendet
  let adding = $state(false);

  async function load() {
    try {
      items = await call<DatapackItem[]>('datapacks.list', { id: instance.id, folder: world.folder });
    } catch (e) {
      app.error(e);
    } finally {
      loaded = true;
    }
  }

  onMount(() => {
    load();
    return on<JobInfo>('job.changed', (job) => {
      if (job.kind === 'datapack.install' && job.subject === instance.id && job.state === 'succeeded') load();
    });
  });

  async function toggle(item: DatapackItem) {
    try {
      items = await call<DatapackItem[]>('datapacks.setEnabled', { id: instance.id, world: world.folder, file: item.file, enabled: !item.enabled });
    } catch (e) {
      app.error(e);
    }
  }

  function remove(item: DatapackItem) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('datapacks.removeTitle', { name: item.name }),
        text: t('datapacks.removeText'),
        confirm: t('common.remove'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            items = await call<DatapackItem[]>('datapacks.remove', { id: instance.id, world: world.folder, file: item.file });
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }
</script>

{#if adding}
  <AddContentDialog {instance} type="dataPack" world={world.folder} installed={items} onclose={() => { adding = false; load(); }} />
{:else}
  <Dialog title={t('datapacks.manage', { name: world.name })} subtitle={instance.minecraft.version ?? ''} icon="puzzle" width={560} {onclose}>
    {#if !loaded}
      <span class="spinner dark" style="margin: 20px auto"></span>
    {:else if items.length === 0}
      <div class="empty">
        <Icon name="puzzle" size={22} />
        <span class="strong">{t('datapacks.empty')}</span>
        <span class="muted">{t('datapacks.emptyHint')}</span>
      </div>
    {:else}
      <div class="list">
        {#each items as d (d.file)}
          <div class="row" class:off={!d.enabled}>
            {#if d.iconUrl}<img class="icon" src={d.iconUrl} alt="" />{:else}<span class="icon none"><Icon name={d.isFolder ? 'folder' : 'puzzle'} size={16} /></span>{/if}
            <span class="text">
              <span class="name truncate">{d.name}</span>
              <span class="meta truncate">{d.version ? `${d.version} · ` : ''}{d.isFolder ? t('datapacks.folder') : d.file} · {bytes(d.size)}</span>
            </span>
            {#if !d.isFolder}
              <Toggle checked={d.enabled} label={t('datapacks.enabledNamed', { name: d.name })} onchange={() => toggle(d)} />
            {/if}
            <button class="icon-btn small" aria-label={t('datapacks.removeNamed', { name: d.name })} title={t('common.remove')} onclick={() => remove(d)}><Icon name="trash" size={14} /></button>
          </div>
        {/each}
      </div>
      <p class="hint"><Icon name="info" size={12} />{t('datapacks.worldgenNote')}</p>
    {/if}

    {#snippet footer()}
      <button class="btn btn-flat" onclick={() => call('shell.openFolder', { kind: 'worldDatapacks', id: instance.id, sub: world.folder })}>
        <Icon name="folder" size={14} />{t('datapacks.openFolder')}
      </button>
      <div class="grow"></div>
      <button class="btn btn-ghost" onclick={onclose}>{t('common.close')}</button>
      <button class="btn btn-grad" onclick={() => (adding = true)}><Icon name="plus" size={13} />{t('datapacks.add')}</button>
    {/snippet}
  </Dialog>
{/if}

<style>
  .list {
    max-height: 380px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .row {
    padding: 8px 10px;
    border-radius: 10px;
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .row:hover {
    background: var(--panel-3);
  }
  .row.off .text {
    opacity: 0.55;
  }
  .icon {
    width: 32px;
    height: 32px;
    border-radius: 8px;
    flex-shrink: 0;
    object-fit: cover;
  }
  .icon.none {
    background: var(--panel-3);
    color: var(--muted);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .name {
    font-size: 13.5px;
    font-weight: 600;
  }
  .meta {
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .empty {
    padding: 24px 12px;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    text-align: center;
    color: var(--muted);
  }
  .strong {
    color: var(--text);
    font-weight: 600;
  }
  .muted {
    font-size: 12.5px;
    max-width: 400px;
  }
  .hint {
    margin-top: 10px;
    font-size: 11.5px;
    color: var(--muted-2);
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .icon-btn.small {
    width: 30px;
    height: 30px;
  }
</style>
