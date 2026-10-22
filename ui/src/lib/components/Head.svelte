<script lang="ts">
  // Kopf eines Spielers aus der Skin-Textur: Gesicht (8,8) plus Hut-Ebene (40,8), rein per CSS.
  let { uuid = null, src = null, size = 24, radius = 5, dim = false }: { uuid?: string | null; src?: string | null; size?: number; radius?: number; dim?: boolean } = $props();
  const url = $derived(src ?? (uuid ? `/axo/head/${uuid.replaceAll('-', '')}` : null));
  let failed = $state(false);
  $effect(() => {
    url;
    failed = false;
  });
</script>

<span class="head-wrap" class:dim style="width: {size}px; height: {size}px; border-radius: {radius}px">
  {#if url && !failed}
    <span class="layer" style="background-image: url('{url}'); background-size: {size * 8}px {size * 8}px; background-position: -{size}px -{size}px"></span>
    <span class="layer" style="background-image: url('{url}'); background-size: {size * 8}px {size * 8}px; background-position: -{size * 5}px -{size}px"></span>
    <img src={url} alt="" class="probe" onerror={() => (failed = true)} />
  {:else}
    <span class="fallback"></span>
  {/if}
</span>

<style>
  .head-wrap {
    position: relative;
    display: inline-block;
    flex-shrink: 0;
    overflow: hidden;
    background: #3a3a3a;
    image-rendering: pixelated;
  }
  .dim {
    filter: grayscale(1) brightness(0.7);
    opacity: 0.6;
  }
  .layer {
    position: absolute;
    inset: 0;
    background-repeat: no-repeat;
    image-rendering: pixelated;
  }
  .probe {
    display: none;
  }
  .fallback {
    position: absolute;
    inset: 0;
    background: var(--accent-soft-strong);
  }
</style>
