<script lang="ts">
  import type { Snippet } from 'svelte';
  import { app } from '../stores/app.svelte';
  import { emotePreview } from '../util/emotePreview';

  /**
   * Emote als Vorschau: der eigene Skin in der Pose des Emotes (gleiches Bild wie im Spiel). Bis es fertig ist –
   * oder ohne Konto –, zeigt sie den Inhalt von children (das Strichmännchen).
   */
  let { id, size, version = 0, children }: { id: string; size: number; version?: number; children?: Snippet } = $props();

  let url = $state<string | null>(null);
  const uuid = $derived(app.activeAccount?.uuid.replaceAll('-', '') ?? null);

  $effect(() => {
    const skin = uuid ? `/axo/head/${uuid}?v=${version}` : null;
    const pixels = Math.max(8, Math.round(size * (window.devicePixelRatio || 1)));
    url = null;
    if (!skin) return;
    let alive = true;
    emotePreview(skin, id, pixels).then((u) => {
      if (alive) url = u;
    });
    return () => {
      alive = false;
    };
  });
</script>

{#if url}
  <img class="preview" src={url} width={size} height={size} alt="" draggable="false" />
{:else}
  {@render children?.()}
{/if}

<style>
  .preview {
    image-rendering: pixelated;
    display: block;
  }
</style>
