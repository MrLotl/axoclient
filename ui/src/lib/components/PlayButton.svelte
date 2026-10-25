<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { call } from '../bridge/bridge';
  import { t, tr } from '../i18n';
  import Icon from './Icon.svelte';
  import TweenNumber from './TweenNumber.svelte';
  import { fadeSlide, DUR } from '../util/motion';

  let { instanceId, height = 56, compact = false }: { instanceId: string | null; height?: number; compact?: boolean } = $props();

  const job = $derived(instanceId ? app.jobFor('launch', instanceId) : undefined);
  const running = $derived(instanceId ? app.running.includes(instanceId) : false);
  // Wechsel zwischen Spielen / Startet / Stoppen blendet über statt zu springen
  const mode = $derived(job ? 'busy' : running ? 'running' : 'idle');
  const pct = $derived(job?.progress !== null && job?.progress !== undefined ? Math.round(job.progress * 100) : null);

  async function onclick() {
    if (!instanceId) return;
    if (job) {
      await call('jobs.cancel', { id: job.id });
      return;
    }
    if (running) {
      try {
        await call('instances.stop', { id: instanceId });
      } catch (e) {
        app.error(e);
      }
      return;
    }
    app.launch(instanceId);
  }
</script>

<button
  class="play"
  class:busy={!!job}
  class:running={running && !job}
  class:compact
  style="height: {height}px"
  disabled={!instanceId}
  aria-busy={!!job}
  aria-label={job ? t('home.cancelLaunch') : running ? t('home.stopGame') : t('common.play')}
  {onclick}>
  {#if job}
    <span class="fill" style="width: {pct ?? 0}%"></span>
    <span class="shine"></span>
  {/if}
  {#key mode}
    <span class="play-label" in:fadeSlide={{ y: 8, duration: DUR.med }}>
      {#if job}
        <span class="stage truncate">{job.status ? tr(job.status) : t('launch.starting')}</span>
        {#if pct !== null}<span class="pct"><TweenNumber value={pct} /> %</span>{/if}
      {:else if running}
        <Icon name="stop" size={16} />{t('home.stopGame')}
      {:else}
        <Icon name="play" size={18} />{t('common.play')}
      {/if}
    </span>
  {/key}
</button>

<style>
  .play {
    position: relative;
    width: 100%;
    border: 0;
    border-radius: 10px;
    background: var(--accent);
    color: #fff;
    font-size: 17px;
    font-weight: 700;
    cursor: pointer;
    overflow: hidden;
    transition: filter 0.12s ease, transform 0.12s ease, color 0.2s ease;
  }
  .play.compact {
    font-size: 14px;
    font-weight: 600;
  }
  .play:not(:disabled):hover {
    filter: brightness(1.12);
  }
  .play:not(:disabled):active {
    transform: scale(0.98);
  }
  .play:disabled {
    opacity: 0.5;
    cursor: default;
  }
  .play.busy {
    background: #3a3a3a;
  }
  /* Solange das Spiel läuft: "Spiel beenden" einfach rot */
  .play.running {
    background: color-mix(in srgb, var(--red) 78%, #8a1c14);
    color: #fff;
  }
  .fill {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    background: var(--accent);
    transition: width 0.4s var(--ease-out);
  }
  .shine {
    position: absolute;
    inset: 0;
    width: 40%;
    background: linear-gradient(90deg, transparent, rgba(255, 255, 255, 0.12), transparent);
    animation: axo-shine 1.6s ease-in-out infinite;
  }
  .play-label {
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 10px;
    padding: 0 16px;
  }
  .stage {
    font-size: 15px;
    font-weight: 600;
    max-width: 300px;
  }
  .pct {
    font-size: 15px;
    font-weight: 700;
    font-variant-numeric: tabular-nums;
    opacity: 0.85;
    white-space: nowrap;
    flex-shrink: 0;
  }
</style>
