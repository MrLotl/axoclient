<script lang="ts">
  import { app, type Page } from '../stores/app.svelte';
  import { t } from '../i18n';
  import Icon from './Icon.svelte';

  const items: { page: Page; icon: string; label: string; also?: Page[] }[] = [
    { page: 'home', icon: 'home', label: 'app.nav.home' },
    { page: 'instances', icon: 'instances', label: 'app.nav.instances', also: ['instance', 'instanceCreate', 'modpacks'] },
    { page: 'localServers', icon: 'servers', label: 'app.nav.localServers' },
    { page: 'skins', icon: 'skins', label: 'app.nav.skins' },
    { page: 'console', icon: 'console', label: 'app.nav.console' },
    { page: 'settings', icon: 'settings', label: 'app.nav.settings' }
  ];

  function isCurrent(item: (typeof items)[number]) {
    return app.route.page === item.page || (item.also?.includes(app.route.page) ?? false);
  }

  // Die Markierung gleitet zum aktiven Eintrag (44px Knopf + 8px Abstand); beim ersten Zeichnen ohne Übergang
  const index = $derived(items.findIndex(isCurrent));
  let ready = $state(false);
  $effect(() => {
    requestAnimationFrame(() => (ready = true));
  });
</script>

<nav aria-label={t('app.nav.main')}>
  {#if index >= 0}<span class="marker" class:ready style="transform: translateY({index * 52}px)" aria-hidden="true"></span>{/if}
  {#each items as item (item.page)}
    <button
      class="nav-btn"
      aria-label={t(item.label)}
      title={t(item.label)}
      aria-current={isCurrent(item) ? 'page' : undefined}
      onclick={() => app.navigate({ page: item.page })}>
      <Icon name={item.icon} size={20} />
      {#if item.page === 'console' && app.running.length > 0}<span class="live"></span>{/if}
    </button>
  {/each}
</nav>

<style>
  nav {
    position: relative;
    width: 66px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    padding-top: 4px;
  }
  .nav-btn {
    position: relative;
    width: 44px;
    height: 44px;
    padding: 0;
    border: 0;
    border-radius: 10px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #8a8a8a;
    background: transparent;
    cursor: pointer;
    transition: background-color 0.12s ease, color 0.2s ease, transform 0.12s ease;
  }
  .nav-btn:active {
    transform: scale(0.94);
  }
  .nav-btn:not([aria-current='page']):hover {
    background: #2a2a2a;
    color: var(--text-2);
  }
  .nav-btn[aria-current='page'] {
    color: var(--accent-text);
  }
  .marker {
    position: absolute;
    top: 4px;
    left: 11px;
    width: 44px;
    height: 44px;
    border-radius: 10px;
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
    pointer-events: none;
  }
  .marker.ready {
    transition: transform var(--dur-slow) var(--ease-out);
  }
  .live {
    position: absolute;
    top: 9px;
    right: 9px;
    width: 7px;
    height: 7px;
    border-radius: 4px;
    background: var(--green);
    box-shadow: 0 0 0 2px var(--bg);
    animation: live-pop 0.3s var(--ease-spring);
  }
  @keyframes live-pop {
    from {
      transform: scale(0);
    }
  }
</style>
