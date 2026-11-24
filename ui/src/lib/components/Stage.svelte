<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { fadeOnly } from '../util/motion';
  import { artLayers, grain, panoramaUrl } from '../util/art';

  // Die Bühne zeigt die Welt der gerade gewählten Instanz hinter der ganzen Oberfläche.
  // Auf der Startseite offen, auf allen anderen Seiten stark abgedunkelt.
  let { dim = false }: { dim?: boolean } = $props();

  const current = $derived.by(() => {
    if (app.route.page === 'instance' && app.route.id) return app.instances.find((i) => i.id === app.route.id) ?? null;
    const last = app.ui?.lastInstance;
    return (last ? app.instances.find((i) => i.id === last) : null) ?? app.instances[0] ?? null;
  });

  const panorama = $derived(app.settings?.appearance.versionPanorama ?? true);

  // Panoramas aller Versionen einmal im Hintergrund laden und dekodieren (bleibt im Bildspeicher)
  const decoded = new Set<string>();
  $effect(() => {
    if (!panorama) return;
    for (const i of app.instances) {
      const url = panoramaUrl(i.minecraftVersion);
      if (!url || decoded.has(url)) continue;
      decoded.add(url);
      const img = new Image();
      img.src = url;
      img.decode?.().catch(() => {});
    }
  });
  const motion = $derived(app.settings?.appearance.stageMotion ?? true);

  // Eigenes Banner > Panorama der Minecraft-Version (aus den Spieldateien) > Farbverlauf
  const bg = $derived(current?.bannerUrl ? `url('${current.bannerUrl}')` : artLayers(current?.id ?? 'axoclient', current?.minecraftVersion, panorama));
</script>

<div class="stage" class:dim class:still={!motion} aria-hidden="true">
  <!-- Alter und neuer Hintergrund blenden ineinander, statt kurz schwarz zu werden -->
  {#key bg}
    <div class="bg" style="background-image: {bg}" in:fadeOnly={{ duration: 600 }} out:fadeOnly={{ duration: 600 }}></div>
  {/key}
  <div class="shade"></div>
  <div class="grain" style="background-image: url('{grain()}')"></div>
  <div class="vignette"></div>
</div>

<style>
  .stage {
    position: absolute;
    inset: 0;
    z-index: 0;
    overflow: hidden;
    background: var(--bg);
    pointer-events: none;
  }
  .bg {
    position: absolute;
    inset: 0;
    background-size: cover;
    background-position: center 80%;
    animation: stage-kb 60s ease-in-out infinite alternate;
    will-change: transform;
  }
  .shade {
    position: absolute;
    inset: 0;
    background:
      linear-gradient(90deg, rgba(8, 9, 11, 0.9) 0%, rgba(8, 9, 11, 0.55) 42%, rgba(8, 9, 11, 0.1) 72%),
      linear-gradient(0deg, rgba(8, 9, 11, 0.97) 0%, rgba(8, 9, 11, 0.3) 40%, transparent 60%),
      linear-gradient(180deg, rgba(8, 9, 11, 0.7) 0%, transparent 16%);
    transition: background var(--dur-slow) ease;
  }
  .dim .shade {
    background: linear-gradient(180deg, rgba(8, 9, 11, 0.78) 0%, rgba(8, 9, 11, 0.93) 34%, rgba(8, 9, 11, 0.97) 100%);
  }
  .grain {
    position: absolute;
    inset: 0;
    opacity: 0.045;
    background-size: 160px 160px;
  }
  .vignette {
    position: absolute;
    inset: 0;
    background: radial-gradient(ellipse at 40% 45%, transparent 55%, rgba(0, 0, 0, 0.45) 100%);
  }
  .still .bg {
    animation: none;
  }
  @media (prefers-reduced-motion: reduce) {
    .bg {
      animation: none;
    }
  }
</style>
