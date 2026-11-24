<script lang="ts">
  import { fadeOnly } from '../../lib/util/motion';
  import { artLayers } from '../../lib/util/art';
  import { onMount } from 'svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import PlayButton from '../../lib/components/PlayButton.svelte';
  import { versionLabel } from '../../lib/util/format';
  import InstanceHero from '../../lib/components/InstanceHero.svelte';
  import Overview from './Overview.svelte';
  import ContentTab from './ContentTab.svelte';
  import WorldsTab from './WorldsTab.svelte';
  import ServersTab from './ServersTab.svelte';
  import ScreenshotsTab from './ScreenshotsTab.svelte';
  import SettingsTab from './SettingsTab.svelte';
  import type { InstanceDetails } from '../../lib/types';
  import { fadeSlide, bounce, DUR } from '../../lib/util/motion';

  let { id }: { id: string } = $props();

  let details = $state<InstanceDetails | null>(null);
  let counts = $state<Record<string, number | null>>({});
  let tab = $state(app.route.tab ?? 'overview');
  // Richtung des Tabwechsels: der neue Inhalt rutscht von der Seite herein, auf der der Tab liegt
  let dir = $state(0);
  function selectTab(next: string) {
    if (next === tab) return;
    const ids = tabs.map((x) => x.id);
    dir = Math.sign(ids.indexOf(next) - ids.indexOf(tab));
    tab = next;
  }

  // Nie endlos drehen: nach 15 s ohne Antwort eine Meldung mit Zurück/Erneut versuchen zeigen
  let slow = $state(false);
  // Nach echtem Speichern wird der Einstellungen-Tab mit den neuen Werten frisch aufgebaut
  let settingsVersion = $state(0);

  async function load() {
    const timer = details ? undefined : setTimeout(() => (slow = true), 15000);
    try {
      details = await call<InstanceDetails>('instances.get', { id });
      slow = false;
    } catch (e) {
      app.error(e);
      app.navigate({ page: 'instances' });
      return;
    } finally {
      clearTimeout(timer);
    }
    // Zahlen an den Tabs, ohne die Tabs selbst zu laden
    const count = async (key: string, type: string, data: object) => {
      try {
        const list = await call<{ source?: string }[]>(type, data);
        counts[key] = list.filter((x) => x.source !== 'axoclient').length;
      } catch {
        counts[key] = null;
      }
    };
    count('mods', 'content.list', { id, type: 'mod' });
    count('packs', 'content.list', { id, type: 'resourcePack' });
    count('shaders', 'content.list', { id, type: 'shaderPack' });
    count('servers', 'serverlist.list', { id });
    count('shots', 'screenshots.list', { id });
  }

  // Instanzbild und Banner direkt im Kopf bearbeiten (Zuschneiden übernimmt ImageBox)
  let imageVersion = $state(0);

  async function saveImage(banner: boolean, base64: string | null) {
    try {
      await call('instances.setImage', { id, banner, base64, fileRef: null });
      imageVersion++;
      await load();
    } catch (e) {
      app.error(e);
    }
  }

  onMount(() => {
    load();
    const offs = [
      on('instances.changed', load),
      on<{ instanceId: string }>('content.changed', (d) => d.instanceId === id && load())
    ];
    return () => offs.forEach((o) => o());
  });

  const tabs = $derived([
    { id: 'overview', label: t('instance.tabs.overview'), count: null },
    { id: 'mods', label: t('instance.tabs.mods'), count: counts.mods ?? null, hidden: details?.instance.loader.type === 'vanilla' },
    { id: 'packs', label: t('instance.tabs.packs'), count: counts.packs ?? null },
    { id: 'shaders', label: t('instance.tabs.shaders'), count: counts.shaders ?? null },
    { id: 'worlds', label: t('instance.tabs.worlds'), count: details?.worlds ?? null },
    { id: 'servers', label: t('instance.tabs.servers'), count: counts.servers ?? null },
    { id: 'shots', label: t('instance.tabs.screenshots'), count: counts.shots ?? null },
    { id: 'settings', label: t('instance.tabs.settings'), count: null }
  ].filter((x) => !x.hidden));

  // Gleitender Unterstrich unter dem aktiven Tab
  let tabsNav: HTMLElement | undefined = $state();
  let indicator = $state<{ left: number; width: number } | null>(null);
  let indicatorReady = $state(false);
  function measureIndicator() {
    const active = tabsNav?.querySelector<HTMLElement>('[aria-current="page"]');
    indicator = active ? { left: active.offsetLeft + 10, width: active.offsetWidth - 20 } : null;
  }
  $effect(() => {
    void tab;
    void tabs;
    if (!tabsNav) return;
    measureIndicator();
    // Beim ersten Anzeigen nicht von links hereinfahren
    requestAnimationFrame(() => (indicatorReady = true));
    const ro = new ResizeObserver(measureIndicator);
    ro.observe(tabsNav);
    for (const el of tabsNav.children) ro.observe(el);
    return () => ro.disconnect();
  });

  const s = $derived(details?.summary);
</script>

<div class="page">
  {#if details && s}
    <InstanceHero
      name={details.instance.name}
      iconUrl={s.hasIcon ? `${s.iconUrl}&r=${imageVersion}` : null}
      bannerUrl={s.bannerUrl ? `${s.bannerUrl}&r=${imageVersion}` : null}
      label={t('instance.label')}
      fallbackArt={artLayers(id, details.instance.minecraft.version, app.settings?.appearance.versionPanorama ?? true)}
      onicon={(b64) => saveImage(false, b64)}
      onbanner={(b64) => saveImage(true, b64)}>
      {#snippet top()}
        <button class="back" onclick={() => app.back()}><Icon name="chevronLeft" />{t('common.back')}</button>
      {/snippet}
      <div class="titles">
        <h1 class="truncate">{details.instance.name}</h1>
        <div class="chips">
          <span class="glass">{versionLabel(details.instance.minecraft.version, details.instance.loader.type)}</span>
          {#if s.modCount > 0}<span class="glass">{t('instances.mods', { count: s.modCount })}</span>{/if}
          {#if details.instance.origin.type === 'modpack' && details.instance.origin.versionName}
            <span class="glass">{t('instance.modpackVersion', { version: details.instance.origin.versionName })}</span>
          {/if}
          {#if details.instance.modLock?.level && details.instance.modLock.level !== 'off'}
            <span class="glass lock" title={t(`modLock.levels.${details.instance.modLock.level}.text`)}>
              <Icon name="lock" size={11} />{t(`modLock.levels.${details.instance.modLock.level}.name`)}
            </span>
          {/if}
        </div>
      </div>
      <button class="hero-btn" aria-label={t('instance.share')} title={t('instance.share')} onclick={() => (app.dialog = { kind: 'share', data: { mode: 'instance', instanceId: id } })}>
        <Icon name="send" size={18} stroke={1.6} />
      </button>
      <button
        class="hero-btn"
        aria-label={t('transfer.button')}
        title={t('transfer.button')}
        onclick={() => (app.dialog = { kind: 'import', data: { target: { id, name: details!.instance.name, loader: details!.instance.loader.type, minecraftVersion: details!.instance.minecraft.version } } })}>
        <Icon name="import" size={18} stroke={1.6} />
      </button>
      <button class="hero-btn" aria-label={t('instance.openFolder')} title={t('instance.openFolder')} onclick={() => call('shell.openFolder', { kind: 'instance', id })}>
        <Icon name="folder" size={18} stroke={1.6} />
      </button>
      <div style="width: 200px"><PlayButton instanceId={id} height={50} /></div>
    </InstanceHero>

    <nav class="tabs" aria-label={t('instance.areas')} bind:this={tabsNav}>
      {#if indicator}
        <span class="indicator" class:ready={indicatorReady} style="transform: translateX({indicator.left}px); width: {indicator.width}px"></span>
      {/if}
      {#each tabs as tb (tb.id)}
        <button class="tab" aria-current={tab === tb.id ? 'page' : undefined} onclick={() => selectTab(tb.id)}>
          {tb.label}
          {#if tb.count !== null}<span class="count" in:bounce>{tb.count}</span>{/if}
        </button>
      {/each}
    </nav>

    {#key tab}
    <div class="tab-body" in:fadeOnly={{ duration: 150 }}>
      {#if tab === 'overview'}
        <Overview {details} />
      {:else if tab === 'mods'}
        <ContentTab instance={details.instance} type="mod" bind:count={counts.mods} />
      {:else if tab === 'packs'}
        <ContentTab instance={details.instance} type="resourcePack" bind:count={counts.packs} />
      {:else if tab === 'shaders'}
        <ContentTab instance={details.instance} type="shaderPack" bind:count={counts.shaders} />
      {:else if tab === 'worlds'}
        <WorldsTab instanceId={id} />
      {:else if tab === 'servers'}
        <ServersTab instanceId={id} bind:count={counts.servers} />
      {:else if tab === 'shots'}
        <ScreenshotsTab instanceId={id} bind:count={counts.shots} />
      {:else if tab === 'settings'}
        {#key details.instance.id + ':' + settingsVersion}<SettingsTab {details} onsaved={async () => { await load(); settingsVersion++; }} onrefresh={load} />{/key}
      {/if}
    </div>
    {/key}
  {:else if slow}
    <div class="loading">
      <p class="slow">{t('instance.loadingSlow')}</p>
      <div class="row" style="gap: 8px">
        <button class="btn btn-ghost" onclick={() => app.back()}>{t('common.back')}</button>
        <button class="btn btn-second" onclick={() => { slow = false; load(); }}>{t('pipeline.action.retry')}</button>
      </div>
    </div>
  {:else}
    <!-- Platzhalter in der Form der Seite, damit beim Laden nichts springt -->
    <div class="skel" aria-busy="true" aria-label={t('common.loading')}>
      <div class="skeleton" style="height: 200px; border-radius: 14px"></div>
      <div class="skel-tabs">
        {#each [70, 56, 92, 64, 58, 62, 84, 78] as w, i (i)}<div class="skeleton" style="width: {w}px; height: 18px; border-radius: 6px"></div>{/each}
      </div>
      <div class="skel-body">
        <div class="skeleton" style="flex: 2"></div>
        <div class="skeleton" style="flex: 1"></div>
      </div>
    </div>
  {/if}
</div>


<style>
  .page {
    height: 100%;
    padding: 8px 32px 20px;
    display: flex;
    flex-direction: column;
    gap: 16px;
    min-height: 0;
  }
  .back {
    align-self: flex-start;
    height: 30px;
    padding: 0 12px 0 8px;
    border: 0;
    border-radius: var(--radius-m);
    background: rgba(10, 11, 13, 0.5);
    backdrop-filter: blur(6px);
    color: var(--text);
    font-size: 13px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 6px;
    cursor: pointer;
  }
  .back:hover {
    background: rgba(0, 0, 0, 0.55);
  }
  .titles {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  h1 {
    font-family: var(--display);
    font-size: 52px;
    font-weight: 700;
    line-height: 0.95;
    letter-spacing: 0em;
    text-transform: uppercase;
    color: var(--text-strong);
  }
  .chips {
    display: flex;
    gap: 8px;
  }
  .glass.lock {
    display: flex;
    align-items: center;
    gap: 5px;
    cursor: help;
  }
  .hero-btn {
    width: 44px;
    height: 44px;
    padding: 0;
    border: 0;
    border-radius: var(--radius-m);
    background: rgba(255, 255, 255, 0.1);
    backdrop-filter: blur(8px);
    color: var(--text-strong);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    flex-shrink: 0;
  }
  .hero-btn:hover {
    background: rgba(255, 255, 255, 0.16);
  }
  .tabs {
    position: relative;
    flex-shrink: 0;
    height: 40px;
    display: flex;
    gap: 22px;
    border-bottom: 1px solid var(--line);
  }
  .tab {
    position: relative;
    height: 40px;
    padding: 0;
    border: 0;
    background: transparent;
    cursor: pointer;
    font-family: var(--display);
    font-size: 15px;
    font-weight: 600;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    white-space: nowrap;
    display: flex;
    align-items: center;
    gap: 7px;
    color: var(--muted-2);
  }
  .tab:hover {
    color: var(--text-2);
  }
  .tab[aria-current='page'] {
    color: var(--text-strong);
  }
  .tab {
    transition: color 0.2s ease;
  }
  .indicator {
    position: absolute;
    left: 0;
    bottom: -1px;
    height: 2px;
    border-radius: 2px;
    background: var(--text-strong);
    pointer-events: none;
  }
  .indicator.ready {
    transition: transform 0.28s cubic-bezier(0.22, 1, 0.36, 1), width 0.28s cubic-bezier(0.22, 1, 0.36, 1);
  }
  .count {
    min-width: 18px;
    height: 18px;
    padding: 0 5px;
    border-radius: var(--radius-s);
    font-family: var(--mono);
    font-size: 10.5px;
    font-weight: 500;
    letter-spacing: 0;
    line-height: 18px;
    text-align: center;
    background: rgba(255, 255, 255, 0.07);
    color: var(--muted-2);
  }
  .tab[aria-current='page'] .count {
    background: rgba(255, 255, 255, 0.14);
    color: var(--text-strong);
  }
  .tab-body {
    flex-grow: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
  }
  .loading {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 14px;
  }
  .skel {
    flex-grow: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .skel-tabs {
    height: 38px;
    padding: 0 12px;
    display: flex;
    align-items: center;
    gap: 22px;
    border-bottom: 1px solid var(--panel-3);
  }
  .skel-body {
    flex-grow: 1;
    min-height: 0;
    display: flex;
    gap: 16px;
  }
  .slow {
    max-width: 360px;
    text-align: center;
    font-size: 13px;
    color: var(--muted);
  }
</style>
