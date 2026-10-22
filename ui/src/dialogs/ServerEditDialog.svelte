<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import type { ServerEntry } from '../lib/types';

  let {
    instanceId,
    server = null,
    onSaved = undefined,
    onclose
  }: { instanceId: string; server?: ServerEntry | null; onSaved?: () => void; onclose: () => void } = $props();

  let name = $state(server?.name ?? '');
  let address = $state(server?.address ?? '');
  let busy = $state(false);
  const valid = $derived(name.trim().length > 0 && /^[^\s/\\]{1,255}$/.test(address.trim()));

  async function save() {
    if (!valid || busy) return;
    busy = true;
    try {
      if (server) await call('serverlist.edit', { id: instanceId, index: server.index, name: name.trim(), address: address.trim() });
      else await call('serverlist.add', { id: instanceId, name: name.trim(), address: address.trim(), index: null });
      onSaved?.();
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog
  title={server ? t('servers.edit') : t('servers.add')}
  subtitle={server ? '' : t('servers.addHint')}
  icon="servers"
  width={440}
  {onclose}>
  <label class="label">
    <span>{t('servers.name')}</span>
    <span class="field"><input bind:value={name} placeholder={t('servers.namePlaceholder')} maxlength="80" data-autofocus /></span>
  </label>
  <label class="label">
    <span>{t('servers.address')}</span>
    <span class="field">
      <span style="color: #7a7a7a; display: flex"><Icon name="globe" /></span>
      <input bind:value={address} placeholder={t('servers.addressPlaceholder')} spellcheck="false" onkeydown={(e) => e.key === 'Enter' && save()} />
    </span>
  </label>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={!valid || busy} onclick={save}>{server ? t('common.save') : t('common.add')}</button>
  {/snippet}
</Dialog>
