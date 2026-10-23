<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import PlayButton from '../../lib/components/PlayButton.svelte';
  import { versionLabel } from '../../lib/util/format';
  import { pickImage } from '../../lib/util/files';
  import InstanceImage from '../../lib/components/InstanceImage.svelte';
  import ImageCropDialog from '../../lib/components/ImageCropDialog.svelte';
  import Overview from './Overview.svelte';
  import ContentTab from './ContentTab.svelte';
  import WorldsTab from './WorldsTab.svelte';
  import ServersTab from './ServersTab.svelte';
  import ScreenshotsTab from './ScreenshotsTab.svelte';
  import SettingsTab from './SettingsTab.svelte';
  import type { InstanceDetails } from '../../lib/types';
  import { fadeSlide, fadeLoad, bounce, DUR } from '../../lib/util/motion';

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

  // Instanzbild und Banner direkt im Kopf bearbeiten
  let cropping = $state<{ banner: boolean; url: string } | null>(null);
  let imageVersion = $state(0);

  async function chooseImage(banner: boolean) {
    const image = await pickImage();
    if (image) cropping = { banner, url: image.url };
  }

  async function saveImage(banner: boolean, base64: string | null) {
    if (cropping) URL.revokeObjectURL(cropping.url);
    cropping = null;
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
  const banner = $derived(s?.bannerUrl ?? (s?.hasIcon ? s.iconUrl : null));
</script>

<div class="page">
  {#if details && s}
    <section class="hero" aria-label={t('instance.label')}>
      {#if banner}
        <img class="bg" class:blurred={!s.bannerUrl} src={`${banner}&r=${imageVersion}`} alt="" use:fadeLoad={`${banner}&r=${imageVersion}`} />
      {:else}
        <span class="bg fallback"></span>
      {/if}
      <div class="shade"></div>
      <!-- Banner bearbeiten: Stift oben rechts beim Hovern -->
      <div class="banner-edit">
        {#if s.bannerUrl}
          <button class="hero-mini" aria-label={t('instance.settings.bannerAuto')} title={t('instance.settings.bannerAuto')} onclick={() => saveImage(true, null)}>
            <Icon name="x" size={13} />
          </button>
        {/if}
        <button class="hero-mini" aria-label={t('instance.editBanner')} title={t('instance.editBanner')} onclick={() => chooseImage(true)}>
          <Icon name="pencil" size={14} />
        </button>
      </div>
      <div class="hero-content">
        <button class="back" onclick={() => app.back()}><Icon name="chevronLeft" />{t('common.back')}</button>
        <div class="hero-row">
          <!-- Instanzbild: beim Hovern bearbeiten -->
          <div class="hero-icon">
            <button class="icon-pick" aria-label={t('instance.editIcon')} title={t('instance.editIcon')} onclick={() => chooseImage(false)}>
              {#key imageVersion}
                <InstanceImage url={`${s.iconUrl}&r=${imageVersion}`} has={s.hasIcon} name={details.instance.name} size={84} radius={18} />
              {/key}
              <span class="icon-overlay"><Icon name="pencil" size={18} /></span>
            </button>
            {#if s.hasIcon}
              <button class="icon-x" aria-label={t('instance.removeIcon')} title={t('instance.removeIcon')} onclick={() => saveImage(false, null)}><Icon name="x" size={11} /></button>
            {/if}
          </div>
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
          <div style="width: 170px"><PlayButton instanceId={id} height={44} compact /></div>
        </div>
      </div>
    </section>

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
    <div class="tab-body" in:fadeSlide={{ x: dir * 14, y: dir === 0 ? 6 : 0, duration: DUR.med + 20 }}>
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
        {#key details.instance}<SettingsTab {details} onsaved={load} />{/key}
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

{#if cropping}
  {@const isBanner = cropping.banner}
  <ImageCropDialog
    src={cropping.url}
    aspect={isBanner ? 5 : 1}
    outputWidth={isBanner ? 1500 : 256}
    round={!isBanner}
    title={isBanner ? t('instance.settings.banner') : t('instance.settings.icon')}
    onconfirm={(b64) => saveImage(isBanner, b64)}
    oncancel={() => {
      URL.revokeObjectURL(cropping!.url);
      cropping = null;
    }} />
{/if}

<style>
  .page {
    height: 100%;
    padding: 24px 28px;
    display: flex;
    flex-direction: column;
    gap: 16px;
    min-height: 0;
  }
  .hero {
    position: relative;
    height: 200px;
    flex-shrink: 0;
    border-radius: 14px;
    overflow: hidden;
    background: #1a1a1a;
  }
  .bg {
    position: absolute;
    inset: 0;
    width: 100%;
    height: 100%;
    object-fit: cover;
    object-position: 50% 60%;
  }
  .bg.blurred {
    inset: -40px;
    width: calc(100% + 80px);
    height: calc(100% + 80px);
    filter: blur(30px) saturate(1.2);
    opacity: 0.8;
  }
  .bg.fallback {
    background: var(--accent-soft-strong);
  }
  .shade {
    position: absolute;
    inset: 0;
    background: linear-gradient(90deg, rgba(24, 24, 24, 0.94) 0%, rgba(24, 24, 24, 0.72) 45%, rgba(24, 24, 24, 0.35) 100%);
  }
  .hero-content {
    position: absolute;
    inset: 0;
    padding: 16px 20px 20px;
    display: flex;
    flex-direction: column;
    justify-content: space-between;
  }
  .back {
    align-self: flex-start;
    height: 30px;
    padding: 0 12px 0 8px;
    border: 0;
    border-radius: 8px;
    background: rgba(0, 0, 0, 0.35);
    backdrop-filter: blur(6px);
    color: var(--text);
    font-size: 13px;
    font-weight: 500;
    display: flex;
    align-items: center;
    gap: 6px;
    cursor: pointer;
  }
  .back:hover {
    background: rgba(0, 0, 0, 0.55);
  }
  .hero-row {
    display: flex;
    align-items: flex-end;
    gap: 16px;
  }
  .titles {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  h1 {
    font-size: 28px;
    font-weight: 700;
    letter-spacing: -0.4px;
    color: #fff;
  }
  .chips {
    display: flex;
    gap: 8px;
  }
  .glass {
    height: 24px;
    padding: 0 9px;
    border-radius: 6px;
    background: rgba(0, 0, 0, 0.45);
    backdrop-filter: blur(6px);
    color: #f2f2f2;
    font-size: 12px;
    font-weight: 600;
    line-height: 24px;
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
    border-radius: 10px;
    background: rgba(255, 255, 255, 0.09);
    backdrop-filter: blur(8px);
    color: #f2f2f2;
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
    height: 38px;
    display: flex;
    gap: 4px;
    border-bottom: 1px solid #2f2f2f;
  }
  .tab {
    position: relative;
    height: 38px;
    padding: 0 12px;
    border: 0;
    background: transparent;
    cursor: pointer;
    font-size: 13px;
    font-weight: 600;
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
  .hero-icon {
    position: relative;
    flex-shrink: 0;
  }
  .icon-pick {
    position: relative;
    display: block;
    padding: 0;
    border: 0;
    border-radius: 18px;
    background: none;
    cursor: pointer;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
  }
  .icon-overlay {
    position: absolute;
    inset: 0;
    border-radius: 18px;
    background: rgba(0, 0, 0, 0.5);
    color: #fff;
    display: flex;
    align-items: center;
    justify-content: center;
    opacity: 0;
    transition: opacity 0.15s ease;
  }
  .icon-pick:hover .icon-overlay,
  .icon-pick:focus-visible .icon-overlay {
    opacity: 1;
  }
  .icon-x {
    position: absolute;
    top: -6px;
    right: -6px;
    width: 22px;
    height: 22px;
    padding: 0;
    border: 0;
    border-radius: 11px;
    background: #3a3a3a;
    color: #fff;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.45);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    opacity: 0;
    transition: opacity 0.15s ease, background-color 0.12s ease;
  }
  .hero-icon:hover .icon-x,
  .icon-x:focus-visible {
    opacity: 1;
  }
  .icon-x:hover {
    background: var(--red);
  }
  .banner-edit {
    position: absolute;
    z-index: 2;
    top: 14px;
    right: 14px;
    display: flex;
    gap: 6px;
    opacity: 0;
    transition: opacity 0.15s ease;
  }
  .hero:hover .banner-edit,
  .banner-edit:focus-within {
    opacity: 1;
  }
  .hero-mini {
    width: 34px;
    height: 34px;
    padding: 0;
    border: 0;
    border-radius: 9px;
    background: rgba(0, 0, 0, 0.45);
    backdrop-filter: blur(8px);
    color: #f2f2f2;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .hero-mini:hover {
    background: rgba(0, 0, 0, 0.65);
  }
  .indicator {
    position: absolute;
    left: 0;
    bottom: -1px;
    height: 2px;
    border-radius: 2px;
    background: linear-gradient(90deg, var(--accent-from), var(--accent-to));
    pointer-events: none;
  }
  .indicator.ready {
    transition: transform 0.28s cubic-bezier(0.22, 1, 0.36, 1), width 0.28s cubic-bezier(0.22, 1, 0.36, 1);
  }
  .count {
    min-width: 18px;
    height: 18px;
    padding: 0 5px;
    border-radius: 9px;
    font-size: 11px;
    line-height: 18px;
    text-align: center;
    background: #2c2c2c;
    color: var(--muted-2);
  }
  .tab[aria-current='page'] .count {
    background: color-mix(in srgb, var(--accent-from) 18%, transparent);
    color: var(--accent-text);
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
    border-bottom: 1px solid #2f2f2f;
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
