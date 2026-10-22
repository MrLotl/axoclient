<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../../lib/components/Dialog.svelte';
  import DropZone from '../../lib/components/DropZone.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import SkinViewer from '../../lib/components/SkinViewer.svelte';
  import Segmented from '../../lib/components/Segmented.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import { toBase64 } from '../../lib/util/files';
  import type { SkinModel, SkinView } from '../../lib/types';

  let { skin = null, initialFile = null, onclose, onsaved }: { skin?: SkinView | null; initialFile?: File | null; onclose: () => void; onsaved: () => void } = $props();

  let name = $state(skin?.name ?? '');
  let model = $state<SkinModel>(skin?.model ?? 'classic');
  let file = $state<{ base64: string; url: string; name: string } | null>(null);
  let problem = $state<string | null>(null);
  let busy = $state(false);
  let input: HTMLInputElement | undefined = $state();

  async function take(f: File | undefined) {
    if (!f) return;
    problem = null;
    if (!/\.png$/i.test(f.name)) {
      problem = t('skins.invalidImage');
      return;
    }
    const url = URL.createObjectURL(f);
    const img = new Image();
    img.onload = async () => {
      if (img.width !== 64 || (img.height !== 64 && img.height !== 32)) {
        problem = t('skins.invalidImage');
        return;
      }
      file = { base64: await toBase64(f), url, name: f.name };
      if (!name) name = f.name.replace(/\.png$/i, '');
    };
    img.src = url;
  }

  // Auf der Skin-Seite abgelegte Datei direkt übernehmen
  onMount(() => {
    if (initialFile) take(initialFile);
  });

  async function save() {
    busy = true;
    try {
      if (skin) await call('skins.update', { id: skin.id, name, model, base64: file?.base64 ?? null });
      else await call('skins.add', { base64: file!.base64, fileRef: null, name, model });
      onsaved();
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }

  const previewUrl = $derived(file?.url ?? skin?.url ?? null);
</script>

<Dialog title={skin ? t('skins.editTitle') : t('skins.uploadTitle')} subtitle={skin ? t('skins.editHint') : t('skins.uploadHint')} icon="skins" width={720} {onclose}>
  <div class="layout">
    <div class="preview">
      {#if previewUrl}
        <SkinViewer skin={previewUrl} model={model === 'slim' ? 'slim' : 'default'} width={220} height={320} label={t('home.skinModel')} />
      {:else}
        <span class="hint" style="text-align: center; padding: 20px">{t('skins.previewHint')}</span>
      {/if}
    </div>
    <div class="form">
      <DropZone padding={20} ondrop={(f) => take(f[0])}>
        <Icon name="upload" size={20} />
        <button class="link" onclick={() => input?.click()}>{file ? t('skins.otherFile') : t('skins.chooseFile')}</button>
        <span class="hint">{file?.name ?? t('skins.fileFormat')}</span>
        <input bind:this={input} type="file" accept="image/png" hidden onchange={(e) => take((e.currentTarget as HTMLInputElement).files?.[0])} />
      </DropZone>
      {#if problem}<p class="error-text">{problem}</p>{/if}
      <label class="label">
        <span>{t('skins.name')}</span>
        <span class="field"><input bind:value={name} maxlength="40" /></span>
      </label>
      <div class="label">
        <span>{t('skins.model')}</span>
        <Segmented bind:value={model} label={t('skins.model')} options={[{ value: 'classic', label: 'Classic' }, { value: 'slim', label: 'Slim' }]} />
        <span class="hint">{model === 'slim' ? t('skins.slimHint') : t('skins.classicHint')}</span>
      </div>
    </div>
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={busy || !name.trim() || (!skin && !file)} onclick={save}>{skin ? t('common.save') : t('skins.uploadButton')}</button>
  {/snippet}
</Dialog>

<style>
  .layout {
    display: flex;
    gap: 20px;
  }
  .preview {
    width: 240px;
    height: 340px;
    flex-shrink: 0;
    border-radius: 12px;
    background: radial-gradient(120% 70% at 50% 85%, #383838 0%, #2c2c2c 55%, #252525 100%);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .form {
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .link {
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--accent-text);
    font-weight: 600;
    cursor: pointer;
  }
</style>
