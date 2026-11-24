<script lang="ts">
  import { tick } from 'svelte';
  import { app, type Route } from '../stores/app.svelte';
  import { t } from '../i18n';
  import Icon from './Icon.svelte';
  import Head from './Head.svelte';
  import { versionLabel } from '../util/format';
  import { fadeSlide, pop, DUR } from '../util/motion';

  // Schnellsuche (Strg + K): Instanzen starten, Freunden nachjoinen, Seiten und Aktionen öffnen.
  type Item = {
    group: string;
    label: string;
    sub?: string;
    act: string;
    img?: string;
    head?: string;
    icon?: string;
    run: () => void;
  };

  let query = $state('');
  let selected = $state(0);
  let input: HTMLInputElement | undefined = $state();

  const go = (route: Route) => () => app.navigate(route);

  const items = $derived.by<Item[]>(() => {
    const list: Item[] = [];
    for (const i of app.instances) {
      list.push({
        group: 'palette.groups.instances',
        label: i.name,
        sub: versionLabel(i.minecraftVersion, i.loader) + (i.modCount ? ' · ' + t('instances.mods', { count: i.modCount }) : ''),
        act: 'palette.acts.play',
        img: i.hasIcon ? i.iconUrl : undefined,
        icon: 'instances',
        run: () => {
          if (app.ui) app.ui.lastInstance = i.id;
          app.launch(i.id);
        }
      });
    }
    for (const f of app.social.friends) {
      if (f.relation !== 'friends' || !f.join) continue;
      const address = f.join;
      list.push({ group: 'palette.groups.friends', label: f.name, sub: address, act: 'palette.acts.join', head: f.uuid, run: () => app.joinAddress(address) });
    }
    const pages: [string, string, Route][] = [
      ['app.nav.instances', 'instances', { page: 'instances' }],
      ['localServers.title', 'servers', { page: 'localServers' }],
      ['palette.items.skins', 'skins', { page: 'skins' }],
      ['palette.items.console', 'console', { page: 'console' }],
      ['palette.items.settings', 'settings', { page: 'settings' }]
    ];
    for (const [label, icon, route] of pages) list.push({ group: 'palette.groups.pages', label: t(label), act: 'palette.acts.open', icon, run: go(route) });
    const actions: [string, string | undefined, string, () => void][] = [
      ['palette.items.newInstance', 'palette.items.newInstanceSub', 'plus', go({ page: 'instanceCreate' })],
      ['palette.items.modpacks', 'palette.items.modpacksSub', 'download', go({ page: 'modpacks' })],
      ['palette.items.import', 'palette.items.importSub', 'import', () => (app.dialog = { kind: 'import' })],
      ['palette.items.addFriend', 'palette.items.addFriendSub', 'addUser', () => (app.dialog = { kind: 'addFriend' })],
      ['palette.items.notifications', undefined, 'bell', () => (app.dialog = { kind: 'notifications' })],
      ['palette.items.diagnostics', undefined, 'wrench', go({ page: 'settings', tab: 'diagnostics' })],
      ['palette.items.storage', undefined, 'archive', go({ page: 'settings', tab: 'storage' })]
    ];
    for (const [label, sub, icon, run] of actions) list.push({ group: 'palette.groups.actions', label: t(label), sub: sub ? t(sub) : undefined, act: 'palette.acts.open', icon, run });
    return list;
  });

  const filtered = $derived.by(() => {
    const q = query.trim().toLowerCase();
    if (!q) return items;
    return items.filter((i) => (i.label + ' ' + (i.sub ?? '') + ' ' + t(i.group)).toLowerCase().includes(q));
  });

  // Auswahl bleibt im gültigen Bereich, wenn die Liste kürzer wird
  $effect(() => {
    if (selected >= filtered.length) selected = Math.max(0, filtered.length - 1);
  });

  $effect(() => {
    if (app.paletteOpen) {
      query = '';
      selected = 0;
      tick().then(() => input?.focus());
    }
  });

  function close() {
    app.paletteOpen = false;
  }

  function run(item: Item | undefined) {
    if (!item) return;
    close();
    item.run();
  }

  function onGlobalKey(e: KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      if (app.ready && app.route.page !== 'welcome' && app.route.page !== 'welcomeBack') app.paletteOpen = !app.paletteOpen;
      return;
    }
    if (!app.paletteOpen) return;
    if (e.key === 'Escape') {
      e.preventDefault();
      close();
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      selected = (selected + 1) % Math.max(1, filtered.length);
      scrollToSelected();
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      selected = (selected - 1 + filtered.length) % Math.max(1, filtered.length);
      scrollToSelected();
    } else if (e.key === 'Enter') {
      e.preventDefault();
      run(filtered[selected]);
    }
  }

  let listEl: HTMLDivElement | undefined = $state();
  function scrollToSelected() {
    tick().then(() => listEl?.querySelector('.row.sel')?.scrollIntoView({ block: 'nearest' }));
  }
</script>

<svelte:window onkeydown={onGlobalKey} />

{#if app.paletteOpen}
  <!-- svelte-ignore a11y_click_events_have_key_events, a11y_no_static_element_interactions -->
  <div class="back" onclick={(e) => { if (e.target === e.currentTarget) close(); }} transition:fadeSlide={{ y: 0, duration: DUR.fast }}>
    <div class="box" role="dialog" aria-modal="true" aria-label={t('palette.title')} in:pop={{ scale: 0.97, y: -8, duration: DUR.med }}>
      <label class="in">
        <Icon name="search" size={18} stroke={1.7} />
        <input
          bind:this={input}
          bind:value={query}
          oninput={() => (selected = 0)}
          placeholder={t('palette.placeholder')}
          aria-label={t('palette.title')}
          autocomplete="off"
          spellcheck="false" />
      </label>
      <div class="list" bind:this={listEl} role="listbox" aria-label={t('palette.title')}>
        {#each filtered as item, n (item.group + item.label + n)}
          {#if n === 0 || filtered[n - 1]?.group !== item.group}
            <div class="group">{t(item.group)}</div>
          {/if}
          <button
            class="row"
            class:sel={n === selected}
            role="option"
            aria-selected={n === selected}
            onmousemove={() => (selected = n)}
            onclick={() => run(item)}>
            {#if item.img}
              <img class="thumb" src={item.img} alt="" />
            {:else if item.head}
              <span class="headwrap"><Head uuid={item.head} size={26} radius={13} /></span>
            {:else}
              <span class="ico"><Icon name={item.icon ?? 'chevronRight'} size={15} stroke={1.7} /></span>
            {/if}
            <span class="txt">
              <b class="truncate">{item.label}</b>
              {#if item.sub}<small class="truncate">{item.sub}</small>{/if}
            </span>
            <span class="act">{t(item.act)}</span>
          </button>
        {:else}
          <p class="empty">{t('palette.empty', { query })}</p>
        {/each}
      </div>
      <div class="foot"><span>{t('palette.hintMove')}</span><span>{t('palette.hintRun')}</span><span>{t('palette.hintClose')}</span></div>
    </div>
  </div>
{/if}

<style>
  .back {
    position: fixed;
    inset: 0;
    z-index: 200;
    display: flex;
    justify-content: center;
    align-items: flex-start;
    padding-top: 110px;
    background: rgba(6, 7, 9, 0.6);
    backdrop-filter: blur(6px);
  }
  .box {
    width: 620px;
    max-width: calc(100% - 40px);
    overflow: hidden;
    border-radius: var(--radius-xl);
    background: var(--glass-strong);
    border: 1px solid var(--line-2);
    box-shadow: 0 40px 90px rgba(0, 0, 0, 0.6);
  }
  .in {
    height: 60px;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 0 20px;
    border-bottom: 1px solid var(--line);
    color: var(--muted);
  }
  .in input {
    flex: 1;
    min-width: 0;
    border: 0;
    outline: none;
    background: transparent;
    color: var(--text-strong);
    font: 500 18px var(--font);
  }
  .in input::placeholder {
    color: var(--faint);
  }
  .list {
    max-height: 420px;
    overflow: auto;
    padding: 6px 8px 10px;
  }
  .group {
    padding: 10px 12px 4px;
    font-family: var(--display);
    font-weight: 600;
    font-size: 13px;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    color: var(--faint);
  }
  .row {
    width: 100%;
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 8px 12px;
    border: 0;
    border-radius: var(--radius-m);
    background: transparent;
    color: inherit;
    text-align: left;
    cursor: pointer;
  }
  .row.sel {
    background: rgba(255, 255, 255, 0.08);
  }
  .thumb {
    width: 48px;
    height: 27px;
    border-radius: 4px;
    object-fit: cover;
    flex-shrink: 0;
  }
  .headwrap,
  .ico {
    width: 48px;
    display: grid;
    place-items: center;
    flex-shrink: 0;
    color: var(--muted);
  }
  .txt {
    flex: 1;
    min-width: 0;
    display: grid;
  }
  .txt b {
    font-weight: 600;
    color: var(--text-strong);
  }
  .txt small {
    font-size: 12.5px;
    color: var(--muted);
  }
  .act {
    font-size: 12.5px;
    font-weight: 600;
    color: var(--faint);
    white-space: nowrap;
  }
  .row.sel .act {
    color: #0d0d0f;
    background: var(--text-strong);
    border-radius: 5px;
    padding: 3px 10px;
  }
  .empty {
    padding: 26px;
    text-align: center;
    color: var(--muted);
  }
  .foot {
    display: flex;
    gap: 18px;
    padding: 10px 20px;
    border-top: 1px solid var(--line);
    font-size: 12px;
    color: var(--faint);
  }
</style>
