<script lang="ts">
  import type { Snippet } from 'svelte';
  import { dropFiles } from '../util/drop';

  /** Gestricheltes Ablagefeld für Dateien, Inhalt (Icon, Text, Auswahl-Button) kommt von außen. */
  let { accept, ondrop, padding = 22, children }: { accept?: (file: File) => boolean; ondrop: (files: File[]) => void; padding?: number; children: Snippet } = $props();
</script>

<div class="zone dashed" style="padding: {padding}px" use:dropFiles={{ accept, ondrop }}>
  {@render children()}
</div>

<style>
  .zone {
    border-radius: 12px;
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 8px;
    color: var(--muted);
    font-size: 13px;
    text-align: center;
  }
</style>
