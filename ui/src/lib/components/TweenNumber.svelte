<script lang="ts">
  // Zahl, die weich auf den neuen Wert zuläuft statt zu springen (Prozentanzeigen)
  import { Tween } from 'svelte/motion';
  import { cubicOut } from 'svelte/easing';
  import { reducedMotion } from '../util/motion';

  let { value }: { value: number } = $props();
  const tween = Tween.of(() => value, { duration: reducedMotion() ? 0 : 400, easing: cubicOut });
</script>

{Math.round(tween.current)}
