<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import Head from '../lib/components/Head.svelte';
  import type { AccountView } from '../lib/types';

  let busy = $state(false);

  async function login() {
    busy = true;
    try {
      const r = await call<{ accounts: AccountView[] }>('accounts.login', { type: 'microsoft' });
      app.accounts = r.accounts;
      app.refreshSocial();
      app.route = { page: 'home' };
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }

  async function use(a: AccountView) {
    try {
      app.accounts = await call<AccountView[]>('accounts.switch', { uuid: a.uuid });
      if (!app.accounts.find((x) => x.uuid === a.uuid)?.needsLogin) app.route = { page: 'home' };
      else await login();
    } catch (e) {
      app.error(e);
    }
  }
</script>

<div class="back">
  <div class="glow"></div>
  <div class="card">
    <img src="/img/logo.svg" alt="" width="72" height="72" />
    <h1>{t('welcomeBack.title')}</h1>
    <p class="lead">{t('welcomeBack.lead')}</p>
    {#if app.instances.length}<span class="chip">{t('welcomeBack.instances', { count: app.instances.length })}</span>{/if}
    {#if app.accounts.length}
      <div class="accounts">
        <span class="section-label">{t('welcomeBack.accounts')}</span>
        {#each app.accounts as a (a.uuid)}
          <button class="acc" onclick={() => use(a)}>
            <Head uuid={a.uuid} size={36} radius={7} />
            <span class="col grow" style="gap: 2px; text-align: left">
              <strong>{a.name}</strong>
              <span class="hint">{a.needsLogin ? t('accounts.needsLogin') : t(`accounts.type.${a.type}`)}</span>
            </span>
            <span class="go">{t('welcomeBack.signIn')}<Icon name="arrowRight" size={13} /></span>
          </button>
        {/each}
      </div>
    {/if}
    <button class="btn btn-grad lg" disabled={busy} onclick={login}>
      {#if busy}<span class="spinner"></span>{:else}<Icon name="microsoft" size={15} />{/if}{t('welcome.login')}
    </button>
    <p class="fine">{t('welcomeBack.windowNote')}</p>
    <button class="btn btn-flat sm" onclick={() => (app.route = { page: 'home' })}>{t('welcomeBack.skip')}</button>
  </div>
</div>

<style>
  .back {
    position: relative;
    flex-grow: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    overflow: hidden;
    background: var(--surface);
    border-top-left-radius: 18px;
    border-top-right-radius: 18px;
  }
  .glow {
    position: absolute;
    inset: 0;
    background: radial-gradient(60% 50% at 50% 20%, color-mix(in srgb, var(--accent-from) 14%, transparent), transparent 70%);
  }
  .card {
    position: relative;
    width: 480px;
    padding: 34px 36px;
    border-radius: 20px;
    background: rgba(42, 42, 42, 0.85);
    box-shadow: 0 30px 80px rgba(0, 0, 0, 0.5), inset 0 0 0 1px #363636;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 14px;
    text-align: center;
  }
  h1 {
    font-size: 26px;
    font-weight: 800;
  }
  .lead {
    font-size: 14px;
    color: var(--muted);
    line-height: 1.55;
  }
  .accounts {
    width: 100%;
    display: flex;
    flex-direction: column;
    gap: 8px;
    text-align: left;
  }
  .acc {
    padding: 10px 12px;
    border: 0;
    border-radius: 12px;
    background: #2e2e2e;
    display: flex;
    align-items: center;
    gap: 12px;
    cursor: pointer;
    color: var(--text);
  }
  .acc:hover {
    background: #353535;
  }
  .go {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12.5px;
    font-weight: 600;
    color: var(--accent-text);
  }
  .fine {
    font-size: 12px;
    color: var(--faint);
  }
</style>
