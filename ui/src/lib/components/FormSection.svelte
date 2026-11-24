<script lang="ts">
  import type { Snippet } from 'svelte';
  let { title, subtitle = '', children }: { title: string; subtitle?: string; children: Snippet } = $props();
</script>

<section class="section">
  <div class="head">
    <h2>{title}</h2>
    {#if subtitle}<span>{subtitle}</span>{/if}
  </div>
  <div class="body">{@render children()}</div>
</section>

<style>
  .section {
    padding: 18px 20px 20px;
    border-radius: 12px;
    background: var(--panel);
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  .head {
    display: flex;
    flex-direction: column;
    gap: 3px;
  }
  h2 {
    font-family: var(--display);
    font-size: 16px;
    font-weight: 700;
    line-height: 1.1;
    letter-spacing: 0.08em;
    text-transform: uppercase;
    color: var(--text-strong);
  }
  .head span {
    font-size: 12.5px;
    color: var(--muted-2);
  }
  .body {
    display: flex;
    flex-direction: column;
    gap: 14px;
  }
</style>
