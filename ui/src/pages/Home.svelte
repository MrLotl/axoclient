<script lang="ts" module>
  // Nur einmal pro Launcher-Start winken, nicht bei jedem Seitenwechsel
  let greeted = false;
</script>

<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call, on } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import Head from '../lib/components/Head.svelte';
  import SkinViewer from '../lib/components/SkinViewer.svelte';
  import PlayButton from '../lib/components/PlayButton.svelte';
  import FriendsPanel from './home/FriendsPanel.svelte';
  import ServerPanel from './home/ServerPanel.svelte';
  import { loaderName, ago, duration } from '../lib/util/format';
  import { artLayers } from '../lib/util/art';
  import { fadeSlide, DUR } from '../lib/util/motion';
  import type { MojangProfile } from '../lib/types';

  let skinAnimation = $state<'idle' | 'wave'>('idle');
  onMount(() => {
    if (greeted) return;
    greeted = true;
    if (app.settings && !app.settings.appearance.waveOnStart) return;
    // Kurz warten, bis der Skin geladen ist; Winken dauert ~3 s
    const start = setTimeout(() => (skinAnimation = 'wave'), 700);
    const stop = setTimeout(() => (skinAnimation = 'idle'), 700 + 3100);
    return () => {
      clearTimeout(start);
      clearTimeout(stop);
    };
  });

  let profile = $state<MojangProfile | null>(null);
  let leftHeight = $state(700);

  // Regal: zuletzt gespielte Instanzen zuerst
  const shelf = $derived(
    [...app.instances].sort((a, b) => (b.lastPlayed ?? b.createdAt).localeCompare(a.lastPlayed ?? a.createdAt))
  );
  const selectedId = $derived(
    app.ui?.lastInstance && app.instances.some((i) => i.id === app.ui!.lastInstance) ? app.ui.lastInstance : (shelf[0]?.id ?? null)
  );
  const current = $derived(app.instances.find((i) => i.id === selectedId) ?? null);
  const account = $derived(app.activeAccount);
  // Ein Freund, dem man gerade nachjoinen kann, steht direkt unter dem Titel
  const joinable = $derived(app.social.friends.find((f) => f.relation === 'friends' && f.state === 'ingame' && f.join) ?? null);

  // 3D-Skin wächst mit dem Fenster, bleibt aber über dem Regal
  const viewerHeight = $derived(Math.round(Math.max(260, Math.min(600, leftHeight * 0.82))));
  const skinWidth = $derived(Math.round(viewerHeight * 0.62));

  // Mausrad scrollt das Regal seitwärts
  function onShelfWheel(e: WheelEvent) {
    const el = e.currentTarget as HTMLElement;
    if (el.scrollWidth <= el.clientWidth || Math.abs(e.deltaX) > Math.abs(e.deltaY)) return;
    e.preventDefault();
    el.scrollLeft += e.deltaY;
  }

  async function loadProfile() {
    if (!account || account.type !== 'microsoft') {
      profile = null;
      return;
    }
    try {
      profile = await call<MojangProfile>('skins.profile');
    } catch {
      profile = null;
    }
  }

  onMount(() => {
    loadProfile();
    const offs = [on('skins.changed', loadProfile), on('accounts.changed', loadProfile)];
    return () => offs.forEach((o) => o());
  });

  async function pick(id: string) {
    if (app.ui) app.ui.lastInstance = id;
    try {
      await call('instances.get', { id });
    } catch (e) {
      app.error(e);
    }
  }

  // Tasten 1–9 wählen eine Instanz aus dem Regal, Enter startet
  function onkeydown(e: KeyboardEvent) {
    if (app.dialog || app.paletteOpen || e.ctrlKey || e.altKey || e.metaKey) return;
    const el = document.activeElement as HTMLElement | null;
    if (el && (el.tagName === 'INPUT' || el.tagName === 'TEXTAREA' || el.isContentEditable)) return;
    if (/^[1-9]$/.test(e.key)) {
      const target = shelf[Number(e.key) - 1];
      if (target) pick(target.id);
    }
  }

  const activeCape = $derived(profile?.capes.find((c) => c.active)?.url ?? null);
</script>

<svelte:window {onkeydown} />

<div class="home">
  <div class="left" bind:clientHeight={leftHeight}>
    <div class="skin" style="width: {skinWidth}px">
      {#key account?.uuid}
        <SkinViewer
          skin={account ? `/axo/head/${account.uuid.replaceAll('-', '')}?v=${profile?.skinUrl ?? ''}` : null}
          cape={activeCape}
          model={profile?.model === 'slim' ? 'slim' : 'default'}
          width={skinWidth}
          height={viewerHeight}
          animate={skinAnimation}
          label={t('home.skinModel')} />
      {/key}
    </div>

    {#if current}
      {#key current.id}
        <div class="hero" in:fadeSlide={{ y: 12, duration: DUR.slow }}>
          <span class="eyebrow">{current.lastPlayed ? t('home.continue', { when: ago(current.lastPlayed) }) : t('home.neverPlayed')}</span>
          <h1 class="display title">{current.name}</h1>
          <p class="meta">
            <span>Minecraft <b>{current.minecraftVersion}</b></span>
            <span>{loaderName(current.loader)}</span>
            {#if current.modCount}<span>{t('instances.mods', { count: current.modCount })}</span>{/if}
            {#if current.totalSeconds > 0}<span>{@html t('home.played', { time: `<b>${duration(current.totalSeconds)}</b>` })}</span>{/if}
          </p>
          {#if joinable}
            <div class="with">
              <Head uuid={joinable.uuid} size={24} radius={12} />
              <span>{t('home.friendPlaying', { name: joinable.name, server: joinable.join ?? '' })}</span>
              <button class="link" onclick={() => joinable?.join && app.joinAddress(joinable.join)}>{t('home.joinFriend')} →</button>
            </div>
          {/if}
          <div class="actions">
            <div class="play"><PlayButton instanceId={current.id} height={54} /></div>
            <button class="btn btn-ghost lg" onclick={() => app.navigate({ page: 'instance', id: current!.id })}>{t('home.details')}</button>
            <button class="btn btn-ghost lg" onclick={() => app.navigate({ page: 'instance', id: current!.id, tab: 'mods' })}>{t('home.addMods')}</button>
          </div>
        </div>
      {/key}
    {:else}
      <div class="hero" in:fadeSlide={{ y: 12, duration: DUR.slow }}>
        <span class="eyebrow">{t('home.emptyEyebrow')}</span>
        <h1 class="display title">{t('home.emptyTitle')}</h1>
        <p class="meta">{t('instances.emptyText')}</p>
        <div class="actions">
          <button class="btn btn-grad lg" onclick={() => app.navigate({ page: 'instanceCreate' })}><Icon name="plus" />{t('instances.new')}</button>
          <button class="btn btn-ghost lg" onclick={() => app.navigate({ page: 'modpacks' })}>{t('instances.browseModpacks')}</button>
        </div>
      </div>
    {/if}

    {#if shelf.length}
      <div class="shelf-head" style="max-width: calc(100% - {skinWidth}px)"><b>{t('home.yourInstances')}</b><span>{t('home.keysHint')}</span></div>
      <div class="shelf" role="listbox" aria-label={t('home.chooseInstance')} style="max-width: calc(100% - {skinWidth - 8}px)" onwheel={onShelfWheel}>
        {#each shelf as inst, n (inst.id)}
          <button class="tile" class:on={inst.id === selectedId} role="option" aria-selected={inst.id === selectedId} onclick={() => pick(inst.id)} title={inst.name}>
            <span class="art" class:pixel={!inst.hasIcon} style="background-image: {inst.hasIcon ? `url('${inst.iconUrl}')` : artLayers(inst.id, inst.minecraftVersion, app.settings?.appearance.versionPanorama ?? true)}">
              {#if n < 9}<i class="key">{n + 1}</i>{/if}
              {#if inst.running}<i class="live">{t('common.running')}</i>{/if}
            </span>
            <b class="truncate">{inst.name}</b>
          </button>
        {/each}
        <button class="tile new" onclick={() => app.navigate({ page: 'instanceCreate' })}>
          <span class="art"><Icon name="plus" size={20} /></span>
          <b>{t('instances.new')}</b>
        </button>
      </div>
    {/if}
  </div>

  <aside class="side" in:fadeSlide|global={{ x: 16, y: 0, delay: 80, duration: DUR.slow }}>
    <FriendsPanel />
    <ServerPanel instanceId={current?.id ?? null} />
  </aside>
</div>

<style>
  .home {
    height: 100%;
    display: grid;
    grid-template-columns: minmax(0, 1fr) 320px;
    gap: 28px;
    padding: 8px 32px 20px;
  }
  .left {
    position: relative;
    min-width: 0;
    display: flex;
    flex-direction: column;
    justify-content: flex-end;
  }
  .skin {
    position: absolute;
    right: 0;
    bottom: -4px;
    display: flex;
    justify-content: center;
    z-index: 0;
    filter: drop-shadow(0 16px 18px rgba(0, 0, 0, 0.5));
    pointer-events: auto;
  }
  .hero {
    position: relative;
    z-index: 1;
    max-width: 600px;
    margin-bottom: 22px;
  }
  .title {
    font-size: 74px;
    line-height: 0.9;
    margin: 8px 0 12px;
    text-wrap: balance;
    text-shadow: 0 2px 30px rgba(0, 0, 0, 0.45);
    overflow-wrap: anywhere;
  }
  .meta {
    display: flex;
    flex-wrap: wrap;
    gap: 6px 16px;
    font-size: 15px;
    color: var(--muted);
  }
  .meta :global(b) {
    color: var(--text-strong);
    font-weight: 600;
  }
  .with {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-top: 14px;
    font-size: 13.5px;
    color: var(--muted);
  }
  .link {
    border: 0;
    padding: 0;
    background: transparent;
    color: var(--accent-text);
    font: inherit;
    font-weight: 700;
    cursor: pointer;
    white-space: nowrap;
  }
  .link:hover {
    text-decoration: underline;
    text-underline-offset: 3px;
  }
  .actions {
    display: flex;
    align-items: center;
    gap: 12px;
    margin-top: 20px;
  }
  .actions .btn.lg {
    height: 54px;
  }
  .play {
    width: 240px;
    flex-shrink: 0;
  }
  .shelf-head {
    position: relative;
    z-index: 1;
    display: flex;
    align-items: baseline;
    gap: 10px;
    margin-bottom: 10px;
    font-size: 12px;
    color: var(--faint);
  }
  .shelf-head b {
    font-family: var(--display);
    font-size: 14px;
    font-weight: 600;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: var(--muted);
  }
  .shelf {
    position: relative;
    z-index: 1;
    display: flex;
    align-items: flex-end;
    gap: 14px;
    overflow-x: auto;
    overflow-y: hidden;
    padding: 6px 4px 4px;
    margin: -6px -4px 0;
    scrollbar-width: none;
    -webkit-mask-image: linear-gradient(90deg, #000 calc(100% - 90px), transparent);
    mask-image: linear-gradient(90deg, #000 calc(100% - 90px), transparent);
    padding-right: 90px;
  }
  .tile {
    flex: 0 0 auto;
    display: flex;
    flex-direction: column;
    gap: 8px;
    padding: 0;
    border: 0;
    background: transparent;
    text-align: left;
    cursor: pointer;
    width: 140px;
  }
  .art {
    position: relative;
    display: grid;
    place-items: center;
    width: 140px;
    height: 79px;
    border-radius: var(--radius-s);
    background-size: cover;
    background-position: center;
    box-shadow: 0 8px 20px rgba(0, 0, 0, 0.45);
    transition: width var(--dur-med) var(--ease-out), height var(--dur-med) var(--ease-out), transform var(--dur-med) var(--ease-out), box-shadow var(--dur-med) ease;
  }
  .art.pixel {
    image-rendering: pixelated;
  }
  .tile b {
    font-size: 12.5px;
    font-weight: 600;
    color: var(--muted);
  }
  .tile:hover .art {
    transform: translateY(-3px);
  }
  .tile.on {
    width: 168px;
  }
  .tile.on .art {
    width: 168px;
    height: 94px;
    box-shadow: 0 0 0 2px var(--text-strong), 0 0 34px color-mix(in srgb, var(--accent-light) 50%, transparent);
  }
  .tile.on b {
    color: var(--text-strong);
  }
  .key {
    position: absolute;
    top: 6px;
    left: 6px;
    font-style: normal;
    font-family: var(--mono);
    font-size: 10.5px;
    line-height: 16px;
    padding: 0 5px;
    border-radius: 4px;
    color: var(--text-strong);
    background: rgba(10, 11, 13, 0.6);
  }
  .live {
    position: absolute;
    bottom: 6px;
    left: 6px;
    font-style: normal;
    font-size: 10.5px;
    font-weight: 700;
    line-height: 16px;
    padding: 0 6px;
    border-radius: 4px;
    color: #0b130e;
    background: var(--green);
  }
  .tile.new .art {
    background: rgba(255, 255, 255, 0.05);
    border: 1px dashed var(--line-2);
    box-shadow: none;
    color: var(--muted);
  }
  .tile.new:hover .art {
    background: rgba(255, 255, 255, 0.1);
    color: var(--text-strong);
  }
  .side {
    min-height: 0;
    display: flex;
    flex-direction: column;
    gap: 14px;
    padding-top: 6px;
  }
  .side :global(.panel) {
    background: var(--glass);
    border: 1px solid var(--line);
    backdrop-filter: blur(14px);
    box-shadow: 0 24px 60px rgba(0, 0, 0, 0.35);
  }
  @media (max-width: 1240px) {
    .title {
      font-size: 60px;
    }
    .home {
      grid-template-columns: minmax(0, 1fr) 290px;
    }
  }
</style>
