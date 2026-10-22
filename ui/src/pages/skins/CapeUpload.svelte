<script lang="ts">
  import Dialog from '../../lib/components/Dialog.svelte';
  import DropZone from '../../lib/components/DropZone.svelte';
  import Icon from '../../lib/components/Icon.svelte';
  import SkinViewer from '../../lib/components/SkinViewer.svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import { toBase64 } from '../../lib/util/files';

  let { onclose, onsaved }: { onclose: () => void; onsaved: () => void } = $props();
  let file = $state<{ base64: string; url: string; name: string; size: string } | null>(null);
  let name = $state('');
  let problem = $state<string | null>(null);
  let busy = $state(false);
  let input: HTMLInputElement | undefined = $state();

  function valid(w: number, h: number) {
    return w * 32 === h * 64 && w >= 64 && w <= 2048 && (w & (w - 1)) === 0;
  }

  async function take(f: File | undefined) {
    if (!f) return;
    problem = null;
    if (!/\.png$/i.test(f.name) || f.size > 1_572_864) {
      problem = t('capes.invalidImage');
      return;
    }
    const url = URL.createObjectURL(f);
    const img = new Image();
    img.onload = async () => {
      if (!valid(img.width, img.height)) {
        problem = t('capes.invalidImage');
        return;
      }
      file = { base64: await toBase64(f), url, name: f.name, size: `${img.width} × ${img.height}` };
      if (!name) name = f.name.replace(/\.png$/i, '');
    };
    img.src = url;
  }

  async function save() {
    busy = true;
    try {
      await call('capes.save', { id: null, name: name.trim(), base64: file!.base64, source: 'upload', edit: null });
      onsaved();
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog title={t('capes.uploadTitle')} subtitle={t('capes.uploadHint')} icon="upload" width={680} {onclose}>
  <div class="layout">
    <div class="preview">
      {#if file && app.activeAccount}
        <SkinViewer skin={`/axo/head/${app.activeAccount.uuid.replaceAll('-', '')}`} cape={file.url} width={200} height={300} facing="back" label={t('capes.previewLabel')} />
      {:else}
        <span class="hint" style="text-align: center; padding: 20px">{t('capes.uploadPreviewHint')}</span>
      {/if}
    </div>
    <div class="form">
      <DropZone padding={22} ondrop={(f) => take(f[0])}>
        <Icon name="upload" size={20} />
        <button class="link" onclick={() => input?.click()}>{file ? t('capes.otherFile') : t('capes.chooseFile')}</button>
        <span class="hint">{file ? `${file.name} · ${file.size}` : t('capes.fileFormat')}</span>
        <input bind:this={input} type="file" accept="image/png" hidden onchange={(e) => take((e.currentTarget as HTMLInputElement).files?.[0])} />
      </DropZone>
      {#if problem}<p class="error-text">{problem}</p>{/if}
      <label class="label">
        <span>{t('skins.name')}</span>
        <span class="field"><input bind:value={name} maxlength="40" /></span>
      </label>
      <p class="hint">{t('capes.reviewNote')}</p>
    </div>
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={busy || !file || !name.trim()} onclick={save}>{t('capes.save')}</button>
  {/snippet}
</Dialog>

<style>
  .layout {
    display: flex;
    gap: 20px;
  }
  .preview {
    width: 220px;
    height: 320px;
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
