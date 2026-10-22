<script lang="ts" generics="T extends string | number">
  import Icon from './Icon.svelte';

  // Auswahlliste, die innerhalb des Fensters aufklappt (statt des System-Dropdowns)
  let {
    value = $bindable(),
    options,
    label,
    placeholder = '',
    disabled = false,
    width = '100%',
    onchange = undefined
  }: {
    value: T | null;
    options: { value: T; label: string; hint?: string; badge?: string }[];
    label: string;
    placeholder?: string;
    disabled?: boolean;
    width?: string;
    onchange?: (v: T) => void;
  } = $props();

  let open = $state(false);
  let root: HTMLDivElement | undefined = $state();
  let list: HTMLDivElement | undefined = $state();
  let filter = $state('');

  const current = $derived(options.find((o) => o.value === value));
  const shown = $derived(options.length > 12 && filter ? options.filter((o) => o.label.toLowerCase().includes(filter.toLowerCase())) : options);

  function choose(v: T) {
    value = v;
    open = false;
    filter = '';
    onchange?.(v);
  }

  $effect(() => {
    if (open)
      queueMicrotask(() => {
        list?.querySelector<HTMLElement>('[aria-selected="true"]')?.scrollIntoView({ block: 'nearest' });
        list?.querySelector<HTMLInputElement>('.filter input')?.focus();
      });
  });
</script>

<svelte:window
  onmousedown={(e) => {
    if (open && root && !root.contains(e.target as Node)) open = false;
  }} />

<div class="select" bind:this={root} style="width: {width}">
  <button
    class="trigger field"
    type="button"
    {disabled}
    aria-haspopup="listbox"
    aria-expanded={open}
    aria-label={label}
    onclick={() => (open = !open)}
    onkeydown={(e) => {
      if (e.key === 'Escape') open = false;
    }}>
    <span class="value truncate" class:placeholder={!current}>{current?.label ?? placeholder}</span>
    {#if current?.badge}<span class="chip accent">{current.badge}</span>{/if}
    <span class="chev" class:up={open}><Icon name="chevronDown" /></span>
  </button>
  {#if open}
    <div class="menu popup" role="listbox" aria-label={label} bind:this={list}>
      {#if options.length > 12}
        <label class="field filter">
          <span class="filter-icon"><Icon name="search" size={14} /></span>
          <input
            bind:value={filter}
            placeholder="Filtern …"
            aria-label="Filtern"
            onkeydown={(e) => {
              if (e.key === 'Enter' && shown[0]) choose(shown[0].value);
              if (e.key === 'Escape') open = false;
            }} />
        </label>
      {/if}
      {#each shown as opt (opt.value)}
        <button type="button" class="menu-item" class:active={opt.value === value} role="option" aria-selected={opt.value === value} onclick={() => choose(opt.value)}>
          <span class="grow truncate">{opt.label}</span>
          {#if opt.badge}<span class="chip accent">{opt.badge}</span>{/if}
          {#if opt.hint}<span class="hint">{opt.hint}</span>{/if}
        </button>
      {/each}
    </div>
  {/if}
</div>

<style>
  .select {
    position: relative;
  }
  .trigger {
    width: 100%;
    border: 0;
    cursor: pointer;
    color: var(--text-strong);
    font-size: 14px;
    text-align: left;
  }
  .trigger:disabled {
    opacity: 0.5;
    cursor: default;
  }
  .value {
    flex-grow: 1;
  }
  .placeholder {
    color: var(--faint);
  }
  .chev {
    display: flex;
    color: var(--muted);
    transition: transform 0.15s ease;
  }
  .chev.up {
    transform: rotate(180deg);
  }
  .popup {
    position: absolute;
    top: calc(100% + 6px);
    left: 0;
    right: 0;
    z-index: 30;
    max-height: 300px;
    overflow-y: auto;
  }
  /* Suchfeld bleibt beim Scrollen oben stehen und wird im Flex-Menü nie zusammengedrückt */
  .filter {
    position: sticky;
    top: -6px;
    z-index: 1;
    flex-shrink: 0;
    height: 40px;
    min-height: 40px;
    margin: -6px -6px 4px;
    padding: 0 12px;
    border-radius: 12px 12px 0 0;
    background: #2a2a2a;
    box-shadow: inset 0 -1px 0 #363636;
    gap: 8px;
  }
  .filter input {
    font-size: 13.5px;
  }
  .filter-icon {
    display: flex;
    color: var(--muted-2);
  }
</style>
