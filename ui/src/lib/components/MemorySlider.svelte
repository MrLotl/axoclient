<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { t } from '../i18n';

  let { value = $bindable(4096), mods = 0 }: { value?: number; mods?: number } = $props();

  const max = $derived(Math.max(4096, Math.min(32768, Math.floor((app.totalRamMb * 0.85) / 1024) * 1024)));
  const recommended = $derived(mods === 0 ? [2048, 4096] : mods < 30 ? [4096, 6144] : mods < 100 ? [6144, 8192] : [8192, 12288]);
  const hint = $derived(
    value < recommended[0] ? t('memory.tooLow') : value > recommended[1] + 2048 ? t('memory.tooMuch') : t('memory.recommended', { from: recommended[0] / 1024, to: recommended[1] / 1024 })
  );
  const pct = $derived(((value - 1024) / (max - 1024)) * 100);
</script>

<div class="mem">
  <div class="top">
    <span class="value">{(value / 1024).toLocaleString('de-DE', { maximumFractionDigits: 1 })} GB</span>
    <span class="hint" class:warn={value < recommended[0]}>{hint}</span>
  </div>
  <input
    type="range"
    min="1024"
    {max}
    step="512"
    bind:value
    aria-label={t('memory.label')}
    style="background: linear-gradient(90deg, var(--accent-from), var(--accent-to)) 0 / {pct}% 100% no-repeat, #3a3a3a" />
  <div class="scale"><span>1 GB</span><span>{max / 1024} GB</span></div>
</div>

<style>
  .mem {
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .top {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
  }
  .value {
    font-size: 20px;
    font-weight: 700;
    color: var(--text-strong);
    font-variant-numeric: tabular-nums;
  }
  .hint.warn {
    color: var(--yellow);
  }
  input[type='range'] {
    -webkit-appearance: none;
    appearance: none;
    width: 100%;
    height: 6px;
    border-radius: 3px;
    outline: none;
    cursor: pointer;
  }
  input[type='range']::-webkit-slider-thumb {
    -webkit-appearance: none;
    width: 18px;
    height: 18px;
    border-radius: 9px;
    background: #fff;
    box-shadow: 0 2px 6px rgba(0, 0, 0, 0.4);
  }
  .scale {
    display: flex;
    justify-content: space-between;
    font-size: 11.5px;
    color: var(--faint);
  }
</style>
