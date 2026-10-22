<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';

  let { onclose }: { onclose: () => void } = $props();
  let name = $state('');
  let busy = $state(false);
  const valid = $derived(/^[A-Za-z0-9_]{3,16}$/.test(name.trim()));

  async function send() {
    if (!valid || busy) return;
    busy = true;
    try {
      await call('social.request', { name: name.trim() });
      await app.refreshSocial();
      app.toast(t('friends.requestSentTo', { name: name.trim() }), 'success');
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog title={t('friends.add')} subtitle={t('friends.addHint')} icon="addUser" width={420} {onclose}>
  <label class="label">
    <span>{t('friends.minecraftName')}</span>
    <span class="field">
      <span style="color: #7a7a7a; display: flex"><Icon name="user" /></span>
      <input
        bind:value={name}
        placeholder={t('friends.namePlaceholder')}
        maxlength="16"
        spellcheck="false"
        data-autofocus
        onkeydown={(e) => e.key === 'Enter' && send()} />
    </span>
  </label>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={!valid || busy} onclick={send}>
      {#if busy}<span class="spinner"></span>{/if}{t('friends.sendRequest')}
    </button>
  {/snippet}
</Dialog>
