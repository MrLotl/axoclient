<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { t } from '../i18n';
  import Icon from './Icon.svelte';
  import Head from './Head.svelte';
  import { statusText, presenceColors } from '../util/social';

  // Freundesauswahl für alle Senden-Dialoge (nur gegenseitige Freunde)
  let { selected = $bindable<string[]>([]) }: { selected?: string[] } = $props();
  let query = $state('');

  const friends = $derived(
    app.social.friends
      .filter((f) => f.relation === 'friends')
      .filter((f) => f.name.toLowerCase().includes(query.trim().toLowerCase()))
  );

  function toggle(uuid: string) {
    selected = selected.includes(uuid) ? selected.filter((u) => u !== uuid) : [...selected, uuid];
  }
</script>

<div class="picker">
  <span class="field search">
    <span style="color: #7a7a7a; display: flex"><Icon name="search" /></span>
    <input bind:value={query} placeholder={t('share.searchFriends')} aria-label={t('share.searchFriends')} />
  </span>
  <div class="list" role="listbox" aria-multiselectable="true" aria-label={t('friends.title')}>
    {#each friends as f (f.uuid)}
      {@const on = selected.includes(f.uuid)}
      <button class="friend" class:on role="option" aria-selected={on} onclick={() => toggle(f.uuid)}>
        <Head uuid={f.uuid} size={32} radius={6} />
        <span class="text">
          <span class="name truncate">{f.name}</span>
          <span class="status"><span class="dot" style="background: {presenceColors[f.state]}"></span>{statusText(f)}</span>
        </span>
        <span class="box" class:on>{#if on}<Icon name="check" size={11} stroke={2.6} />{/if}</span>
      </button>
    {:else}
      <p class="empty">{app.social.available ? t('share.noFriends') : t('service.unavailable')}</p>
    {/each}
  </div>
</div>

<style>
  .picker {
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .search {
    height: 38px;
  }
  .list {
    max-height: 240px;
    overflow-y: auto;
    display: flex;
    flex-direction: column;
    gap: 4px;
  }
  .friend {
    padding: 8px 10px;
    border: 0;
    border-radius: 9px;
    background: transparent;
    display: flex;
    align-items: center;
    gap: 10px;
    cursor: pointer;
    text-align: left;
    color: var(--text);
  }
  .friend:hover {
    background: var(--panel-3);
  }
  .friend.on {
    background: var(--accent-soft);
  }
  .text {
    flex-grow: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  .name {
    font-size: 13.5px;
    font-weight: 600;
  }
  .status {
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 11.5px;
    color: var(--muted-2);
  }
  .dot {
    width: 7px;
    height: 7px;
    border-radius: 4px;
  }
  .box {
    width: 18px;
    height: 18px;
    border-radius: 5px;
    box-shadow: inset 0 0 0 1.5px #555;
    display: flex;
    align-items: center;
    justify-content: center;
    color: #fff;
  }
  .box.on {
    background: var(--accent);
    box-shadow: none;
  }
  .empty {
    padding: 16px 0;
    text-align: center;
    font-size: 13px;
    color: var(--muted-2);
  }
</style>
