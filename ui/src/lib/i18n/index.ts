import de from './de.json';
import type { TextRef } from '../types';

type Dict = { [key: string]: string | Dict };

const dictionary: Dict = de as Dict;

/** Sucht einen Schlüssel; erlaubt auch Schlüssel, die selbst Punkte enthalten ("ram.solution"). */
function lookup(node: Dict | string | undefined, parts: string[]): string | undefined {
  if (node === undefined) return undefined;
  if (parts.length === 0) return typeof node === 'string' ? node : undefined;
  if (typeof node === 'string') return undefined;
  for (let i = parts.length; i > 0; i--) {
    const key = parts.slice(0, i).join('.');
    if (key in node) {
      const found = lookup(node[key], parts.slice(i));
      if (found !== undefined) return found;
    }
  }
  return undefined;
}

export function has(code: string): boolean {
  return lookup(dictionary, code.split('.')) !== undefined;
}

/** Übersetzt einen Schlüssel aus de.json und setzt Parameter wie {name} ein. */
export function t(code: string, params?: Record<string, unknown> | null): string {
  const template = lookup(dictionary, code.split('.'));
  if (template === undefined) return code;
  if (!params) return template;
  return template.replace(/\{(\w+)\}/g, (_, key: string) => {
    const value = params[key];
    if (value === undefined || value === null) return '';
    // Parameter, die selbst ein Code sind (z. B. Grund eines Fehlers), mit übersetzen
    if (typeof value === 'string' && /^[a-z]+(\.[a-zA-Z_]+)+$/.test(value) && has(value)) return t(value);
    if (typeof value === 'number') return value.toLocaleString('de-DE');
    return String(value);
  });
}

/** Übersetzt einen Text aus C# (Code mit Parametern). */
export function tr(ref: TextRef | null | undefined): string {
  if (!ref) return '';
  return t(ref.code, ref.params ?? undefined);
}
