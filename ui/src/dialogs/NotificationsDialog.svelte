<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import Head from '../lib/components/Head.svelte';
  import ImageBox from '../lib/components/ImageBox.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';
  import { ago } from '../lib/util/format';
  import type { InboxItem, NotificationItem } from '../lib/types';

  let { onclose }: { onclose: () => void } = $props();

  let busy = $state<Record<string, boolean>>({});
  let done = $state<Record<string, string | null>>({});
  let target = $state<Record<string, string>>({});

  onMount(() => {
    app.refreshNotifications();
    // Beim Öffnen gilt alles als gelesen
    const timer = setTimeout(() => call('notifications.read').catch(() => {}), 1200);
    return () => clearTimeout(timer);
  });

  const icons: Record<string, string> = {
    friendRequest: 'addUser',
    friendAccepted: 'heart',
    friendOnline: 'user',
    friendOnServer: 'servers',
    friendOpenWorld: 'world',
    invite: 'send',
    inbox: 'send',
    modUpdates: 'download',
    modpackUpdates: 'download',
    capeReview: 'cape',
    launcherUpdate: 'sparkle',
    jobFinished: 'check',
    authExpired: 'lock',
    lowDiskSpace: 'warning',
    serverCrashed: 'warning',
    serverPlayerJoined: 'servers'
  };

  function uuidOf(n: NotificationItem): string | null {
    const u = n.data?.['uuid'];
    return typeof u === 'string' ? u : null;
  }

  async function accept(item: InboxItem) {
    if ((item.kind === 'mod' || item.kind === 'server') && !target[item.id]) {
      app.toast(t('inbox.chooseInstance'), 'error');
      return;
    }
    busy[item.id] = true;
    try {
      const result = await call<string | null>('inbox.accept', { id: item.id, instanceId: target[item.id] ?? null });
      done[item.id] = result;
      await app.refreshSocial();
    } catch (e) {
      app.error(e);
    } finally {
      busy[item.id] = false;
    }
  }

  async function discard(item: InboxItem) {
    try {
      await call('inbox.discard', { id: item.id });
      await app.refreshSocial();
    } catch (e) {
      app.error(e);
    }
  }

  function openNotification(n: NotificationItem) {
    const instanceId = n.data?.['instanceId'];
    if (typeof instanceId === 'string') {
      // Alte Benachrichtigung zu einer gelöschten Instanz: nicht ins Leere navigieren
      if (!app.instances.some((i) => i.id === instanceId)) {
        app.toast(t('instances.notFound'), 'error');
        return;
      }
      onclose();
      app.navigate({ page: 'instance', id: instanceId, tab: n.type === 'modUpdates' ? 'mods' : 'overview' });
    } else if (n.type === 'launcherUpdate') {
      app.dialog = { kind: 'update' };
    } else if (n.type === 'authExpired') {
      onclose();
      app.route = { page: 'welcomeBack' };
    } else if (typeof n.data?.['address'] === 'string') {
      onclose();
      app.joinAddress(n.data['address'] as string);
    }
  }

  async function friendAction(type: string, n: NotificationItem) {
    const uuid = uuidOf(n);
    if (!uuid) return;
    try {
      await call(type, { uuid });
      await app.refreshSocial();
      await call('notifications.remove', { id: n.id });
    } catch (e) {
      app.error(e);
    }
  }
</script>

<Dialog title={t('notifications.title')} width={680} {onclose} padded={false}>
  {#snippet header()}
    <div class="top">
      <h2 id="dlg-notif">{t('notifications.title')}</h2>
      {#if app.unread > 0}<span class="chip accent">{t('notifications.unread', { count: app.unread })}</span>{/if}
      <div class="grow"></div>
      <button class="icon-btn flat" aria-label={t('common.close')} onclick={onclose}><Icon name="x" size={14} /></button>
    </div>
  {/snippet}
  <div class="list">
    {#each app.social.inbox as item (item.id)}
      <div class="item">
        <Head uuid={item.sender.uuid} size={36} radius={8} />
        <div class="main">
          <span class="title truncate">{t('notifications.inbox', { name: item.sender.name })}</span>
          <div class="share">
            <div class="share-head">
              {#if item.kind === 'instance'}
                <ImageBox name={item.title} width={32} radius={6} />
              {:else}
                <span class="kind-icon"><Icon name={item.kind === 'mod' ? 'puzzle' : item.kind === 'server' ? 'servers' : 'send'} /></span>
              {/if}
              <span class="share-text">
                <span class="share-name truncate">{item.title}</span>
                <span class="share-meta">{t('inbox.expires', { when: new Date(item.expires).toLocaleString('de-DE', { dateStyle: 'short', timeStyle: 'short' }) })}</span>
              </span>
              <span class="badge-kind">{t(`inbox.kinds.${item.kind}`)}</span>
            </div>
            {#if done[item.id] !== undefined}
              <div class="row">
                {#if item.kind === 'instance' && done[item.id]}
                  <button class="btn btn-second sm" onclick={() => { onclose(); app.navigate({ page: 'instance', id: done[item.id]! }); }}>
                    <Icon name="check" size={13} />{t('inbox.openInstance')}
                  </button>
                  {@const received = app.instances.find((i) => i.id === done[item.id])}
                  {#if received && received.modLock !== 'off'}
                    <span class="note"><Icon name="lock" size={11} />{t('inbox.withModLock', { level: t(`modLock.levels.${received.modLock}.name`) })}</span>
                  {/if}
                {:else}
                  <span class="ok"><Icon name="check" size={13} />{t('inbox.accepted')}</span>
                {/if}
              </div>
            {:else}
              {#if item.kind === 'mod' || item.kind === 'server'}
                <select class="select" bind:value={target[item.id]} aria-label={t('inbox.chooseInstance')}>
                  <option value={undefined} disabled selected>{t('inbox.chooseInstance')}</option>
                  {#each app.instances as inst (inst.id)}<option value={inst.id}>{inst.name}</option>{/each}
                </select>
              {/if}
              <div class="row">
                {#if item.kind === 'invite'}
                  <button class="btn btn-second sm" onclick={() => { onclose(); app.joinAddress(item.title); discard(item); }}>
                    <Icon name="play" size={13} />{t('common.join')}
                  </button>
                {:else}
                  <button class="btn btn-second sm" disabled={busy[item.id]} onclick={() => accept(item)}>
                    {#if busy[item.id]}<span class="spinner"></span>{:else}<Icon name="download" size={13} stroke={1.8} />{/if}
                    {t(item.kind === 'instance' ? 'inbox.install' : 'inbox.accept')}
                  </button>
                {/if}
                <button class="btn btn-flat sm" onclick={() => discard(item)}>{t('inbox.decline')}</button>
                <span class="grow note">{t('inbox.officialFiles')}</span>
              </div>
            {/if}
          </div>
          <span class="time">{ago(item.created)}</span>
        </div>
      </div>
    {/each}

    {#each app.notifications.filter((n) => n.type !== 'inbox') as n (n.id)}
      <div class="item" class:unread={!n.read}>
        {#if uuidOf(n)}
          <Head uuid={uuidOf(n)} size={36} radius={8} />
        {:else}
          <span class="n-icon"><Icon name={icons[n.type] ?? 'bell'} size={17} /></span>
        {/if}
        <div class="main">
          <button class="title-btn" onclick={() => openNotification(n)}>{tr(n.text)}</button>
          {#if n.type === 'friendRequest'}
            <div class="row">
              <button class="btn btn-second sm" onclick={() => friendAction('social.accept', n)}>{t('friends.acceptShort')}</button>
              <button class="btn btn-flat sm" onclick={() => friendAction('social.decline', n)}>{t('friends.declineShort')}</button>
            </div>
          {/if}
          <span class="time">{ago(n.created)}</span>
        </div>
        {#if !n.read}<span class="dot" aria-label={t('notifications.unreadDot')}></span>{/if}
      </div>
    {/each}

    {#if app.social.inbox.length === 0 && app.notifications.filter((n) => n.type !== 'inbox').length === 0}
      <p class="empty">{t('notifications.empty')}</p>
    {/if}
  </div>
</Dialog>

<style>
  .top {
    padding: 20px 20px 14px;
    display: flex;
    align-items: center;
    gap: 10px;
  }
  h2 {
    font-size: 17px;
    font-weight: 600;
    color: var(--text-strong);
  }
  .list {
    max-height: 560px;
    overflow-y: auto;
    padding: 0 12px 14px 20px;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .item {
    padding: 12px;
    border-radius: 12px;
    display: flex;
    align-items: flex-start;
    gap: 12px;
  }
  .item.unread {
    background: rgba(255, 255, 255, 0.025);
  }
  .n-icon {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .main {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .title,
  .title-btn {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-strong);
  }
  .title-btn {
    padding: 0;
    border: 0;
    background: transparent;
    text-align: left;
    cursor: pointer;
    line-height: 1.4;
  }
  .title-btn:hover {
    color: var(--accent-text);
  }
  .share {
    padding: 10px;
    border-radius: 10px;
    background: #262626;
    box-shadow: inset 0 0 0 1px #363636;
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .share-head {
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .kind-icon {
    width: 32px;
    height: 32px;
    border-radius: 6px;
    background: #333;
    color: var(--muted);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .share-text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .share-name {
    font-size: 13px;
    font-weight: 600;
    color: var(--text-strong);
  }
  .share-meta {
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .badge-kind {
    height: 20px;
    padding: 0 7px;
    border-radius: 6px;
    background: #333;
    color: #bdbdbd;
    font-size: 10.5px;
    font-weight: 600;
    line-height: 20px;
  }
  .row {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .note {
    text-align: right;
    font-size: 11px;
    color: var(--faint);
    white-space: nowrap;
  }
  .ok {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12.5px;
    font-weight: 600;
    color: var(--green);
  }
  .select {
    height: 32px;
    padding: 0 10px;
    border-radius: 8px;
    border: 0;
    background: var(--field);
    color: var(--text);
    box-shadow: inset 0 0 0 1px var(--line-2);
  }
  .time {
    font-size: 11.5px;
    color: var(--faint);
  }
  .dot {
    width: 8px;
    height: 8px;
    margin-top: 5px;
    border-radius: 4px;
    background: var(--accent);
    flex-shrink: 0;
  }
  .empty {
    padding: 40px 0;
    text-align: center;
    color: var(--muted-2);
    font-size: 13px;
  }
</style>
