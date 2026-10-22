import { describe, expect, it } from 'vitest';
import { has, t, tr } from './index';
import { bytes, compactNumber, duration, versionLabel } from '../util/format';

describe('t', () => {
  it('liefert den Schlüssel zurück, wenn er fehlt', () => {
    expect(t('gibt.es.nicht')).toBe('gibt.es.nicht');
  });

  it('setzt Parameter ein', () => {
    expect(t('friends.requestSentTo', { name: 'Steve' })).toBe('Anfrage an Steve gesendet');
  });

  it('formatiert Zahlen deutsch', () => {
    expect(t('servers.players', { online: 1200, max: 2000 })).toBe('1.200 / 2.000 Spieler');
  });

  it('lässt fehlende Parameter leer', () => {
    expect(t('friends.requestSentTo')).toContain('{name}');
    expect(t('friends.requestSentTo', {})).toBe('Anfrage an  gesendet');
  });

  it('übersetzt Codes als Parameter mit', () => {
    expect(has('loader.fabric')).toBe(true);
    expect(t('instance.modpackVersion', { version: 'loader.fabric' })).toBe(`Modpack ${t('loader.fabric')}`);
  });

  it('tr nimmt TextRefs aus C#', () => {
    expect(tr({ code: 'friends.requestSentTo', params: { name: 'Alex' } })).toBe('Anfrage an Alex gesendet');
    expect(tr(null)).toBe('');
  });
});

describe('format', () => {
  it('bytes', () => {
    expect(bytes(512)).toBe('512 B');
    expect(bytes(1536)).toBe('1,5 KB');
    expect(bytes(5 * 1024 ** 3)).toBe('5 GB');
  });

  it('compactNumber', () => {
    expect(compactNumber(950)).toBe('950');
    expect(compactNumber(12_300)).toBe('12,3 Tsd.');
    expect(compactNumber(4_500_000)).toBe('4,5 Mio.');
  });

  it('duration wechselt von Minuten auf Stunden', () => {
    expect(duration(12 * 60)).toBe(t('common.minutes', { count: 12 }));
    expect(duration(3 * 3600)).toBe(t('common.hours', { count: 3 }));
    expect(duration(5 * 3600 + 20 * 60)).toBe(t('common.hoursMinutes', { h: 5, m: 20 }));
  });

  it('versionLabel', () => {
    expect(versionLabel('1.21.1', 'fabric')).toBe(`1.21.1 · ${t('loader.fabric')}`);
  });
});
