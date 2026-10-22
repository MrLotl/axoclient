<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import SkinViewer from '../../lib/components/SkinViewer.svelte';
  import Segmented from '../../lib/components/Segmented.svelte';
  import CapeEditor from './CapeEditor.svelte';
  import CapeUpload from './CapeUpload.svelte';
  import type { CapeView, MojangProfile, OfficialCape } from '../../lib/types';

  let { profile, onchange }: { profile: MojangProfile | null; onchange: () => void } = $props();

  let own = $state<CapeView[]>([]);
  let official = $state<OfficialCape[]>([]);
  let serviceOk = $state(true);
  let editor = $state<null | { cape: CapeView | null } | 'upload'>(null);
  let elytra = $state<'cape' | 'elytra'>('cape');
  let previewCape = $state<string | null>(null);

  async function load() {
    try {
      own = await call<CapeView[]>('capes.list');
    } catch (e) {
      app.error(e);
    }
    try {
      official = await call<OfficialCape[]>('capes.official');
      serviceOk = true;
    } catch {
      official = [];
      serviceOk = false;
    }
  }

  onMount(() => {
    load();
    return on('capes.changed', load);
  });

  const account = $derived(app.activeAccount);
  const activeAxo = $derived(own.find((c) => c.active));
  const mojangActive = $derived(profile?.capes.find((c) => c.active) ?? null);

  async function setMojang(id: string | null) {
    try {
      await call('skins.mojangCape', { capeId: id });
      onchange();
    } catch (e) {
      app.error(e);
    }
  }

  async function setAxo(source: 'official' | 'custom' | null, id: string | null) {
    try {
      await call('capes.setActive', { source, id });
      await load();
      app.toast(t('capes.activeSet'), 'success');
    } catch (e) {
      app.error(e);
    }
  }

  async function submit(c: CapeView) {
    try {
      own = await call<CapeView[]>('capes.submit', { id: c.id });
      app.toast(t('capes.submitted'), 'success');
    } catch (e) {
      app.error(e);
    }
  }

  async function withdraw(c: CapeView) {
    try {
      own = await call<CapeView[]>('capes.withdraw', { id: c.id });
    } catch (e) {
      app.error(e);
    }
  }

  function remove(c: CapeView) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('capes.deleteTitle', { name: c.name }),
        confirm: t('common.delete'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            own = await call<CapeView[]>('capes.remove', { id: c.id });
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }

  const status = (c: CapeView) => t(`capes.status.${c.status}`);
</script>

<div class="body">
  <div class="stage panel">
    {#if account}
      <SkinViewer
        skin={`/axo/head/${account.uuid.replaceAll('-', '')}`}
        cape={previewCape ?? activeAxo?.url ?? mojangActive?.url ?? null}
        elytra={elytra === 'elytra'}
        model={profile?.model === 'slim' ? 'slim' : 'default'}
        width={300}
        height={420}
        facing="back"
        label={t('capes.previewLabel')} />
    {/if}
    <Segmented bind:value={elytra} label={t('capes.previewMode')} options={[{ value: 'cape', label: t('capes.cape') }, { value: 'elytra', label: 'Elytra' }]} />
    <span class="hint" style="text-align: center">{previewCape ? t('capes.previewing') : t('capes.howOthersSee')}</span>
  </div>

  <div class="sections">
    <section class="panel sec">
      <div class="sec-head">
        <h2>{t('capes.mojangTitle')}</h2>
        <span class="hint">{t('capes.mojangHint')}</span>
      </div>
      <div class="capes">
        <button class="cape" class:on={!mojangActive} onclick={() => setMojang(null)}>
          <span class="cape-img none"><Icon name="x" size={18} /></span>
          <span class="cape-name">{t('capes.noCape')}</span>
        </button>
        {#each profile?.capes ?? [] as c (c.id)}
          <button class="cape" class:on={c.active} onclick={() => setMojang(c.id)} onmouseenter={() => (previewCape = c.url)} onmouseleave={() => (previewCape = null)}>
            {#if c.url}<span class="cape-img" style="background-image: url('{c.url}')"></span>{/if}
            <span class="cape-name truncate">{c.alias}</span>
          </button>
        {:else}
          <span class="hint">{profile ? t('capes.noMojangCapes') : t('service.notSignedIn')}</span>
        {/each}
      </div>
    </section>

    <section class="panel sec">
      <div class="sec-head">
        <h2>{t('capes.axoTitle')}</h2>
        <span class="hint">{t('capes.axoHint')}</span>
      </div>
      {#if !serviceOk}
        <p class="hint">{t('service.unavailable')}</p>
      {:else}
        <div class="capes">
          <button class="cape" class:on={!activeAxo} onclick={() => setAxo(null, null)}>
            <span class="cape-img none"><Icon name="x" size={18} /></span>
            <span class="cape-name">{t('capes.noAxoCape')}</span>
          </button>
          {#each official as c (c.id)}
            <button class="cape" onclick={() => setAxo('official', c.id)} onmouseenter={() => (previewCape = c.url)} onmouseleave={() => (previewCape = null)}>
              <span class="cape-img" style="background-image: url('{c.url}')"></span>
              <span class="cape-name truncate">{c.name}</span>
            </button>
          {/each}
        </div>
      {/if}
    </section>

    <section class="panel sec">
      <div class="own-head">
        <div class="sec-head grow">
          <h2>{t('capes.ownTitle')}</h2>
          <span class="hint">{t('capes.ownHint')}</span>
        </div>
        <button class="btn btn-second sm" onclick={() => (editor = { cape: null })}><Icon name="pencil" size={13} />{t('capes.paint')}</button>
        <button class="btn btn-second sm" onclick={() => (editor = 'upload')}><Icon name="upload" size={13} />{t('capes.uploadFile')}</button>
      </div>
      <div class="own">
        {#each own as c (c.id)}
          <div class="own-card" class:on={c.active} onmouseenter={() => (previewCape = c.url)} onmouseleave={() => (previewCape = null)} role="group" aria-label={c.name}>
            <div class="own-tools">
              {#if c.hasEditState}<button class="tool" aria-label={t('common.edit')} title={t('common.edit')} onclick={() => (editor = { cape: c })}><Icon name="edit" size={12} /></button>{/if}
              <button class="tool trash" aria-label={t('capes.deleteNamed', { name: c.name })} title={t('common.delete')} onclick={() => remove(c)}><Icon name="trash" size={12} /></button>
            </div>
            <span class="cape-img" style="background-image: url('{c.url}')" role="img" aria-label={c.name}></span>
            <span class="cape-name truncate">{c.name}</span>
            <span class="st {c.status}" title={c.reason ?? ''}>{c.active ? t('skins.active') : status(c)}</span>
            {#if c.status === 'approved'}
              <button class="btn {c.active ? 'btn-ghost' : 'btn-second'} sm full" disabled={c.active} onclick={() => setAxo('custom', c.id)}>{c.active ? t('skins.active') : t('skins.use')}</button>
            {:else if c.status === 'pending'}
              <button class="btn btn-ghost sm full" onclick={() => withdraw(c)}>{t('capes.withdraw')}</button>
            {:else}
              <button class="btn btn-second sm full" disabled={!serviceOk} title={serviceOk ? t('capes.submitHint') : t('service.unavailable')} onclick={() => submit(c)}>
                <Icon name="send" size={12} />{t('capes.submitShort')}
              </button>
            {/if}
          </div>
        {:else}
          <p class="hint">{t('capes.noOwn')}</p>
        {/each}
      </div>
    </section>
  </div>
</div>

{#if editor === 'upload'}
  <CapeUpload onclose={() => (editor = null)} onsaved={load} />
{:else if editor}
  <CapeEditor cape={editor.cape} onclose={() => (editor = null)} onsaved={load} />
{/if}

<style>
  .body {
    flex-grow: 1;
    min-height: 0;
    display: flex;
    gap: 20px;
  }
  .stage {
    width: 340px;
    flex-shrink: 0;
    padding: 20px;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 14px;
    background: radial-gradient(120% 70% at 50% 85%, #383838 0%, #2c2c2c 55%, #252525 100%);
  }
  .sections {
    flex-grow: 1;
    min-width: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .sec {
    padding: 16px 18px;
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .sec-head {
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  h2 {
    font-size: 15px;
    font-weight: 600;
  }
  .capes {
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
  }
  .cape {
    width: 124px;
    padding: 12px 10px 10px;
    border: 0;
    border-radius: 10px;
    background: var(--panel-2);
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
    cursor: pointer;
    color: var(--text);
  }
  .cape:hover {
    background: var(--panel-3);
  }
  .cape.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .cape-img {
    width: 55px;
    height: 88px;
    border-radius: 4px;
    background-size: 640% 200%;
    background-position: 1.85% 6.25%;
    image-rendering: pixelated;
    background-color: #1f1f1f;
  }
  .cape-img.small {
    width: 25px;
    height: 40px;
    flex-shrink: 0;
  }
  .cape-img.none {
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--muted-2);
  }
  .cape-name {
    font-size: 12px;
    font-weight: 600;
    max-width: 100%;
  }
  .own-head {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .own {
    display: flex;
    flex-wrap: wrap;
    gap: 10px;
  }
  .own-card {
    position: relative;
    width: 150px;
    padding: 12px 10px 10px;
    border-radius: 12px;
    background: var(--panel-2);
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 6px;
  }
  .own-card.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .own-card .cape-img {
    margin-bottom: 2px;
  }
  .own-tools {
    position: absolute;
    top: 6px;
    right: 6px;
    display: flex;
    gap: 4px;
    opacity: 0;
    transition: opacity 0.15s ease;
  }
  .own-card:hover .own-tools,
  .own-tools:focus-within {
    opacity: 1;
  }
  .tool {
    width: 24px;
    height: 24px;
    padding: 0;
    border: 0;
    border-radius: 6px;
    background: #3a3a3a;
    color: var(--text-2);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .tool:hover {
    background: #454545;
  }
  .tool.trash:hover {
    background: var(--red-soft);
    color: var(--red);
  }
  .full {
    width: 100%;
    justify-content: center;
    margin-top: 2px;
  }
  .st {
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .st.pending {
    color: var(--yellow);
  }
  .st.approved {
    color: var(--green);
  }
  .st.rejected {
    color: var(--red);
  }
  .icon-btn.small {
    width: 30px;
    height: 30px;
    color: var(--muted);
  }
</style>
