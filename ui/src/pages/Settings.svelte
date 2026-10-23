<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import Toggle from '../lib/components/Toggle.svelte';
  import Select from '../lib/components/Select.svelte';
  import Segmented from '../lib/components/Segmented.svelte';
  import MemorySlider from '../lib/components/MemorySlider.svelte';
  import { bytes } from '../lib/util/format';
  import { copyText } from '../lib/util/clipboard';
  import { fadeSlide, DUR } from '../lib/util/motion';
  import type { DiagnosticItem, FileRef, LauncherSettings, StorageOverview } from '../lib/types';

  const s = $derived(app.settings!);
  let section = $state('general');
  let storage = $state<StorageOverview | null>(null);
  let diagnostics = $state<DiagnosticItem[] | null>(null);
  let diagnosing = $state(false);
  let contrast = $state<{ ratio: number; ok: boolean } | null>(null);
  let memory = $state(app.settings?.defaults.memoryMb ?? 6144);
  let javaInfo = $state<string>('');

  const sections = [
    { id: 'general', icon: 'settings', label: 'settings.sections.general' },
    { id: 'game', icon: 'play', label: 'settings.sections.game' },
    { id: 'appearance', icon: 'palette', label: 'settings.sections.appearance' },
    { id: 'notifications', icon: 'bell', label: 'settings.sections.notifications' },
    { id: 'privacy', icon: 'shield', label: 'settings.sections.privacy' },
    { id: 'integrations', icon: 'puzzle', label: 'settings.sections.integrations' },
    { id: 'updates', icon: 'download', label: 'settings.sections.updates' },
    { id: 'storage', icon: 'folder', label: 'settings.sections.storage' },
    { id: 'diagnostics', icon: 'wrench', label: 'settings.sections.diagnostics' }
  ];

  const notificationTypes = [
    'friendRequest', 'friendAccepted', 'friendOnline', 'friendOnServer', 'friendOpenWorld', 'invite', 'inbox', 'modUpdates',
    'modpackUpdates', 'capeReview', 'launcherUpdate', 'jobFinished', 'authExpired', 'lowDiskSpace', 'serverCrashed', 'serverPlayerJoined'
  ];

  function set(change: (x: LauncherSettings) => void) {
    app.saveSettings(change);
  }

  let memoryTimer: ReturnType<typeof setTimeout> | undefined;
  $effect(() => {
    const value = memory;
    clearTimeout(memoryTimer);
    if (value !== app.settings?.defaults.memoryMb) memoryTimer = setTimeout(() => set((x) => (x.defaults.memoryMb = value)), 500);
  });

  async function checkContrast(from: string, to: string, angle: number) {
    try {
      contrast = await call('appearance.contrast', { from, to, angle });
    } catch {
      contrast = null;
    }
  }

  function setPreset(id: string) {
    const p = app.presets[id];
    if (!p) return;
    set((x) => {
      x.appearance.preset = id;
      x.appearance.accent = { ...p };
    });
    contrast = null;
  }

  function setCustom(field: 'from' | 'to' | 'angle', value: string | number) {
    const accent = { ...s.appearance.accent, [field]: value };
    checkContrast(accent.from, accent.to, accent.angle);
    set((x) => {
      x.appearance.preset = 'custom';
      x.appearance.accent = accent;
    });
  }

  async function chooseJava() {
    try {
      const files = await call<FileRef[]>('dialog.openFile', { purpose: 'java' });
      if (!files[0]) return;
      const r = await call<{ path: string; major: number | null }>('settings.checkJava', { fileRef: files[0].id });
      if (r.major === null) {
        app.toast(t('settings.javaBroken'), 'error');
        return;
      }
      javaInfo = `Java ${r.major}`;
      set((x) => (x.defaults.java = r.path));
    } catch (e) {
      app.error(e);
    }
  }

  async function loadStorage() {
    storage = null;
    try {
      storage = await call<StorageOverview>('storage.scan');
    } catch (e) {
      app.error(e);
    }
  }

  async function clean(area: string) {
    try {
      const r = await call<{ freed: number; overview: StorageOverview }>('storage.clean', { area });
      storage = r.overview;
      app.toast(t('settings.storage.freed', { size: bytes(r.freed) }), 'success');
    } catch (e) {
      app.error(e);
    }
  }

  async function runDiagnostics() {
    diagnosing = true;
    try {
      diagnostics = await call<DiagnosticItem[]>('diagnostics.run');
    } catch (e) {
      app.error(e);
    } finally {
      diagnosing = false;
    }
  }

  async function copyReport() {
    if (!diagnostics) return;
    const text = [
      `AxoClient ${app.version} · ${new Date().toLocaleString('de-DE')}`,
      ...diagnostics.map((d) => `${d.status === 'ok' ? 'OK     ' : d.status === 'warning' ? 'WARNUNG' : 'FEHLER '} [${t(`diagnostics.areas.${d.area}`)}] ${tr(d.title)}${d.detail ? ' – ' + tr(d.detail) : ''}`)
    ].join('\n');
    try {
      await copyText(await call<string>('text.redact', { text }));
      app.toast(t('settings.diagnostics.copied'), 'success');
    } catch (e) {
      app.error(e);
    }
  }

  async function checkUpdate() {
    try {
      const u = await call('update.check');
      if (!u) app.toast(t('settings.updates.upToDate'), 'success');
      else app.dialog = { kind: 'update' };
    } catch (e) {
      app.error(e);
    }
  }

  function deleteOnline() {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('settings.privacy.deleteTitle'),
        text: t('settings.privacy.deleteText'),
        confirm: t('settings.privacy.deleteConfirm'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            await call('social.deleteAccount');
            app.toast(t('settings.privacy.deleted'), 'success');
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }

  onMount(() => {
    if (s.appearance.preset === 'custom') checkContrast(s.appearance.accent.from, s.appearance.accent.to, s.appearance.accent.angle);
  });

  $effect(() => {
    if (section === 'storage' && !storage) loadStorage();
  });

  const storageParts = $derived(
    storage
      ? [
          { id: null, label: t('settings.storage.instances'), size: storage.instances.reduce((a, i) => a + i.worlds + i.mods + i.rest, 0) },
          { id: 'backups', label: t('settings.storage.backups'), size: storage.backups, action: 'settings.storage.cleanBackups' },
          { id: 'game', label: t('settings.storage.game'), size: storage.game, action: 'settings.storage.cleanGame' },
          { id: 'java', label: t('settings.storage.java'), size: storage.java, action: 'settings.storage.cleanJava' },
          { id: 'cache', label: t('settings.storage.cache'), size: storage.cache, action: 'settings.storage.cleanCache' },
          { id: 'logs', label: t('settings.storage.logs'), size: storage.logs, action: 'settings.storage.cleanLogs' },
          { id: null, label: t('settings.storage.servers'), size: storage.servers }
        ]
      : []
  );

  // Markierung in der Navigation gleitet zum gewählten Bereich
  let side: HTMLElement | undefined = $state();
  let marker = $state<{ top: number; height: number } | null>(null);
  let markerReady = $state(false);
  $effect(() => {
    void section;
    const el = side?.querySelector<HTMLElement>('[aria-current="page"]');
    if (!el) return;
    marker = { top: el.offsetTop, height: el.offsetHeight };
    if (!markerReady) requestAnimationFrame(() => (markerReady = true));
  });
</script>

{#if s}
  <div class="page">
    <nav class="side" aria-label={t('settings.title')} bind:this={side}>
      {#if marker}<span class="marker" class:ready={markerReady} style="transform: translateY({marker.top}px); height: {marker.height}px" aria-hidden="true"></span>{/if}
      <h1>{t('settings.title')}</h1>
      {#each sections as sec (sec.id)}
        <button class="nav" aria-current={section === sec.id ? 'page' : undefined} onclick={() => (section = sec.id)}>
          <Icon name={sec.icon} />{t(sec.label)}
        </button>
      {/each}
      <div class="grow"></div>
      <span class="about">AxoClient {app.version}</span>
      <button class="link" onclick={() => call('shell.openFolder', { kind: 'data' })}>{t('settings.openDataFolder')}</button>
      <button class="link" onclick={() => call('shell.openFolder', { kind: 'logs' })}>{t('settings.openLogs')}</button>
    </nav>

    <div class="content">
      <!-- Mittig mit fester Breite statt oben links zu kleben -->
      {#key section}
      <div class="inner" in:fadeSlide={{ y: 8, duration: DUR.med + 20 }}>
      {#if section === 'general'}
        <h2>{t('settings.sections.general')}</h2>
        <div class="card">
          <div class="item">
            <div class="text"><span class="name">{t('settings.autostart')}</span><span class="desc">{t('settings.autostartHint')}</span></div>
            <Toggle checked={s.autostart} label={t('settings.autostart')} onchange={(v) => set((x) => (x.autostart = v))} />
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('settings.afterLaunch')}</span><span class="desc">{t('settings.afterLaunchHint')}</span></div>
            <Select
              value={s.afterLaunch}
              label={t('settings.afterLaunch')}
              width="220px"
              options={[
                { value: 'open', label: t('settings.afterLaunchOptions.open') },
                { value: 'minimize', label: t('settings.afterLaunchOptions.minimize') },
                { value: 'hide', label: t('settings.afterLaunchOptions.hide') },
                { value: 'close', label: t('settings.afterLaunchOptions.close') }
              ]}
              onchange={(v) => set((x) => (x.afterLaunch = v as LauncherSettings['afterLaunch']))} />
          </div>
        </div>
      {:else if section === 'game'}
        <h2>{t('settings.sections.game')}</h2>
        <div class="card">
          <div class="item">
            <div class="text"><span class="name">{t('settings.windowMode')}</span><span class="desc">{t('settings.windowModeHint')}</span></div>
            <Segmented
              value={s.game.windowMode}
              label={t('settings.windowMode')}
              options={[
                { value: 'window', label: t('settings.windowModes.window') },
                { value: 'maximized', label: t('settings.windowModes.maximized') },
                { value: 'fullscreen', label: t('settings.windowModes.fullscreen') }
              ]}
              onchange={(v) => set((x) => (x.game.windowMode = v as LauncherSettings['game']['windowMode']))} />
          </div>
          {#if s.game.windowMode === 'window'}
            <div class="item">
              <div class="text"><span class="name">{t('settings.windowSize')}</span></div>
              <div class="row">
                <span class="field small"><input type="number" min="320" max="7680" value={s.game.windowSize.w} onchange={(e) => set((x) => (x.game.windowSize = { ...x.game.windowSize, w: +(e.currentTarget as HTMLInputElement).value }))} aria-label={t('settings.width')} /></span>
                <span class="hint">×</span>
                <span class="field small"><input type="number" min="240" max="4320" value={s.game.windowSize.h} onchange={(e) => set((x) => (x.game.windowSize = { ...x.game.windowSize, h: +(e.currentTarget as HTMLInputElement).value }))} aria-label={t('settings.height')} /></span>
                <span class="hint">px</span>
              </div>
            </div>
          {/if}
          <div class="item">
            <div class="text"><span class="name">{t('settings.checkMods')}</span><span class="desc">{t('settings.checkModsHint')}</span></div>
            <Toggle checked={s.game.checkModsOnLaunch} label={t('settings.checkMods')} onchange={(v) => set((x) => (x.game.checkModsOnLaunch = v))} />
          </div>
        </div>
        <h3>{t('settings.defaults')}</h3>
        <div class="card">
          <div class="item column">
            <div class="text"><span class="name">{t('settings.defaultMemory')}</span><span class="desc">{t('settings.defaultMemoryHint')}</span></div>
            <MemorySlider bind:value={memory} />
          </div>
          <div class="item">
            <div class="text">
              <span class="name">Java</span>
              <span class="desc">{s.defaults.java === 'auto' ? t('settings.javaAuto') : `${s.defaults.java}${javaInfo ? ` · ${javaInfo}` : ''}`}</span>
            </div>
            <div class="row">
              {#if s.defaults.java !== 'auto'}<button class="btn btn-flat sm" onclick={() => set((x) => (x.defaults.java = 'auto'))}>{t('settings.javaReset')}</button>{/if}
              <button class="btn btn-second sm" onclick={chooseJava}>{t('settings.javaChoose')}</button>
            </div>
          </div>
        </div>
      {:else if section === 'appearance'}
        <h2>{t('settings.sections.appearance')}</h2>
        <p class="intro">{t('settings.appearanceHint')}</p>
        <div class="presets">
          {#each Object.entries(app.presets) as [id, p] (id)}
            <button class="preset" class:on={s.appearance.preset === id} aria-pressed={s.appearance.preset === id} onclick={() => setPreset(id)}>
              <span class="swatch" style="background: linear-gradient({p.angle}deg, {p.from}, {p.to})"></span>
              <span>{t(`settings.presets.${id}`)}</span>
            </button>
          {/each}
          <button class="preset" class:on={s.appearance.preset === 'custom'} aria-pressed={s.appearance.preset === 'custom'} onclick={() => setCustom('from', s.appearance.accent.from)}>
            <span class="swatch custom" style="background: conic-gradient(from 0deg, #db2777, #ea580c, #facc15, #16a34a, #2563eb, #7c3aed, #db2777)"></span>
            <span>{t('settings.presets.custom')}</span>
          </button>
        </div>
        {#if s.appearance.preset === 'custom'}
          <div class="card">
            <div class="item">
              <div class="text"><span class="name">{t('settings.colors')}</span></div>
              <div class="row">
                <label class="color"><input type="color" value={s.appearance.accent.from} onchange={(e) => setCustom('from', (e.currentTarget as HTMLInputElement).value)} aria-label={t('settings.colorFrom')} /></label>
                <Icon name="arrowRight" />
                <label class="color"><input type="color" value={s.appearance.accent.to} onchange={(e) => setCustom('to', (e.currentTarget as HTMLInputElement).value)} aria-label={t('settings.colorTo')} /></label>
              </div>
            </div>
            <div class="item">
              <div class="text"><span class="name">{t('settings.angle')}</span></div>
              <div class="row" style="width: 260px">
                <input type="range" min="0" max="360" step="5" value={s.appearance.accent.angle} onchange={(e) => setCustom('angle', +(e.currentTarget as HTMLInputElement).value)} aria-label={t('settings.angle')} style="flex-grow: 1" />
                <span class="hint" style="width: 40px">{s.appearance.accent.angle}°</span>
              </div>
            </div>
            {#if contrast && !contrast.ok}
              <p class="warn"><Icon name="warning" size={14} />{t('settings.lowContrast', { ratio: contrast.ratio.toLocaleString('de-DE') })}</p>
            {/if}
          </div>
        {/if}
        <div class="card">
          <div class="item">
            <div class="text"><span class="name">{t('settings.waveOnStart')}</span><span class="desc">{t('settings.waveOnStartHint')}</span></div>
            <Toggle checked={s.appearance.waveOnStart} label={t('settings.waveOnStart')} onchange={(v) => set((x) => (x.appearance.waveOnStart = v))} />
          </div>
        </div>
        <h3>{t('settings.samplePreview')}</h3>
        <div class="sample" aria-hidden="true">
          <div class="mock-side">
            <span><Icon name="home" /></span>
            <span class="on"><Icon name="instances" /></span>
            <span><Icon name="servers" /></span>
            <span><Icon name="skins" /></span>
          </div>
          <div class="mock-main">
            <div class="mock-tabs"><span class="on">{t('instance.tabs.overview')}</span><span>{t('instance.tabs.mods')}</span><span>{t('instance.tabs.worlds')}</span></div>
            <div class="mock-row">
              <button class="btn btn-grad" tabindex="-1"><Icon name="play" size={12} />{t('common.play')}</button>
              <button class="btn btn-second" tabindex="-1">{t('settings.sampleButton')}</button>
              <span class="chip accent">{t('settings.sampleChip')}</span>
              <Toggle checked={true} label={t('settings.sampleButton')} />
            </div>
            <div class="mock-bar"><span></span></div>
          </div>
        </div>
      {:else if section === 'notifications'}
        <h2>{t('settings.sections.notifications')}</h2>
        <div class="card">
          <div class="item">
            <div class="text"><span class="name">{t('settings.toasts')}</span><span class="desc">{t('settings.toastsHint')}</span></div>
            <Toggle checked={s.notifications.windowsToasts} label={t('settings.toasts')} onchange={(v) => set((x) => (x.notifications.windowsToasts = v))} />
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('settings.quietWhilePlaying')}</span><span class="desc">{t('settings.quietWhilePlayingHint')}</span></div>
            <Toggle checked={s.notifications.quietWhilePlaying} label={t('settings.quietWhilePlaying')} onchange={(v) => set((x) => (x.notifications.quietWhilePlaying = v))} />
          </div>
          {#if s.notifications.quietWhilePlaying}
            <div class="item">
              <div class="text"><span class="name">{t('settings.invitesWhilePlaying')}</span></div>
              <Toggle checked={s.notifications.invitesWhilePlaying} label={t('settings.invitesWhilePlaying')} onchange={(v) => set((x) => (x.notifications.invitesWhilePlaying = v))} />
            </div>
          {/if}
          <div class="item">
            <div class="text"><span class="name">{t('settings.friendOnServerScope')}</span><span class="desc">{t('settings.friendOnServerScopeHint')}</span></div>
            <Segmented
              value={s.notifications.friendOnServerScope}
              label={t('settings.friendOnServerScope')}
              options={[{ value: 'active', label: t('settings.scopeActive') }, { value: 'all', label: t('settings.scopeAll') }]}
              onchange={(v) => set((x) => (x.notifications.friendOnServerScope = v as 'active' | 'all'))} />
          </div>
        </div>
        <h3>{t('settings.notificationTypes')}</h3>
        <div class="card grid2">
          {#each notificationTypes as type (type)}
            <div class="item compact">
              <span class="name small">{t(`settings.types.${type}`)}</span>
              <Toggle checked={s.notifications.types[type]} label={t(`settings.types.${type}`)} onchange={(v) => set((x) => (x.notifications.types[type] = v))} />
            </div>
          {/each}
        </div>
      {:else if section === 'privacy'}
        <h2>{t('settings.sections.privacy')}</h2>
        <div class="card">
          <div class="item">
            <div class="text"><span class="name">{t('settings.showOnline')}</span><span class="desc">{t('settings.showOnlineHint')}</span></div>
            <Toggle checked={s.social.showOnline} label={t('settings.showOnline')} onchange={(v) => set((x) => (x.social.showOnline = v))} />
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('settings.allowJoin')}</span><span class="desc">{t('settings.allowJoinHint')}</span></div>
            <Toggle checked={s.social.allowJoin} label={t('settings.allowJoin')} onchange={(v) => set((x) => (x.social.allowJoin = v))} />
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('settings.streamerMode')}</span><span class="desc">{t('settings.streamerModeHint')}</span></div>
            <Toggle checked={s.integrations.streamerMode} label={t('settings.streamerMode')} onchange={(v) => set((x) => (x.integrations.streamerMode = v))} />
          </div>
        </div>
        <h3>{t('settings.privacy.service')}</h3>
        <div class="card">
          <p class="desc" style="line-height: 1.55">{t('settings.privacy.serviceText')}</p>
          <div class="item">
            <div class="text"><span class="name">{t('settings.privacy.deleteTitle')}</span><span class="desc">{t('settings.privacy.deleteHint')}</span></div>
            <button class="btn btn-danger sm" style="background: var(--red-soft)" onclick={deleteOnline}>{t('settings.privacy.deleteConfirm')}</button>
          </div>
        </div>
      {:else if section === 'integrations'}
        <h2>{t('settings.sections.integrations')}</h2>
        <div class="card">
          <div class="item">
            <div class="text"><span class="name">{t('settings.discord')}</span><span class="desc">{t('settings.discordHint')}</span></div>
            <Toggle checked={s.integrations.discordRpc} label={t('settings.discord')} onchange={(v) => set((x) => (x.integrations.discordRpc = v))} />
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('settings.relay')}</span><span class="desc">{t('settings.relayHint')}</span></div>
            <Toggle checked={s.integrations.friendJoinRelay} label={t('settings.relay')} onchange={(v) => set((x) => (x.integrations.friendJoinRelay = v))} />
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('settings.tablist')}</span><span class="desc">{t('settings.tablistHint')}</span></div>
            <Toggle checked={s.integrations.tablistIcon} label={t('settings.tablist')} onchange={(v) => set((x) => (x.integrations.tablistIcon = v))} />
          </div>
        </div>
      {:else if section === 'updates'}
        <h2>{t('settings.sections.updates')}</h2>
        <div class="card">
          <div class="item">
            <div class="text"><span class="name">{t('settings.updates.version', { version: app.version })}</span><span class="desc">{app.update ? t('settings.updates.available', { version: app.update.version }) : t('settings.updates.hint')}</span></div>
            <button class="btn btn-second sm" onclick={checkUpdate}><Icon name="refresh" size={13} />{t('settings.updates.check')}</button>
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('updates.auto')}</span><span class="desc">{t('settings.updates.autoHint')}</span></div>
            <Toggle checked={s.updates.auto} label={t('updates.auto')} onchange={(v) => set((x) => (x.updates.auto = v))} />
          </div>
          <div class="item">
            <div class="text"><span class="name">{t('settings.updates.channel')}</span><span class="desc">{t('settings.updates.channelHint')}</span></div>
            <Segmented value={s.updates.channel} label={t('settings.updates.channel')} options={[{ value: 'stable', label: t('settings.updates.stable') }, { value: 'beta', label: 'Beta' }]} onchange={(v) => set((x) => (x.updates.channel = v as 'stable' | 'beta'))} />
          </div>
        </div>
      {:else if section === 'storage'}
        <h2>{t('settings.sections.storage')}</h2>
        {#if !storage}
          <div class="row"><span class="spinner dark"></span><span class="hint">{t('settings.storage.scanning')}</span></div>
        {:else}
          <div class="card">
            <div class="total"><span class="big">{bytes(storage.total)}</span><span class="hint">{t('settings.storage.total')}</span></div>
            <div class="bar" aria-hidden="true">
              {#each storageParts as p, i (p.label)}
                <span style="width: {storage.total ? (p.size / storage.total) * 100 : 0}%; background: {['var(--accent-from)', 'var(--accent-to)', '#4c8dff', '#3fb950', '#e3b341', '#6b6b6b', '#a3a3a3'][i]}"></span>
              {/each}
            </div>
            {#each storageParts as p (p.label)}
              <div class="item compact">
                <span class="name small grow">{p.label}</span>
                <span class="hint">{bytes(p.size)}</span>
                {#if p.id && p.action}<button class="btn btn-flat sm" disabled={p.size === 0} onclick={() => clean(p.id!)}>{t(p.action)}</button>{/if}
              </div>
            {/each}
          </div>
          <h3>{t('settings.storage.perInstance')}</h3>
          <div class="card">
            {#each storage.instances as i (i.id)}
              <div class="item compact">
                <span class="name small grow truncate">{i.name}</span>
                <span class="hint">{t('instance.overview.sizeParts', { worlds: bytes(i.worlds), mods: bytes(i.mods), rest: bytes(i.rest) })}</span>
              </div>
            {/each}
          </div>
        {/if}
      {:else if section === 'diagnostics'}
        <h2>{t('settings.sections.diagnostics')}</h2>
        <p class="intro">{t('settings.diagnostics.hint')}</p>
        <div class="row">
          <button class="btn btn-grad" disabled={diagnosing} onclick={runDiagnostics}>
            {#if diagnosing}<span class="spinner"></span>{:else}<Icon name="wrench" size={14} />{/if}{t('settings.diagnostics.run')}
          </button>
          {#if diagnostics}<button class="btn btn-second" onclick={copyReport}><Icon name="copy" size={14} />{t('settings.diagnostics.copy')}</button>{/if}
        </div>
        {#if diagnostics}
          {#each ['system', 'paths', 'java', 'network', 'launcher', 'account', 'instances'] as area (area)}
            {@const items = diagnostics.filter((d) => d.area === area)}
            {#if items.length}
              <h3>{t(`diagnostics.areas.${area}`)}</h3>
              <div class="card">
                {#each items as d, i (i)}
                  <div class="diag {d.status}">
                    <span class="d-icon"><Icon name={d.status === 'ok' ? 'check' : 'warning'} size={13} /></span>
                    <span class="col grow" style="gap: 2px">
                      <span class="name small">{tr(d.title)}</span>
                      {#if d.detail}<span class="desc">{tr(d.detail)}</span>{/if}
                      {#if d.solution}<span class="solution">{tr(d.solution)}</span>{/if}
                    </span>
                  </div>
                {/each}
              </div>
            {/if}
          {/each}
        {/if}
      {/if}
      </div>
      {/key}
    </div>
  </div>
{/if}

<style>
  .page {
    height: 100%;
    display: flex;
  }
  .side {
    position: relative;
    width: 230px;
    flex-shrink: 0;
    padding: 24px 12px 20px 20px;
    border-right: 1px solid #2a2a2a;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  h1 {
    font-size: 20px;
    font-weight: 600;
    margin: 0 0 14px 8px;
  }
  .nav {
    height: 38px;
    padding: 0 10px;
    border: 0;
    border-radius: 9px;
    background: transparent;
    color: var(--muted);
    font-size: 13.5px;
    font-weight: 500;
    display: flex;
    align-items: center;
    gap: 10px;
    cursor: pointer;
    text-align: left;
  }
  .nav:hover {
    background: var(--panel);
    color: var(--text);
  }
  .nav[aria-current='page'] {
    background: transparent;
    color: var(--accent-text);
  }
  .nav {
    position: relative;
  }
  .marker {
    position: absolute;
    top: 0;
    left: 20px;
    right: 12px;
    border-radius: 9px;
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
    pointer-events: none;
  }
  .marker.ready {
    transition: transform var(--dur-slow) var(--ease-out), height var(--dur-slow) var(--ease-out);
  }
  .about {
    padding: 0 10px;
    font-size: 12px;
    color: var(--faint);
  }
  .link {
    padding: 4px 10px;
    border: 0;
    background: transparent;
    color: var(--muted-2);
    font-size: 12.5px;
    text-align: left;
    cursor: pointer;
  }
  .link:hover {
    color: var(--accent-text);
  }
  .content {
    flex-grow: 1;
    min-width: 0;
    overflow-y: auto;
    padding: 28px 32px 40px;
  }
  /* Mittig im freien Bereich neben der Navigation */
  .inner {
    width: 100%;
    max-width: 1100px;
    margin: 0 auto;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  h2 {
    font-size: 18px;
    font-weight: 600;
  }
  h3 {
    margin-top: 10px;
    font-size: 13px;
    font-weight: 600;
    color: var(--muted);
    text-transform: uppercase;
    letter-spacing: 0.6px;
  }
  .intro {
    font-size: 13px;
    color: var(--muted);
    line-height: 1.5;
  }
  .card {
    padding: 6px 18px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
  }
  .card.grid2 {
    display: grid;
    grid-template-columns: 1fr 1fr;
    column-gap: 24px;
  }
  .item {
    min-height: 60px;
    padding: 12px 0;
    border-bottom: 1px solid #303030;
    display: flex;
    align-items: center;
    gap: 16px;
  }
  .item:last-child {
    border-bottom: 0;
  }
  .item.column {
    flex-direction: column;
    align-items: stretch;
  }
  .item.compact {
    min-height: 46px;
    padding: 8px 0;
  }
  /* Schalter immer rechtsbündig, auch in der zweispaltigen Liste */
  .item.compact .name {
    flex-grow: 1;
    min-width: 0;
  }
  .text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  .name {
    font-size: 14px;
    font-weight: 500;
    color: var(--text);
  }
  .name.small {
    font-size: 13px;
  }
  .desc {
    font-size: 12.5px;
    color: var(--muted-2);
    line-height: 1.45;
  }
  .field.small {
    width: 90px;
    height: 36px;
  }
  .presets {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(170px, 1fr));
    gap: 12px;
  }
  .preset {
    padding: 10px 10px 12px;
    border: 0;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 8px;
    align-items: stretch;
    cursor: pointer;
    color: var(--text);
    font-size: 13px;
    font-weight: 600;
  }
  .preset.on {
    box-shadow: inset 0 0 0 2px var(--accent-text);
  }
  .preset:not(.on):hover {
    background: #2f2f2f;
  }
  .swatch {
    aspect-ratio: 16 / 7;
    border-radius: 8px;
  }
  .color input {
    width: 52px;
    height: 36px;
    padding: 0;
    border: 0;
    border-radius: 8px;
    background: transparent;
    cursor: pointer;
  }
  .warn {
    display: flex;
    gap: 8px;
    padding: 10px 0;
    font-size: 12.5px;
    color: var(--yellow);
  }
  /* Vorschau: verkleinerter Launcher in der gewählten Akzentfarbe */
  .sample {
    height: 230px;
    border-radius: 12px;
    background: var(--panel);
    overflow: hidden;
    display: flex;
  }
  .mock-side {
    width: 54px;
    padding: 14px 0;
    background: #1d1d1d;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
    color: var(--muted-2);
  }
  .mock-side span {
    width: 34px;
    height: 34px;
    border-radius: 9px;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .mock-side span.on {
    background: var(--accent-soft);
    color: var(--accent-text);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .mock-main {
    flex-grow: 1;
    min-width: 0;
    padding: 18px 22px;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .mock-tabs {
    display: flex;
    gap: 22px;
    border-bottom: 1px solid #303030;
    font-size: 13px;
    font-weight: 600;
    color: var(--muted);
  }
  .mock-tabs span {
    padding-bottom: 9px;
  }
  .mock-tabs span.on {
    color: var(--text-strong);
    box-shadow: inset 0 -2px 0 var(--accent-text);
  }
  .mock-row {
    display: flex;
    align-items: center;
    gap: 14px;
  }
  .mock-bar {
    height: 8px;
    border-radius: 4px;
    background: #3a3a3a;
    overflow: hidden;
  }
  .mock-bar span {
    display: block;
    width: 62%;
    height: 100%;
    background: var(--accent);
  }
  .total {
    display: flex;
    align-items: baseline;
    gap: 10px;
    padding-top: 10px;
  }
  .big {
    font-size: 24px;
    font-weight: 700;
  }
  .bar {
    height: 8px;
    margin: 10px 0 8px;
    border-radius: 4px;
    overflow: hidden;
    display: flex;
    gap: 2px;
    background: #333;
  }
  .diag {
    padding: 10px 0;
    border-bottom: 1px solid #303030;
    display: flex;
    gap: 10px;
  }
  .diag:last-child {
    border-bottom: 0;
  }
  .d-icon {
    width: 22px;
    height: 22px;
    border-radius: 11px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: rgba(63, 185, 80, 0.15);
    color: var(--green);
    flex-shrink: 0;
  }
  .warning .d-icon {
    background: rgba(227, 179, 65, 0.15);
    color: var(--yellow);
  }
  .error .d-icon {
    background: rgba(244, 112, 103, 0.15);
    color: var(--red);
  }
  .solution {
    font-size: 12.5px;
    color: var(--accent-text);
  }
</style>
