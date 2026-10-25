<script lang="ts">
  // "Pro Seite: 24 · 48 · 96" – gilt überall, wo seitenweise geladen wird
  import Segmented from './Segmented.svelte';
  import { pageSize, PAGE_SIZES } from '../stores/pageSize.svelte';
  import { t } from '../i18n';

  let { onchange }: { onchange: () => void } = $props();
  let value = $state(String(pageSize.value));
</script>

<span class="page-size">
  <span class="label">{t('content.perPage')}</span>
  <Segmented
    bind:value
    label={t('content.perPage')}
    options={PAGE_SIZES.map((n) => ({ value: String(n), label: String(n) }))}
    onchange={(v) => {
      pageSize.set(Number(v));
      onchange();
    }} />
</span>

<style>
  .page-size {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 12px;
    color: var(--muted-2);
  }
</style>
