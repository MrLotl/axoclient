<script lang="ts" module>
  // Alle Icons als Strich-SVG (aus dem Design übernommen). viewBox 16, außer wo angegeben.
  const paths: Record<string, { d: string; box?: number; fill?: boolean; width?: number }> = {
    home: { box: 20, d: '<path d="M3 9.5L10 3.5l7 6V16a1 1 0 0 1-1 1h-3.5v-5h-5v5H4a1 1 0 0 1-1-1z"/>' },
    instances: { box: 20, d: '<rect x="2.5" y="4.5" width="3" height="12" rx="0.5"/><rect x="6.5" y="3" width="3" height="13.5" rx="0.5"/><rect x="10.5" y="5.5" width="3" height="11" rx="0.5"/><path d="M14.3 6.6l2.4-.6 2.6 10-2.4.6z"/><path d="M1.5 17.5h17"/>' },
    servers: { box: 20, d: '<rect x="3" y="3" width="14" height="6" rx="1.5"/><rect x="3" y="11" width="14" height="6" rx="1.5"/><path d="M6 6h.01M6 14h.01"/><path d="M10 6h4M10 14h4"/>' },
    skins: { box: 20, d: '<circle cx="10" cy="7" r="3.2"/><path d="M4 17c0-3.3 2.7-5.5 6-5.5s6 2.2 6 5.5"/>' },
    console: { box: 20, d: '<rect x="2.5" y="3.5" width="15" height="13" rx="2"/><path d="M6 8.5l2.5 2-2.5 2"/><path d="M10.5 12.5H14"/>' },
    settings: { box: 24, width: 1.8, d: '<path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/>' },
    bell: { d: '<path d="M4 11V7a4 4 0 0 1 8 0v4l1 1H3z"/><path d="M6.5 14c.3.6.9 1 1.5 1s1.2-.4 1.5-1"/><path d="M8 1.5V3"/>' },
    chevronDown: { d: '<path d="M4 6l4 4 4-4"/>' },
    chevronUp: { d: '<path d="M4 10l4-4 4 4"/>' },
    chevronRight: { d: '<path d="M6 3.5L10.5 8 6 12.5"/>' },
    chevronLeft: { d: '<path d="M10 3.5L5.5 8 10 12.5"/>' },
    minimize: { box: 26, d: '<path d="M8 13h10"/>' },
    maximize: { box: 26, d: '<path d="M8.5 8.5h9v9h-9z"/>' },
    restore: { box: 26, d: '<path d="M10.5 8.5h7v7"/><path d="M8.5 10.5h7v7h-7z"/>' },
    close: { box: 26, d: '<path d="M8 8l10 10M18 8L8 18"/>' },
    x: { d: '<path d="M4 4l8 8M12 4l-8 8"/>', width: 1.7 },
    check: { d: '<path d="M3.5 8.5l3 3 6-7"/>', width: 2.4 },
    play: { d: '<path d="M5 3.5v9l7-4.5z"/>', fill: true },
    stop: { d: '<rect x="4" y="4" width="8" height="8" rx="1.5"/>', fill: true },
    plus: { d: '<path d="M8 3v10M3 8h10"/>' },
    addUser: { d: '<circle cx="6" cy="5" r="2.5"/><path d="M1.5 13.5c0-2.5 2-4 4.5-4s4.5 1.5 4.5 4"/><path d="M13 5v5M10.5 7.5h5"/>' },
    user: { d: '<circle cx="8" cy="5.5" r="2.8"/><path d="M2.5 14c0-3 2.5-4.8 5.5-4.8s5.5 1.8 5.5 4.8"/>' },
    logout: { d: '<path d="M6 2.5H3.5a1 1 0 0 0-1 1v9a1 1 0 0 0 1 1H6"/><path d="M10.5 11L13.5 8l-3-3"/><path d="M13.5 8H6"/>' },
    trash: { d: '<path d="M2.5 4.5h11"/><path d="M6 4.5V3a1 1 0 0 1 1-1h2a1 1 0 0 1 1 1v1.5"/><path d="M4 4.5l.8 8.5a1 1 0 0 0 1 1h4.4a1 1 0 0 0 1-1l.8-8.5"/><path d="M6.75 7v4.5M9.25 7v4.5"/>' },
    download: { d: '<path d="M8 2.5v7.5"/><path d="M4.8 7L8 10.2 11.2 7"/><path d="M2.5 11.5v1a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1v-1"/>' },
    import: { d: '<path d="M9.5 2.5h3a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-3"/><path d="M2 8h8"/><path d="M7 5l3 3-3 3"/>' },
    export: { d: '<path d="M6.5 2.5h-3a1 1 0 0 0-1 1v9a1 1 0 0 0 1 1h3"/><path d="M6 8h8"/><path d="M11 5l3 3-3 3"/>' },
    send: { d: '<path d="M14 2L7 9"/><path d="M14 2l-4.5 12-2.5-5-5-2.5z"/>' },
    folder: { d: '<path d="M2 4.5a1 1 0 0 1 1-1h3.2l1.5 1.5H13a1 1 0 0 1 1 1V12a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1z"/>' },
    search: { d: '<circle cx="7" cy="7" r="4.5"/><path d="M10.5 10.5L14 14"/>' },
    grip: { d: '<circle cx="6" cy="4" r="0.9"/><circle cx="10" cy="4" r="0.9"/><circle cx="6" cy="8" r="0.9"/><circle cx="10" cy="8" r="0.9"/><circle cx="6" cy="12" r="0.9"/><circle cx="10" cy="12" r="0.9"/>', fill: true },
    grid: { d: '<rect x="2" y="2" width="5" height="5" rx="1"/><rect x="9" y="2" width="5" height="5" rx="1"/><rect x="2" y="9" width="5" height="5" rx="1"/><rect x="9" y="9" width="5" height="5" rx="1"/>' },
    list: { d: '<path d="M5.5 4h8.5M5.5 8h8.5M5.5 12h8.5"/><path d="M2 4h.5M2 8h.5M2 12h.5"/>' },
    edit: { d: '<path d="M11 2.5l2.5 2.5L6 12.5H3.5V10z"/><path d="M9.5 4l2.5 2.5"/>' },
    copy: { d: '<rect x="5.5" y="5.5" width="8" height="8" rx="1.5"/><path d="M10.5 5.5V4a1.5 1.5 0 0 0-1.5-1.5H4A1.5 1.5 0 0 0 2.5 4v5A1.5 1.5 0 0 0 4 10.5h1.5"/>' },
    wrench: { d: '<path d="M10.2 2.3a3.2 3.2 0 0 0-3.9 4.1L2.6 10.1a1.4 1.4 0 0 0 2 2l3.7-3.7a3.2 3.2 0 0 0 4.1-3.9l-1.9 1.9-1.6-.4-.4-1.6z"/>' },
    move: { d: '<path d="M8 2v12M2 8h12"/><path d="M6.2 3.8L8 2l1.8 1.8M6.2 12.2L8 14l1.8-1.8M3.8 6.2L2 8l1.8 1.8M12.2 6.2L14 8l-1.8 1.8"/>' },
    minus: { d: '<path d="M3 8h10"/>' },
    archive: { d:'<rect x="2" y="2.5" width="12" height="3.5" rx="1"/><path d="M3 6v6.5a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1V6"/><path d="M6.5 8.5h3"/>' },
    map: { d: '<path d="M2 4l4-1.5 4 1.5 4-1.5v9.5l-4 1.5-4-1.5-4 1.5z"/><path d="M6 2.5V12M10 4v9.5"/>' },
    swap: { d:'<path d="M2.5 5h10"/><path d="M10 2.5L12.5 5 10 7.5"/><path d="M13.5 11h-10"/><path d="M6 8.5L3.5 11 6 13.5"/>' },
    refresh: { d:'<path d="M13.5 8a5.5 5.5 0 1 1-1.6-3.9"/><path d="M13.5 2.5v3h-3"/>' },
    warning: { d: '<path d="M8 1.8L14.6 13.5H1.4z"/><path d="M8 6.2v3.3M8 11.6v.01"/>' },
    info: { d: '<circle cx="8" cy="8" r="6"/><path d="M8 7.2v3.8M8 5v.01"/>' },
    clock: { d: '<circle cx="8" cy="8" r="6"/><path d="M8 4.8V8l2 1.3"/>' },
    globe: { d: '<circle cx="8" cy="8" r="6"/><path d="M2 8h12M8 2c1.8 1.7 2.6 3.7 2.6 6S9.8 12.3 8 14M8 2C6.2 3.7 5.4 5.7 5.4 8s.8 4.3 2.6 6"/>' },
    external: { d: '<path d="M9 2.5h4.5V7"/><path d="M13.5 2.5L7 9"/><path d="M12 9.5v3a1 1 0 0 1-1 1H3.5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h3"/>' },
    more: { d: '<circle cx="3.5" cy="8" r="1"/><circle cx="8" cy="8" r="1"/><circle cx="12.5" cy="8" r="1"/>', fill: true },
    filter: { d: '<path d="M2.5 3.5h11l-4.2 5v4l-2.6 1.2V8.5z"/>' },
    upload: { d: '<path d="M8 10.5V3"/><path d="M4.8 6.2L8 3l3.2 3.2"/><path d="M2.5 11.5v1a1 1 0 0 0 1 1h9a1 1 0 0 0 1-1v-1"/>' },
    image: { d: '<rect x="2" y="2.5" width="12" height="11" rx="1.5"/><circle cx="5.8" cy="6.2" r="1.2"/><path d="M14 11l-3.5-3.5L4 13.5"/>' },
    world: { d: '<path d="M8 1.8l5.5 3.1v6.2L8 14.2l-5.5-3.1V4.9z"/><path d="M2.5 4.9L8 8l5.5-3.1M8 8v6.2"/>' },
    camera: { d: '<path d="M2 5.5a1 1 0 0 1 1-1h2l1-1.5h4l1 1.5h2a1 1 0 0 1 1 1v6.5a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1z"/><circle cx="8" cy="8.5" r="2.3"/>' },
    puzzle: { d: '<path d="M6 2.5h2.5v1.2a1.3 1.3 0 1 0 2.6 0V2.5h2.4V5h-1.2a1.3 1.3 0 1 0 0 2.6h1.2V13.5H11v-1.2a1.3 1.3 0 1 0-2.6 0v1.2H2.5V11h1.2a1.3 1.3 0 1 0 0-2.6H2.5V2.5z"/>' },
    palette: { d: '<path d="M8 2a6 6 0 1 0 0 12c.9 0 1.3-.6 1.3-1.2 0-.8-.6-1-.6-1.8 0-.7.6-1.2 1.3-1.2H11a3 3 0 0 0 3-3C14 4.4 11.3 2 8 2z"/><circle cx="5" cy="7" r=".8"/><circle cx="7.5" cy="4.8" r=".8"/><circle cx="10.5" cy="5.3" r=".8"/>' },
    sun: { d: '<circle cx="8" cy="8" r="3"/><path d="M8 1.5v1.5M8 13v1.5M1.5 8H3M13 8h1.5M3.4 3.4l1 1M11.6 11.6l1 1M3.4 12.6l1-1M11.6 4.4l1-1"/>' },
    memory: { d: '<rect x="2" y="4.5" width="12" height="7" rx="1"/><path d="M5 7v2M8 7v2M11 7v2M4 11.5v1.5M12 11.5v1.5M8 11.5v1.5"/>' },
    shield: { d: '<path d="M8 1.8L13 3.6v4c0 3-2.1 5.4-5 6.6-2.9-1.2-5-3.6-5-6.6v-4z"/>' },
    cape: { d: '<path d="M4.5 2.5h7l1.5 11h-10z"/><path d="M6 2.5c0 1.2.9 2 2 2s2-.8 2-2"/>' },
    pencil: { d: '<path d="M2.5 13.5l1-3.5 7.5-7.5 2.5 2.5-7.5 7.5z"/>' },
    eye: { d: '<path d="M1.5 8s2.4-4.5 6.5-4.5S14.5 8 14.5 8s-2.4 4.5-6.5 4.5S1.5 8 1.5 8z"/><circle cx="8" cy="8" r="2"/>' },
    sparkle: { d: '<path d="M8 2l1.4 3.6L13 7l-3.6 1.4L8 12l-1.4-3.6L3 7l3.6-1.4z"/>' },
    lock: { d: '<rect x="3.5" y="7" width="9" height="6.5" rx="1.2"/><path d="M5.5 7V5a2.5 2.5 0 0 1 5 0v2"/>' },
    arrowRight: { d: '<path d="M3 8h10M9.5 4.5L13 8l-3.5 3.5"/>' },
    microsoft: { d: '<path d="M2 2h5.5v5.5H2zM8.5 2H14v5.5H8.5zM2 8.5h5.5V14H2zM8.5 8.5H14V14H8.5z"/>', fill: true },
    heart: { d: '<path d="M8 13.5S2.5 10.3 2.5 6.4A2.9 2.9 0 0 1 8 4.8a2.9 2.9 0 0 1 5.5 1.6C13.5 10.3 8 13.5 8 13.5z"/>' },
    stats: { d: '<path d="M2.5 13.5h11"/><path d="M4.5 11V8M8 11V4.5M11.5 11V7"/>' },
    terminal: { d: '<path d="M3 5l3 3-3 3"/><path d="M7.5 11.5H13"/>' }
  };
  export type IconName = keyof typeof paths;
</script>

<script lang="ts">
  let { name, size = 16, stroke = undefined, class: className = '' }: { name: string; size?: number; stroke?: number; class?: string } = $props();
  const icon = $derived(paths[name] ?? paths.info);
</script>

<svg
  class={className}
  width={size}
  height={size}
  viewBox="0 0 {icon.box ?? 16} {icon.box ?? 16}"
  fill={icon.fill ? 'currentColor' : 'none'}
  stroke="currentColor"
  stroke-width={stroke ?? icon.width ?? 1.5}
  stroke-linecap="round"
  stroke-linejoin="round"
  aria-hidden="true">{@html icon.d}</svg>
