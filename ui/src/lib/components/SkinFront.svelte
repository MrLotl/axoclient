<script lang="ts">
  // Flache Vorderansicht eines Skins (für Karten in der Bibliothek, ohne WebGL).
  let { src, slim = false, scale = 6 }: { src: string; slim?: boolean; scale?: number } = $props();
  let canvas: HTMLCanvasElement | undefined = $state();

  $effect(() => {
    const url = src;
    const isSlim = slim;
    const s = scale;
    const img = new Image();
    img.onload = () => {
      if (!canvas) return;
      const ctx = canvas.getContext('2d')!;
      ctx.imageSmoothingEnabled = false;
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      const legacy = img.height === 32;
      const arm = isSlim ? 3 : 4;
      // [sx, sy, w, h, dx, dy] in Skin-Pixeln; zweite Liste ist die Außenebene (Hut, Jacke …)
      const base: number[][] = [
        [8, 8, 8, 8, 4, 0],
        [20, 20, 8, 12, 4, 8],
        [44, 20, arm, 12, 4 - arm, 8],
        legacy ? [44, 20, arm, 12, 12, 8] : [36, 52, arm, 12, 12, 8],
        [4, 20, 4, 12, 4, 20],
        legacy ? [4, 20, 4, 12, 8, 20] : [20, 52, 4, 12, 8, 20]
      ];
      const overlay: number[][] = legacy
        ? [[40, 8, 8, 8, 4, 0]]
        : [
            [40, 8, 8, 8, 4, 0],
            [20, 36, 8, 12, 4, 8],
            [44, 36, arm, 12, 4 - arm, 8],
            [52, 52, arm, 12, 12, 8],
            [4, 36, 4, 12, 4, 20],
            [4, 52, 4, 12, 8, 20]
          ];
      const offset = isSlim ? 1 : 0;
      for (const [sx, sy, w, h, dx, dy] of [...base, ...overlay])
        ctx.drawImage(img, sx, sy, w, h, (dx + offset) * s, dy * s, w * s, h * s);
    };
    img.src = url;
  });
</script>

<canvas bind:this={canvas} width={16 * scale} height={32 * scale} aria-hidden="true"></canvas>

<style>
  canvas {
    image-rendering: pixelated;
    display: block;
  }
</style>
