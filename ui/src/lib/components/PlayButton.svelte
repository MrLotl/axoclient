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
  /* Bühne: weißer Knopf in Titelschrift, beim Start Fortschritt, solange das Spiel läuft grün */
  .play {
    position: relative;
    width: 100%;
    border: 0;
    border-radius: var(--radius-m);
    background: var(--text-strong);
    color: #0d0d0f;
    font-family: var(--display);
    font-size: 22px;
    font-weight: 700;
    letter-spacing: 0.1em;
    text-transform: uppercase;
    cursor: pointer;
    overflow: hidden;
    transition: transform 0.15s var(--ease-out), box-shadow 0.2s ease, background-color 0.2s ease, color 0.2s ease;
  }
  .play.compact {
    font-size: 15px;
    letter-spacing: 0.08em;
  }
  .play:not(:disabled):not(.busy):not(.running):hover {
    box-shadow: 0 0 0 5px color-mix(in srgb, var(--accent-light) 28%, transparent), 0 10px 36px color-mix(in srgb, var(--accent-light) 40%, transparent);
  }
  .play:not(:disabled):active {
    transform: scale(0.98);
  }
  .play:disabled {
    opacity: 0.5;
    cursor: default;
  }
  .play.busy {
    background: rgba(255, 255, 255, 0.14);
    color: var(--text-strong);
    backdrop-filter: blur(8px);
  }
  .play.running {
    background: var(--green);
    color: #0b130e;
  }
  .play.running:hover {
    background: color-mix(in srgb, var(--red) 80%, #ffffff);
    color: #1b0805;
  }
  .fill {
    position: absolute;
    left: 0;
    top: 0;
    bottom: 0;
    background: rgba(255, 255, 255, 0.24);
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
    gap: 12px;
    padding: 0 18px;
    white-space: nowrap;
  }
  .stage {
    font-size: 16px;
    letter-spacing: 0.06em;
    max-width: 300px;
  }
  .pct {
    font-family: var(--mono);
    font-size: 13px;
    font-weight: 500;
    letter-spacing: 0;
    font-variant-numeric: tabular-nums;
    opacity: 0.85;
    white-space: nowrap;
    flex-shrink: 0;
  }
</style>
