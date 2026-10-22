<script lang="ts">
  import Dialog from '../lib/components/Dialog.svelte';
  import { t } from '../lib/i18n';

  let {
    title,
    text = '',
    confirm = '',
    danger = false,
    icon = 'warning',
    onconfirm,
    onclose
  }: { title: string; text?: string; confirm?: string; danger?: boolean; icon?: string; onconfirm: () => void | Promise<void>; onclose: () => void } =
    $props();
  let busy = $state(false);

  async function run() {
    busy = true;
    try {
      await onconfirm();
      onclose();
    } finally {
      busy = false;
    }
  }
</script>

<Dialog {title} subtitle={text} {icon} tone={danger ? 'danger' : 'accent'} role="alertdialog" width={440} {onclose}>
  {#snippet footer()}
    <button class="btn btn-ghost" onclick={onclose} data-autofocus>{t('common.cancel')}</button>
    <button class="btn {danger ? 'btn-danger' : 'btn-grad'}" style={danger ? 'background: var(--red-soft)' : ''} disabled={busy} onclick={run}>
      {confirm || t('common.yes')}
    </button>
  {/snippet}
  <span></span>
</Dialog>
