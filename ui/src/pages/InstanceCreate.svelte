<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import FormSection from '../lib/components/FormSection.svelte';
  import VersionPicker from '../lib/components/VersionPicker.svelte';
  import MemorySlider from '../lib/components/MemorySlider.svelte';
  import Toggle from '../lib/components/Toggle.svelte';
  import InstanceImage from '../lib/components/InstanceImage.svelte';
  import ImageCropDialog from '../lib/components/ImageCropDialog.svelte';
  import Select from '../lib/components/Select.svelte';
  import { pickImage, upload } from '../lib/util/files';
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
  let dragging = $state(false);

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

  let banner = $state<{ base64: string; url: string } | null>(null);
  let crop = $state<{ url: string; banner: boolean } | null>(null);

  async function chooseImage(isBanner: boolean) {
    const image = await pickImage();
    if (image) crop = { url: image.url, banner: isBanner };
  }

  function cropped(base64: string) {
    const isBanner = crop?.banner ?? false;
    if (crop) URL.revokeObjectURL(crop.url);
    crop = null;
    const image = { base64, url: `data:image/png;base64,${base64}` };
    if (isBanner) banner = image;
    else icon = image;
  }

  // Wie der Kopf der Instanz später aussieht: eigener Banner, sonst das Instanzbild verschwommen
  const heroImage = $derived(banner?.url ?? icon?.url ?? null);

  async function chooseFile() {
    try {
      const files = await call<FileRef[]>('dialog.openFile', { purpose: 'import' });
      if (files[0]) importRef = files[0];
    } catch (e) {
      app.error(e);
    }
  }

  async function ondrop(e: DragEvent) {
    e.preventDefault();
    dragging = false;
    const file = e.dataTransfer?.files[0];
    if (!file || !/\.(mrpack|zip)$/i.test(file.name)) return;
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
          iconRef: null
        });
        if (icon) await call('instances.setImage', { id: created.id, banner: false, base64: icon.base64, fileRef: null });
        if (banner) await call('instances.setImage', { id: created.id, banner: true, base64: banner.base64, fileRef: null });
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
  <div class="head">
    <button class="icon-btn" aria-label={t('common.back')} onclick={() => app.back()}><Icon name="chevronLeft" /></button>
    <h1>{t('create.title')}</h1>
  </div>

  {#if created}
    <div class="done">
      <InstanceImage url={`/axo/instance-icon/${created.id}`} has={!!icon} name={created.name} size={72} radius={16} />
      <h2>{t('create.created', { name: created.name })}</h2>
      <p>{t('create.createdText')}</p>
      <div class="row">
        <button class="btn btn-second" onclick={() => app.navigate({ page: 'instance', id: created!.id })}>{t('create.openInstance')}</button>
        <button class="btn btn-grad" onclick={() => app.launch(created!.id)}><Icon name="play" size={13} />{t('common.play')}</button>
      </div>
    </div>
  {:else}
    <div class="body">
      {#if start === 'empty'}
        <section class="hero" aria-label={t('create.preview')}>
          {#if heroImage}
            <img class="bg" class:blurred={!banner} src={heroImage} alt="" />
          {:else}
            <span class="bg fallback"></span>
          {/if}
          <div class="shade"></div>
          <div class="hero-content">
            <div class="hero-icon">
              {#if icon}<img class="preview" src={icon.url} alt="" />{:else}<InstanceImage name={name || '?'} has={false} size={72} radius={16} />{/if}
              <button class="hero-mini" aria-label={t('create.chooseIcon')} title={t('create.chooseIcon')} onclick={() => chooseImage(false)}><Icon name="image" size={13} /></button>
              {#if icon}<button class="hero-x" aria-label={t('common.remove')} title={t('common.remove')} onclick={() => (icon = null)}><Icon name="x" size={11} /></button>{/if}
            </div>
            <div class="titles">
              <h2 class="truncate" class:placeholder={!name.trim()}>{name.trim() || t('create.previewName')}</h2>
              {#if minecraft}<span class="glass">{versionLabel(minecraft, loader)}</span>{/if}
            </div>
            <div class="hero-actions">
              {#if banner}
                <button class="glass-btn" onclick={() => (banner = null)}><Icon name="x" size={12} />{t('create.bannerRemove')}</button>
              {/if}
              <button class="glass-btn" onclick={() => chooseImage(true)}><Icon name="image" size={13} />{banner ? t('create.bannerChange') : t('create.bannerChoose')}</button>
            </div>
          </div>
        </section>
      {/if}

      <FormSection title={t('create.start')} subtitle={t('create.startHint')}>
            <div class="starts">
              {#each starts as s (s.id)}
                <button
                  class="start"
                  class:on={start === s.id}
                  aria-pressed={start === s.id}
                  onclick={() => (s.id === 'modpack' ? app.navigate({ page: 'modpacks' }) : (start = s.id))}>
                  <span class="s-icon"><Icon name={s.icon} size={18} /></span>
                  <span class="s-title">{t(s.title)}</span>
                  <span class="s-text">{t(s.text)}</span>
                </button>
              {/each}
            </div>
      </FormSection>

      <div class="cols" class:single={start !== 'empty'}>
          {#if start === 'import'}
            <FormSection title={t('create.importTitle')}>
              <!-- svelte-ignore a11y_no_static_element_interactions -->
              <div
                class="dropzone"
                class:dragging
                ondragover={(e) => {
                  e.preventDefault();
                  dragging = true;
                }}
                ondragleave={() => (dragging = false)}
                {ondrop}>
                <Icon name="import" size={24} />
                {#if importRef}
                  <span class="file">{importRef.name}</span>
                {:else}
                  <span><button class="link" onclick={chooseFile}>{t('create.chooseFile')}</button> {t('create.orDrop')}</span>
                {/if}
                <span class="hint">{t('create.importFormats')}</span>
              </div>
              {#if importJob}<p class="hint">{t('instances.importing', { file: importRef?.name ?? '' })}</p>{/if}
              <button class="other-launcher" onclick={() => (app.dialog = { kind: 'import' })}>
                <span class="s-icon"><Icon name="search" size={16} /></span>
                <span class="col grow" style="gap: 2px; text-align: left">
                  <strong>{t('create.fromLauncher')}</strong>
                  <span class="hint">{t('create.fromLauncherText')}</span>
                </span>
                <Icon name="chevronRight" size={14} />
              </button>
            </FormSection>
          {:else}
            <FormSection title={t('create.general')} subtitle={t('create.generalHint')}>
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
              {/if}
            </FormSection>
          {/if}

        {#if start === 'empty'}
            <FormSection title={t('instance.settings.performance')} subtitle={t('instance.settings.performanceHint')}>
              <div class="row" style="justify-content: space-between">
                <span style="font-size: 13.5px">{t('instance.settings.ownMemory')}</span>
                <Toggle bind:checked={ownMemory} label={t('instance.settings.ownMemory')} />
              </div>
              {#if ownMemory}
                <MemorySlider bind:value={memory} />
              {:else}
                <p class="hint">{t('instance.settings.memoryDefault', { gb: ((app.settings?.defaults.memoryMb ?? 4096) / 1024).toLocaleString('de-DE') })}</p>
              {/if}
            </FormSection>
        {/if}
      </div>
    </div>
    <div class="footer">
      <button class="btn btn-ghost" onclick={() => app.back()}>{t('common.cancel')}</button>
      <button class="btn btn-grad" disabled={!canCreate} onclick={create}>
        {#if busy || copyJob}<span class="spinner"></span>{/if}{start === 'import' ? t('create.importButton') : t('create.create')}
      </button>
    </div>
  {/if}
</div>

{#if crop}
  {@const isBanner = crop.banner}
  <ImageCropDialog
    src={crop.url}
    aspect={isBanner ? 5 : 1}
    outputWidth={isBanner ? 1500 : 256}
    round={!isBanner}
    title={isBanner ? t('instance.settings.banner') : t('instance.settings.icon')}
    onconfirm={cropped}
    oncancel={() => {
      URL.revokeObjectURL(crop!.url);
      crop = null;
    }} />
{/if}

<style>
  .page {
    height: 100%;
    padding: 24px 28px 0;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .head {
    display: flex;
    align-items: center;
    gap: 12px;
  }
  h1 {
    font-size: 20px;
    font-weight: 600;
  }
  .body {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    padding-bottom: 16px;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .cols {
    display: grid;
    grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr);
    gap: 16px;
    align-items: stretch;
  }
  .cols.single {
    grid-template-columns: minmax(0, 1fr);
  }
  .starts {
    display: grid;
    grid-template-columns: repeat(4, minmax(0, 1fr));
    gap: 10px;
  }
  @media (max-width: 1100px) {
    .starts {
      grid-template-columns: repeat(2, minmax(0, 1fr));
    }
  }
  /* Vorschau des Instanz-Kopfs */
  .hero {
    position: relative;
    height: 170px;
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
    padding: 20px 22px;
    display: flex;
    align-items: flex-end;
    gap: 18px;
  }
  .hero-icon {
    position: relative;
    flex-shrink: 0;
  }
  .hero-mini,
  .hero-x {
    position: absolute;
    padding: 0;
    border: 0;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.45);
  }
  .hero-mini {
    right: -6px;
    bottom: -6px;
    width: 28px;
    height: 28px;
    background: #fff;
    color: #1a1a1a;
  }
  .hero-x {
    top: -6px;
    right: -6px;
    width: 22px;
    height: 22px;
    background: #3a3a3a;
    color: #fff;
  }
  .hero-x:hover {
    background: var(--red);
  }
  .titles {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    align-items: flex-start;
    gap: 8px;
  }
  .titles h2 {
    max-width: 100%;
    font-size: 26px;
    font-weight: 700;
    letter-spacing: -0.4px;
    color: #fff;
  }
  .titles h2.placeholder {
    color: rgba(255, 255, 255, 0.45);
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
  .hero-actions {
    flex-shrink: 0;
    display: flex;
    gap: 8px;
  }
  .glass-btn {
    height: 34px;
    padding: 0 12px;
    border: 0;
    border-radius: 9px;
    background: rgba(255, 255, 255, 0.1);
    backdrop-filter: blur(8px);
    color: #f2f2f2;
    font-size: 12.5px;
    font-weight: 600;
    display: flex;
    align-items: center;
    gap: 7px;
    cursor: pointer;
  }
  .glass-btn:hover {
    background: rgba(255, 255, 255, 0.18);
  }
  .start {
    padding: 14px;
    border: 0;
    border-radius: 12px;
    background: var(--panel-2);
    box-shadow: inset 0 0 0 1px var(--line-2);
    display: grid;
    grid-template-columns: auto 1fr;
    grid-template-rows: auto auto;
    column-gap: 12px;
    row-gap: 3px;
    text-align: left;
    cursor: pointer;
    color: var(--text);
  }
  .start:hover {
    background: var(--panel-3);
  }
  .start.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .s-icon {
    grid-row: span 2;
    width: 36px;
    height: 36px;
    border-radius: 9px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .s-title {
    font-size: 13.5px;
    font-weight: 600;
  }
  .s-text {
    font-size: 12px;
    color: var(--muted-2);
  }
  .other-launcher {
    margin-top: 12px;
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
  .dropzone {
    padding: 28px;
    border-radius: 12px;
    border: 1.5px dashed #444;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    color: var(--muted);
    font-size: 13px;
    text-align: center;
  }
  .dropzone.dragging {
    border-color: var(--accent-text);
    background: var(--accent-soft);
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
  .preview {
    width: 72px;
    height: 72px;
    border-radius: 16px;
    object-fit: cover;
  }
  .footer {
    flex-shrink: 0;
    padding: 14px 0 20px;
    border-top: 1px solid var(--line);
    display: flex;
    justify-content: flex-end;
    gap: 8px;
  }
  .done {
    flex-grow: 1;
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
