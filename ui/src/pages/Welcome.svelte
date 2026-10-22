<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import MemorySlider from '../lib/components/MemorySlider.svelte';
  import type { AccountView, ForeignLauncherView, Instance } from '../lib/types';

  let step = $state<'welcome' | 'login' | 'setup' | 'import' | 'done'>(app.accounts.length ? 'setup' : 'welcome');
  let found = $state<{ instances: number; launchers: string[] } | null>(null);

  function openImport() {
    step = 'done';
    app.dialog = { kind: 'import' };
  }
  let memory = $state(Math.min(6144, Math.max(2048, Math.floor((app.totalRamMb / 2) / 1024) * 1024)));
  let devName = $state('');
  let firstInstance = $state<Instance | null>(null);
  let busy = $state(false);

  const account = $derived(app.activeAccount);

  async function login(type: 'microsoft' | 'dev') {
    step = 'login';
    try {
      const r = await call<{ accounts: AccountView[] }>('accounts.login', { type, devName: type === 'dev' ? devName : null });
      app.accounts = r.accounts;
      step = 'setup';
      app.refreshSocial();
    } catch (e) {
      app.error(e);
      step = 'welcome';
    }
  }

  async function finish() {
    busy = true;
    try {
      await app.saveSettings((s) => {
        s.defaults.memoryMb = memory;
        s.setupDone = true;
      });
      // Eine erste Instanz, damit man direkt spielen kann – mit Fabric auf der neuesten Version, die die
      // AxoClient-Mod kann (Vanilla lädt keine Mods). Nur wenn es keine gibt, eine Vanilla-Instanz.
      if (app.instances.length === 0) {
        const versions = await call<{ id: string; type: string }[]>('versions.list', { snapshots: false, old: false });
        const axo = versions.find((v) => app.axoModVersions.includes(v.id))?.id;
        const latest = versions[0]?.id;
        if (axo)
          firstInstance = await call<Instance>('instances.create', { name: `AxoClient ${axo}`, minecraft: axo, loader: 'fabric', loaderVersion: null, memoryMb: null, iconRef: null });
        else if (latest)
          firstInstance = await call<Instance>('instances.create', { name: `Vanilla ${latest}`, minecraft: latest, loader: 'vanilla', loaderVersion: null, memoryMb: null, iconRef: null });
        await app.refreshInstances();
      }
      // Instanzen aus anderen Launchern gefunden? Dann fragen, ob etwas übernommen werden soll
      try {
        const launchers = await call<ForeignLauncherView[]>('import.scan');
        const withInstances = launchers.filter((l) => l.instances.length > 0);
        found = { instances: withInstances.reduce((a, l) => a + l.instances.length, 0), launchers: withInstances.map((l) => l.name) };
      } catch {
        found = null;
      }
      step = found && found.instances > 0 ? 'import' : 'done';
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<div class="welcome">
  <div class="glow"></div>
  <div class="card">
    {#if step === 'welcome'}
      <img src="/img/logo.svg" alt="" width="88" height="88" />
      <span class="eyebrow">{t('welcome.firstStart')}</span>
      <h1>{t('welcome.title')} <span class="brand">AxoClient</span></h1>
      <p class="lead">{t('welcome.lead')}</p>
      <div class="features">
        <div class="feature"><span class="f-icon"><Icon name="instances" size={18} /></span><span class="col"><strong>{t('welcome.f1')}</strong><span>{t('welcome.f1Text')}</span></span></div>
        <div class="feature"><span class="f-icon"><Icon name="addUser" size={18} /></span><span class="col"><strong>{t('welcome.f2')}</strong><span>{t('welcome.f2Text')}</span></span></div>
        <div class="feature"><span class="f-icon"><Icon name="cape" size={18} /></span><span class="col"><strong>{t('welcome.f3')}</strong><span>{t('welcome.f3Text')}</span></span></div>
      </div>
      <button class="btn btn-grad lg" onclick={() => login('microsoft')}><Icon name="microsoft" size={15} />{t('welcome.login')}</button>
      <p class="fine">{t('welcome.loginNote')}</p>
      {#if app.devAuth}
        <div class="dev">
          <span class="field"><input bind:value={devName} placeholder={t('welcome.devName')} maxlength="16" /></span>
          <button class="btn btn-ghost sm" disabled={devName.trim().length < 3} onclick={() => login('dev')}>{t('welcome.devLogin')}</button>
        </div>
      {/if}
    {:else if step === 'login'}
      <span class="spin-wrap"><span class="spinner dark big"></span></span>
      <h1 class="small">{t('welcome.loginTitle')}</h1>
      <p class="lead">{t('welcome.loginText')}</p>
      <p class="fine">{t('welcome.loginWaiting')}</p>
    {:else if step === 'setup'}
      <h1 class="small">{t('welcome.hello', { name: account?.name ?? '' })}</h1>
      <p class="lead">{t('welcome.setupLead')}</p>
      <div class="setup">
        <div class="block">
          <strong>{t('memory.label')}</strong>
          <span class="hint">{t('welcome.memoryHint', { total: Math.round(app.totalRamMb / 1024) })}</span>
          <MemorySlider bind:value={memory} mods={30} />
        </div>
        <div class="block">
          <strong>Java</strong>
          <span class="ok"><Icon name="check" size={13} />{t('settings.javaAuto')}</span>
          <span class="hint">{t('welcome.javaHint')}</span>
        </div>
      </div>
      <button class="btn btn-grad lg" disabled={busy} onclick={finish}>{#if busy}<span class="spinner"></span>{/if}{t('welcome.finishSetup')}</button>
    {:else if step === 'import' && found}
      <span class="done-icon"><Icon name="import" size={26} stroke={2} /></span>
      <h1 class="small">{t('welcome.importTitle')}</h1>
      <p class="lead">{t('welcome.importText', { count: found.instances, launchers: found.launchers.join(', ') })}</p>
      <div class="row" style="gap: 10px">
        <button class="btn btn-ghost lg" onclick={() => (step = 'done')}>{t('welcome.importSkip')}</button>
        <button class="btn btn-grad lg" onclick={openImport}><Icon name="import" size={15} />{t('welcome.importChoose')}</button>
      </div>
      <p class="fine">{t('welcome.importLater')}</p>
    {:else}
      <span class="done-icon"><Icon name="check" size={28} stroke={2.4} /></span>
      <h1 class="small">{t('welcome.doneTitle')}</h1>
      <p class="lead">{firstInstance ? t('welcome.doneFirst', { name: firstInstance.name }) : t('welcome.doneText')}</p>
      <button class="btn btn-grad lg" onclick={() => (app.route = { page: 'home' })}>{t('welcome.toLauncher')}<Icon name="arrowRight" size={14} /></button>
    {/if}
  </div>
</div>

<style>
  .welcome {
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
    background: radial-gradient(60% 50% at 50% 20%, color-mix(in srgb, var(--accent-from) 14%, transparent), transparent 70%),
      radial-gradient(50% 50% at 70% 90%, color-mix(in srgb, var(--accent-to) 10%, transparent), transparent 70%);
  }
  .card {
    position: relative;
    width: 560px;
    padding: 36px 40px;
    border-radius: 20px;
    background: rgba(42, 42, 42, 0.85);
    backdrop-filter: blur(10px);
    box-shadow: 0 30px 80px rgba(0, 0, 0, 0.5), inset 0 0 0 1px #363636;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 14px;
    text-align: center;
    animation: pop-in 0.2s ease-out;
  }
  .eyebrow {
    font-size: 12px;
    font-weight: 600;
    text-transform: uppercase;
    letter-spacing: 1px;
    color: var(--accent-text);
  }
  h1 {
    font-size: 30px;
    font-weight: 800;
    letter-spacing: -0.6px;
  }
  h1.small {
    font-size: 24px;
  }
  .brand {
    background: var(--accent);
    -webkit-background-clip: text;
    background-clip: text;
    color: transparent;
  }
  .lead {
    font-size: 14px;
    color: var(--muted);
    line-height: 1.55;
    max-width: 440px;
  }
  .features {
    width: 100%;
    margin: 6px 0;
    display: flex;
    flex-direction: column;
    gap: 10px;
    text-align: left;
  }
  .feature {
    padding: 12px;
    border-radius: 12px;
    background: #2e2e2e;
    display: flex;
    gap: 12px;
    align-items: center;
  }
  .feature .col {
    gap: 2px;
    font-size: 12.5px;
    color: var(--muted-2);
  }
  .feature strong {
    font-size: 13.5px;
    color: var(--text-strong);
  }
  .f-icon {
    width: 38px;
    height: 38px;
    border-radius: 10px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .fine {
    font-size: 12px;
    color: var(--faint);
    line-height: 1.5;
    max-width: 420px;
  }
  .dev {
    display: flex;
    gap: 8px;
    margin-top: 6px;
  }
  .dev .field {
    height: 34px;
  }
  .spin-wrap {
    padding: 10px;
  }
  .spinner.big {
    width: 40px;
    height: 40px;
    border-width: 4px;
    border-radius: 20px;
  }
  .setup {
    width: 100%;
    display: flex;
    flex-direction: column;
    gap: 12px;
    text-align: left;
  }
  .block {
    padding: 14px 16px;
    border-radius: 12px;
    background: #2e2e2e;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .ok {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 13px;
    font-weight: 600;
    color: var(--green);
  }
  .done-icon {
    width: 64px;
    height: 64px;
    border-radius: 32px;
    background: rgba(63, 185, 80, 0.16);
    color: var(--green);
    display: flex;
    align-items: center;
    justify-content: center;
  }
</style>
