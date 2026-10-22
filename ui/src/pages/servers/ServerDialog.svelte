<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import Select from '../../lib/components/Select.svelte';
  import Checkbox from '../../lib/components/Checkbox.svelte';
  import Toggle from '../../lib/components/Toggle.svelte';
  import MemorySlider from '../../lib/components/MemorySlider.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import ImageBox from '../../lib/components/ImageBox.svelte';
  import type { ServerImportDraft, ServerSoftware, ServerView } from '../../lib/types';

  let {
    server,
    draft = null,
    onclose,
    onsaved
  }: { server: ServerView | null; draft?: ServerImportDraft | null; onclose: () => void; onsaved: () => void } = $props();

  // Import: Werte aus dem gewählten Ordner vorbelegen, übernommen wird beim Speichern
  const seed = draft?.info;

  let name = $state(server?.name ?? seed?.name ?? '');
  let software = $state<ServerSoftware>(server?.software ?? seed?.software ?? 'paper');
  let version = $state<string | null>(server?.version ?? seed?.version ?? null);
  let port = $state(server?.port ?? seed?.port ?? 25565);
  let maxPlayers = $state(server?.maxPlayers ?? seed?.maxPlayers ?? 10);
  let memory = $state(server?.memoryMb ?? 4096);
  let openToInternet = $state(server?.openToInternet ?? false);
  let eula = $state(server?.eulaAccepted ?? seed?.eulaAccepted ?? false);
  let snapshots = $state(false);
  let versions = $state<{ version: string; stable: boolean }[]>([]);
  let busy = $state(false);

  // Bilder: undefined = unverändert, base64 null = entfernen. Neue Server bekommen sie direkt nach dem Anlegen.
  type PendingImage = { base64: string | null; url: string | null };
  let icon = $state<PendingImage | undefined>(undefined);
  let banner = $state<PendingImage | undefined>(undefined);
  const iconSrc = $derived(icon ? icon.url : (server?.iconUrl ?? null));
  const bannerSrc = $derived(banner ? banner.url : (server?.bannerUrl ?? null));
  const pending = (base64: string | null, url: string | null): PendingImage => ({ base64, url });

  const softwares: { id: ServerSoftware; name: string; text: string }[] = [
    { id: 'vanilla', name: 'Vanilla', text: 'localServers.software.vanilla' },
    { id: 'paper', name: 'Paper', text: 'localServers.software.paper' },
    { id: 'fabric', name: 'Fabric', text: 'localServers.software.fabric' },
    { id: 'velocity', name: 'Velocity', text: 'localServers.software.velocity' }
  ];

  async function loadVersions() {
    versions = [];
    try {
      versions = await call('localServers.versions', { software });
      if (!version || !versions.some((v) => v.version === version)) version = versions.find((v) => v.stable)?.version ?? versions[0]?.version ?? null;
    } catch (e) {
      app.error(e);
    }
  }

  onMount(async () => {
    if (!server && !seed) port = await call<number>('localServers.nextPort').catch(() => 25565);
  });

  $effect(() => {
    software;
    loadVersions();
  });

  const shown = $derived(versions.filter((v) => snapshots || v.stable));
  const needsEula = $derived(software !== 'velocity');
  const valid = $derived(name.trim().length > 0 && !!version && port >= 1024 && port <= 65535 && (!needsEula || eula));

  async function save() {
    busy = true;
    try {
      const saved = await call<{ id: string }>('localServers.save', { id: server?.id ?? null, name: name.trim(), software, version, port, maxPlayers, memoryMb: memory, openToInternet, eulaAccepted: eula, importRef: draft?.folderRef ?? null });
      if (icon) await call('localServers.setImage', { id: saved.id, banner: false, base64: icon.base64 });
      if (banner) await call('localServers.setImage', { id: saved.id, banner: true, base64: banner.base64 });
      onsaved();
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog
  title={server ? t('localServers.editTitle') : draft ? t('localServers.import.dialogTitle') : t('localServers.createTitle')}
  subtitle={server ? t('localServers.editHint') : draft ? t('localServers.import.dialogHint') : ''} icon="servers" width={860} {onclose}>
  <div class="form">
  {#if seed}
    <p class="import-note">
      <Icon name="import" size={14} />
      <span>
        {t('localServers.import.found', { worlds: seed.worlds, plugins: seed.plugins, mods: seed.mods })}
        {#if !seed.version}<strong> {t('localServers.import.noVersion')}</strong>{/if}
      </span>
    </p>
  {/if}
  <div class="head">
    <div class="label icon-col">
      <span>{t('localServers.icon')}</span>
      <div title={t('localServers.iconHint')}>
        <ImageBox url={iconSrc} fallbackIcon="image" width={144} radius={22} pixelated dashed editable removable
          outputWidth={64} cropTitle={t('localServers.icon')} editLabel={t('localServers.chooseIcon')}
          onchange={(b64, url) => (icon = pending(b64, url))} />
      </div>
    </div>
    <div class="head-main">
      <label class="label">
        <span>{t('servers.name')}</span>
        <span class="field"><input bind:value={name} maxlength="60" placeholder={t('localServers.namePlaceholder')} data-autofocus /></span>
      </label>
      <div class="label">
        <span>{t('localServers.banner')}</span>
        <ImageBox url={bannerSrc} placeholder={t('localServers.bannerHint')} width="100%" height={63} radius={12} dashed editable removable
          aspect={4} outputWidth={1200} cropTitle={t('localServers.banner')} editLabel={t('localServers.chooseBanner')}
          onchange={(b64, url) => (banner = pending(b64, url))} />
      </div>
    </div>
  </div>
  <div class="label">
    <span>{t('localServers.softwareLabel')}</span>
    <div class="softwares" role="radiogroup" aria-label={t('localServers.softwareLabel')}>
      {#each softwares as s (s.id)}
        <button class="sw" class:on={software === s.id} role="radio" aria-checked={software === s.id} onclick={() => (software = s.id)}>
          <span class="sw-name">{s.name}</span>
          <span class="sw-text">{t(s.text)}</span>
        </button>
      {/each}
    </div>
  </div>
  <div class="two">
    <div class="label">
      <span>{t('localServers.version')}</span>
      <Select bind:value={version} label={t('localServers.version')} placeholder={t('common.loading')} options={shown.map((v) => ({ value: v.version, label: v.version, hint: v.stable ? undefined : 'Snapshot' }))} />
      {#if software !== 'velocity'}<Checkbox bind:checked={snapshots} label={t('versions.snapshots')} small />{/if}
    </div>
    <div class="two inner">
      <label class="label"><span>{t('localServers.port')}</span><span class="field"><input type="number" min="1024" max="65535" bind:value={port} /></span></label>
      <label class="label"><span>{t('localServers.maxPlayersLabel')}</span><span class="field"><input type="number" min="1" max="500" bind:value={maxPlayers} /></span></label>
    </div>
  </div>
  <div class="label">
    <span>{t('memory.label')}</span>
    <MemorySlider bind:value={memory} />
  </div>
  <div class="internet">
    <div class="row" style="justify-content: space-between">
      <span class="col" style="gap: 3px">
        <span class="strong">{t('localServers.openToInternet')}</span>
        <span class="hint">{t('localServers.openToInternetHint')}</span>
      </span>
      <Toggle bind:checked={openToInternet} label={t('localServers.openToInternet')} />
    </div>
    {#if openToInternet}<p class="warn"><Icon name="shield" size={14} />{t('localServers.internetWarning')}</p>{/if}
  </div>
  {#if needsEula}
    <div class="row">
      <Checkbox bind:checked={eula} label={t('localServers.eula')} small />
      <button class="link" onclick={() => call('shell.openUrl', { url: 'https://aka.ms/MinecraftEULA' })}>{t('localServers.eulaRead')}</button>
    </div>
  {/if}
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={!valid || busy} onclick={save}>{server ? t('common.save') : t('localServers.create')}</button>
  {/snippet}
</Dialog>


<style>
  .form {
    display: flex;
    flex-direction: column;
    gap: 22px;
  }
  /* Feste Höhen, damit Icon und Banner unten bündig abschließen:
     Label (16 + 7) + Icon 144 = Label + Name 42 + Abstand 16 + Label + Banner 63 */
  .head {
    display: flex;
    gap: 20px;
  }
  .head .label > span:first-child {
    line-height: 16px;
  }
  .import-note {
    padding: 10px 12px;
    border-radius: 10px;
    background: var(--accent-soft);
    color: var(--accent-text);
    font-size: 12.5px;
    line-height: 1.45;
    display: flex;
    gap: 8px;
  }
  .icon-col {
    flex-shrink: 0;
  }
  .head-main {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .softwares {
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 8px;
  }
  .sw {
    padding: 12px;
    border: 0;
    border-radius: 10px;
    background: var(--panel-2);
    box-shadow: inset 0 0 0 1px var(--line-2);
    display: flex;
    flex-direction: column;
    gap: 3px;
    text-align: left;
    cursor: pointer;
    color: var(--text);
  }
  .sw.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .sw-name {
    font-size: 13.5px;
    font-weight: 600;
  }
  .sw-text {
    font-size: 12px;
    color: var(--muted-2);
    line-height: 1.4;
  }
  .two {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }
  .two.inner {
    gap: 10px;
  }
  .internet {
    padding: 12px 14px;
    border-radius: 10px;
    background: var(--panel-2);
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .strong {
    font-size: 13.5px;
    font-weight: 600;
  }
  .warn {
    display: flex;
    gap: 8px;
    font-size: 12.5px;
    line-height: 1.45;
    color: var(--yellow);
  }
  .link {
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--accent-text);
    font-size: 12.5px;
    cursor: pointer;
  }
</style>
