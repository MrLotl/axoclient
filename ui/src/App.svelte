<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from './lib/stores/app.svelte';
  import { t } from './lib/i18n';
  import TitleBar from './lib/components/TitleBar.svelte';
  import Sidebar from './lib/components/Sidebar.svelte';
  import Toasts from './lib/components/Toasts.svelte';
  import ResizeHandles from './lib/components/ResizeHandles.svelte';
  import JobBar from './lib/components/JobBar.svelte';
  import Home from './pages/Home.svelte';
  import Instances from './pages/Instances.svelte';
  import InstanceView from './pages/instance/InstanceView.svelte';
  import InstanceCreate from './pages/InstanceCreate.svelte';
  import Modpacks from './pages/Modpacks.svelte';
  import LocalServers from './pages/LocalServers.svelte';
  import Skins from './pages/Skins.svelte';
  import Console from './pages/Console.svelte';
  import Settings from './pages/Settings.svelte';
  import Welcome from './pages/Welcome.svelte';
  import WelcomeBack from './pages/WelcomeBack.svelte';
  import GlobalDialogs from './dialogs/GlobalDialogs.svelte';

  let failed = $state(false);

  onMount(() => {
    app.init().catch((e) => {
      console.error(e);
      failed = true;
    });
    // Rechtsklick-Menü des Browsers aus, Drag & Drop von Dateien nur dort, wo eine Seite es erlaubt
    const prevent = (e: Event) => e.preventDefault();
    document.addEventListener('contextmenu', prevent);
    document.addEventListener('dragover', prevent);
    document.addEventListener('drop', prevent);
    return () => {
      document.removeEventListener('contextmenu', prevent);
      document.removeEventListener('dragover', prevent);
      document.removeEventListener('drop', prevent);
    };
  });

  const fullscreenPage = $derived(app.route.page === 'welcome' || app.route.page === 'welcomeBack');
</script>

<ResizeHandles />

{#if !app.ready}
  <div class="boot">
    <TitleBar minimal />
    <div class="boot-body">
      {#if failed}
        <p>{t('errors.unexpected', { detail: 'Bridge' })}</p>
      {:else}
        <span class="spinner dark"></span>
      {/if}
    </div>
  </div>
{:else if fullscreenPage}
  <div class="shell">
    <TitleBar minimal />
    {#if app.route.page === 'welcome'}<Welcome />{:else}<WelcomeBack />{/if}
  </div>
  <GlobalDialogs />
{:else}
  <div class="shell">
    <TitleBar />
    <div class="frame">
      <Sidebar />
      <main class="content">
        {#key app.route.page + (app.route.id ?? '')}
          {#if app.route.page === 'home'}
            <Home />
          {:else if app.route.page === 'instances'}
            <Instances />
          {:else if app.route.page === 'instance' && app.route.id}
            <InstanceView id={app.route.id} />
          {:else if app.route.page === 'instanceCreate'}
            <InstanceCreate />
          {:else if app.route.page === 'modpacks'}
            <Modpacks />
          {:else if app.route.page === 'localServers'}
            <LocalServers />
          {:else if app.route.page === 'skins'}
            <Skins />
          {:else if app.route.page === 'console'}
            <Console />
          {:else if app.route.page === 'settings'}
            <Settings />
          {/if}
        {/key}
        <JobBar />
      </main>
    </div>
  </div>
  <GlobalDialogs />
{/if}

<Toasts />

<style>
  .shell,
  .boot {
    height: 100%;
    display: flex;
    flex-direction: column;
    background: var(--bg);
  }
  .boot-body {
    flex-grow: 1;
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--muted);
  }
  .frame {
    flex-grow: 1;
    min-height: 0;
    display: flex;
  }
  .content {
    position: relative;
    flex-grow: 1;
    min-width: 0;
    background: var(--surface);
    border-top-left-radius: 18px;
    overflow: hidden;
    display: flex;
    flex-direction: column;
  }
</style>
