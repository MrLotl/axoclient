<script lang="ts">
  import Icon from '../lib/components/Icon.svelte';
  import Head from '../lib/components/Head.svelte';
  import SkinViewer from '../lib/components/SkinViewer.svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import { statusText, presenceColors } from '../lib/util/social';

  let { uuid, onclose }: { uuid: string; onclose: () => void } = $props();
  const friend = $derived(app.social.friends.find((f) => f.uuid === uuid));
  let confirmRemove = $state(false);

  async function remove() {
    try {
      await call('social.remove', { uuid });
      await app.refreshSocial();
      onclose();
    } catch (e) {
      app.error(e);
    }
  }

  async function block() {
    try {
      await call('social.block', { uuid, block: true });
      await app.refreshSocial();
      onclose();
    } catch (e) {
      app.error(e);
    }
  }

  function invite() {
    app.dialog = { kind: 'share', data: { mode: 'invite', preselect: [uuid] } };
  }
</script>

{#if friend}
  <Dialog title={friend.name} width={794} padded={false} {onclose}>
    {#snippet header()}<span></span>{/snippet}
    <div class="profile">
      <div class="model">
        <span class="glow"></span>
        <SkinViewer skin={`/axo/head/${uuid.replaceAll('-', '')}`} width={256} height={410} label={t('friends.skinOf', { name: friend.name })} />
      </div>
      <div class="info">
        <div class="top">
          <Head {uuid} size={48} radius={9} />
          <div class="titles">
            <h2>{friend.name}</h2>
            <span class="status"><span class="dot" style="background: {presenceColors[friend.state]}"></span>{statusText(friend)}</span>
          </div>
          <button class="icon-btn flat" aria-label={t('common.close')} onclick={onclose}><Icon name="x" size={14} /></button>
        </div>

        {#if friend.join}
          <div class="where">
            <span class="section-label">{t('friends.playingOn')}</span>
            <span class="addr"><Icon name="servers" size={14} />{friend.join}</span>
          </div>
        {/if}

        <p class="privacy">{t('friends.privacyNote')}</p>

        <div class="grow"></div>
        {#if confirmRemove}
          <div class="confirm">
            <span>{t('friends.removeConfirm', { name: friend.name })}</span>
            <button class="btn btn-ghost sm" onclick={() => (confirmRemove = false)}>{t('common.cancel')}</button>
            <button class="btn btn-danger sm" style="background: var(--red-soft)" onclick={remove}>{t('friends.remove')}</button>
          </div>
        {:else}
          <div class="row">
            {#if friend.join}
              <button class="btn btn-second" onclick={() => { onclose(); app.joinAddress(friend.join!); }}><Icon name="play" size={13} />{t('friends.joinShort')}</button>
            {/if}
            <button class="btn btn-second" onclick={invite}><Icon name="send" size={14} stroke={1.6} />{t('friends.invite')}</button>
            <div class="grow"></div>
            <button class="btn btn-flat" onclick={block}>{t('friends.block')}</button>
            <button class="btn btn-danger" onclick={() => (confirmRemove = true)}>{t('friends.remove')}</button>
          </div>
        {/if}
      </div>
    </div>
  </Dialog>
{/if}

<style>
  .profile {
    height: 496px;
    display: flex;
  }
  .model {
    width: 286px;
    flex-shrink: 0;
    position: relative;
    background: radial-gradient(120% 70% at 50% 85%, #383838 0%, #2c2c2c 55%, #252525 100%);
    display: flex;
    align-items: flex-end;
    justify-content: center;
    padding-bottom: 18px;
  }
  .glow {
    position: absolute;
    left: 0;
    right: 0;
    top: 0;
    height: 120px;
    background: linear-gradient(180deg, color-mix(in srgb, var(--accent-from) 18%, transparent), transparent);
  }
  .info {
    flex-grow: 1;
    min-width: 0;
    padding: 22px;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .top {
    display: flex;
    align-items: flex-start;
    gap: 12px;
  }
  .titles {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    gap: 5px;
  }
  h2 {
    font-size: 19px;
    font-weight: 700;
    color: var(--text-strong);
  }
  .status {
    display: flex;
    align-items: center;
    gap: 7px;
    font-size: 12.5px;
    color: var(--muted);
  }
  .dot {
    width: 8px;
    height: 8px;
    border-radius: 4px;
  }
  .where {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .addr {
    align-self: flex-start;
    height: 30px;
    padding: 0 10px;
    border-radius: 8px;
    background: var(--panel-3);
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 12.5px;
    font-weight: 500;
  }
  .privacy {
    font-size: 12.5px;
    color: var(--muted-2);
    line-height: 1.5;
  }
  .confirm {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 13px;
    color: var(--text-2);
  }
  .confirm span {
    flex-grow: 1;
  }
</style>
