/**
 * Hängt ein Element direkt an <body>, damit es über allem liegt. Dialoge stecken sonst in der Seite,
 * und jedes Elternelement mit eigener Ebene (transform, filter, backdrop-filter, contain …)
 * schließt sie ein: dann liegen Teile der Seite darüber oder der Dialog wird abgeschnitten.
 *
 * Das Element muss in einem eigenen Wrapper stehen (nicht direkt in einem {#if}/Komponenten-Wurzel),
 * weil Svelte die Grenzen seiner Blöcke im DOM wiederfinden muss. Entfernt wird es hier selbst,
 * und zwar erst nach dem Ausblenden (Svelte räumt Aktionen nach den Übergängen ab).
 */
export function portal(node: HTMLElement) {
  document.body.appendChild(node);
  return {
    destroy() {
      node.remove();
    }
  };
}
