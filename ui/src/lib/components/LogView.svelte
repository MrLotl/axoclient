<script lang="ts">
  import { tick } from 'svelte';
  import type { LogLine } from '../types';
  import { clock } from '../util/format';

  // Log-Anzeige für Konsole und Absturz-Dialog. Zeigt höchstens die letzten 3000 passenden Zeilen,
  // scrollt mit, solange man unten ist.
  let { lines, search = '', levels = ['info', 'warn', 'error', 'debug'] }: { lines: LogLine[]; search?: string; levels?: string[] } = $props();

  let box: HTMLDivElement | undefined = $state();
  let follow = $state(true);

  const shown = $derived.by(() => {
    const q = search.trim().toLowerCase();
    const filtered = lines.filter((l) => levels.includes(l.level) && (!q || l.text.toLowerCase().includes(q)));
    return filtered.slice(-3000);
  });

  $effect(() => {
    shown;
    if (follow) tick().then(() => box && (box.scrollTop = box.scrollHeight));
  });

  function onscroll() {
    if (!box) return;
    follow = box.scrollHeight - box.scrollTop - box.clientHeight < 40;
  }

  export function scrollToEnd() {
    follow = true;
    if (box) box.scrollTop = box.scrollHeight;
  }

  // Inhalt eines Minecraft-Logs: "[12:00:00] [Thread/LEVEL]: Text" → nur der Text
  function message(text: string): string {
    const m = text.match(/^\[[\d:.]+\] \[[^\]]+\/[A-Z]+\](?: \[[^\]]+\])?: ?(.*)$/);
    return m ? m[1] : text;
  }
</script>

<div class="log" bind:this={box} {onscroll} role="log" aria-live="off">
  {#each shown as l (l.seq)}
    <div class="line {l.level}">
      <span class="time">{clock(l.time)}</span>
      <span class="lvl">{l.level.toUpperCase()}</span>
      {#if l.thread}<span class="thread truncate">{l.thread}</span>{/if}
      <span class="text">{message(l.text)}</span>
    </div>
  {/each}
</div>
{#if !follow}
  <button class="jump" onclick={scrollToEnd}>↓</button>
{/if}

<style>
  .log {
    flex-grow: 1;
    min-height: 0;
    overflow: auto;
    padding: 10px 0;
    border-radius: 12px;
    background: #191919;
    box-shadow: inset 0 0 0 1px #2a2a2a;
    font-family: var(--mono);
    font-size: 12px;
    line-height: 1.6;
    user-select: text;
  }
  .line {
    padding: 0 14px;
    display: flex;
    gap: 10px;
    white-space: pre-wrap;
    word-break: break-word;
  }
  .line:hover {
    background: rgba(255, 255, 255, 0.03);
  }
  .time {
    color: #5f5f5f;
    flex-shrink: 0;
  }
  .lvl {
    width: 40px;
    flex-shrink: 0;
    font-weight: 700;
    color: #5f9cff;
  }
  .warn .lvl {
    color: var(--yellow);
  }
  .error .lvl {
    color: var(--red);
  }
  .debug .lvl {
    color: #6b6b6b;
  }
  .thread {
    width: 110px;
    flex-shrink: 0;
    color: #6b6b6b;
  }
  .text {
    color: #cfcfcf;
    min-width: 0;
  }
  .warn .text {
    color: #e8d28f;
  }
  .error .text {
    color: #ff9a92;
  }
  .jump {
    position: absolute;
    right: 24px;
    bottom: 24px;
    width: 36px;
    height: 36px;
    border: 0;
    border-radius: 18px;
    background: var(--accent);
    color: #fff;
    font-size: 16px;
    cursor: pointer;
    box-shadow: var(--accent-shadow);
  }
</style>
