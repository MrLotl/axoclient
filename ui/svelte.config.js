import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

export default {
  preprocess: vitePreprocess(),
  // Startwerte aus Props sind hier gewollt (Formulare übernehmen den Zustand beim Öffnen)
  onwarn(warning, handler) {
    if (warning.code === 'state_referenced_locally') return;
    handler(warning);
  }
};
