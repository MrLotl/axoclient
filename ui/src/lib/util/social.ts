import { t } from '../i18n';
import { ago } from './format';
import type { Friend } from '../types';

export const presenceColors: Record<string, string> = { launcher: '#3fb950', ingame: '#4c8dff', offline: '#6b6b6b' };

/** "Im Spiel · seit 25 Min." */
export function statusText(f: Friend): string {
  if (f.state === 'offline') return t('friends.offline');
  const base = f.state === 'ingame' ? t('friends.ingame') : t('friends.online');
  if (!f.since) return base;
  const minutes = Math.max(1, Math.round((Date.now() - f.since) / 60000));
  const value = minutes < 60 ? t('common.minutes', { count: minutes }) : t('common.hours', { count: Math.round(minutes / 60) });
  return `${base} · ${t('common.since', { value })}`;
}

export { ago };
