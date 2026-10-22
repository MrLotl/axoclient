<script lang="ts">
  let { checked = $bindable(false), label, disabled = false, onchange = undefined }: { checked?: boolean; label: string; disabled?: boolean; onchange?: (v: boolean) => void } = $props();
</script>

<button
  class="toggle"
  class:on={checked}
  role="switch"
  aria-checked={checked}
  aria-label={label}
  {disabled}
  onclick={() => {
    checked = !checked;
    onchange?.(checked);
  }}>
  <span class="knob"></span>
</button>

<style>
  .toggle {
    position: relative;
    width: 36px;
    height: 20px;
    padding: 0;
    border: 0;
    border-radius: 10px;
    background: #4a4a4a;
    cursor: pointer;
    flex-shrink: 0;
    transition: background 0.15s ease;
  }
  .toggle.on {
    background: var(--accent);
  }
  .toggle:disabled {
    opacity: 0.4;
    cursor: default;
  }
  .knob {
    position: absolute;
    top: 2px;
    left: 2px;
    width: 16px;
    height: 16px;
    border-radius: 8px;
    background: #fff;
    box-shadow: 0 1px 3px rgba(0, 0, 0, 0.35);
    transition: transform 0.15s ease;
  }
  .on .knob {
    transform: translateX(16px);
  }
</style>
