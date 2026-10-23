<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { call } from '../bridge/bridge';
  import { t } from '../i18n';
  import Icon from './Icon.svelte';
  import Head from './Head.svelte';
  import { pop, bounce, DUR } from '../util/motion';

  let { minimal = false }: { minimal?: boolean } = $props();
  let accountOpen = $state(false);
  let busy = $state(false);

  const account = $derived(app.activeAccount);

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
<header class="titlebar" {onmousedown}>
  <div class="brand">
    <span class="word"><span class="axo">Axo</span><span class="client">Client</span></span>
    <span class="ver">v{app.shortVersion}</span>
  </div>
  <div class="grow"></div>
  <div class="actions">
    {#if !minimal}
      {#if app.update}
        <button class="update-pill" aria-haspopup="dialog" in:pop={{ scale: 0.85, duration: DUR.slow }} onclick={() => (app.dialog = { kind: 'update' })}>
          <Icon name="download" size={15} stroke={1.6} />{t('app.updateAvailable')}
        </button>
      {/if}
      <button
        class="icon-btn bar"
        aria-label={app.unread > 0 ? t('app.bellUnread', { count: app.unread }) : t('app.bell')}
        aria-haspopup="dialog"
        onclick={() => (app.dialog = { kind: 'notifications' })}>
        <Icon name="bell" />
        {#if app.unread > 0}<span class="badge" in:bounce out:pop={{ scale: 0.5 }}>{app.unread > 9 ? '9+' : app.unread}</span>{/if}
      </button>
      {#if account}
        <div class="account-area">
          <button
            class="account-btn"
            class:open={accountOpen}
            aria-label={t('app.accountMenu')}
            aria-haspopup="menu"
            aria-expanded={accountOpen}
            onclick={() => (accountOpen = !accountOpen)}>
            <Head uuid={account.uuid} size={24} />
            <span class="name">{account.name}</span>
            <span class="chev" class:up={accountOpen}><Icon name="chevronDown" /></span>
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
                  <Head uuid={a.uuid} size={30} radius={6} />
                  <span class="acc-text">
                    <span class="acc-name truncate">{a.name}</span>
                    <span class="acc-type">{a.needsLogin ? t('accounts.needsLogin') : t(`accounts.type.${a.type}`)}</span>
                  </span>
                  {#if a.active}<span class="tick"><Icon name="check" size={11} stroke={2.6} /></span>{/if}
                </button>
              {/each}
              <div class="menu-sep"></div>
              <button class="menu-item" role="menuitem" onclick={addAccount} disabled={busy}>
                <span style="color: var(--accent-text)"><Icon name="addUser" stroke={1.6} /></span>{t('accounts.add')}
              </button>
              <button class="menu-item danger" role="menuitem" onclick={logout}>
                <Icon name="logout" stroke={1.6} />{t('accounts.logout', { name: account.name })}
              </button>
            </div>
          {/if}
        </div>
      {/if}
    {/if}
    <div class="win">
      <button class="win-btn" aria-label={t('app.window.minimize')} onclick={() => call('window.minimize')}><Icon name="minimize" size={26} /></button>
      <button class="win-btn" aria-label={app.maximized ? t('app.window.restore') : t('app.window.maximize')} onclick={() => call('window.toggleMaximize')}>
        <Icon name={app.maximized ? 'restore' : 'maximize'} size={26} />
      </button>
      <button class="win-btn close" aria-label={t('app.window.close')} onclick={() => call('window.close')}><Icon name="close" size={26} /></button>
    </div>
  </div>
</header>

<style>
  .titlebar {
    height: 52px;
    padding: 0 0 0 19px;
    display: flex;
    align-items: center;
    flex-shrink: 0;
  }
  .brand {
    display: flex;
    align-items: baseline;
    gap: 5px;
    pointer-events: none;
  }
  .word {
    font-size: 28px;
    line-height: 1;
    letter-spacing: -0.5px;
  }
  .axo {
    font-weight: 600;
    color: #434343;
  }
  .client {
    font-weight: 500;
    color: #3d3d3d;
  }
  .ver {
    font-size: 12px;
    font-weight: 500;
    color: #3d3d3d;
  }
  .actions {
    align-self: stretch;
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .update-pill {
    height: 32px;
    padding: 0 12px 0 10px;
    border: 0;
    border-radius: 8px;
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
    color: var(--accent-text);
    font-size: 12.5px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 7px;
    cursor: pointer;
    transition: filter 0.12s ease;
  }
  .update-pill:hover {
    filter: brightness(1.25);
  }
  .icon-btn.bar {
    position: relative;
    background: #242424;
  }
  .icon-btn.bar:hover {
    background: #3a3a3a;
  }
  .badge {
    position: absolute;
    top: -3px;
    right: -3px;
    min-width: 14px;
    height: 14px;
    padding: 0 3px;
    border-radius: 7px;
    background: var(--badge);
    box-shadow: 0 0 0 2px var(--bg);
    color: #fff;
    font-size: 9px;
    font-weight: 700;
    line-height: 14px;
    text-align: center;
  }
  .account-area {
    position: relative;
  }
  .account-btn {
    height: 32px;
    padding: 4px 6px 4px 4px;
    border: 0;
    border-radius: 8px;
    background: #242424;
    display: flex;
    align-items: center;
    gap: 8px;
    cursor: pointer;
    transition: background-color 0.12s ease;
  }
  .account-btn:hover,
  .account-btn.open {
    background: #3a3a3a;
  }
  .name {
    font-size: 14px;
    font-weight: 500;
    color: var(--text);
  }
  .chev {
    display: flex;
    color: var(--text-2);
    transition: transform 0.15s ease;
  }
  .chev.up {
    transform: rotate(180deg);
  }
  .account-menu {
    position: absolute;
    top: 40px;
    right: 0;
    z-index: 40;
    width: 270px;
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
  /* Fenster-Buttons reichen wie bei Windows bis an den oberen und rechten Rand: Maus in die Ecke = Treffer */
  .win {
    align-self: stretch;
    display: flex;
    margin-left: 10px;
  }
  .win-btn {
    width: 46px;
    height: 100%;
    padding: 0;
    border: 0;
    border-radius: 0;
    background: transparent;
    color: var(--text-2);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .win-btn:hover {
    background: #2c2c2c;
    color: var(--text);
  }
  .win-btn.close:hover {
    color: #fff;
  }
  .win-btn.close:hover {
    background: #c42b1c;
  }
</style>
