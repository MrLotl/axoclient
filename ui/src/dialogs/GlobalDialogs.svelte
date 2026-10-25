<script lang="ts">
  import { app } from '../lib/stores/app.svelte';
  import AddFriendDialog from './AddFriendDialog.svelte';
  import ServerEditDialog from './ServerEditDialog.svelte';
  import NotificationsDialog from './NotificationsDialog.svelte';
  import FriendProfileDialog from './FriendProfileDialog.svelte';
  import UpdateDialog from './UpdateDialog.svelte';
  import LaunchPromptDialog from './LaunchPromptDialog.svelte';
  import PromptFallback from './PromptFallback.svelte';
  import CrashDialog from './CrashDialog.svelte';
  import ServerCrashDialog from './ServerCrashDialog.svelte';
  import ConfirmDialog from './ConfirmDialog.svelte';
  import ShareDialog from './ShareDialog.svelte';
  import ImportDialog from './ImportDialog.svelte';
  import MissingModsDialog from './MissingModsDialog.svelte';

  const close = () => (app.dialog = null);
</script>

{#if app.dialog?.kind === 'addFriend'}
  <AddFriendDialog onclose={close} />
{:else if app.dialog?.kind === 'serverEdit'}
  <ServerEditDialog {...app.dialog.data} onclose={close} />
{:else if app.dialog?.kind === 'notifications'}
  <NotificationsDialog onclose={close} />
{:else if app.dialog?.kind === 'friendProfile'}
  <FriendProfileDialog uuid={app.dialog.data} onclose={close} />
{:else if app.dialog?.kind === 'update'}
  <UpdateDialog onclose={close} />
{:else if app.dialog?.kind === 'confirm'}
  <ConfirmDialog {...app.dialog.data} onclose={close} />
{:else if app.dialog?.kind === 'share'}
  <ShareDialog {...app.dialog.data} onclose={close} />
{:else if app.dialog?.kind === 'missingMods'}
  <MissingModsDialog {...app.dialog.data} onclose={close} />
{:else if app.dialog?.kind === 'import'}
  <ImportDialog file={app.dialog.data?.file ?? null} foreign={app.dialog.data?.foreign ?? null} target={app.dialog.data?.target ?? null} onclose={close} />
{/if}

<!-- Diese beiden kommen von der C#-Seite und überlagern alles -->
{#if app.prompt}
  <!-- Jede Rückfrage bekommt einen eigenen Dialog: kommt die nächste, während der alte noch ausblendet,
       würde Svelte sonst den ausblendenden (nicht mehr klickbaren) Dialog weiterverwenden -->
  {#key app.prompt.promptId}
    <!-- Stürzt der Dialog beim Anzeigen ab, bleibt der Start nicht stumm hängen: einfacher Ersatz mit denselben Antworten -->
    <svelte:boundary onerror={(e) => console.error('Rückfrage-Dialog', e)}>
      <LaunchPromptDialog prompt={app.prompt} />
      {#snippet failed()}
        <PromptFallback prompt={app.prompt!} />
      {/snippet}
    </svelte:boundary>
  {/key}
{/if}
{#if app.crash && !app.prompt}
  <CrashDialog crash={app.crash} onclose={() => (app.crash = null)} />
{/if}
{#if app.serverCrash}
  <ServerCrashDialog crash={app.serverCrash} onclose={() => (app.serverCrash = null)} />
{/if}
