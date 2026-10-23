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
  import SkinViewer from '../lib/components/SkinViewer.svelte';
  import PlayButton from '../lib/components/PlayButton.svelte';
  import InstanceImage from '../lib/components/InstanceImage.svelte';
  import FriendsPanel from './home/FriendsPanel.svelte';
  import ServerPanel from './home/ServerPanel.svelte';
  import { versionLabel } from '../lib/util/format';
  import { pop, fadeSlide, DUR } from '../lib/util/motion';
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

  let pickerOpen = $state(false);
  let profile = $state<MojangProfile | null>(null);
  let viewerHeight = $state(480);
  let stageWidth = $state(800);
  let stage: HTMLDivElement | undefined = $state();
  let card: HTMLDivElement | undefined = $state();

  const selectedId = $derived(
    app.ui?.lastInstance && app.instances.some((i) => i.id === app.ui!.lastInstance) ? app.ui.lastInstance : (app.instances[0]?.id ?? null)
  );
  const current = $derived(app.instances.find((i) => i.id === selectedId) ?? null);
  const account = $derived(app.activeAccount);

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
    // 3D-Ansicht wächst mit dem Fenster, bleibt aber immer ganz sichtbar (Höhe minus Startkarte, Breite des Bereichs)
    const observer = new ResizeObserver(() => {
      if (!stage) return;
      const free = stage.clientHeight - 56 - 24 - (card?.offsetHeight ?? 152);
      const byWidth = (stage.clientWidth - 40) / 0.62;
      stageWidth = stage.clientWidth - 40;
      viewerHeight = Math.round(Math.max(140, Math.min(720, free, byWidth)));
    });
    if (stage) observer.observe(stage);
    if (card) observer.observe(card);
    return () => {
      offs.forEach((o) => o());
      observer.disconnect();
    };
  });

  async function pick(id: string) {
    pickerOpen = false;
    if (app.ui) app.ui.lastInstance = id;
    try {
      await call('instances.get', { id });
    } catch (e) {
      app.error(e);
    }
  }

  const activeCape = $derived(profile?.capes.find((c) => c.active)?.url ?? null);
</script>

<svelte:window
  onmousedown={(e) => {
    if (pickerOpen && !(e.target as HTMLElement).closest('.launch-card')) pickerOpen = false;
  }}
  onkeydown={(e) => {
    if (e.key === 'Escape') pickerOpen = false;
  }} />

<div class="home">
  <div class="stage" bind:this={stage}>
    {#key account?.uuid}
      <SkinViewer
        skin={account ? `/axo/head/${account.uuid.replaceAll('-', '')}?v=${profile?.skinUrl ?? ''}` : null}
        cape={activeCape}
        model={profile?.model === 'slim' ? 'slim' : 'default'}
        width={Math.round(Math.min(viewerHeight * 1.05, Math.max(viewerHeight * 0.62, stageWidth)))}
        height={viewerHeight}
        animate={skinAnimation}
        label={t('home.skinModel')} />
    {/key}

    <div class="launch-card" bind:this={card} in:fadeSlide|global={{ y: 14, delay: 40, duration: DUR.slow }}>
      {#if pickerOpen}
        <div
          class="picker menu"
          role="listbox"
          aria-label={t('home.chooseInstance')}
          in:pop={{ origin: 'bottom center', scale: 0.96, y: 8, duration: DUR.med }}
          out:pop={{ origin: 'bottom center', scale: 0.98, y: 6, duration: DUR.fast }}>
          <span class="section-label" style="padding: 6px 8px 4px">{t('home.chooseInstance')}</span>
          <div class="picker-list">
            {#each app.instances as inst (inst.id)}
              <button class="menu-item pick" class:active={inst.id === selectedId} role="option" aria-selected={inst.id === selectedId} onclick={() => pick(inst.id)}>
                <InstanceImage url={inst.iconUrl} has={inst.hasIcon} name={inst.name} size={32} radius={7} />
                <span class="pick-name truncate">{inst.name}</span>
                <span class="pick-ver">{versionLabel(inst.minecraftVersion, inst.loader)}</span>
              </button>
            {/each}
          </div>
          <div class="menu-sep"></div>
          <button class="menu-item" onclick={() => app.navigate({ page: 'instanceCreate' })}>
            <span style="color: var(--accent-text)"><Icon name="plus" /></span>{t('instances.new')}
          </button>
        </div>
      {/if}

      {#if current}
        <button class="current" aria-haspopup="listbox" aria-expanded={pickerOpen} aria-label={t('home.chooseInstance')} onclick={() => (pickerOpen = !pickerOpen)}>
          {#key current.id}
            <span class="current-swap" in:fadeSlide={{ y: 6, duration: DUR.med }}>
              <InstanceImage url={current.iconUrl} has={current.hasIcon} name={current.name} size={36} />
              <span class="current-text">
                <span class="current-label">{t('home.instance')}</span>
                <span class="current-name truncate">{current.name} <span class="current-ver">· {versionLabel(current.minecraftVersion, current.loader)}</span></span>
              </span>
            </span>
          {/key}
          <span class="chev" class:down={!pickerOpen}><Icon name="chevronUp" /></span>
        </button>
      {:else}
        <button class="current empty" onclick={() => app.navigate({ page: 'instanceCreate' })}>
          <span class="plus"><Icon name="plus" size={18} /></span>
          <span class="current-text">
            <span class="current-label">{t('home.instance')}</span>
            <span class="current-name">{t('home.createFirst')}</span>
          </span>
        </button>
      {/if}

      <PlayButton instanceId={current?.id ?? null} />
    </div>
  </div>

  <aside class="side" in:fadeSlide|global={{ x: 16, y: 0, delay: 80, duration: DUR.slow }}>
    <FriendsPanel />
    <ServerPanel instanceId={current?.id ?? null} />
  </aside>
</div>

<style>
  .home {
    position: relative;
    height: 100%;
    display: flex;
  }
  .stage {
    flex-grow: 1;
    min-width: 0;
    padding: 28px 0;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: flex-end;
    gap: 24px;
  }
  .launch-card {
    position: relative;
    width: 460px;
    max-width: calc(100% - 40px);
    padding: 16px;
    border-radius: 14px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .current-swap {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    align-items: center;
    gap: inherit;
  }
  .picker {
    position: absolute;
    left: 16px;
    right: 16px;
    bottom: 136px;
    z-index: 5;
    background: #2f2f2f;
    box-shadow: 0 16px 40px rgba(0, 0, 0, 0.5), inset 0 0 0 1px #3a3a3a;
    max-height: 420px;
  }
  .picker-list {
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .pick {
    height: 46px;
    padding: 0 8px;
  }
  .pick-name {
    flex-grow: 1;
    min-width: 0;
    font-size: 13.5px;
    font-weight: 600;
  }
  .pick-ver {
    font-size: 12px;
    color: var(--muted-2);
    white-space: nowrap;
  }
  .current {
    height: 52px;
    padding: 0 14px 0 10px;
    border: 0;
    border-radius: 10px;
    background: var(--panel-3);
    display: flex;
    align-items: center;
    gap: 12px;
    cursor: pointer;
    text-align: left;
    transition: background-color 0.12s ease;
  }
  .current:hover {
    background: #3a3a3a;
  }
  .current-text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .current-label {
    font-size: 11px;
    font-weight: 500;
    color: var(--muted-2);
    text-transform: uppercase;
    letter-spacing: 0.6px;
  }
  .current-name {
    font-size: 14px;
    font-weight: 500;
    color: var(--text);
  }
  .current-ver {
    color: var(--muted-2);
    font-weight: 400;
  }
  .chev {
    display: flex;
    color: var(--text-2);
    transition: transform 0.15s ease;
  }
  .chev.down {
    transform: rotate(180deg);
  }
  .plus {
    width: 36px;
    height: 36px;
    border-radius: 8px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .side {
    width: 300px;
    flex-shrink: 0;
    margin: 20px 20px 20px 0;
    display: flex;
    flex-direction: column;
    gap: 16px;
    min-height: 0;
  }
</style>
