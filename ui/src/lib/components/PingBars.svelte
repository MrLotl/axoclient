<script lang="ts">
  import { t } from '../i18n';
  let { ms = null, offline = false }: { ms?: number | null; offline?: boolean } = $props();
  const level = $derived(offline ? 0 : ms === null ? -1 : ms < 60 ? 4 : ms < 120 ? 3 : ms < 250 ? 2 : 1);
  const color = $derived(['#6b6b6b', '#f47067', '#f0883e', '#e3b341', '#3fb950'][Math.max(0, level)]);
  const off = 'rgba(255,255,255,0.25)';
</script>

<span class="ping" title={level === -1 ? t('servers.pinging') : offline ? t('servers.offline') : `${ms} ms`}>
  <svg width="14" height="11" viewBox="0 0 14 11" aria-hidden="true">
    <rect x="0" y="7" width="2.2" height="4" rx="1" fill={level > 0 ? color : off}></rect>
    <rect x="3.6" y="4.6" width="2.2" height="6.4" rx="1" fill={level > 1 ? color : off}></rect>
    <rect x="7.2" y="2.2" width="2.2" height="8.8" rx="1" fill={level > 2 ? color : off}></rect>
    <rect x="10.8" y="0" width="2.2" height="11" rx="1" fill={level > 3 ? color : off}></rect>
  </svg>
  <span style="color: {color}">{level === -1 ? '…' : offline ? '–' : `${ms} ms`}</span>
</span>

<style>
  .ping {
    display: flex;
    align-items: center;
    gap: 6px;
    flex-shrink: 0;
    font-size: 11.5px;
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }
</style>
