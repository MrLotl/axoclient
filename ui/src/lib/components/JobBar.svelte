<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { call } from '../bridge/bridge';
  import { tr, t } from '../i18n';
  import Icon from './Icon.svelte';

  // Start-Jobs zeigt der Spielen-Knopf selbst; hier alle anderen langen Vorgänge
  const jobs = $derived(app.activeJobs.filter((j) => j.kind !== 'launch'));

  // Unten rechts wie die Toasts: die rücken um die Höhe der Jobleiste nach oben
  let height = $state(0);
  $effect(() => {
    document.documentElement.style.setProperty('--jobs-height', jobs.length ? `${height + 8}px` : '0px');
  });
</script>

{#if jobs.length > 0}
  <div class="jobs" aria-live="polite" bind:clientHeight={height}>
    {#each jobs.slice(0, 3) as job (job.id)}
      <div class="job">
        <div class="top">
          <span class="title truncate">{job.status ? tr(job.status) : tr(job.title)}</span>
          {#if job.progress !== null}<span class="pct">{Math.round(job.progress * 100)} %</span>{/if}
          <button class="icon-btn flat" style="width: 24px; height: 24px" aria-label={t('common.cancel')} onclick={() => call('jobs.cancel', { id: job.id })}>
            <Icon name="x" size={11} />
          </button>
        </div>
        <div class="bar" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow={job.progress === null ? undefined : Math.round(job.progress * 100)}>
          <span class:indeterminate={job.progress === null} style="width: {job.progress === null ? 35 : Math.max(2, job.progress * 100)}%"></span>
        </div>
      </div>
    {/each}
  </div>
{/if}

<style>
  .jobs {
    position: absolute;
    right: 20px;
    bottom: 20px;
    /* Über Dialogen und ihrem Blur, wie die Toasts */
    z-index: 89;
    width: 320px;
    display: flex;
    flex-direction: column;
    gap: 8px;
  }
  .job {
    padding: 10px 10px 12px 14px;
    border-radius: 12px;
    background: var(--panel-2);
    box-shadow: 0 12px 32px rgba(0, 0, 0, 0.45), inset 0 0 0 1px #3c3c3c;
    display: flex;
    flex-direction: column;
    gap: 8px;
    animation: toast-in 0.2s ease-out;
  }
  .top {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .title {
    flex-grow: 1;
    font-size: 12.5px;
    font-weight: 600;
    color: var(--accent-text);
  }
  .pct {
    font-size: 12px;
    font-weight: 600;
    color: var(--accent-text);
    font-variant-numeric: tabular-nums;
    white-space: nowrap;
    flex-shrink: 0;
  }
  .bar {
    height: 6px;
    border-radius: 3px;
    background: #333333;
    overflow: hidden;
  }
  .bar span {
    display: block;
    height: 100%;
    border-radius: 3px;
    background: var(--accent);
    transition: width 0.2s ease;
  }
  .bar span.indeterminate {
    animation: axo-shine 1.2s ease-in-out infinite;
  }
</style>
