<script lang="ts">
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import Head from '../../lib/components/Head.svelte';
  import { statusText, presenceColors as colors } from '../../lib/util/social';
  import { flip } from 'svelte/animate';
  import { fadeSlide, fadeOut, pop, stagger, reducedMotion, DUR } from '../../lib/util/motion';

  const delay = stagger(35, 8, 600);
  const flipMs = reducedMotion() ? 0 : DUR.slow;

  const friends = $derived(app.social.friends.filter((f) => f.relation === 'friends'));
  const pending = $derived(app.social.friends.filter((f) => f.relation === 'pending'));
  const incoming = $derived(app.social.friends.filter((f) => f.relation === 'incoming'));

  async function act(type: string, uuid: string) {
    try {
      await call(type, { uuid });
      await app.refreshSocial();
    } catch (e) {
      app.error(e);
    }
  }
</script>

<section class="panel friends" aria-label={t('friends.title')}>
  <div class="head-row">
    <h2>{t('friends.title')}</h2>
    <button class="icon-btn" aria-label={t('friends.add')} aria-haspopup="dialog" onclick={() => (app.dialog = { kind: 'addFriend' })}>
      <Icon name="addUser" />
    </button>
  </div>

  <div class="list">
    {#if !app.social.available}
      <p class="empty">{app.activeAccount?.type === 'microsoft' ? t('friends.unavailable') : t('service.notSignedIn')}</p>
    {:else}
      {#each incoming as f, i (f.uuid)}
        <div class="row incoming" animate:flip={{ duration: flipMs }} in:fadeSlide|global={{ delay: delay(i), y: -6 }} out:fadeOut={{ duration: DUR.med }}>
          <Head uuid={f.uuid} size={40} radius={6} />
          <div class="text">
            <span class="name truncate">{f.name}</span>
            <span class="status">{t('friends.wantsToBeFriends')}</span>
          </div>
          <button class="icon-btn" aria-label={t('friends.accept', { name: f.name })} title={t('friends.acceptShort')} onclick={() => act('social.accept', f.uuid)}>
            <Icon name="check" size={14} />
          </button>
          <button class="icon-btn trash" aria-label={t('friends.decline', { name: f.name })} title={t('friends.declineShort')} onclick={() => act('social.decline', f.uuid)}>
            <Icon name="x" size={14} />
          </button>
        </div>
      {/each}
      {#each pending as f, i (f.uuid)}
        <div class="row pending" animate:flip={{ duration: flipMs }} in:fadeSlide|global={{ delay: delay(i), y: -6 }} out:fadeOut={{ duration: DUR.med }}>
          <Head uuid={f.uuid} size={40} radius={6} dim />
          <div class="text">
            <span class="name muted truncate">{f.name}</span>
            <span class="status"><span style="color: var(--yellow); display: flex"><Icon name="clock" size={12} stroke={1.6} /></span>{t('friends.requestSent')}</span>
          </div>
          <button class="icon-btn trash" aria-label={t('friends.cancelRequest', { name: f.name })} title={t('friends.cancelRequestShort')} onclick={() => act('social.decline', f.uuid)}>
            <Icon name="x" size={14} />
          </button>
        </div>
      {/each}
      {#each friends as f, i (f.uuid)}
        <div class="row" animate:flip={{ duration: flipMs }} in:fadeSlide|global={{ delay: delay(i), y: -6 }} out:fadeOut={{ duration: DUR.med }}>
          <button class="open" aria-label={t('friends.openProfile', { name: f.name })} aria-haspopup="dialog" onclick={() => (app.dialog = { kind: 'friendProfile', data: f.uuid })}>
            <Head uuid={f.uuid} size={40} radius={6} />
            <span class="text">
              <span class="name fname truncate">{f.name}</span>
              <span class="status"><span class="dot" style="background: {colors[f.state]}"></span>{statusText(f)}</span>
            </span>
          </button>
          {#if f.join}
            <button in:pop={{ scale: 0.7 }} class="icon-btn" aria-label={t('friends.join', { name: f.name })} title={t('friends.joinShort')} onclick={() => app.joinAddress(f.join!)}>
              <Icon name="play" />
            </button>
          {/if}
        </div>
      {/each}
      {#if friends.length + pending.length + incoming.length === 0}
        <p class="empty" in:fadeSlide={{ y: -6 }}>{t('friends.empty')}</p>
      {/if}
    {/if}
  </div>
</section>

<style>
  .friends {
    padding: 16px;
    display: flex;
    flex-direction: column;
    gap: 14px;
    flex: 1 1 auto;
    min-height: 0;
  }
  .head-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
  }
  h2 {
    font-size: 16px;
    font-weight: 600;
  }
  .list {
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
    overflow-y: auto;
    margin-right: -6px;
    padding-right: 6px;
  }
  .row {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 10px;
    border-radius: 8px;
    background: var(--panel-3);
  }
  .row.pending,
  .row.incoming {
    background: var(--panel-2);
    box-shadow: inset 0 0 0 1px var(--line-2);
  }
  .row.incoming {
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .open {
    flex-grow: 1;
    min-width: 0;
    padding: 0;
    border: 0;
    background: transparent;
    display: flex;
    align-items: center;
    gap: 12px;
    cursor: pointer;
    text-align: left;
  }
  .open:hover .fname {
    color: var(--accent-text);
  }
  .text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .name {
    font-size: 14px;
    font-weight: 500;
    color: var(--text);
    transition: color 0.12s ease;
  }
  .name.muted {
    color: #9a9a9a;
  }
  .status {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
    color: var(--muted);
  }
  .dot {
    width: 8px;
    height: 8px;
    border-radius: 4px;
    flex-shrink: 0;
  }
  .empty {
    margin-top: 24px;
    font-size: 13px;
    color: var(--muted-2);
    text-align: center;
    line-height: 1.5;
  }
</style>
