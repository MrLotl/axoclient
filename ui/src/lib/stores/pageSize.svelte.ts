/**
 * Wie viele Treffer eine Seite lädt – eine Einstellung für alle Listen mit Seiten (Mods hinzufügen, Modpacks).
 * Durch 3 teilbar, damit Karten-Raster aufgehen. Gemerkt im Browser-Speicher des Launchers.
 */
export const PAGE_SIZES = [24, 48, 96] as const;
const KEY = 'axo.pageSize';

function read(): number {
  try {
    const v = Number(localStorage.getItem(KEY));
    return (PAGE_SIZES as readonly number[]).includes(v) ? v : PAGE_SIZES[0];
  } catch {
    return PAGE_SIZES[0];
  }
}

class PageSize {
  value = $state(read());

  set(v: number) {
    this.value = v;
    try {
      localStorage.setItem(KEY, String(v));
    } catch {
      // ohne Speicher gilt die Wahl nur bis zum Neustart
    }
  }

  /** Was die Quelle wirklich liefert: CurseForge höchstens 50 je Seite, Modrinth 100. */
  for(source: string): number {
    return Math.min(this.value, source === 'curseforge' ? 50 : 100);
  }
}

export const pageSize = new PageSize();
