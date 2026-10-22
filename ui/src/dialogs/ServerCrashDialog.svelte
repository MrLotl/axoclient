<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import { app } from '../lib/stores/app.svelte';
  import { call } from '../lib/bridge/bridge';
  import { t, tr } from '../lib/i18n';

  let { crash, onclose }: { crash: { serverId: string; serverName: string; analysis: any }; onclose: () => void } = $props();
  let busy = $state(false);

  async function run(action: string) {
    busy = true;
    try {
      await call('localServers.crashAction', { id: crash.serverId, action, file: crash.analysis.configFile ?? null });
      await call('localServers.start', { id: crash.serverId });
      onclose();
    } catch (e) {
      app.error(e);
    } finally {
      busy = false;
    }
  }
</script>

<Dialog title={t('localServers.crashed', { name: crash.serverName })} subtitle={tr(crash.analysis.cause)} icon="warning" tone="danger" role="alertdialog" width={520} {onclose}>
  <div class="row" style="flex-wrap: wrap">
    {#each crash.analysis.actions as action (action)}
      <button class="btn btn-second sm" disabled={busy} onclick={() => run(action)}>{t(`crash.actions.${action}`)}</button>
    {/each}
  </div>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={() => { onclose(); app.navigate({ page: 'console', id: `server:${crash.serverId}` }); }}>{t('localServers.openLogs')}</button>
    <button class="btn btn-grad" onclick={onclose}>{t('common.close')}</button>
  {/snippet}
</Dialog>
