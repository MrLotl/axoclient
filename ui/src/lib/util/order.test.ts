import { describe, expect, it } from 'vitest';
import { moveId, orderInstances } from './order';
import type { InstanceSummary, UiState } from '../types';

const inst = (id: string, lastPlayed: string | null, extra: Partial<InstanceSummary> = {}): InstanceSummary => ({
  id,
  name: id,
  minecraftVersion: '1.21.4',
  loader: 'fabric',
  loaderVersion: null,
  iconUrl: '',
  bannerUrl: null,
  hasIcon: false,
  lastPlayed,
  totalSeconds: 0,
  running: false,
  modCount: 0,
  createdAt: '2026-01-01T00:00:00Z',
  modLock: 'off',
  artSeed: id,
  ...extra
});

const ui = (patch: Partial<UiState>): UiState =>
  ({ favorites: [], instanceOrder: [], sort: { modpacks: 'popular', addMods: 'popular', instances: 'recent' }, ...patch }) as UiState;

const list = [inst('a', '2026-03-01T00:00:00Z'), inst('b', '2026-05-01T00:00:00Z'), inst('c', null)];

describe('orderInstances', () => {
  it('sortiert standardmäßig nach zuletzt gespielt', () => {
    expect(orderInstances(list, ui({})).map((i) => i.id)).toEqual(['b', 'a', 'c']);
  });

  it('stellt Favoriten nach oben', () => {
    expect(orderInstances(list, ui({ favorites: ['c'] })).map((i) => i.id)).toEqual(['c', 'b', 'a']);
  });

  it('nutzt die eigene Reihenfolge und hängt Unbekannte hinten an', () => {
    const order = orderInstances(list, ui({ instanceOrder: ['c', 'a'], favorites: ['b'] }), 'custom');
    expect(order.map((i) => i.id)).toEqual(['c', 'a', 'b']);
  });
});

describe('moveId', () => {
  it('verschiebt vor bzw. hinter das Ziel', () => {
    expect(moveId(['a', 'b', 'c', 'd'], 'd', 'b', false)).toEqual(['a', 'd', 'b', 'c']);
    expect(moveId(['a', 'b', 'c', 'd'], 'a', 'c', true)).toEqual(['b', 'c', 'a', 'd']);
  });

  it('lässt die Liste bei gleichem oder unbekanntem Ziel unverändert', () => {
    expect(moveId(['a', 'b'], 'a', 'a', true)).toEqual(['a', 'b']);
    expect(moveId(['a', 'b'], 'a', 'x', true)).toEqual(['a', 'b']);
  });
});
