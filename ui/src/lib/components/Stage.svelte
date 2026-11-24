<script lang="ts">
  import { app } from '../stores/app.svelte';
  import { artLayers, grain } from '../util/art';

  // Die Bühne zeigt die Welt der gerade gewählten Instanz hinter der ganzen Oberfläche.
  // Auf der Startseite offen, auf allen anderen Seiten stark abgedunkelt.
  let { dim = false }: { dim?: boolean } = $props();

  const current = $derived.by(() => {
    if (app.route.page === 'instance' && app.route.id) return app.instances.find((i) => i.id === app.route.id) ?? null;
    const last = app.ui?.lastInstance;
    return (last ? app.instances.find((i) => i.id === last) : null) ?? app.instances[0] ?? null;
  });

  const panorama = $derived(app.settings?.appearance.versionPanorama ?? true);
  const motion = $derived(app.settings?.appearance.stageMotion ?? true);

  // Eigenes Banner > Panorama der Minecraft-Version (aus den Spieldateien) > Instanzbild weichgezeichnet > Pixel-Landschaft
  const bg = $derived.by(() => {
    if (current?.bannerUrl) return { css: `url('${current.bannerUrl}')`, kind: 'photo' };
    if (current?.hasIcon && !panorama) return { css: `url('${current.iconUrl}')`, kind: 'icon' };
    return { css: artLayers(current?.id ?? 'axoclient', current?.minecraftVersion, panorama), kind: 'pixel' };
  });
</script>

<div class="stage" class:dim class:still={!motion} aria-hidden="true">
  {#key bg.css}
    <div class="bg {bg.kind}" style="background-image: {bg.css}"></div>
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
    animation:
      stage-fade 0.7s ease-out,
      stage-kb 46s ease-in-out infinite alternate;
  }
  .bg.pixel {
    image-rendering: pixelated;
  }
  .bg.icon {
    inset: -60px;
    filter: blur(36px) saturate(1.15);
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
    opacity: 0.08;
    mix-blend-mode: overlay;
    background-size: 160px 160px;
  }
  .vignette {
    position: absolute;
    inset: 0;
    background: radial-gradient(ellipse at 40% 45%, transparent 55%, rgba(0, 0, 0, 0.45) 100%);
  }
  .still .bg {
    animation: stage-fade 0.7s ease-out;
  }
  @media (prefers-reduced-motion: reduce) {
    .bg {
      animation: none;
    }
  }
</style>
