<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call, on } from '../lib/bridge/bridge';
  import { t } from '../lib/i18n';
  import Icon from '../lib/components/Icon.svelte';
  import Segmented from '../lib/components/Segmented.svelte';
  import SkinViewer from '../lib/components/SkinViewer.svelte';
  import SkinFront from '../lib/components/SkinFront.svelte';
  import SkinDialog from './skins/SkinDialog.svelte';
  import AdoptDialog from './skins/AdoptDialog.svelte';
  import Capes from './skins/Capes.svelte';
  import Emotes from './skins/Emotes.svelte';
  import type { MojangProfile, SkinView } from '../lib/types';

  let tab = $state<'skins' | 'capes' | 'emotes'>(app.route.tab === 'capes' ? 'capes' : app.route.tab === 'emotes' ? 'emotes' : 'skins');
  let skins = $state<SkinView[]>([]);
  let profile = $state<MojangProfile | null>(null);
  let preview = $state<SkinView | null>(null);
  let dialog = $state<null | 'upload' | 'adopt' | { edit: SkinView }>(null);
  let applying = $state<string | null>(null);

  async function load() {
    try {
      skins = await call<SkinView[]>('skins.list');
    } catch (e) {
      app.error(e);
    }
    try {
      profile = await call<MojangProfile>('skins.profile');
    } catch {
      profile = null;
    }
  }

  onMount(() => {
    load();
    return on('skins.changed', load);
  });

  const account = $derived(app.activeAccount);
  const shownSkin = $derived(preview?.url ?? (account ? `/axo/head/${account.uuid.replaceAll('-', '')}?v=${profile?.skinUrl ?? ''}` : null));
  const shownModel = $derived(preview ? preview.model : profile?.model ?? 'classic');
  const activeCape = $derived(profile?.capes.find((c) => c.active)?.url ?? null);

  async function apply(s: SkinView) {
    applying = s.id;
    try {
      profile = await call<MojangProfile>('skins.apply', { id: s.id });
      app.toast(t('skins.applied', { name: s.name }), 'success');
      await load();
    } catch (e) {
      app.error(e);
    } finally {
      applying = null;
    }
  }

  function remove(s: SkinView) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('skins.deleteTitle', { name: s.name }),
        confirm: t('common.delete'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            skins = await call<SkinView[]>('skins.remove', { id: s.id });
            if (preview?.id === s.id) preview = null;
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }
</script>

<div class="page">
  <div class="toolbar">
    <h1>{t('skins.title')}</h1>
    <Segmented bind:value={tab} label={t('skins.areas')} options={[{ value: 'skins', label: t('skins.tabSkins') }, { value: 'capes', label: t('skins.tabCapes') }, { value: 'emotes', label: t('skins.tabEmotes') }]} />
    <div class="grow"></div>
    {#if tab === 'skins'}
      <button class="btn btn-tool" onclick={() => (dialog = 'adopt')}><Icon name="user" />{t('skins.adopt')}</button>
      <button class="btn btn-grad" style="height: 36px" onclick={() => (dialog = 'upload')}><Icon name="upload" stroke={1.8} />{t('skins.upload')}</button>
    {/if}
  </div>

  {#if tab === 'skins'}
    <div class="body">
      <div class="stage panel">
        <SkinViewer skin={shownSkin} cape={preview ? null : activeCape} model={shownModel === 'slim' ? 'slim' : 'default'} width={300} height={440} label={t('home.skinModel')} />
        <span class="stage-label">{preview ? t('skins.previewOf', { name: preview.name }) : t('skins.current')}</span>
        {#if preview}
          <div class="row">
            <button class="btn btn-ghost sm" onclick={() => (preview = null)}>{t('skins.backToCurrent')}</button>
            <button class="btn btn-grad sm" disabled={applying !== null} onclick={() => apply(preview!)}>{t('skins.use')}</button>
          </div>
        {/if}
      </div>
      <div class="library">
        <span class="section-label">{t('skins.library', { count: skins.length })}</span>
        <div class="grid">
          {#each skins as s (s.id)}
            <article class="card" class:active={s.active} class:selected={preview?.id === s.id}>
              <button class="remove" aria-label={t('skins.deleteNamed', { name: s.name })} title={t('common.delete')} onclick={() => remove(s)}><Icon name="x" size={12} /></button>
              <button class="figure" aria-label={t('skins.preview', { name: s.name })} onclick={() => (preview = s)}>
                <SkinFront src={s.url} slim={s.model === 'slim'} scale={7} />
                {#if s.active}<span class="badge">{t('skins.active')}</span>{/if}
              </button>
              <div class="meta">
                <span class="name truncate">{s.name}</span>
                <span class="sub">{s.model === 'slim' ? 'Slim' : 'Classic'}{s.player ? ` · ${t('skins.fromPlayer')}` : ''}</span>
              </div>
              <div class="actions">
                <button class="btn {s.active ? 'btn-ghost' : 'btn-grad'} sm grow" disabled={s.active || applying !== null} onclick={() => apply(s)}>
                  {#if applying === s.id}<span class="spinner"></span>{/if}{s.active ? t('skins.active') : t('skins.use')}
                </button>
                <button class="icon-btn small" aria-label={t('skins.editNamed', { name: s.name })} title={t('common.edit')} onclick={() => (dialog = { edit: s })}><Icon name="edit" size={14} /></button>
              </div>
            </article>
          {/each}
          <button class="card add" onclick={() => (dialog = 'upload')}>
            <span class="plus"><Icon name="plus" size={20} /></span>
            <span>{t('skins.upload')}</span>
          </button>
        </div>
      </div>
    </div>
  {:else if tab === 'capes'}
    <Capes {profile} onchange={load} />
  {:else}
    <Emotes />
  {/if}
</div>

{#if dialog === 'upload'}
  <SkinDialog onclose={() => (dialog = null)} onsaved={load} />
{:else if dialog === 'adopt'}
  <AdoptDialog onclose={() => (dialog = null)} onsaved={load} />
{:else if dialog && typeof dialog === 'object'}
  <SkinDialog skin={dialog.edit} onclose={() => (dialog = null)} onsaved={load} />
{/if}

<style>
  .page {
    height: 100%;
    padding: 24px 28px;
    display: flex;
    flex-direction: column;
    gap: 20px;
  }
  .toolbar {
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
  .stage-label {
    font-size: 13px;
    color: var(--muted);
  }
  .library {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .grid {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(220px, 1fr));
    grid-auto-rows: max-content;
    gap: 14px;
    align-content: start;
  }
  .card {
    position: relative;
    padding: 10px;
    border: 0;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  /* Löschen: kleines X oben rechts, nur beim Hovern */
  .remove {
    position: absolute;
    z-index: 1;
    top: 16px;
    right: 16px;
    width: 22px;
    height: 22px;
    border-radius: 11px;
    border: none;
    padding: 0;
    background: #3a3a3a;
    color: var(--text);
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.45);
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
    opacity: 0;
    transform: scale(0.85);
    transition: opacity 0.12s ease, transform 0.12s ease, background-color 0.12s ease;
  }
  .card:hover .remove,
  .remove:focus-visible {
    opacity: 1;
    transform: none;
  }
  .remove:hover {
    background: var(--red);
    color: #fff;
  }
  .card.selected {
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .card.active {
    background: linear-gradient(var(--panel), var(--panel)) padding-box, var(--accent-soft-strong);
  }
  .figure {
    position: relative;
    height: 252px;
    border: 0;
    border-radius: 8px;
    background: #1f1f1f;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .badge {
    position: absolute;
    top: 8px;
    left: 8px;
    height: 20px;
    padding: 0 8px;
    border-radius: 10px;
    background: var(--accent);
    color: #fff;
    font-size: 10.5px;
    font-weight: 700;
    line-height: 20px;
  }
  .meta {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .name {
    font-size: 13.5px;
    font-weight: 600;
  }
  .sub {
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .actions {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  /* Feste Aufteilung: Verwenden/Aktiv/Spinner verändern die Breite nicht, die Icons bleiben stehen */
  .actions .btn {
    flex: 1 1 0;
    min-width: 0;
    height: 30px;
    box-shadow: none;
  }
  .icon-btn.small {
    width: 30px;
    height: 30px;
    color: var(--muted);
  }
  .add {
    align-items: center;
    justify-content: center;
    min-height: 280px;
    color: var(--muted);
    cursor: pointer;
    box-shadow: inset 0 0 0 1.5px #3a3a3a;
    background: transparent;
    font-size: 13px;
    font-weight: 600;
  }
  .add:hover {
    color: var(--accent-text);
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .plus {
    width: 44px;
    height: 44px;
    border-radius: 12px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
</style>
