import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

// Die gebaute Oberfläche wird in die App eingebettet (siehe AxoClient.App.csproj).
export default defineConfig({
  plugins: [svelte()],
  base: './',
  build: {
    outDir: '../src/AxoClient.App/wwwroot',
    emptyOutDir: true,
    target: 'es2022',
    assetsInlineLimit: 0,
    chunkSizeWarningLimit: 1500
  },
  test: {
    environment: 'node',
    include: ['src/**/*.test.ts']
  }
});
