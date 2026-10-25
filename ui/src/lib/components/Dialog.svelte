<script lang="ts">
  import { untrack, type Snippet } from 'svelte';
  import Icon from './Icon.svelte';
  import { t } from '../i18n';
  import { fadeOut, pop, DUR } from '../util/motion';
  import { fade } from 'svelte/transition';
  import { portal } from '../util/portal';

  let {
    title,
    subtitle = '',
    icon = '',
    width = 440,
    onclose,
    role = 'dialog',
    tone = 'accent',
    padded = true,
    fullscreen = false,
    header = undefined,
    children,
    footer = undefined
  }: {
    title: string;
    subtitle?: string;
    icon?: string;
    width?: number;
    onclose?: () => void;
    role?: 'dialog' | 'alertdialog';
    tone?: 'accent' | 'danger' | 'plain';
    padded?: boolean;
    /** Füllt das ganze Fenster unter der Titelleiste (wie eine Seite), z. B. beim Mods-Installieren. */
    fullscreen?: boolean;
    header?: Snippet;
    children: Snippet;
    footer?: Snippet;
  } = $props();

  let box: HTMLDivElement | undefined = $state();
  // Während des Ausblendens keine Klicks oder Tasten mehr annehmen
  let closing = $state(false);
  const reduce = typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const id = `dlg-${Math.random().toString(36).slice(2, 8)}`;

  $effect(() => {
    // Fokus in den Dialog, beim Schließen zurück
    const previous = document.activeElement as HTMLElement | null;
    queueMicrotask(() => {
      const target = box?.querySelector<HTMLElement>('[data-autofocus], input, textarea, select, button.btn-grad') ?? box;
      target?.focus();
    });
    return () => previous?.focus?.();
  });

  // Vollbild an/aus: weich von der alten auf die neue Größe (Lage und Größe vorher merken, danach animieren)
  let before: DOMRect | null = null;
  let lastFull = untrack(() => fullscreen);
  $effect.pre(() => {
    if (fullscreen !== lastFull) before = box?.getBoundingClientRect() ?? null;
  });
  $effect(() => {
    if (fullscreen === lastFull) return;
    lastFull = fullscreen;
    const from = before;
    before = null;
    if (!box || !from || reduce) return;
    const to = box.getBoundingClientRect();
    // Wo der Dialog mit der alten Größe stünde: im Vollbild fest oben links, sonst mittig (wandert mit der Größe mit)
    const startLeft = fullscreen ? to.left : to.left + (to.width - from.width) / 2;
    const startTop = fullscreen ? to.top : to.top + (to.height - from.height) / 2;
    box.animate(
      [
        { width: `${from.width}px`, height: `${from.height}px`, transform: `translate(${from.left - startLeft}px, ${from.top - startTop}px)` },
        { width: `${to.width}px`, height: `${to.height}px`, transform: 'translate(0, 0)' }
      ],
      { duration: 260, easing: 'cubic-bezier(0.22, 1, 0.36, 1)' }
    );
  });

  function onkeydown(e: KeyboardEvent) {
    if (closing) return;
    if (e.key === 'Escape' && onclose) {
      e.stopPropagation();
      onclose();
    }
    if (e.key === 'Tab' && box) {
      const items = Array.from(box.querySelectorAll<HTMLElement>('button:not(:disabled), input:not(:disabled), textarea, select, a[href], [tabindex]:not([tabindex="-1"])'));
      if (items.length === 0) return;
      const first = items[0];
      const last = items[items.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        last.focus();
        e.preventDefault();
      } else if (!e.shiftKey && document.activeElement === last) {
        first.focus();
        e.preventDefault();
      }
    }
  }
</script>

<!-- Der Wrapper bleibt an Ort und Stelle, der Dialog selbst hängt an <body> und liegt so immer ganz oben -->
<div class="anchor">
<!-- svelte-ignore a11y_no_static_element_interactions -->
<div
  use:portal
  class="backdrop"
  class:closing
  onmousedown={(e) => !closing && e.target === e.currentTarget && onclose?.()}
  {onkeydown}
  in:fade|global={{ duration: reduce ? 0 : DUR.med }}
  out:fadeOut|global={{ duration: DUR.med - 20 }}
  onoutrostart={() => (closing = true)}
  onintrostart={() => (closing = false)}>
  <div
    bind:this={box}
    class="dialog {tone}"
    class:full={fullscreen}
    {role}
    aria-modal="true"
    aria-labelledby={id}
    tabindex="-1"
    style={fullscreen ? '' : `width: ${width}px`}
    in:pop|global={{ duration: DUR.med + 20, scale: 0.95, y: 6 }}
    out:pop|global={{ duration: DUR.fast + 20, scale: 0.97, y: 4 }}>
    {#if header}
      {@render header()}
    {:else}
      <div class="head" class:danger={tone === 'danger'}>
        {#if icon}
          <span class="badge" class:danger={tone === 'danger'}><Icon name={icon} size={18} /></span>
        {/if}
        <div class="titles">
          <h2 {id}>{title}</h2>
          {#if subtitle}<span class="sub">{subtitle}</span>{/if}
        </div>
        {#if onclose}
          <button class="icon-btn flat" aria-label={t('common.close')} onclick={onclose}><Icon name="x" size={14} /></button>
        {/if}
      </div>
    {/if}
    <div class="body" class:padded>
      {@render children()}
    </div>
    {#if footer}
      <div class="foot">{@render footer()}</div>
    {/if}
  </div>
</div>
</div>

<style>
  .anchor {
    display: contents;
  }
  .backdrop {
    position: fixed;
    inset: 0;
    z-index: 50;
    background: rgba(10, 10, 10, 0.62);
    backdrop-filter: blur(3px);
    display: flex;
    align-items: center;
    justify-content: center;
  }
  .backdrop.closing {
    pointer-events: none;
  }
  .dialog {
    max-width: calc(100vw - 48px);
    max-height: calc(100vh - 80px);
    border-radius: 16px;
    background: var(--panel);
    box-shadow: 0 24px 60px rgba(0, 0, 0, 0.55), inset 0 0 0 1px #363636;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    outline: none;
  }
  .dialog.danger {
    box-shadow: 0 24px 60px rgba(0, 0, 0, 0.55), inset 0 0 0 1px #3a2c2c;
  }
  .head {
    padding: 22px 22px 16px;
    display: flex;
    align-items: flex-start;
    gap: 12px;
  }
  .head.danger {
    background: radial-gradient(110% 140% at 0% 0%, rgba(244, 112, 103, 0.16) 0%, rgba(42, 42, 42, 0) 70%);
  }
  .badge {
    width: 40px;
    height: 40px;
    border-radius: 10px;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
  }
  .badge.danger {
    background: rgba(244, 112, 103, 0.16);
    color: #ff8a80;
  }
  .titles {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  h2 {
    font-size: 17px;
    font-weight: 600;
    color: var(--text-strong);
  }
  .sub {
    font-size: 13px;
    color: var(--muted-2);
    line-height: 1.45;
  }
  .body {
    min-height: 0;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 16px;
  }
  /* Vollbild: unter der Titelleiste (52 px) bis an die Ränder, Inhalt füllt die Höhe */
  .dialog.full {
    position: fixed;
    top: 60px;
    left: 12px;
    right: 12px;
    bottom: 12px;
    max-width: none;
    max-height: none;
  }
  .dialog.full .body {
    flex: 1;
  }
  .body.padded {
    padding: 0 22px 20px;
  }
  .foot {
    padding: 14px 22px;
    border-top: 1px solid var(--line);
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 8px;
  }
</style>
