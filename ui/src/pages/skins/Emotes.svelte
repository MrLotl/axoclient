<script lang="ts">
  import { onMount } from 'svelte';
  import { flip } from 'svelte/animate';
  import { fadeSlide, pop, stagger, reducedMotion, DUR } from '../../lib/util/motion';

  const delay = stagger(30, 12, 700);
  const flipMs = reducedMotion() ? 0 : DUR.slow;
  import { app } from '../../lib/stores/app.svelte';
  import { call, on } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import Select from '../../lib/components/Select.svelte';
  import EmotePreview from '../../lib/components/EmotePreview.svelte';
  import { clearEmotePreviews } from '../../lib/util/emotePreview';

  type EmoteInfo = { id: string; name: string; source: string; createdAt?: string; loop?: boolean; moving?: boolean };
  type EmoteList = { builtin: EmoteInfo[]; imported: EmoteInfo[]; slots: (string | null)[] };

  let builtin = $state<EmoteInfo[]>([]);
  let imported = $state<EmoteInfo[]>([]);
  let slots = $state<(string | null)[]>([null, null, null, null, null, null, null, null]);
  let importing = $state(false);
  let renaming = $state<string | null>(null);
  let renameValue = $state('');
  /** Erhöht sich bei Skin- oder Emote-Änderungen: die Vorschaubilder werden neu gerechnet. */
  let previewVersion = $state(0);

  // Strichmännchen der eingebauten Emotes (wie im Ingame-Menü), 24er Raster
  const legs = 'M12 14l-3 6M12 14l3 6';
  const figures: Record<string, { hx: number; hy: number; d: string }> = {
    wave: { hx: 12, hy: 5, d: 'M12 7.5V14M12 9l-3.5 3.5M12 9l3.5-3.5 1-2.5' + legs },
    bow: { hx: 16, hy: 8, d: 'M12 14l2.5-4.5M13.6 11l2.4 2.5' + legs },
    clap: { hx: 12, hy: 5, d: 'M12 7.5V14M12 9l-2 2.2 2 1.3M12 9l2 2.2-2 1.3' + legs },
    cheer: { hx: 12, hy: 5, d: 'M12 7.5V14M12 9L8.5 4M12 9l3.5-5' + legs },
    dance: { hx: 12, hy: 5, d: 'M12 7.5V14M12 9L8 6.5M12 9l4 3M12 14l-4 3.5M12 14l2 6' },
    sit: { hx: 12, hy: 8, d: 'M12 10.5V16M12 16h5v4M12 12.5l3 1.5' },
    point: { hx: 12, hy: 5, d: 'M12 7.5V14M12 9l6-1M12 9l-3 3.5' + legs },
    salute: { hx: 12, hy: 5, d: 'M12 7.5V14M12 9l2.2-2.8-3.4-1.6M12 9l-3 4' + legs },
    facepalm: { hx: 12, hy: 5, d: 'M12 7.5V14M12 9l2.2-1.8-1.4-2M12 9l-3 4' + legs },
    nod: { hx: 12, hy: 5.5, d: 'M12 8V14M12 9.5l-2.5 3.5M12 9.5l2.5 3.5M8.5 3.5l1 1M15.5 3.5l-1 1' + legs }
  };
  const custom = { hx: 12, hy: 5, d: 'M12 7.5V14M12 9l-4 2M12 9l4 2' + legs };
  const figure = (id: string) => figures[id] ?? custom;

  async function load() {
    try {
      const r = await call<EmoteList>('emotes.list');
      builtin = r.builtin;
      imported = r.imported;
      slots = r.slots;
    } catch (e) {
      app.error(e);
    }
  }

  function refreshPreviews() {
    clearEmotePreviews();
    previewVersion++;
  }

  onMount(() => {
    load();
    const offEmotes = on('emotes.changed', () => {
      refreshPreviews();
      load();
    });
    const offSkins = on('skins.changed', refreshPreviews);
    return () => {
      offEmotes();
      offSkins();
    };
  });

  const options = $derived([
    { value: '', label: t('emotes.empty') },
    ...builtin.map((e) => ({ value: e.id, label: e.name, hint: 'AxoClient' })),
    ...imported.map((e) => ({ value: e.id, label: e.name, hint: t('emotes.imported') }))
  ]);

  async function pick() {
    try {
      const files = await call<{ id: string; name: string }[]>('dialog.openFile', { purpose: 'emote', multi: true });
      if (!files.length) return;
      importing = true;
      const added = await call<EmoteInfo[]>('emotes.import', { fileRefs: files.map((f) => f.id) });
      app.toast(added.length === 1 ? t('emotes.importedOne', { name: added[0].name }) : t('emotes.importedMany', { count: added.length }), 'success');
      await load();
    } catch (e) {
      app.error(e);
    } finally {
      importing = false;
    }
  }

  async function setSlot(index: number, id: string) {
    const next = [...slots];
    const old = next.indexOf(id);
    if (id && old !== -1 && old !== index) next[old] = next[index];
    next[index] = id || null;
    try {
      slots = await call<(string | null)[]>('emotes.setSlots', { slots: next });
    } catch (e) {
      app.error(e);
    }
  }

  // Dauerhaft / beim Laufen: beim Import erkannt, hier umstellbar
  async function setOption(e: EmoteInfo, field: 'loop' | 'moving') {
    const next = { loop: !!e.loop, moving: !!e.moving, [field]: !e[field] };
    try {
      imported = await call<EmoteInfo[]>('emotes.setOptions', { id: e.id, ...next });
    } catch (err) {
      app.error(err);
    }
  }

  function startRename(e: EmoteInfo) {
    renaming = e.id;
    renameValue = e.name;
  }

  async function saveRename() {
    if (!renaming) return;
    try {
      imported = await call<EmoteInfo[]>('emotes.rename', { id: renaming, name: renameValue });
    } catch (e) {
      app.error(e);
    } finally {
      renaming = null;
    }
  }

  function remove(e: EmoteInfo) {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('emotes.deleteTitle', { name: e.name }),
        text: t('emotes.deleteText'),
        confirm: t('common.delete'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            imported = await call<EmoteInfo[]>('emotes.remove', { id: e.id });
            await load();
          } catch (err) {
            app.error(err);
          }
        }
      }
    };
  }

  const nameOf = (id: string | null) => (id ? (builtin.find((b) => b.id === id) ?? imported.find((i) => i.id === id))?.name ?? id : t('emotes.empty'));
</script>

<div class="body">
  <section class="wheel panel" aria-label={t('emotes.wheel')}>
    <div class="head">
      <span class="section-label">{t('emotes.wheel')}</span>
      <span class="count">{slots.filter(Boolean).length} / 8</span>
    </div>
    <div class="ring" aria-hidden="true">
      {#each slots as id, i}
        {@const a = (i * Math.PI) / 4}
        <span class="slot" class:filled={!!id} style="left: {120 + 96 * Math.sin(a) - 25}px; top: {120 - 96 * Math.cos(a) - 25}px">
          {#if id}
            {@const f = figure(id)}
            <EmotePreview {id} size={40} version={previewVersion}>
              <svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx={f.hx} cy={f.hy} r="2.2" /><path d={f.d} /></svg>
            </EmotePreview>
          {:else}
            <Icon name="plus" size={16} />
          {/if}
        </span>
      {/each}
      <span class="center"></span>
    </div>
    <div class="slots">
      {#each slots as id, i}
        <div class="slot-row">
          <span class="slot-no">{i + 1}</span>
          <Select value={id ?? ''} {options} label={t('emotes.slotLabel', { n: i + 1 })} onchange={(v) => setSlot(i, v as string)} />
        </div>
      {/each}
    </div>
    <p class="hint">{t('emotes.wheelHint')}</p>
  </section>

  <section class="library" aria-label={t('emotes.library')}>
    <div class="head">
      <span class="section-label">{t('emotes.own', { count: imported.length })}</span>
      <div class="grow"></div>
      <button class="btn btn-tool" disabled={importing} onclick={pick}>
        {#if importing}<span class="spinner"></span>{:else}<Icon name="upload" size={14} stroke={1.8} />{/if}{t('emotes.import')}
      </button>
    </div>
    {#if imported.length === 0}
      <div class="empty panel">
        <span class="empty-icon"><Icon name="upload" size={22} /></span>
        <strong>{t('emotes.emptyTitle')}</strong>
        <p>{t('emotes.emptyText')}</p>
        <button class="btn btn-second" disabled={importing} onclick={pick}>{t('emotes.import')}</button>
      </div>
    {:else}
      <div class="grid">
        {#each imported as e, i (e.id)}
          <article class="card" animate:flip={{ duration: flipMs }} in:fadeSlide|global={{ delay: delay(i), y: -6 }} out:pop={{ scale: 0.94, duration: DUR.med }}>
            <span class="figure">
              {#if true}
                {@const f = figure(e.id)}
                <EmotePreview id={e.id} size={56} version={previewVersion}>
                  <svg width="34" height="34" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><circle cx={f.hx} cy={f.hy} r="2.2" /><path d={f.d} /></svg>
                </EmotePreview>
              {/if}
              {#if slots.includes(e.id)}<span class="badge">{slots.indexOf(e.id) + 1}</span>{/if}
            </span>
            {#if renaming === e.id}
              <!-- svelte-ignore a11y_autofocus -->
              <input class="input sm" bind:value={renameValue} maxlength="24" autofocus aria-label={t('emotes.rename')}
                onkeydown={(ev) => { if (ev.key === 'Enter') saveRename(); if (ev.key === 'Escape') renaming = null; }} onblur={saveRename} />
            {:else}
              <span class="name truncate">{e.name}</span>
            {/if}
            <div class="options">
              <button class="opt" class:on={e.loop} aria-pressed={!!e.loop} title={t('emotes.loopHint')} onclick={() => setOption(e, 'loop')}>
                <Icon name="refresh" size={12} />{t('emotes.loop')}
              </button>
              <button class="opt" class:on={e.moving} aria-pressed={!!e.moving} title={t('emotes.movingHint')} onclick={() => setOption(e, 'moving')}>
                <Icon name="play" size={11} />{t('emotes.moving')}
              </button>
            </div>
            <div class="actions">
              <button class="icon-btn small" aria-label={t('emotes.renameNamed', { name: e.name })} title={t('emotes.rename')} onclick={() => startRename(e)}><Icon name="edit" size={14} /></button>
              <button class="icon-btn small danger" aria-label={t('emotes.deleteNamed', { name: e.name })} title={t('common.delete')} onclick={() => remove(e)}><Icon name="trash" size={14} /></button>
            </div>
          </article>
        {/each}
      </div>
    {/if}

    <span class="section-label">{t('emotes.builtin', { count: builtin.length })}</span>
    <div class="chips">
      {#each builtin as e (e.id)}
        {@const f = figure(e.id)}
        <span class="chip">
          <EmotePreview id={e.id} size={28} version={previewVersion}>
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><circle cx={f.hx} cy={f.hy} r="2.2" /><path d={f.d} /></svg>
          </EmotePreview>
          {e.name}
        </span>
      {/each}
    </div>
  </section>
</div>

<style>
  .body {
    flex-grow: 1;
    min-height: 0;
    display: flex;
    gap: 20px;
  }
  .wheel {
    width: 300px;
    flex-shrink: 0;
    padding: 16px;
    display: flex;
    flex-direction: column;
    gap: 12px;
    overflow-y: auto;
  }
  .head {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 10px;
  }
  .count {
    font-size: 12px;
    color: var(--muted-2);
  }
  .ring {
    position: relative;
    width: 240px;
    height: 240px;
    align-self: center;
    flex-shrink: 0;
  }
  .ring::before {
    content: '';
    position: absolute;
    inset: 22px;
    border-radius: 50%;
    background: radial-gradient(circle, rgba(139, 92, 246, 0.1) 0%, rgba(0, 0, 0, 0) 70%);
    box-shadow: inset 0 0 0 1px #3a3a3a;
  }
  .slot {
    position: absolute;
    width: 50px;
    height: 50px;
    border-radius: 50%;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #6b6b6b;
    box-shadow: inset 0 0 0 1.5px #444;
  }
  .slot.filled {
    background: #333;
    color: #d9d9d9;
    box-shadow: none;
  }
  .center {
    position: absolute;
    left: 75px;
    top: 75px;
    width: 90px;
    height: 90px;
    border-radius: 50%;
    background: #242424;
    box-shadow: inset 0 0 0 1px #3a3a3a;
    display: flex;
    align-items: center;
    justify-content: center;
    text-align: center;
    padding: 10px;
    box-sizing: border-box;
    font-size: 11px;
    line-height: 1.35;
    color: var(--muted-2);
  }
  .slots {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .slot-row {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .slot-no {
    width: 22px;
    height: 22px;
    flex-shrink: 0;
    border-radius: 11px;
    background: var(--accent);
    color: #fff;
    font-size: 11px;
    font-weight: 700;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .hint {
    margin: 0;
    font-size: 12px;
    line-height: 1.45;
    color: var(--muted-2);
  }
  .library {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
    overflow-y: auto;
  }
  .grow {
    flex-grow: 1;
  }
  .grid {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(180px, 1fr));
    gap: 12px;
  }
  .card {
    padding: 8px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .figure {
    position: relative;
    height: 72px;
    border-radius: 8px;
    background: #1f1f1f;
    color: #ecc6f2;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .badge {
    position: absolute;
    top: 6px;
    right: 6px;
    min-width: 18px;
    height: 18px;
    padding: 0 5px;
    box-sizing: border-box;
    border-radius: 9px;
    background: var(--accent);
    color: #fff;
    font-size: 10px;
    font-weight: 700;
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .name {
    font-size: 13px;
    font-weight: 600;
  }
  .actions {
    display: flex;
    gap: 6px;
  }
  .options {
    display: flex;
    gap: 4px;
  }
  .opt {
    flex: 1;
    min-width: 0;
    height: 26px;
    padding: 0 6px;
    border: 0;
    border-radius: 7px;
    background: var(--panel-3);
    color: var(--muted-2);
    font-size: 11px;
    font-weight: 600;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 5px;
    cursor: pointer;
    white-space: nowrap;
    transition: background-color 0.12s ease, color 0.12s ease;
  }
  .opt:hover {
    background: var(--panel-4);
    color: var(--text-2);
  }
  .opt.on {
    background: var(--accent-soft);
    color: var(--accent-text);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
  }
  .icon-btn.small {
    width: 30px;
    height: 30px;
    color: var(--muted);
  }
  .icon-btn.danger:hover {
    color: var(--red);
  }
  .empty {
    padding: 28px;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
    text-align: center;
  }
  .empty p {
    margin: 0;
    max-width: 460px;
    font-size: 13px;
    line-height: 1.5;
    color: var(--muted);
  }
  .empty-icon {
    width: 48px;
    height: 48px;
    border-radius: 14px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .chips {
    display: flex;
    flex-wrap: wrap;
    gap: 6px;
  }
  .chip {
    height: 30px;
    padding: 0 10px 0 8px;
    border-radius: 8px;
    background: var(--panel);
    color: var(--text-2);
    font-size: 12.5px;
    display: inline-flex;
    align-items: center;
    gap: 6px;
  }
  .chip svg {
    color: #ecc6f2;
  }
</style>
