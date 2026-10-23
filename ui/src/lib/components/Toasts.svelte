<script lang="ts">
  import { app } from '../stores/app.svelte';
  import Icon from './Icon.svelte';
  import { t } from '../i18n';
  import { flip } from 'svelte/animate';
  import { fadeSlide, DUR, reducedMotion } from '../util/motion';
</script>

<div class="toasts" role="status" aria-live="polite">
  {#each app.toasts as toast (toast.id)}
    <div
      class="toast {toast.kind}"
      animate:flip={{ duration: reducedMotion() ? 0 : DUR.slow }}
      in:fadeSlide={{ y: 16, duration: DUR.slow }}
      out:fadeSlide={{ x: 60, y: 0, duration: DUR.med }}>
      <span class="icon"><Icon name={toast.kind === 'error' ? 'warning' : toast.kind === 'success' ? 'check' : 'info'} size={15} /></span>
      <span class="text">{toast.text}</span>
      {#if toast.action}
        <button class="btn btn-second sm" onclick={() => { toast.action!.run(); app.dismissToast(toast.id); }}>{toast.action.label}</button>
      {/if}
      <button class="icon-btn flat" style="width:26px;height:26px" aria-label={t('common.close')} onclick={() => app.dismissToast(toast.id)}><Icon name="x" size={12} /></button>
    </div>
  {/each}
</div>

<style>
  .toasts {
    position: fixed;
    right: 20px;
    bottom: calc(20px + var(--jobs-height, 0px));
    transition: bottom var(--dur-slow) var(--ease-out);
    z-index: 90;
    display: flex;
    flex-direction: column;
    gap: 8px;
    width: 380px;
    pointer-events: none;
  }
  .toast {
    pointer-events: auto;
    padding: 12px 12px 12px 14px;
    border-radius: 14px;
    background: var(--panel-2);
    box-shadow: 0 18px 44px rgba(0, 0, 0, 0.55), inset 0 0 0 1px #3c3c3c;
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 13px;
    color: var(--text);
  }
  .text {
    flex-grow: 1;
    line-height: 1.45;
  }
  .icon {
    width: 28px;
    height: 28px;
    border-radius: 8px;
    display: flex;
    align-items: center;
    justify-content: center;
    flex-shrink: 0;
    background: var(--accent-soft-strong);
    color: var(--accent-text);
  }
  .error .icon {
    background: rgba(244, 112, 103, 0.16);
    color: #ff8a80;
  }
  .success .icon {
    background: rgba(63, 185, 80, 0.16);
    color: var(--green);
  }
</style>
