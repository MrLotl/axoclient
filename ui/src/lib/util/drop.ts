/**
 * Svelte-Action für Ablageflächen. Markiert das Element mit `drop-target` (gestrichelter Rahmen,
 * siehe app.css) und beim Darüberziehen zusätzlich mit `drag-over`. Abgelegte Dateien gehen,
 * nach `accept` gefiltert, an `ondrop`.
 */
export type DropOptions = {
  ondrop: (files: File[]) => void;
  accept?: (file: File) => boolean;
  disabled?: boolean;
  /** Ohne gestrichelten Rahmen (z. B. wenn ein eigenes Overlay den Zustand zeigt). */
  unmarked?: boolean;
  /** Meldet, ob gerade Dateien über dem Element schweben. */
  onactive?: (active: boolean) => void;
};

export const isImage = (f: File) => /^image\/(png|jpeg)$/.test(f.type) || /\.(png|jpe?g)$/i.test(f.name);
export const isPng = (f: File) => f.type === 'image/png' || /\.png$/i.test(f.name);

const hasFiles = (e: DragEvent) => !!e.dataTransfer?.types.includes('Files');

export function dropFiles(node: HTMLElement, options: DropOptions) {
  let opts = options;
  let depth = 0;
  const mark = () => node.classList.toggle('drop-target', !opts.disabled && !opts.unmarked);
  mark();

  const reset = () => {
    depth = 0;
    node.classList.remove('drag-over');
    opts.onactive?.(false);
  };
  const enter = (e: DragEvent) => {
    if (opts.disabled || !hasFiles(e)) return;
    e.preventDefault();
    // Verschachtelte Ablageflächen (Icon im Banner) sollen nur selbst leuchten
    e.stopPropagation();
    if (depth++ === 0) opts.onactive?.(true);
    if (!opts.unmarked) node.classList.add('drag-over');
  };
  const over = (e: DragEvent) => {
    if (opts.disabled || !hasFiles(e)) return;
    e.preventDefault();
    if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
  };
  const leave = (e: DragEvent) => {
    if (opts.disabled || !hasFiles(e)) return;
    e.stopPropagation();
    if (--depth <= 0) reset();
  };
  const drop = (e: DragEvent) => {
    if (opts.disabled || !hasFiles(e)) return;
    e.preventDefault();
    e.stopPropagation();
    reset();
    const files = [...(e.dataTransfer?.files ?? [])].filter((f) => !opts.accept || opts.accept(f));
    if (files.length) opts.ondrop(files);
  };

  node.addEventListener('dragenter', enter);
  node.addEventListener('dragover', over);
  node.addEventListener('dragleave', leave);
  node.addEventListener('drop', drop);
  return {
    update(next: DropOptions) {
      opts = next;
      mark();
      if (opts.disabled) reset();
    },
    destroy() {
      node.classList.remove('drop-target', 'drag-over');
      node.removeEventListener('dragenter', enter);
      node.removeEventListener('dragover', over);
      node.removeEventListener('dragleave', leave);
      node.removeEventListener('drop', drop);
    }
  };
}

/**
 * Setzt `files-dragging` auf <body>, solange Dateien über das Fenster gezogen werden.
 * So können auch unauffällige Ablageflächen (z. B. Server-Icons) dann sichtbar werden.
 */
export function trackFileDrag(): () => void {
  let depth = 0;
  const body = document.body;
  const enter = (e: DragEvent) => {
    if (!hasFiles(e)) return;
    depth++;
    body.classList.add('files-dragging');
  };
  const leave = () => {
    if (--depth <= 0) {
      depth = 0;
      body.classList.remove('files-dragging');
    }
  };
  const end = () => {
    depth = 0;
    body.classList.remove('files-dragging');
  };
  // Capture, weil Ablageflächen dragenter/dragleave nicht weiterreichen
  document.addEventListener('dragenter', enter, true);
  document.addEventListener('dragleave', leave, true);
  document.addEventListener('drop', end, true);
  document.addEventListener('dragend', end);
  return () => {
    document.removeEventListener('dragenter', enter, true);
    document.removeEventListener('dragleave', leave, true);
    document.removeEventListener('drop', end, true);
    document.removeEventListener('dragend', end);
    end();
  };
}
