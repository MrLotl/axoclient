<script lang="ts">
  // Instanzbild; ohne eigenes Bild ein Verlauf mit Anfangsbuchstaben.
  let { url = null, name = '', size = 36, radius = 8, has = true }: { url?: string | null; name?: string; size?: number; radius?: number; has?: boolean } = $props();
  let failed = $state(false);
  const initials = $derived(
    name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((w) => w[0]?.toUpperCase())
      .join('')
  );
</script>

{#if url && has && !failed}
  <img src={url} alt="" style="width: {size}px; height: {size}px; border-radius: {radius}px" onerror={() => (failed = true)} />
{:else}
  <span class="fallback" style="width: {size}px; height: {size}px; border-radius: {radius}px; font-size: {Math.round(size * 0.38)}px">{initials}</span>
{/if}

<style>
  img {
    object-fit: cover;
    display: block;
    flex-shrink: 0;
  }
  .fallback {
    flex-shrink: 0;
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--accent);
    color: #fff;
    font-weight: 700;
    letter-spacing: -0.5px;
  }
</style>
