<script lang="ts">
  import type { Snippet } from 'svelte';
  import ImageBox from './ImageBox.svelte';
  import { t } from '../i18n';

  /**
   * Kopf einer Instanz: Banner (sonst das Instanzbild weich gezeichnet) mit Instanzbild davor.
   * Gleich in der Detailansicht und beim Erstellen. Mit `onicon`/`onbanner` werden die Bilder beim
   * Hovern bearbeitbar; ohne bleiben sie reine Anzeige.
   */
  let {
    name,
    iconUrl = null,
    bannerUrl = null,
    height = 200,
    iconSize = 84,
    label = '',
    fallbackArt = null,
    onicon,
    onbanner,
    top,
    children
  }: {
    name: string;
    iconUrl?: string | null;
    bannerUrl?: string | null;
    height?: number;
    iconSize?: number;
    label?: string;
    /** CSS-Hintergrund hinter dem Kopf, solange es weder Banner noch Instanzbild gibt (Panorama bzw. Pixel-Landschaft) */
    fallbackArt?: string | null;
    onicon?: (base64: string | null, url: string | null) => void;
    onbanner?: (base64: string | null, url: string | null) => void;
    top?: Snippet;
    children?: Snippet;
  } = $props();
</script>

<section class="hero" aria-label={label} style="height: {height}px">
  <ImageBox
    url={bannerUrl}
    blur={false}
    width="100%"
    height="100%"
    radius={10}
    editable={!!onbanner}
    removable={!!bannerUrl}
    editButton="corner"
    aspect={5}
    outputWidth={1500}
    cropTitle={t('instance.settings.banner')}
    editLabel={t('instance.editBanner')}
    removeLabel={t('instance.settings.bannerAuto')}
    onchange={onbanner}>
    {#if !bannerUrl && fallbackArt}<div class="art" style="background-image: {fallbackArt}"></div>{/if}
    <div class="shade"></div>
    <div class="content" class:has-top={!!top}>
      {@render top?.()}
      <div class="row">
        <span class="icon">
          <ImageBox
            url={iconUrl}
            {name}
            width={iconSize}
            radius={10}
            editable={!!onicon}
            removable={!!iconUrl}
            cropTitle={t('instance.settings.icon')}
            editLabel={t('instance.editIcon')}
            removeLabel={t('instance.removeIcon')}
            onchange={onicon} />
        </span>
        {@render children?.()}
      </div>
    </div>
  </ImageBox>
</section>

<style>
  .hero {
    position: relative;
    flex-shrink: 0;
    border-radius: var(--radius-xl);
    overflow: hidden;
    /* Ecken sicher abschneiden, auch wenn darin Grafikebenen liegen */
    clip-path: inset(0 round var(--radius-xl));
    background: var(--accent-soft-strong), var(--field);
  }
  .art {
    position: absolute;
    inset: 0;
    background-size: cover;
    background-position: center 70%;
  }
  .shade {
    position: absolute;
    inset: 0;
    background: linear-gradient(90deg, rgba(8, 9, 11, 0.92) 0%, rgba(8, 9, 11, 0.6) 45%, rgba(8, 9, 11, 0.2) 100%), linear-gradient(0deg, rgba(8, 9, 11, 0.7) 0%, transparent 60%);
  }
  .content {
    position: absolute;
    inset: 0;
    padding: 20px 22px;
    display: flex;
    flex-direction: column;
    justify-content: flex-end;
  }
  .content.has-top {
    padding-top: 16px;
    justify-content: space-between;
  }
  .row {
    display: flex;
    align-items: flex-end;
    gap: 16px;
  }
  .icon {
    flex-shrink: 0;
    border-radius: var(--radius-xl);
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.45);
  }
</style>
