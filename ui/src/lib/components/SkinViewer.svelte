<script lang="ts" module>
  // Letzte Drehung über alle Viewer: Beim Wechsel Skins ↔ Capes beginnt der neue Viewer dort,
  // wo der alte stand, und dreht sich sanft auf seine Seite statt zu springen.
  let lastYaw = 0;
</script>

<script lang="ts">
  import { onMount } from 'svelte';
  import { SkinViewer, IdleAnimation, WalkingAnimation, FunctionAnimation } from 'skinview3d';

  let {
    skin = null,
    cape = null,
    model = 'auto-detect',
    width = 320,
    height = 480,
    animate = 'idle',
    rotate = false,
    zoom = 0.9,
    elytra = false,
    facing = 'front',
    label = ''
  }: {
    skin?: string | null;
    cape?: string | null;
    model?: 'default' | 'slim' | 'auto-detect';
    width?: number;
    height?: number;
    animate?: 'idle' | 'walk' | 'wave' | 'none';
    rotate?: boolean;
    zoom?: number;
    elytra?: boolean;
    /** back: dreht einmal kurz auf den Rücken (Capes), statt sich dauernd zu drehen */
    facing?: 'front' | 'back';
    label?: string;
  } = $props();

  let canvas: HTMLCanvasElement | undefined = $state();
  let viewer: SkinViewer | null = null;

  onMount(() => {
    viewer = new SkinViewer({ canvas, width, height, zoom, enableControls: true });
    viewer.controls.enableZoom = false;
    viewer.controls.enablePan = false;
    viewer.fov = 40;
    viewer.globalLight.intensity = 3;
    viewer.cameraLight.intensity = 0.6;
    viewer.autoRotate = rotate;
    viewer.autoRotateSpeed = 0.6;
    viewer.playerWrapper.rotation.y = lastYaw;
    return () => {
      viewer?.dispose();
      viewer = null;
    };
  });

  $effect(() => {
    if (!viewer) return;
    if (skin) viewer.loadSkin(skin, { model }).catch(() => viewer?.loadSkin(null));
    else viewer.loadSkin(null);
  });

  $effect(() => {
    if (!viewer) return;
    if (cape) viewer.loadCape(cape, { backEquipment: elytra ? 'elytra' : 'cape' }).catch(() => viewer?.loadCape(null));
    else viewer.loadCape(null);
  });

  $effect(() => {
    if (!viewer) return;
    if (animate === 'wave') {
      viewer.animation = waveAnimation();
      return;
    }
    viewer.animation = animate === 'walk' ? new WalkingAnimation() : animate === 'idle' ? new IdleAnimation() : null;
    if (viewer.animation) viewer.animation.speed = 0.6;
  });

  /**
   * Kurzes Winken zur Begrüßung: rechter Arm hebt sich, winkt ~2 s und senkt sich wieder, darunter läuft die
   * normale Ruhe-Animation weiter. Danach bleibt es bei der Ruhe-Animation.
   */
  function waveAnimation() {
    const idle = new IdleAnimation();
    idle.speed = 0.6;
    const RAISE = 0.45;
    const HOLD = 2.0;
    const LOWER = 0.5;
    const ease = (x: number) => 1 - Math.pow(1 - Math.min(1, Math.max(0, x)), 3);
    return new FunctionAnimation((player, progress, delta) => {
      idle.update(player, delta);
      const env = progress < RAISE ? ease(progress / RAISE) : progress < RAISE + HOLD ? 1 : 1 - ease((progress - RAISE - HOLD) / LOWER);
      if (env <= 0) return;
      const arm = player.skin.rightArm;
      const wave = Math.sin((progress - RAISE) * 9) * 0.32 * (progress > RAISE ? 1 : 0);
      arm.rotation.x = arm.rotation.x * (1 - env);
      arm.rotation.z = arm.rotation.z * (1 - env) + (-2.35 + wave) * env;
    });
  }

  // Sanft (ease-out, ~0,6 s) auf die gewünschte Seite drehen – bei Capes gerade von hinten
  let turnFrame = 0;
  $effect(() => {
    const target = facing === 'back' ? Math.PI : 0;
    const v = viewer;
    if (!v) return;
    const start = v.playerWrapper.rotation.y;
    const begin = performance.now();
    cancelAnimationFrame(turnFrame);
    const step = (now: number) => {
      const p = Math.min(1, (now - begin) / 600);
      const eased = 1 - Math.pow(1 - p, 3);
      v.playerWrapper.rotation.y = start + (target - start) * eased;
      lastYaw = v.playerWrapper.rotation.y;
      if (p < 1) turnFrame = requestAnimationFrame(step);
    };
    turnFrame = requestAnimationFrame(step);
    return () => cancelAnimationFrame(turnFrame);
  });

  $effect(() => {
    if (viewer) {
      viewer.width = width;
      viewer.height = height;
      viewer.autoRotate = rotate;
    }
  });
</script>

<div role="img" aria-label={label}><canvas bind:this={canvas}></canvas></div>

<style>
  canvas {
    display: block;
    filter: drop-shadow(0 18px 30px rgba(0, 0, 0, 0.35));
    cursor: grab;
  }
  canvas:active {
    cursor: grabbing;
  }
</style>
