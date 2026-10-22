<script lang="ts">
  import Dialog from '../../lib/components/Dialog.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';

  let { onclose, onsaved }: { onclose: () => void; onsaved: () => void } = $props();
  let name = $state('');
  let busy = $state(false);
  let notFound = $state(false);

  async function adopt() {
    if (!/^[A-Za-z0-9_]{3,16}$/.test(name.trim())) return;
    busy = true;
    notFound = false;
    try {
      await call('skins.adopt', { player: name.trim() });
      app.toast(t('skins.adopted', { name: name.trim() }), 'success');
      onsaved();
      onclose();
    } catch (e: any) {
      if (e?.text?.code === 'skins.playerNotFound') notFound = true;
      else app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog title={t('skins.adoptTitle')} subtitle={t('skins.adoptHint')} icon="user" width={440} {onclose}>
  <label class="label">
    <span>{t('friends.minecraftName')}</span>
    <span class="field" class:invalid={notFound}>
      <span style="color: #7a7a7a; display: flex"><Icon name="search" /></span>
      <input bind:value={name} maxlength="16" spellcheck="false" data-autofocus onkeydown={(e) => e.key === 'Enter' && adopt()} />
    </span>
  </label>
  {#if notFound}
    <p class="error-text">{t('skins.playerNotFound')} {t('skins.checkSpelling')}</p>
  {/if}
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={busy || !/^[A-Za-z0-9_]{3,16}$/.test(name.trim())} onclick={adopt}>
      {#if busy}<span class="spinner"></span>{t('skins.loadingSkin')}{:else}{t('skins.adoptButton')}{/if}
    </button>
  {/snippet}
</Dialog>
