import type { InstanceSort, InstanceSummary, UiState } from '../types';

/** Vergleichsfunktionen für die Sortierungen der Instanzen-Seite. */
export const sorters: Record<Exclude<InstanceSort, 'custom'>, (a: InstanceSummary, b: InstanceSummary) => number> = {
  recent: (a, b) => (b.lastPlayed ?? b.createdAt).localeCompare(a.lastPlayed ?? a.createdAt),
  name: (a, b) => a.name.localeCompare(b.name, 'de'),
  playtime: (a, b) => b.totalSeconds - a.totalSeconds,
  mods: (a, b) => b.modCount - a.modCount
};

/**
 * Instanzen in der gewählten Reihenfolge. Bei „Eigene Reihenfolge“ gilt die gespeicherte Position;
 * Instanzen ohne Position (z. B. neu angelegt) kommen ans Ende. Sonst stehen Favoriten oben.
 */
export function orderInstances(list: InstanceSummary[], ui: UiState | null | undefined, sort: InstanceSort = ui?.sort.instances ?? 'recent'): InstanceSummary[] {
  if (sort === 'custom') {
    const pos = new Map((ui?.instanceOrder ?? []).map((id, i) => [id, i]));
    const at = (i: InstanceSummary) => pos.get(i.id) ?? Number.MAX_SAFE_INTEGER;
    return [...list].sort((a, b) => at(a) - at(b) || sorters.recent(a, b));
  }
  const fav = new Set(ui?.favorites ?? []);
  return [...list].sort((a, b) => Number(fav.has(b.id)) - Number(fav.has(a.id)) || sorters[sort](a, b));
}

/** Verschiebt `id` direkt vor bzw. hinter `target`. */
export function moveId(ids: string[], id: string, target: string, after: boolean): string[] {
  if (id === target) return ids;
  const rest = ids.filter((x) => x !== id);
  const at = rest.indexOf(target);
  if (at < 0) return ids;
  rest.splice(after ? at + 1 : at, 0, id);
  return rest;
}
