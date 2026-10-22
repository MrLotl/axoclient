<script lang="ts">
  import Icon from './Icon.svelte';
  let { checked = $bindable(false), label = '', small = false, disabled = false, onchange = undefined }: { checked?: boolean; label?: string; small?: boolean; disabled?: boolean; onchange?: (v: boolean) => void } =
    $props();
</script>

<label class="chk" class:small class:disabled>
  <input class="sr" type="checkbox" bind:checked {disabled} onchange={() => onchange?.(checked)} />
  <span class="box" class:on={checked} aria-hidden="true">{#if checked}<Icon name="check" size={11} stroke={2.6} />{/if}</span>
  {#if label}<span class="text">{label}</span>{/if}
</label>

<style>
  .chk {
    position: relative;
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 13px;
    color: var(--text-2);
    cursor: pointer;
  }
  .chk.small {
    font-size: 12.5px;
    color: var(--muted);
  }
  .chk.disabled {
    opacity: 0.5;
    cursor: default;
  }
  .box {
    width: 18px;
    height: 18px;
    border-radius: 5px;
    box-shadow: inset 0 0 0 1.5px #555;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #fff;
    flex-shrink: 0;
    transition: background 0.12s ease;
  }
  .chk:hover .box:not(.on) {
    box-shadow: inset 0 0 0 1.5px #6a6a6a;
  }
  .box.on {
    background: var(--accent);
    box-shadow: none;
  }
  .sr:focus-visible + .box {
    outline: 2px solid var(--focus);
    outline-offset: 2px;
  }
</style>
