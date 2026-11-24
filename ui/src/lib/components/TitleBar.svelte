<script lang="ts">
  import { app, type Page } from '../stores/app.svelte';
  import { call } from '../bridge/bridge';
  import { t } from '../i18n';
  import Icon from './Icon.svelte';
  import Head from './Head.svelte';
  import { pop, bounce, DUR } from '../util/motion';

  let { minimal = false }: { minimal?: boolean } = $props();
  let accountOpen = $state(false);
  let busy = $state(false);

  const account = $derived(app.activeAccount);

  // Hauptnavigation sitzt auf der Bühne direkt in der Titelleiste
  const items: { page: Page; label: string; also?: Page[] }[] = [
    { page: 'home', label: 'app.nav.home' },
    { page: 'instances', label: 'app.nav.instances', also: ['instance', 'instanceCreate', 'modpacks'] },
    { page: 'localServers', label: 'app.nav.localServers' },
    { page: 'skins', label: 'app.nav.skins' },
    { page: 'console', label: 'app.nav.console' },
    { page: 'settings', label: 'app.nav.settings' }
  ];
  function isCurrent(item: (typeof items)[number]) {
    return app.route.page === item.page || (item.also?.includes(app.route.page) ?? false);
  }

  // Wer gerade online ist (Launcher oder Spiel), als kleiner Stapel neben der Suche
  const online = $derived(app.social.friends.filter((f) => f.relation === 'friends' && f.state !== 'offline'));

  // Leere Fläche der Titelleiste zieht das Fenster, Doppelklick maximiert
  function onmousedown(e: MouseEvent) {
    if (e.button !== 0 || (e.target as HTMLElement).closest('button, a, input, [role=menu]')) return;
    if (e.detail === 2) {
      call('window.toggleMaximize');
      return;
    }
    call('window.drag');
  }

  async function switchTo(uuid: string) {
    accountOpen = false;
    try {
      app.accounts = await call('accounts.switch', { uuid });
      app.refreshSocial();
    } catch (e) {
      app.error(e);
    }
  }

  async function addAccount() {
    accountOpen = false;
    busy = true;
    try {
      const r = await call<{ accounts: typeof app.accounts }>('accounts.login', { type: 'microsoft' });
      app.accounts = r.accounts;
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }

  async function logout() {
    accountOpen = false;
    if (!account) return;
    try {
      app.accounts = await call('accounts.remove', { uuid: account.uuid });
      if (app.accounts.length === 0) app.route = { page: 'welcomeBack' };
    } catch (e) {
      app.error(e);
    }
  }

  function onWindowKey(e: KeyboardEvent) {
    if (e.key === 'Escape' && accountOpen) accountOpen = false;
  }
</script>

<svelte:window onkeydown={onWindowKey} onmousedown={(e) => { if (accountOpen && !(e.target as HTMLElement).closest('.account-area')) accountOpen = false; }} />

<!-- svelte-ignore a11y_no_static_element_interactions -->
<header class="titlebar" class:minimal {onmousedown}>
  <div class="brand">
    <i class="mark" aria-hidden="true"></i>
    <span class="word">AXOCLIENT</span>
    {#if minimal}<span class="ver">v{app.shortVersion}</span>{/if}
  </div>

  {#if !minimal}
    <nav class="nav" aria-label={t('app.nav.main')}>
      {#each items as item (item.page)}
        <button
          class="nav-btn"
          class:on={isCurrent(item)}
          aria-current={isCurrent(item) ? 'page' : undefined}
          onclick={() => app.navigate({ page: item.page })}>
          {t(item.label)}
          {#if item.page === 'console' && app.running.length > 0}<i class="live" aria-hidden="true"></i>{/if}
        </button>
      {/each}
    </nav>
  {/if}

  <div class="grow"></div>

  <div class="actions">
    {#if !minimal}
      <button class="search" onclick={() => (app.paletteOpen = true)} aria-label={t('palette.open')} title={t('palette.open')}>
        <Icon name="search" size={15} stroke={1.8} />
        <span class="search-label">{t('palette.short')}</span>
        <kbd>{t('palette.key')}</kbd>
      </button>

      {#if online.length > 0}
        <button class="stack" onclick={() => app.navigate({ page: 'home' })} title={t('app.onlineCount', { count: online.length })} aria-label={t('app.onlineCount', { count: online.length })}>
          {#each online.slice(0, 3) as f (f.uuid)}<Head uuid={f.uuid} size={26} radius={13} />{/each}
          <span class="stack-count">{t('app.onlineCount', { count: online.length })}</span>
        </button>
      {/if}

      {#if app.update}
        <button class="update-pill" aria-haspopup="dialog" in:pop={{ scale: 0.85, duration: DUR.slow }} onclick={() => (app.dialog = { kind: 'update' })}>
          <Icon name="download" size={15} stroke={1.6} />{t('app.updateAvailable')}
        </button>
      {/if}
      <button
        class="round"
        aria-label={app.unread > 0 ? t('app.bellUnread', { count: app.unread }) : t('app.bell')}
        aria-haspopup="dialog"
        onclick={() => (app.dialog = { kind: 'notifications' })}>
        <Icon name="bell" size={18} stroke={1.6} />
        {#if app.unread > 0}<span class="badge" in:bounce out:pop={{ scale: 0.5 }}>{app.unread > 9 ? '9+' : app.unread}</span>{/if}
      </button>
      {#if account}
        <div class="account-area">
          <button
            class="account-btn"
            class:open={accountOpen}
            aria-label={t('app.accountMenu')}
            title={account.name}
            aria-haspopup="menu"
            aria-expanded={accountOpen}
            onclick={() => (accountOpen = !accountOpen)}>
            <Head uuid={account.uuid} size={34} radius={17} />
          </button>
          {#if accountOpen}
            <div
              class="menu account-menu"
              role="menu"
              aria-label={t('accounts.title')}
              in:pop={{ origin: 'top right', scale: 0.94, y: -6, duration: DUR.med }}
              out:pop={{ origin: 'top right', scale: 0.96, y: -4, duration: DUR.fast }}>
              <span class="section-label" style="padding: 8px 10px 6px">{t('accounts.title')}</span>
              {#each app.accounts as a (a.uuid)}
                <button class="menu-item acc" class:active={a.active} role="menuitemradio" aria-checked={a.active} onclick={() => switchTo(a.uuid)}>
                  <Head uuid={a.uuid} size={30} radius={15} />
                  <span class="acc-text">
                    <span class="acc-name truncate">{a.name}</span>
                    <span class="acc-type">{a.needsLogin ? t('accounts.needsLogin') : t(`accounts.type.${a.type}`)}</span>
                  </span>
                  {#if a.active}<span class="tick"><Icon name="check" size={11} stroke={2.6} /></span>{/if}
                </button>
              {/each}
              <div class="menu-sep"></div>
              <button class="menu-item" role="menuitem" onclick={addAccount} disabled={busy}>
                <span style="color: var(--accent-text); display: flex"><Icon name="addUser" stroke={1.6} /></span>{t('accounts.add')}
              </button>
              <button class="menu-item" role="menuitem" onclick={() => { accountOpen = false; app.navigate({ page: 'skins' }); }}>
                <Icon name="skins" stroke={1.6} />{t('app.nav.skins')}
              </button>
              <button class="menu-item" role="menuitem" onclick={() => { accountOpen = false; app.navigate({ page: 'settings' }); }}>
                <Icon name="settings" stroke={1.6} />{t('app.nav.settings')}
              </button>
              <div class="menu-sep"></div>
              <button class="menu-item danger" role="menuitem" onclick={logout}>
                <Icon name="logout" stroke={1.6} />{t('accounts.logout', { name: account.name })}
              </button>
            </div>
          {/if}
        </div>
      {/if}
    {/if}
    <div class="win">
      <button class="win-btn" aria-label={t('app.window.minimize')} onclick={() => call('window.minimize')}><Icon name="minimize" size={24} /></button>
      <button class="win-btn" aria-label={app.maximized ? t('app.window.restore') : t('app.window.maximize')} onclick={() => call('window.toggleMaximize')}>
        <Icon name={app.maximized ? 'restore' : 'maximize'} size={24} />
      </button>
      <button class="win-btn close" aria-label={t('app.window.close')} onclick={() => call('window.close')}><Icon name="close" size={24} /></button>
    </div>
  </div>
</header>

<style>
  .titlebar {
    position: relative;
    z-index: 30;
    height: 64px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    gap: 20px;
    padding: 0 10px 0 28px;
    white-space: nowrap;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
    flex-shrink: 0;
  }
  .mark {
    width: 10px;
    height: 10px;
    background: var(--accent-light);
  }
  .word {
    font-family: var(--display);
    font-weight: 700;
    font-size: 20px;
    letter-spacing: 0.14em;
    color: var(--text-strong);
  }
  .ver {
    font-family: var(--mono);
    font-size: 11px;
    color: var(--faint);
  }
  .nav {
    display: flex;
    gap: 20px;
    height: 100%;
    margin-left: 10px;
  }
  .nav-btn {
    position: relative;
    height: 100%;
    padding: 0;
    border: 0;
    background: transparent;
    font-family: var(--display);
    font-weight: 600;
    font-size: 15px;
    letter-spacing: 0.07em;
    text-transform: uppercase;
    color: var(--muted);
    display: flex;
    align-items: center;
    gap: 7px;
    cursor: pointer;
    transition: color var(--dur-fast) ease;
  }
  .nav-btn:hover,
  .nav-btn.on {
    color: var(--text-strong);
  }
  .nav-btn::after {
    content: '';
    position: absolute;
    left: 0;
    right: 0;
    bottom: 14px;
    height: 2px;
    background: var(--text-strong);
    transform: scaleX(0);
    transition: transform var(--dur-med) var(--ease-out);
  }
  .nav-btn.on::after {
    transform: scaleX(1);
  }
  .live {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    background: var(--green);
  }
  .actions {
    display: flex;
    align-items: center;
    gap: 12px;
    height: 100%;
  }
  .actions > * {
    flex-shrink: 0;
  }
  .search {
    height: 36px;
    padding: 0 8px 0 12px;
    border: 0;
    border-radius: var(--radius-m);
    background: rgba(255, 255, 255, 0.07);
    color: var(--muted);
    display: flex;
    align-items: center;
    gap: 9px;
    font-size: 13.5px;
    font-weight: 500;
    cursor: pointer;
  }
  .search:hover {
    background: rgba(255, 255, 255, 0.12);
    color: var(--text-strong);
  }
  kbd {
    font-family: var(--mono);
    font-size: 10.5px;
    color: var(--muted);
    background: rgba(255, 255, 255, 0.08);
    border-radius: 4px;
    padding: 2px 6px;
  }
  .stack {
    display: flex;
    align-items: center;
    padding: 0;
    border: 0;
    background: transparent;
    cursor: pointer;
  }
  .stack > :global(*:not(.stack-count)) {
    box-shadow: 0 0 0 2px #121316;
  }
  .stack > :global(*:not(:first-child):not(.stack-count)) {
    margin-left: -8px;
  }
  .stack-count {
    margin-left: 9px;
    font-size: 12.5px;
    color: var(--muted);
  }
  .stack:hover .stack-count {
    color: var(--text-strong);
  }
  .update-pill {
    height: 32px;
    padding: 0 12px;
    border: 0;
    border-radius: var(--radius-m);
    background: var(--green-bg);
    color: var(--green);
    font-size: 12.5px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 7px;
    cursor: pointer;
  }
  .update-pill:hover {
    background: color-mix(in srgb, var(--green) 24%, transparent);
  }
  .round {
    position: relative;
    width: 36px;
    height: 36px;
    padding: 0;
    border: 0;
    border-radius: var(--radius-m);
    background: transparent;
    color: var(--muted);
    display: grid;
    place-items: center;
    cursor: pointer;
  }
  .round:hover {
    color: var(--text-strong);
    background: var(--hover);
  }
  .badge {
    position: absolute;
    top: 3px;
    right: 2px;
    min-width: 16px;
    height: 16px;
    padding: 0 4px;
    border-radius: 8px;
    background: var(--accent-light);
    color: #0d0d0f;
    font-size: 10px;
    font-weight: 700;
    line-height: 16px;
    text-align: center;
  }
  .account-area {
    position: relative;
  }
  .account-btn {
    width: 36px;
    height: 36px;
    padding: 0;
    border: 0;
    border-radius: 50%;
    background: transparent;
    display: grid;
    place-items: center;
    cursor: pointer;
    transition: box-shadow var(--dur-fast) ease;
  }
  .account-btn:hover,
  .account-btn.open {
    box-shadow: 0 0 0 2px var(--line-2);
  }
  .account-menu {
    position: absolute;
    top: 46px;
    right: 0;
    z-index: 40;
    width: 280px;
  }
  .acc {
    height: 48px;
  }
  .acc-text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 1px;
  }
  .acc-name {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text-strong);
  }
  .acc-type {
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .tick {
    width: 20px;
    height: 20px;
    border-radius: 10px;
    background: var(--accent);
    color: #fff;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .win {
    display: flex;
    gap: 2px;
    margin-left: 4px;
  }
  .win-btn {
    width: 38px;
    height: 36px;
    padding: 0;
    border: 0;
    border-radius: var(--radius-m);
    background: transparent;
    color: var(--muted);
    display: grid;
    place-items: center;
    cursor: pointer;
  }
  .win-btn:hover {
    background: var(--hover);
    color: var(--text-strong);
  }
  .win-btn.close:hover {
    background: #c4372b;
    color: #fff;
  }
  /* Schmale Fenster: erst Beschriftungen, dann den Freunde-Stapel einklappen, damit nichts umbricht */
  @media (max-width: 1260px) {
    .search-label,
    .stack-count {
      display: none;
    }
    .nav {
      gap: 16px;
    }
  }
  @media (max-width: 1140px) {
    .stack {
      display: none;
    }
    .nav-btn {
      font-size: 14px;
      letter-spacing: 0.05em;
    }
  }
</style>
