<script lang="ts">
  import { call } from '../bridge/bridge';
  import { app } from '../stores/app.svelte';
  // Rahmenloses Fenster: unsichtbare Ränder zum Größe ändern (Win32 übernimmt das eigentliche Ziehen).
  // Maximiert gibt es keine – dort gehört die Ecke oben rechts dem Schließen-Button.
  const edges = ['top', 'bottom', 'left', 'right', 'topleft', 'topright', 'bottomleft', 'bottomright'];
  function start(e: MouseEvent, edge: string) {
    if (e.button !== 0) return;
    e.preventDefault();
    call('window.resize', { edge });
  }
</script>

{#if !app.maximized}
  {#each edges as edge (edge)}
    <!-- svelte-ignore a11y_no_static_element_interactions -->
    <div class="edge {edge}" onmousedown={(e) => start(e, edge)}></div>
  {/each}
{/if}

<style>
  .edge {
    position: fixed;
    z-index: 100;
  }
  .top { top: 0; left: 8px; right: 8px; height: 4px; cursor: ns-resize; }
  .bottom { bottom: 0; left: 8px; right: 8px; height: 4px; cursor: ns-resize; }
  .left { left: 0; top: 8px; bottom: 8px; width: 4px; cursor: ew-resize; }
  .right { right: 0; top: 8px; bottom: 8px; width: 4px; cursor: ew-resize; }
  .topleft { top: 0; left: 0; width: 8px; height: 8px; cursor: nwse-resize; }
  .topright { top: 0; right: 0; width: 8px; height: 8px; cursor: nesw-resize; }
  .bottomleft { bottom: 0; left: 0; width: 8px; height: 8px; cursor: nesw-resize; }
  .bottomright { bottom: 0; right: 0; width: 8px; height: 8px; cursor: nwse-resize; }
</style>
