<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import FriendPicker from '../lib/components/FriendPicker.svelte';
  import Checkbox from '../lib/components/Checkbox.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';

  /**
   * Alle "Senden"-Dialoge: Instanz, Mod, Server oder Einladung an Freunde.
   * Es werden nur IDs geschickt; der Empfänger lädt selbst.
   */
  let {
    mode,
    instanceId = null,
    mod = null,
    server = null,
    preselect = [],
    onclose
  }: {
    mode: 'instance' | 'mod' | 'server' | 'invite';
    instanceId?: string | null;
    mod?: { title: string; type: string; source: string; projectId: string; versionId: string } | null;
    server?: { name: string; address: string } | null;
    preselect?: string[];
    onclose: () => void;
  } = $props();

  let selected = $state<string[]>([...preselect]);
  let includeServers = $state(true);
  let includeOptions = $state(false);
  let includeModLock = $state(true);
  let busy = $state(false);
  let inviteAddress = $state(server?.address ?? '');

  const instance = $derived(instanceId ? app.instances.find((i) => i.id === instanceId) : null);
  const title = $derived(
    mode === 'instance' ? t('share.instanceTitle', { name: instance?.name ?? '' })
      : mode === 'mod' ? t('share.modTitle', { name: mod?.title ?? '' })
      : mode === 'server' ? t('share.serverTitle', { name: server?.name ?? '' })
      : t('share.inviteTitle')
  );

  async function send() {
    if (selected.length === 0) return;
    busy = true;
    try {
      if (mode === 'instance') await call('inbox.sendInstance', { instanceId, recipients: selected, servers: includeServers, options: includeOptions, modLock: includeModLock });
      else if (mode === 'mod') await call('inbox.sendMod', { ...mod, recipients: selected });
      else if (mode === 'server') await call('inbox.sendServer', { ...server, recipients: selected });
      else await call('inbox.invite', { address: inviteAddress.trim(), instanceName: null, minecraft: null, recipients: selected });
      app.toast(t('share.sent', { count: selected.length }), 'success');
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog {title} subtitle={t('share.hint')} icon="send" width={480} {onclose}>
  {#if mode === 'invite'}
    <label class="label">
      <span>{t('share.inviteAddress')}</span>
      <span class="field"><input bind:value={inviteAddress} placeholder={t('servers.addressPlaceholder')} spellcheck="false" /></span>
    </label>
  {/if}
  <FriendPicker bind:selected />
  {#if mode === 'instance'}
    <div class="col" style="gap: 10px">
      <Checkbox bind:checked={includeServers} label={t('share.includeServers')} small />
      <Checkbox bind:checked={includeOptions} label={t('share.includeOptions')} small />
      {#if instance && instance.modLock !== 'off'}
        <Checkbox bind:checked={includeModLock} label={t('share.includeModLock', { level: t(`modLock.levels.${instance.modLock}.name`) })} small />
      {/if}
      <p class="hint">{t('share.instanceNote')}</p>
    </div>
  {/if}
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={selected.length === 0 || busy || (mode === 'invite' && !inviteAddress.trim())} onclick={send}>
      {#if busy}<span class="spinner"></span>{/if}{t('common.send')}
    </button>
  {/snippet}
</Dialog>
