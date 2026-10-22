import { t } from '../i18n';
import type { LoaderType } from '../types';

export function bytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let v = value / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i++;
  }
  return `${v.toLocaleString('de-DE', { maximumFractionDigits: v < 10 ? 1 : 0 })} ${units[i]}`;
}

export function megabytes(mb: number): string {
  return mb >= 1024 ? t('common.gb', { value: (mb / 1024).toLocaleString('de-DE', { maximumFractionDigits: 1 }) }) : t('common.mb', { value: mb });
}

/** "vor 5 Min.", "gestern", "vor 3 Tagen". */
export function ago(iso: string | number | null | undefined): string {
  if (iso === null || iso === undefined) return t('common.never');
  const time = typeof iso === 'number' ? iso : Date.parse(iso);
  if (Number.isNaN(time)) return t('common.never');
  const minutes = Math.floor((Date.now() - time) / 60000);
  if (minutes < 1) return t('common.justNow');
  if (minutes < 60) return t('common.minutesAgo', { count: minutes });
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return t('common.hoursAgo', { count: hours });
  const days = Math.floor(hours / 24);
  if (days === 1) return t('common.yesterday');
  if (days < 30) return t('common.daysAgo', { count: days });
  return new Date(time).toLocaleDateString('de-DE');
}

/** Dauer in Sekunden: "12 Min.", "3 Std.", "5 Std. 20 Min.". */
export function duration(seconds: number): string {
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return t('common.minutes', { count: minutes });
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m === 0 || h >= 10 ? t('common.hours', { count: h }) : t('common.hoursMinutes', { h, m });
}

export function loaderName(loader: LoaderType | string): string {
  return t(`loader.${loader}`);
}

export function versionLabel(mc: string, loader: LoaderType | string): string {
  return `${mc} · ${loaderName(loader)}`;
}

export function compactNumber(n: number): string {
  if (n >= 1_000_000) return `${(n / 1_000_000).toLocaleString('de-DE', { maximumFractionDigits: 1 })} Mio.`;
  if (n >= 1_000) return `${(n / 1_000).toLocaleString('de-DE', { maximumFractionDigits: 1 })} Tsd.`;
  return n.toLocaleString('de-DE');
}

export function clock(iso: string): string {
  return new Date(iso).toLocaleTimeString('de-DE', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}
