<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import DropZone from '../lib/components/DropZone.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import FormSection from '../lib/components/FormSection.svelte';
  import VersionPicker from '../lib/components/VersionPicker.svelte';
  import MemorySlider from '../lib/components/MemorySlider.svelte';
  import Toggle from '../lib/components/Toggle.svelte';
  import ImageBox from '../lib/components/ImageBox.svelte';
  import InstanceHero from '../lib/components/InstanceHero.svelte';
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

  let banner = $state<{ base64: string; url: string } | null>(null);

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
      <ImageBox url={icon ? `/axo/instance-icon/${created.id}` : null} name={created.name} width={72} radius={16} />
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
        <InstanceHero
          name={name || '?'}
          iconUrl={icon?.url ?? null}
          bannerUrl={banner?.url ?? null}
          height={170}
          iconSize={72}
          label={t('create.preview')}
          onicon={(base64, url) => (icon = base64 && url ? { base64, url } : null)}
          onbanner={(base64, url) => (banner = base64 && url ? { base64, url } : null)}>
          <div class="titles">
            <h2 class="truncate" class:placeholder={!name.trim()}>{name.trim() || t('create.previewName')}</h2>
            {#if minecraft}<span class="glass">{versionLabel(minecraft, loader)}</span>{/if}
          </div>
        </InstanceHero>
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
