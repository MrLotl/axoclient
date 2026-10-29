<script lang="ts">
  import { app } from '../../lib/stores/app.svelte';
  import { call } from '../../lib/bridge/bridge';
  import { t } from '../../lib/i18n';
  import Icon from '../../lib/components/Icon.svelte';
  import FormSection from '../../lib/components/FormSection.svelte';
  import VersionPicker from '../../lib/components/VersionPicker.svelte';
  import MemorySlider from '../../lib/components/MemorySlider.svelte';
  import Toggle from '../../lib/components/Toggle.svelte';
  import Checkbox from '../../lib/components/Checkbox.svelte';
  import type { FileRef, InstanceDetails, LoaderType, ModLockLevel } from '../../lib/types';
  import { expand } from '../../lib/util/motion';

  let { details, onsaved }: { details: InstanceDetails; onsaved: () => void } = $props();

  const original = details.instance;
  let name = $state(original.name);
  let loader = $state<LoaderType>(original.loader.type);
  let minecraft = $state<string | null>(original.minecraft.version);
  let loaderVersion = $state<string | null>(original.loader.version);
  let ownMemory = $state(original.memoryMb !== null);
  let memory = $state(original.memoryMb ?? details.effectiveMemoryMb);
  let javaArgs = $state(original.javaArgs);
  // Ohne Unterstützung für die Version ist der Schalter aus und gesperrt – egal, was gespeichert ist
  let axoMod = $state(original.axoMod && details.axoModSupported);
  // Unterstützung hängt an Version und Loader: nach einem Wechsel neu bewerten
  const axoSupported = $derived(
    minecraft === original.minecraft.version && loader === original.loader.type
      ? details.axoModSupported
      : !!minecraft && (loader === 'fabric' || loader === 'quilt') && app.axoModVersions.includes(minecraft)
  );
  $effect(() => {
    if (!axoSupported) axoMod = false;
  });
  let saving = $state(false);
  // Beim Wechsel von Version oder Loader die Mods gleich mit umstellen
  let adaptMods = $state(true);
  const setupChanged = $derived(minecraft !== original.minecraft.version || loader !== original.loader.type);
  const canAdapt = $derived(loader !== 'vanilla' && original.loader.type !== 'vanilla' && (original.modLock?.level ?? 'off') !== 'full');

  async function adapt() {
    try {
      const r = await call<{ updated: number; disabled: string[]; failed: number }>('instances.adaptMods', { id: original.id });
      if (r.updated === 0 && r.disabled.length === 0 && r.failed === 0) {
        app.toast(t('instance.settings.adaptNothing'), 'success');
        return;
      }
      app.toast(t('instance.settings.adaptDone', { updated: r.updated, disabled: r.disabled.length }), 'success');
      if (r.disabled.length > 0)
        app.toast(t('instance.settings.adaptDisabledNames', { names: r.disabled.slice(0, 5).join(', ') + (r.disabled.length > 5 ? ' …' : '') }));
      if (r.failed > 0) app.toast(t('instance.settings.adaptFailed', { count: r.failed }), 'error');
    } catch (e) {
      app.error(e);
    }
  }

  const dirty = $derived(
    name !== original.name ||
      loader !== original.loader.type ||
      minecraft !== original.minecraft.version ||
      (loader !== 'vanilla' && loaderVersion !== original.loader.version) ||
      ownMemory !== (original.memoryMb !== null) ||
      (ownMemory && memory !== original.memoryMb) ||
      javaArgs !== original.javaArgs ||
      axoMod !== (original.axoMod && details.axoModSupported)
  );

  async function save() {
    saving = true;
    const adaptAfter = setupChanged && canAdapt && adaptMods;
    try {
      await call('instances.update', {
        id: original.id,
        name,
        minecraft,
        loader,
        loaderVersion: loader === 'vanilla' ? null : loaderVersion,
        memoryMb: ownMemory ? memory : null,
        resetMemory: !ownMemory,
        javaArgs,
        bannerAuto: null,
        axoMod: axoSupported ? axoMod : original.axoMod
      });
      app.toast(t('instance.settings.saved'), 'success');
      onsaved();
    } catch (e) {
      app.error(e);
      return;
    } finally {
      saving = false;
    }
    // Läuft als Job mit eigener Fortschrittsanzeige; die Seite muss darauf nicht warten
    if (adaptAfter) void adapt();
  }

  // Mod-Schutz wirkt sofort (nicht über die Speichern-Leiste), weil er die Mod-Liste direkt betrifft
  let lockLevel = $state<ModLockLevel>(original.modLock?.level ?? 'off');
  let lockBusy = $state(false);
  const lockLevels: ModLockLevel[] = ['off', 'pack', 'full'];

  async function setLock(level: ModLockLevel, adoptCurrent = false) {
    lockBusy = true;
    try {
      await call('instances.setModLock', { id: original.id, level, adoptCurrent });
      lockLevel = level;
      if (adoptCurrent) app.toast(t('modLock.adopted'), 'success');
      onsaved();
    } catch (e) {
      app.error(e);
    } finally {
      lockBusy = false;
    }
  }

  async function exportAs(format: 'mrpack' | 'backup') {
    try {
      const target = await call<FileRef | null>('dialog.saveFile', {
        purpose: format,
        name: `${original.name}.${format === 'mrpack' ? 'mrpack' : 'zip'}`
      });
      if (!target) return;
      await call('instances.export', { id: original.id, format, fileRef: target.id });
      app.toast(t('instance.settings.exportStarted', { file: target.name }), 'success');
    } catch (e) {
      app.error(e);
    }
  }

  function copy() {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('instance.settings.copyTitle', { name: original.name }),
        text: t('instance.settings.copyText'),
        confirm: t('instance.settings.copy'),
        icon: 'copy',
        onconfirm: () => call('instances.copy', { id: original.id, name: t('instance.settings.copyName', { name: original.name }) }).catch((e) => app.error(e))
      }
    };
  }

  function remove() {
    app.dialog = {
      kind: 'confirm',
      data: {
        title: t('instance.settings.deleteTitle', { name: original.name }),
        text: t('instance.settings.deleteText'),
        confirm: t('instance.settings.delete'),
        danger: true,
        icon: 'trash',
        onconfirm: async () => {
          try {
            await call('instances.delete', { id: original.id });
            await app.refreshInstances();
            app.navigate({ page: 'instances' });
          } catch (e) {
            app.error(e);
          }
        }
      }
    };
  }
</script>

<div class="settings">
  <div class="scroll">
  <!-- Zwei Zeilen mit je zwei Bereichen; nebeneinanderliegende Bereiche sind gleich hoch -->
  <div class="cols">
      <FormSection title={t('instance.settings.general')} subtitle={t('instance.settings.generalHint')}>
        <label class="label">
          <span>{t('instance.settings.name')}</span>
          <span class="field"><input bind:value={name} maxlength="60" /></span>
        </label>
        <VersionPicker bind:loader bind:minecraft bind:loaderVersion />
        <div class="axo-check" title={axoSupported ? t('instance.settings.axoHint') : t('versions.axoUnsupported')}>
          <Checkbox bind:checked={axoMod} label={t('instance.settings.axoMod')} disabled={!axoSupported} />
          <span class="hint">{axoSupported ? t('instance.settings.axoHint') : t('versions.axoUnsupported')}</span>
        </div>
        {#if setupChanged}
          <div class="adapt" transition:expand>
            {#if canAdapt}
              <Checkbox bind:checked={adaptMods} label={t('instance.settings.adaptMods')} />
            {/if}
            <p class="note" class:calm={canAdapt && adaptMods}><Icon name={canAdapt && adaptMods ? 'info' : 'warning'} size={14} />{canAdapt && adaptMods ? t('instance.settings.adaptModsHint') : t('instance.settings.versionChangeNote')}</p>
          </div>
        {/if}
      </FormSection>

      <FormSection title={t('instance.settings.performance')} subtitle={t('instance.settings.performanceHint')}>
        <!-- Zwei abgesetzte Blöcke mit viel Luft, damit die hohe Karte nicht gequetscht wirkt -->
        <div class="perf">
          <div class="perf-block">
            <div class="row between">
              <span class="text">{t('instance.settings.ownMemory')}</span>
              <Toggle bind:checked={ownMemory} label={t('instance.settings.ownMemory')} />
            </div>
            {#if ownMemory}
              <div transition:expand><MemorySlider bind:value={memory} mods={details.summary.modCount} /></div>
            {:else}
              <p class="hint" transition:expand>{t('instance.settings.memoryDefault', { gb: (details.effectiveMemoryMb / 1024).toLocaleString('de-DE') })}</p>
            {/if}
          </div>
          <label class="label perf-block">
            <span>{t('instance.settings.javaArgs')}</span>
            <span class="field mono"><input bind:value={javaArgs} placeholder="-XX:+UseG1GC" spellcheck="false" /></span>
            <span class="hint">{t('instance.settings.javaArgsHint')}</span>
          </label>
        </div>
      </FormSection>

      <FormSection title={t('modLock.title')} subtitle={t('modLock.subtitle')}>
        <div class="locks" role="radiogroup" aria-label={t('modLock.title')}>
          {#each lockLevels as level (level)}
            <button class="lock-opt" class:on={lockLevel === level} role="radio" aria-checked={lockLevel === level} disabled={lockBusy} onclick={() => level !== lockLevel && setLock(level)}>
              <span class="lock-icon"><Icon name={level === 'off' ? 'puzzle' : level === 'pack' ? 'shield' : 'lock'} size={15} /></span>
              <span class="col grow" style="gap: 2px; text-align: left; min-width: 0">
                <strong>{t(`modLock.levels.${level}.name`)}</strong>
                <span class="hint">{t(`modLock.levels.${level}.text`)}</span>
              </span>
            </button>
          {/each}
        </div>
        {#if lockLevel === 'pack'}
          <div class="row between" transition:expand>
            <span class="hint">{t('modLock.packCount', { count: original.modLock?.packMods.length ?? 0 })}</span>
            <button class="btn btn-flat sm" disabled={lockBusy} onclick={() => setLock('pack', true)}>{t('modLock.adopt')}</button>
          </div>
        {/if}
        {#if lockLevel !== 'off'}<p class="hint" transition:expand>{t('modLock.shareNote')}</p>{/if}
      </FormSection>

      <FormSection title={t('instance.settings.manage')} subtitle={t('instance.settings.manageHint')}>
        <div class="manage">
          <div class="group">
            <span class="sub">{t('instance.settings.manageExport')}</span>
            <button class="btn btn-second" onclick={() => exportAs('mrpack')}><Icon name="download" />{t('instance.settings.exportMrpack')}</button>
            <button class="btn btn-second" onclick={() => exportAs('backup')}><Icon name="archive" />{t('instance.settings.exportBackup')}</button>
          </div>
          <div class="group">
            <span class="sub">{t('instance.settings.manageInstance')}</span>
            <button class="btn btn-second" onclick={() => (app.dialog = { kind: 'import', data: { target: { id: original.id, name: original.name, loader: original.loader.type, minecraftVersion: original.minecraft.version } } })}>
              <Icon name="import" />{t('transfer.button')}
            </button>
            <button class="btn btn-second" onclick={copy}><Icon name="copy" />{t('instance.settings.copy')}</button>
          </div>
          <div class="group danger">
            <span class="sub">{t('instance.settings.manageDanger')}</span>
            <button class="btn btn-danger" onclick={remove}><Icon name="trash" />{t('instance.settings.delete')}</button>
            <span class="hint">{t('instance.settings.deleteHint')}</span>
          </div>
        </div>
      </FormSection>
  </div>
  </div>

  <div class="savebar" class:visible={dirty}>
    <span class="grow">{t('instance.settings.unsaved')}</span>
    <button class="btn btn-ghost" onclick={() => onsaved()}>{t('common.cancel')}</button>
    <button class="btn btn-grad" disabled={saving || !name.trim()} onclick={save}>{t('common.save')}</button>
  </div>
</div>


<style>
  .settings {
    position: relative;
    flex-grow: 1;
    min-height: 0;
    display: flex;
    flex-direction: column;
  }
  .scroll {
    flex-grow: 1;
    min-height: 0;
    overflow-y: auto;
    padding-bottom: 84px;
  }
  .cols {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
    gap: 16px;
    /* Bereiche einer Zeile werden auf die gleiche Höhe gezogen */
    align-items: stretch;
  }
  .axo-check {
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .axo-check .hint {
    padding-left: 28px;
  }
  @media (max-width: 1000px) {
    .cols {
      grid-template-columns: minmax(0, 1fr);
    }
  }
  .sub {
    font-size: 12px;
    font-weight: 600;
    color: var(--muted);
  }
  .between {
    justify-content: space-between;
  }
  .perf {
    display: flex;
    flex-direction: column;
    gap: 20px;
    padding-top: 4px;
  }
  .perf-block {
    padding: 18px;
    border-radius: 10px;
    background: var(--panel-2);
    box-shadow: inset 0 0 0 1px var(--line);
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  .perf-block .hint {
    line-height: 1.55;
  }
  .text {
    font-size: 13.5px;
    color: var(--text-2);
  }
  .mono input {
    font-family: var(--mono);
    font-size: 12.5px;
  }
  .note {
    display: flex;
    gap: 8px;
    font-size: 12.5px;
    line-height: 1.45;
    color: var(--yellow);
  }
  .note.calm {
    color: var(--muted);
  }
  .adapt {
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .locks {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .lock-opt {
    padding: 10px 12px;
    border: 0;
    border-radius: 10px;
    background: var(--panel-2);
    box-shadow: inset 0 0 0 1px var(--line-2);
    color: var(--text);
    display: flex;
    align-items: center;
    gap: 12px;
    cursor: pointer;
    transition: background-color 0.15s ease, box-shadow 0.15s ease;
  }
  .lock-opt:not(.on):hover {
    background: var(--panel-3);
  }
  .lock-opt.on {
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1.5px var(--accent-ring);
  }
  .lock-opt strong {
    font-size: 13.5px;
    font-weight: 600;
  }
  .lock-icon {
    width: 30px;
    height: 30px;
    border-radius: 8px;
    background: #353535;
    color: var(--muted);
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .lock-opt.on .lock-icon {
    background: var(--accent-soft-strong);
    color: var(--accent-text);
  }
  .manage {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 16px 20px;
  }
  .group {
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .group.danger {
    grid-column: 1 / -1;
    padding-top: 14px;
    border-top: 1px solid var(--line-2);
    flex-direction: row;
    align-items: center;
    gap: 12px;
  }
  .manage .btn {
    justify-content: flex-start;
  }
  /* Immer ganz unten im Tab, egal wie lang der Inhalt ist oder wie weit gescrollt wurde */
  .savebar {
    position: absolute;
    left: 0;
    right: 0;
    bottom: 8px;
    z-index: 5;
    padding: 12px 14px 12px 18px;
    border-radius: 12px;
    background: var(--panel-2);
    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.45), inset 0 0 0 1px var(--accent-ring);
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 13px;
    color: var(--accent-text);
    transform: translateY(120%);
    opacity: 0;
    transition: transform 0.18s ease, opacity 0.18s ease;
    pointer-events: none;
  }
  .savebar.visible {
    transform: none;
    opacity: 1;
    pointer-events: auto;
  }
</style>
