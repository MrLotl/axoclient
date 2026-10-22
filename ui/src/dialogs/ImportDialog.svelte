<script lang="ts">
  import { onMount } from 'svelte';
  import Dialog from '../lib/components/Dialog.svelte';
  import Icon from '../lib/components/Icon.svelte';
  import Checkbox from '../lib/components/Checkbox.svelte';
  import Select from '../lib/components/Select.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';
  import { loaderName } from '../lib/util/format';
  import { upload } from '../lib/util/files';
  import type { FileRef, ForeignImportParts, ForeignImportResult, ForeignInstanceView, ForeignLauncherView, Instance, LoaderType } from '../lib/types';

  /**
   * Import aus anderen Launchern: links die gefundenen Launcher, rechts deren Instanzen zum Auswählen.
   * Name, Loader und Version lassen sich pro Instanz anpassen; was mitkommt, gilt für alle.
   * Unten links zusätzlich "Datei oder Ordner" für .mrpack, Backups und Exporte anderer Launcher.
   */
  let {
    onclose,
    file = null,
    foreign = null,
    target = null
  }: {
    onclose: () => void;
    file?: FileRef | null;
    foreign?: ForeignInstanceView | null;
    /** Gesetzt = "Übernehmen": genau eine Quelle wählen und in diese bestehende Instanz kopieren */
    target?: { id: string; name: string; loader: LoaderType; minecraftVersion: string } | null;
  } = $props();

  const into = !!target;

  type Choice = { name: string; loader: LoaderType; version: string };

  let launchers = $state<ForeignLauncherView[] | null>(null);
  let fromFiles = $state<ForeignInstanceView[]>([]);
  let active = $state<string>('file');
  let selected = $state<Record<string, Choice>>({});
  let parts = $state<ForeignImportParts>({ worlds: true, mods: true, packs: true, settings: true, screenshots: true });
  let running = $state(false);
  let results = $state<ForeignImportResult[] | null>(null);
  let fileBusy = $state(false);
  let dragging = $state(false);

  const loaders: LoaderType[] = ['vanilla', 'fabric', 'forge', 'neoforge', 'quilt'];
  const found = $derived((launchers ?? []).filter((l) => l.installed));
  const missing = $derived((launchers ?? []).filter((l) => !l.installed));
  const current = $derived(launchers?.find((l) => l.id === active) ?? null);
  const shownInstances = $derived(active === 'file' ? fromFiles : (current?.instances ?? []));
  const all = $derived([...(launchers ?? []).flatMap((l) => l.instances), ...fromFiles]);
  const count = $derived(Object.keys(selected).length);
  const noParts = $derived(!Object.values(parts).some(Boolean));
  const invalid = $derived(!into && Object.values(selected).some((c) => !c.name.trim() || !/^[\w.\-+ ]{2,40}$/.test(c.version.trim())));

  onMount(async () => {
    if (foreign) {
      active = 'file';
      addFromFile(foreign);
    }
    if (file) await importFile(file);
    try {
      const scanned = await call<ForeignLauncherView[]>('import.scan');
      launchers = target ? [await call<ForeignLauncherView>('import.ownSources', { id: target.id }), ...scanned] : scanned;
      if (!file && !foreign) active = launchers.find((l) => l.instances.length > 0)?.id ?? 'file';
    } catch (e) {
      app.error(e);
      launchers = [];
    }
  });

  function toggle(inst: ForeignInstanceView, on: boolean) {
    if (on && into) selected = {};
    if (on) selected[inst.id] = { name: inst.name, loader: inst.loader ?? 'vanilla', version: inst.minecraftVersion ?? '' };
    else delete selected[inst.id];
  }

  function toggleAll() {
    const usable = shownInstances.filter((i) => !i.problem);
    const allOn = usable.every((i) => selected[i.id]);
    for (const inst of usable) toggle(inst, !allOn);
  }

  function selectedIn(l: ForeignLauncherView) {
    return l.instances.filter((i) => selected[i.id]).length;
  }

  function describe(inst: ForeignInstanceView) {
    const bits = [inst.loader ? loaderName(inst.loader) : t('import.loaderUnknown'), inst.minecraftVersion ?? t('import.versionUnknown')];
    if (inst.mods) bits.push(t('import.modsCount', { count: inst.mods }));
    if (inst.worlds) bits.push(t('import.worldsCount', { count: inst.worlds }));
    return bits.join(' · ');
  }

  async function importFile(ref: FileRef) {
    fileBusy = true;
    active = 'file';
    try {
      const r = await call<{ kind: 'instance'; instance: Instance } | { kind: 'foreign'; foreign: ForeignInstanceView }>('import.file', { fileRef: ref.id });
      if (r.kind === 'instance') {
        app.toast(t('instances.imported', { name: r.instance.name }), 'success');
        await app.refreshInstances();
        onclose();
      } else addFromFile(r.foreign);
    } catch (e) {
      app.error(e);
    } finally {
      fileBusy = false;
    }
  }

  function addFromFile(inst: ForeignInstanceView) {
    fromFiles = [...fromFiles.filter((i) => i.id !== inst.id), inst];
    if (!inst.problem) toggle(inst, true);
  }

  async function chooseFile() {
    try {
      const files = await call<FileRef[]>('dialog.openFile', { purpose: 'import' });
      if (files[0]) await importFile(files[0]);
    } catch (e) {
      app.error(e);
    }
  }

  async function chooseFolder() {
    try {
      const inst = await call<ForeignInstanceView | null>('import.folder');
      if (inst) addFromFile(inst);
    } catch (e) {
      app.error(e);
    }
  }

  async function ondrop(e: DragEvent) {
    e.preventDefault();
    dragging = false;
    const dropped = e.dataTransfer?.files[0];
    if (!dropped || !/\.(mrpack|zip)$/i.test(dropped.name)) return;
    await importFile(await upload(dropped));
  }

  async function run() {
    running = true;
    try {
      if (target) {
        const sourceId = Object.keys(selected)[0];
        results = [await call<ForeignImportResult>('import.into', { sourceId, targetId: target.id, parts })];
        await app.refreshInstances();
        return;
      }
      const choices = Object.entries(selected).map(([id, c]) => ({ id, name: c.name.trim(), loader: c.loader, minecraftVersion: c.version.trim(), parts }));
      results = await call<ForeignImportResult[]>('import.run', { choices });
      await app.refreshInstances();
    } catch (e) {
      app.error(e);
    } finally {
      running = false;
    }
  }

  function open(id: string) {
    onclose();
    app.navigate({ page: 'instance', id });
  }
</script>

<Dialog
  title={target ? t('transfer.dialogTitle', { name: target.name }) : t('import.title')}
  subtitle={target ? t('transfer.subtitle') : t('import.subtitle')} icon="import" width={980} padded={false} {onclose}>
  {#if results}
    <div class="results">
      {#each results as r (r.name + (r.instanceId ?? ''))}
        <div class="result" class:failed={!!r.error}>
          <span class="r-icon"><Icon name={r.error ? 'warning' : 'check'} size={14} /></span>
          <div class="col grow" style="gap: 4px; min-width: 0">
            <strong class="truncate">{r.name}</strong>
            {#if r.error}<span class="r-note err">{tr(r.error)}</span>{/if}
            {#each r.notes as n, i (i)}<span class="r-note">{tr(n)}</span>{/each}
          </div>
          {#if r.instanceId}<button class="btn btn-second sm" onclick={() => open(r.instanceId!)}>{t('import.open')}</button>{/if}
        </div>
      {/each}
    </div>
  {:else}
    <div class="body">
      <nav class="nav" aria-label={t('import.launchers')}>
        {#if launchers === null}
          <div class="scanning"><span class="spinner dark"></span>{t('import.scanningText')}</div>
        {:else}
          {#if found.length}<span class="nav-head">{t('import.found')}</span>{/if}
          {#each found as l (l.id)}
            <button class="nav-item" class:active={active === l.id} onclick={() => (active = l.id)}>
              <span class="grow truncate">{l.name}</span>
              {#if selectedIn(l)}<span class="chip accent">{selectedIn(l)}</span>{/if}
              <span class="n">{l.instances.length}</span>
            </button>
          {/each}
          {#if missing.length}<span class="nav-head">{t('import.notFound')}</span>{/if}
          {#each missing as l (l.id)}
            <button class="nav-item off" class:active={active === l.id} title={t('import.notInstalled')} onclick={() => (active = l.id)}>
              <span class="grow truncate">{l.name}</span>
            </button>
          {/each}
        {/if}
        <div class="grow"></div>
        <button class="nav-item file" class:active={active === 'file'} onclick={() => (active = 'file')}>
          <Icon name="folder" size={14} /><span class="grow">{t('import.fromFile')}</span>
          {#if fromFiles.length}<span class="n">{fromFiles.length}</span>{/if}
        </button>
      </nav>

      <section class="main">
        {#if active === 'file'}
          <!-- svelte-ignore a11y_no_static_element_interactions -->
          <div
            class="drop"
            class:dragging
            ondragover={(e) => {
              e.preventDefault();
              dragging = true;
            }}
            ondragleave={() => (dragging = false)}
            {ondrop}>
            {#if fileBusy}
              <span class="spinner dark"></span>
            {:else}
              <Icon name="import" size={24} />
            {/if}
            <strong>{t('import.fileTitle')}</strong>
            <span class="hint">{t('import.fileText')}</span>
            <div class="row" style="gap: 8px">
              <button class="btn btn-second sm" disabled={fileBusy} onclick={chooseFile}><Icon name="upload" size={13} />{t('import.chooseFile')}</button>
              <button class="btn btn-second sm" disabled={fileBusy} onclick={chooseFolder}><Icon name="folder" size={13} />{t('import.chooseFolder')}</button>
            </div>
          </div>
        {:else if current}
          <div class="main-head">
            <h3>{current.name}</h3>
            <div class="grow"></div>
            {#if !into && current.instances.some((i) => !i.problem)}
              <button class="btn btn-flat sm" onclick={toggleAll}>
                {current.instances.filter((i) => !i.problem).every((i) => selected[i.id]) ? t('import.selectNone') : t('import.selectAll')}
              </button>
            {/if}
          </div>
          {#if !current.installed}
            <p class="empty">{t('import.notInstalledText', { name: current.name })}</p>
          {:else if current.problem}
            <p class="empty err">{tr(current.problem)}</p>
          {:else if current.instances.length === 0}
            <p class="empty">{t('import.noInstances')}</p>
          {/if}
        {/if}

        <div class="list">
          {#each shownInstances as inst (inst.id)}
            {@const choice = selected[inst.id]}
            <div class="inst" class:on={!!choice} class:disabled={!!inst.problem}>
              <div class="inst-row">
                <Checkbox checked={!!choice} disabled={!!inst.problem} label="" onchange={(v) => toggle(inst, v)} />
                <div class="col grow" style="gap: 3px; min-width: 0">
                  <strong class="truncate">{inst.name}</strong>
                  <span class="desc">{describe(inst)}{inst.shared ? ` · ${t('import.shared')}` : ''}</span>
                </div>
              </div>
              {#if inst.problem}<p class="note err"><Icon name="warning" size={12} />{tr(inst.problem)}</p>{/if}
              {#if inst.hint}<p class="note"><Icon name="info" size={12} />{tr(inst.hint)}</p>{/if}
              {#if choice && target}
                {#if inst.mods && parts.mods && (inst.loader !== target.loader || (inst.minecraftVersion && inst.minecraftVersion !== target.minecraftVersion))}
                  <p class="note warn">
                    <Icon name="info" size={12} />{inst.id.startsWith('axo:') ? t('transfer.modsReinstall', { version: target.minecraftVersion }) : t('import.modsWontFit', { count: inst.mods })}
                  </p>
                {/if}
              {:else if choice}
                <div class="edit">
                  <label class="label">
                    <span>{t('import.nameLabel')}</span>
                    <span class="field small"><input bind:value={choice.name} maxlength="60" /></span>
                  </label>
                  <div class="label">
                    <span>{t('import.loaderLabel')}</span>
                    <Select bind:value={choice.loader} label={t('import.loaderLabel')} options={loaders.map((l) => ({ value: l, label: loaderName(l) }))} />
                  </div>
                  <label class="label">
                    <span>{t('import.versionLabel')}</span>
                    <span class="field small" class:invalid={!choice.version.trim()}><input bind:value={choice.version} placeholder="1.21.1" spellcheck="false" /></span>
                  </label>
                </div>
                {#if inst.loader && inst.mods && (choice.loader !== inst.loader || (inst.minecraftVersion && choice.version.trim() !== inst.minecraftVersion))}
                  <p class="note warn"><Icon name="warning" size={12} />{t('import.modsWontFit', { count: inst.mods })}</p>
                {/if}
              {/if}
            </div>
          {/each}
        </div>
      </section>
    </div>

    <div class="parts">
      <span class="parts-head">{t('import.partsTitle')}</span>
      <Checkbox bind:checked={parts.worlds} label={t('import.parts.worlds')} small />
      <Checkbox bind:checked={parts.mods} label={t('import.parts.mods')} small />
      <Checkbox bind:checked={parts.packs} label={t('import.parts.packs')} small />
      <Checkbox bind:checked={parts.settings} label={t('import.parts.settings')} small />
      <Checkbox bind:checked={parts.screenshots} label={t('import.parts.screenshots')} small />
    </div>
  {/if}

  {#snippet footer()}
    {#if results}
      <button class="btn btn-grad" onclick={onclose}>{t('import.done')}</button>
    {:else}
      <span class="hint grow">{into ? t('transfer.note') : count ? t('import.selected', { count }) : t('import.note')}</span>
      <button class="btn btn-ghost" onclick={onclose}>{t('common.cancel')}</button>
      <button class="btn btn-grad" disabled={!count || invalid || noParts || running} onclick={run}>
        {#if running}<span class="spinner"></span>{/if}{into ? t('transfer.run') : t('import.run', { count })}
      </button>
    {/if}
  {/snippet}
</Dialog>

<style>
  .body {
    height: min(520px, calc(100vh - 300px));
    display: flex;
    border-top: 1px solid var(--line);
  }
  .nav {
    width: 230px;
    flex-shrink: 0;
    padding: 12px 10px;
    border-right: 1px solid var(--line);
    display: flex;
    flex-direction: column;
    gap: 2px;
    overflow-y: auto;
  }
  .nav-head {
    padding: 10px 10px 4px;
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.5px;
    text-transform: uppercase;
    color: var(--faint);
  }
  .nav-item {
    height: 36px;
    padding: 0 10px;
    border: 0;
    border-radius: 8px;
    background: transparent;
    color: var(--text-2);
    font-size: 13px;
    font-weight: 500;
    display: flex;
    align-items: center;
    gap: 8px;
    cursor: pointer;
    text-align: left;
    flex-shrink: 0;
  }
  .nav-item:hover {
    background: #303030;
  }
  .nav-item.active {
    background: var(--accent-soft);
    color: var(--accent-text);
  }
  .nav-item.off {
    color: var(--faint);
  }
  .nav-item.file {
    margin-top: 8px;
    box-shadow: inset 0 0 0 1px var(--line-2);
  }
  .n {
    font-size: 12px;
    color: var(--muted-2);
    font-variant-numeric: tabular-nums;
  }
  .scanning {
    padding: 14px 10px;
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 13px;
    color: var(--muted-2);
  }
  .main {
    flex-grow: 1;
    min-width: 0;
    padding: 16px 20px;
    display: flex;
    flex-direction: column;
    gap: 12px;
    overflow-y: auto;
  }
  .main-head {
    display: flex;
    align-items: center;
  }
  h3 {
    font-size: 15px;
    font-weight: 600;
  }
  .empty {
    padding: 30px 0;
    text-align: center;
    font-size: 13px;
    color: var(--muted-2);
  }
  .err {
    color: var(--red);
  }
  .list {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .inst {
    padding: 12px 14px;
    border-radius: 12px;
    background: var(--panel-2);
    box-shadow: inset 0 0 0 1px var(--line-2);
    display: flex;
    flex-direction: column;
    gap: 8px;
    transition: box-shadow 0.15s ease, background-color 0.15s ease;
  }
  .inst.on {
    background: color-mix(in srgb, var(--accent-from) 7%, var(--panel-2));
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .inst.disabled {
    opacity: 0.6;
  }
  .inst-row {
    display: flex;
    align-items: center;
    gap: 12px;
  }
  .desc {
    font-size: 12.5px;
    color: var(--muted-2);
  }
  .note {
    display: flex;
    gap: 7px;
    font-size: 12px;
    line-height: 1.45;
    color: var(--muted);
  }
  .note.warn {
    color: var(--yellow);
  }
  .edit {
    display: grid;
    grid-template-columns: minmax(0, 1.4fr) minmax(0, 1fr) minmax(0, 0.8fr);
    gap: 10px;
  }
  .field.small {
    height: 38px;
  }
  .drop {
    padding: 34px 20px;
    border-radius: 14px;
    box-shadow: inset 0 0 0 1.5px #3a3a3a;
    background: var(--panel-2);
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 10px;
    text-align: center;
    color: var(--muted);
    transition: box-shadow 0.15s ease, background-color 0.15s ease;
  }
  .drop.dragging {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 2px var(--accent-ring);
  }
  .drop strong {
    color: var(--text);
    font-size: 14px;
  }
  .parts {
    padding: 12px 20px;
    border-top: 1px solid var(--line);
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 8px 18px;
  }
  .parts-head {
    font-size: 12px;
    font-weight: 600;
    color: var(--muted);
  }
  .results {
    max-height: min(520px, calc(100vh - 300px));
    padding: 16px 20px;
    border-top: 1px solid var(--line);
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .result {
    padding: 12px 14px;
    border-radius: 12px;
    background: var(--panel-2);
    display: flex;
    align-items: flex-start;
    gap: 12px;
  }
  .r-icon {
    width: 26px;
    height: 26px;
    border-radius: 13px;
    background: var(--green-bg);
    color: var(--green);
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .result.failed .r-icon {
    background: var(--red-soft);
    color: var(--red);
  }
  .r-note {
    font-size: 12.5px;
    color: var(--muted);
    line-height: 1.45;
  }
</style>
