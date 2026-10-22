<script lang="ts">
  import { onMount } from 'svelte';
  import Icon from './Icon.svelte';
  import { dropFiles } from '../util/drop';

  /**
   * Datei-Drop für eine ganze Seite oder einen Tab: hängt sich an das Elternelement
   * (das `position: relative` braucht) und zeigt beim Darüberziehen ein gestricheltes Overlay.
   */
  let { icon = 'upload', hint, inset = 12, accept, ondrop }: { icon?: string; hint: string; inset?: number; accept?: (file: File) => boolean; ondrop: (files: File[]) => void } = $props();

  let anchor: HTMLElement | undefined = $state();
  let active = $state(false);

  onMount(() => {
    const parent = anchor?.parentElement;
    if (!parent) return;
    const action = dropFiles(parent, { unmarked: true, accept: (f) => !accept || accept(f), ondrop: (f) => ondrop(f), onactive: (v) => (active = v) });
    return () => action.destroy();
  });
</script>

<span bind:this={anchor} hidden></span>
{#if active}
  <div class="drop" style="inset: {inset}px"><Icon name={icon} size={28} /><span>{hint}</span></div>
{/if}

<style>
  .drop {
    position: absolute;
    z-index: 20;
    border-radius: 16px;
    border: 2px dashed var(--accent-text);
    background: rgba(20, 20, 20, 0.85);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 10px;
    color: var(--accent-text);
    font-weight: 600;
    pointer-events: none;
  }
</style>
