<script lang="ts">
  import { onMount } from 'svelte';
  import { app } from '../stores/app.svelte';
  import { call } from '../bridge/bridge';
  import { t } from '../i18n';
  import Icon from './Icon.svelte';
  import Select from './Select.svelte';
  import Segmented from './Segmented.svelte';
  import Checkbox from './Checkbox.svelte';
  import type { LoaderType } from '../types';

  let {
    loader = $bindable<LoaderType>('fabric'),
    minecraft = $bindable<string | null>(null),
    loaderVersion = $bindable<string | null>(null)
  }: { loader?: LoaderType; minecraft?: string | null; loaderVersion?: string | null } = $props();

  let versions = $state<{ id: string; type: string; axo: boolean }[]>([]);
  let loaderVersions = $state<{ version: string; stable: boolean; recommended: boolean }[]>([]);
  let axoOnly = $state(false);
  let snapshots = $state(false);
  let old = $state(false);

  async function loadVersions() {
    try {
      versions = await call('versions.list', { snapshots, old });
      if (!minecraft && versions.length) minecraft = versions.find((v) => v.type === 'release')?.id ?? versions[0].id;
    } catch (e) {
      app.error(e);
    }
  }

  async function loadLoaderVersions() {
    loaderVersions = [];
    if (loader === 'vanilla' || !minecraft) {
      loaderVersion = null;
      return;
    }
    try {
      loaderVersions = await call('loaders.versions', { loader, minecraft });
      if (!loaderVersion || !loaderVersions.some((v) => v.version === loaderVersion))
        loaderVersion = (loaderVersions.find((v) => v.recommended) ?? loaderVersions[0])?.version ?? null;
    } catch {
      loaderVersions = [];
    }
  }

  onMount(loadVersions);

  $effect(() => {
    loader;
    minecraft;
    loadLoaderVersions();
  });

  const shown = $derived(versions.filter((v) => !axoOnly || v.axo));
  const unsupported = $derived(!!minecraft && app.axoModVersions.length > 0 && !app.axoModVersions.includes(minecraft));
  const noLoaderVersion = $derived(loader !== 'vanilla' && minecraft && loaderVersions.length === 0);
</script>

<div class="picker">
  <div class="label">
    <span>{t('versions.loader')}</span>
    <Segmented
      bind:value={loader}
      label={t('versions.loader')}
      options={[
        { value: 'vanilla', label: 'Vanilla' },
        { value: 'fabric', label: 'Fabric' },
        { value: 'forge', label: 'Forge' },
        { value: 'neoforge', label: 'NeoForge' },
        { value: 'quilt', label: 'Quilt' }
      ]} />
  </div>
  <div class="two">
    <div class="label">
      <span>{t('versions.minecraft')}</span>
      <Select
        bind:value={minecraft}
        label={t('versions.minecraft')}
        placeholder={t('common.loading')}
        options={shown.map((v) => ({ value: v.id, label: v.id, badge: v.axo ? 'Axo' : undefined, hint: v.type === 'snapshot' ? 'Snapshot' : v.type === 'old' ? 'Alpha/Beta' : undefined }))} />
    </div>
    {#if loader !== 'vanilla'}
      <div class="label">
        <span>{t('versions.loaderVersion', { loader: t(`loader.${loader}`) })}</span>
        <Select
          bind:value={loaderVersion}
          label={t('versions.loaderVersion', { loader: t(`loader.${loader}`) })}
          placeholder={noLoaderVersion ? t('versions.noLoader') : t('common.loading')}
          options={loaderVersions.map((v) => ({ value: v.version, label: v.version, badge: v.recommended ? t('versions.recommended') : undefined, hint: v.stable ? undefined : 'Beta' }))} />
      </div>
    {/if}
  </div>
  <div class="filters">
    <span title={app.axoModVersions.length ? '' : t('versions.axoNone')}>
      <Checkbox bind:checked={axoOnly} label={t('versions.axoOnly')} small disabled={app.axoModVersions.length === 0} />
    </span>
    <Checkbox bind:checked={snapshots} label={t('versions.snapshots')} small onchange={loadVersions} />
    <Checkbox bind:checked={old} label={t('versions.old')} small onchange={loadVersions} />
  </div>
  {#if noLoaderVersion}
    <p class="warn"><Icon name="warning" size={14} />{t('game.loaderUnsupported', { loader: t(`loader.${loader}`), version: minecraft })}</p>
  {:else if unsupported}
    <p class="note"><Icon name="info" size={14} />{t('versions.axoUnsupported')}</p>
  {/if}
</div>

<style>
  .picker {
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
  .two {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
  }
  .filters {
    display: flex;
    gap: 18px;
    flex-wrap: wrap;
  }
  .note,
  .warn {
    display: flex;
    align-items: flex-start;
    gap: 8px;
    font-size: 12.5px;
    line-height: 1.45;
    color: var(--muted);
    padding: 10px 12px;
    border-radius: 10px;
    background: #262626;
  }
  .warn {
    color: var(--yellow);
  }
</style>
