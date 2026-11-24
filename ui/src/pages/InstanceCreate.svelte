<script lang="ts">
  import { artLayers } from '../lib/util/art';
  import { onDestroy } from 'svelte';
  // Farbverlauf einmal beim Öffnen festlegen: ändert sich nicht beim Tippen und bleibt nach dem Erstellen gleich
  const artSeed = crypto.randomUUID().replaceAll('-', '').slice(0, 16);
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import DropZone from '../lib/components/DropZone.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import VersionPicker from '../lib/components/VersionPicker.svelte';
  import MemorySlider from '../lib/components/MemorySlider.svelte';
  import Toggle from '../lib/components/Toggle.svelte';
  import ImageBox from '../lib/components/ImageBox.svelte';
  import Select from '../lib/components/Select.svelte';
  import { upload } from '../lib/util/files';
  import { versionLabel } from '../lib/util/format';
  import type { FileRef, ForeignInstanceView, Instance, LoaderType } from '../lib/types';

  type Start = 'empty' | 'copy' | 'import' | 'modpack';

  let start = $state<Start>('empty');
  let name = $state('');
  let loader = $state<LoaderType>('fabric');
  let minecraft = $state<string | null>(null);
  let loaderVersion = $state<string | null>(null);
  let ownMemory = $state(false);
  let memory = $state(app.settings?.defaults.memoryMb ?? 4096);
  let copyFrom = $state<string | null>(app.instances[0]?.id ?? null);
  let importRef = $state<FileRef | null>(null);
  let icon = $state<{ base64: string; url: string } | null>(null);
  let busy = $state(false);
  let created = $state<Instance | null>(null);

  const copyJob = $derived(app.jobFor('instance.copy'));
  const importJob = $derived(app.jobFor('instance.import'));
  const canCreate = $derived(
    !busy &&
      (start === 'import' ? !!importRef : start === 'copy' ? !!copyFrom && !!name.trim() : start === 'empty' ? !!name.trim() && !!minecraft : false)
  );

  const starts: { id: Start; icon: string; title: string; text: string }[] = [
    { id: 'empty', icon: 'plus', title: 'create.empty', text: 'create.emptyText' },
    { id: 'copy', icon: 'copy', title: 'create.copy', text: 'create.copyText' },
    { id: 'import', icon: 'import', title: 'create.import', text: 'create.importText' },
    { id: 'modpack', icon: 'download', title: 'create.modpack', text: 'create.modpackText' }
  ];

  // Vorschau des Hintergrunds direkt auf der Bühne (Panorama der gewählten Version, sonst Farbverlauf)
  $effect(() => {
    app.stageOverride = start === 'empty' ? artLayers(artSeed, minecraft, app.settings?.appearance.versionPanorama ?? true) : null;
  });
  onDestroy(() => (app.stageOverride = null));

  async function chooseFile() {
    try {
      const files = await call<FileRef[]>('dialog.openFile', { purpose: 'import' });
      if (files[0]) importRef = files[0];
    } catch (e) {
      app.error(e);
    }
  }

  async function ondrop(files: File[]) {
    const file = files[0]!;
    try {
      importRef = await upload(file);
    } catch (err) {
      app.error(err);
    }
  }

  async function create() {
    busy = true;
    try {
      if (start === 'import' && importRef) {
        // .mrpack und Backups direkt; Exporte anderer Launcher zur Kontrolle im Import-Dialog
        const r = await call<{ kind: 'instance'; instance: Instance } | { kind: 'foreign'; foreign: ForeignInstanceView }>('import.file', { fileRef: importRef.id });
        if (r.kind === 'foreign') {
          app.dialog = { kind: 'import', data: { foreign: r.foreign } };
          return;
        }
        created = r.instance;
      } else if (start === 'copy' && copyFrom) {
        await call('instances.copy', { id: copyFrom, name: name.trim() });
        app.toast(t('create.copyStarted'), 'success');
        app.navigate({ page: 'instances' });
        return;
      } else {
        created = await call<Instance>('instances.create', {
          name: name.trim(),
          minecraft,
          loader,
          loaderVersion: loader === 'vanilla' ? null : loaderVersion,
          memoryMb: ownMemory ? memory : null,
          iconRef: null,
          artSeed
        });
        if (icon) await call('instances.setImage', { id: created.id, banner: false, base64: icon.base64, fileRef: null });
      }
      await app.refreshInstances();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<div class="page">
  {#if created}
    <div class="done">
      <ImageBox url={icon ? `/axo/instance-icon/${created.id}` : null} name={created.name} width={72} radius={16} />
      <h2>{t('create.created', { name: created.name })}</h2>
      <p>{t('create.createdText')}</p>
      <div class="row">
        <button class="btn btn-second" onclick={() => app.navigate({ page: 'instance', id: created!.id })}>{t('create.openInstance')}</button>
        <button class="btn btn-grad" onclick={() => app.launch(created!.id)}><Icon name="play" size={13} />{t('common.play')}</button>
      </div>
    </div>
  {:else}
    <div class="column">
      <!-- Kopf wie bei einer Instanz: Bild und Name stehen auf der Bühne, dahinter die Welt der gewählten Version -->
      <header class="preview" aria-label={t('create.preview')}>
        {#if start === 'empty'}
          <span class="picon">
            <ImageBox
              url={icon?.url ?? null}
              name={name || '?'}
              width={76}
              radius={12}
              editable
              removable={!!icon}
              cropTitle={t('instance.settings.icon')}
              editLabel={t('instance.editIcon')}
              removeLabel={t('instance.removeIcon')}
              onchange={(base64, url) => (icon = base64 && url ? { base64, url } : null)} />
          </span>
        {/if}
        <div class="titles">
          <span class="eyebrow">{start === 'import' ? t('create.title') : t('create.preview')}</span>
          {#if start === 'import'}
            <h1 class="truncate">{t('create.importTitle')}</h1>
          {:else}
            <h1 class="truncate" class:placeholder={!name.trim()}>{name.trim() || t('create.previewName')}</h1>
          {/if}
          {#if start === 'empty' && minecraft}<span class="glass">{versionLabel(minecraft, loader)}</span>{/if}
        </div>
      </header>

      <!-- Ausgangspunkt als kompakte Leiste; die Erklärung steht darunter -->
      <div class="starts" role="tablist" aria-label={t('create.start')}>
        {#each starts as s (s.id)}
          <button
            class="start"
            class:on={start === s.id}
            role="tab"
            aria-selected={start === s.id}
            title={t(s.text)}
            onclick={() => (s.id === 'modpack' ? app.navigate({ page: 'modpacks' }) : (start = s.id))}>
            <Icon name={s.icon} size={15} />{t(s.title)}
            {#if s.id === 'modpack'}<Icon name="chevronRight" size={12} />{/if}
          </button>
        {/each}
      </div>

      <div class="panel">
        {#if start === 'import'}
          <DropZone accept={(f) => /\.(mrpack|zip)$/i.test(f.name)} padding={28} ondrop={ondrop}>
            <Icon name="import" size={24} />
            {#if importRef}
              <span class="file">{importRef.name}</span>
            {:else}
              <span><button class="link" onclick={chooseFile}>{t('create.chooseFile')}</button> {t('create.orDrop')}</span>
            {/if}
            <span class="hint">{t('create.importFormats')}</span>
          </DropZone>
          {#if importJob}<p class="hint">{t('instances.importing', { file: importRef?.name ?? '' })}</p>{/if}
          <button class="other-launcher" onclick={() => (app.dialog = { kind: 'import' })}>
            <span class="s-icon"><Icon name="search" size={16} /></span>
            <span class="col grow" style="gap: 2px; text-align: left">
              <strong>{t('create.fromLauncher')}</strong>
              <span class="hint">{t('create.fromLauncherText')}</span>
            </span>
            <Icon name="chevronRight" size={14} />
          </button>
        {:else}
          <label class="label">
            <span>{t('create.name')}</span>
            <span class="field"><input bind:value={name} maxlength="60" placeholder={t('create.namePlaceholder')} data-autofocus /></span>
          </label>
          {#if start === 'copy'}
            <div class="label">
              <span>{t('create.copyFrom')}</span>
              <Select bind:value={copyFrom} label={t('create.copyFrom')} options={app.instances.map((i) => ({ value: i.id, label: `${i.name} · ${versionLabel(i.minecraftVersion, i.loader)}` }))} />
              <span class="hint">{t('create.copyNote')}</span>
            </div>
          {:else}
            <VersionPicker bind:loader bind:minecraft bind:loaderVersion />
            <div class="memory">
              <div class="row" style="justify-content: space-between">
                <span class="col" style="gap: 3px">
                  <span class="m-title">{t('instance.settings.ownMemory')}</span>
                  {#if !ownMemory}
                    <span class="hint">{t('instance.settings.memoryDefault', { gb: ((app.settings?.defaults.memoryMb ?? 4096) / 1024).toLocaleString('de-DE') })}</span>
                  {/if}
                </span>
                <Toggle bind:checked={ownMemory} label={t('instance.settings.ownMemory')} />
              </div>
              {#if ownMemory}<MemorySlider bind:value={memory} />{/if}
            </div>
          {/if}
        {/if}

        <div class="footer">
          <button class="btn btn-ghost" onclick={() => app.back()}>{t('common.cancel')}</button>
          <button class="btn btn-grad" disabled={!canCreate} onclick={create}>
            {#if busy || copyJob}<span class="spinner"></span>{/if}{start === 'import' ? t('create.importButton') : t('create.create')}
          </button>
        </div>
      </div>
    </div>
  {/if}
</div>

<style>
  .page {
    height: 100%;
    overflow-y: auto;
    padding: 12px 28px 28px;
  }
  /* Eine schmale Spalte statt über die ganze Breite gezogener Kästen */
  .column {
    max-width: 760px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .preview {
    min-height: 120px;
    display: flex;
    align-items: flex-end;
    gap: 16px;
    padding: 0 2px 4px;
  }
  .picon {
    flex-shrink: 0;
    border-radius: 12px;
    box-shadow: 0 10px 30px rgba(0, 0, 0, 0.5);
  }
  .titles {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 6px;
  }
  .titles h1 {
    max-width: 100%;
    font-family: var(--display);
    font-size: 44px;
    font-weight: 700;
    line-height: 1;
    text-transform: uppercase;
    color: var(--text-strong);
    text-shadow: 0 2px 24px rgba(0, 0, 0, 0.55);
  }
  .titles h1.placeholder {
    color: rgba(255, 255, 255, 0.45);
  }
  .starts {
    padding: 4px;
    border-radius: var(--radius-l);
    background: var(--glass);
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 4px;
  }
  .start {
    height: 36px;
    padding: 0 10px;
    border: 0;
    border-radius: var(--radius-m);
    background: transparent;
    color: var(--muted);
    font-size: 13px;
    font-weight: 600;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 7px;
    white-space: nowrap;
    cursor: pointer;
    transition: background-color 0.15s ease, color 0.15s ease;
  }
  .start:hover {
    background: rgba(255, 255, 255, 0.06);
    color: var(--text);
  }
  .start.on {
    background: #fff;
    color: #0a0b0d;
  }
  .panel {
    padding: 20px;
    border-radius: var(--radius-xl);
    background: var(--glass);
    border: 1px solid var(--line);
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .memory {
    padding-top: 16px;
    border-top: 1px solid var(--line);
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .m-title {
    font-size: 13.5px;
    font-weight: 600;
    color: var(--text);
  }
  .s-icon {
    width: 36px;
    height: 36px;
    flex-shrink: 0;
    border-radius: 9px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .other-launcher {
    width: 100%;
    padding: 12px 14px;
    border: 0;
    border-radius: 12px;
    background: var(--panel-2);
    box-shadow: inset 0 0 0 1px var(--line-2);
    color: var(--text);
    display: flex;
    align-items: center;
    gap: 12px;
    cursor: pointer;
    transition: box-shadow 0.15s ease;
  }
  .other-launcher:hover {
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .file {
    font-weight: 600;
    color: var(--text-strong);
  }
  .link {
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--accent-text);
    font-weight: 600;
    cursor: pointer;
  }
  .footer {
    padding-top: 16px;
    border-top: 1px solid var(--line);
    display: flex;
    justify-content: flex-end;
    gap: 8px;
  }
  .done {
    height: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 12px;
    text-align: center;
  }
  .done h2 {
    font-size: 18px;
  }
  .done p {
    color: var(--muted);
    max-width: 420px;
    line-height: 1.5;
  }
</style>
