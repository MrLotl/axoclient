<script lang="ts" generics="T extends string">
  import Icon from './Icon.svelte';
  let {
    value = $bindable(),
    options,
    label = '',
    onchange = undefined
  }: { value: T; options: { value: T; label?: string; icon?: string; aria?: string }[]; label?: string; onchange?: (v: T) => void } = $props();

  let buttons: HTMLButtonElement[] = $state([]);
  let pill = $state<{ left: number; width: number } | null>(null);
  // Erst nach der ersten Messung animieren, sonst fliegt die Markierung beim Öffnen von links herein
  let animate = $state(false);

  function measure() {
    const index = options.findIndex((o) => o.value === value);
    const el = buttons[index];
    if (el) pill = { left: el.offsetLeft, width: el.offsetWidth };
  }

  $effect(() => {
    void value;
    void options.length;
    measure();
    if (!animate) requestAnimationFrame(() => (animate = true));
  });

  $effect(() => {
    const observer = new ResizeObserver(measure);
    buttons.forEach((b) => b && observer.observe(b));
    return () => observer.disconnect();
  });
</script>

<div class="seg" role="group" aria-label={label}>
  {#if pill}<span class="pill" class:animate style="transform: translateX({pill.left}px); width: {pill.width}px"></span>{/if}
  {#each options as opt, i (opt.value)}
    <button
      bind:this={buttons[i]}
      class="seg-btn"
      class:icon-only={!opt.label}
      aria-pressed={value === opt.value}
      aria-label={opt.aria ?? opt.label}
      onclick={() => {
        value = opt.value;
        onchange?.(opt.value);
      }}>
      {#if opt.icon}<Icon name={opt.icon} />{/if}{opt.label ?? ''}
    </button>
  {/each}
</div>

<style>
  .seg {
    position: relative;
    padding: 3px;
    border-radius: 10px;
    background: var(--panel);
    display: flex;
    gap: 2px;
    flex-shrink: 0;
  }
  .pill {
    position: absolute;
    top: 3px;
    bottom: 3px;
    left: 0;
    border-radius: 8px;
    background: var(--accent-soft);
    box-shadow: inset 0 0 0 1px var(--accent-ring);
    pointer-events: none;
  }
  .pill.animate {
    transition:
      transform 0.25s cubic-bezier(0.3, 0.7, 0.2, 1),
      width 0.25s cubic-bezier(0.3, 0.7, 0.2, 1);
  }
  .seg-btn {
    position: relative;
    height: 30px;
    padding: 0 14px;
    border: 0;
    border-radius: 8px;
    background: transparent;
    color: var(--muted-2);
    font-size: 13px;
    font-weight: 600;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 6px;
    cursor: pointer;
    transition: color 0.2s ease;
  }
  .seg-btn.icon-only {
    width: 34px;
    padding: 0;
  }
  .seg-btn:not([aria-pressed='true']):hover {
    color: var(--text-2);
  }
  .seg-btn[aria-pressed='true'] {
    color: var(--accent-text);
  }
  @media (prefers-reduced-motion: reduce) {
    .pill.animate {
      transition: none;
    }
  }
</style>
