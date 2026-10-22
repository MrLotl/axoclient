import type { TextRef } from '../types';

/**
 * Die einzige Verbindung zur C#-Seite. Seiten sprechen nie direkt mit Photino, sondern nur über
 * call(type, data) und on(type, handler).
 */

interface PhotinoExternal {
  sendMessage(message: string): void;
  receiveMessage(callback: (message: string) => void): void;
}

function host(): PhotinoExternal {
  return (window as unknown as { external: PhotinoExternal }).external;
}

/** Fehler aus C# mit übersetzbarem Text. */
export class BridgeError extends Error {
  constructor(public readonly text: TextRef) {
    super(text.code);
  }
}

type Handler = (data: any) => void;

const pending = new Map<number, { resolve: (v: any) => void; reject: (e: unknown) => void }>();
const listeners = new Map<string, Set<Handler>>();
let nextId = 1;
let connected = false;

function hasHost(): boolean {
  return typeof window !== 'undefined' && !!host() && typeof host().sendMessage === 'function';
}

function connect() {
  if (connected || !hasHost()) return;
  connected = true;
  host().receiveMessage((raw: string) => {
    let message: any;
    try {
      message = JSON.parse(raw);
    } catch {
      return;
    }
    if (message.kind === 'reply') {
      const entry = pending.get(message.id);
      if (!entry) return;
      pending.delete(message.id);
      if (message.ok) entry.resolve(message.data);
      else entry.reject(new BridgeError(message.error ?? { code: 'errors.unexpected' }));
    } else if (message.kind === 'event') {
      listeners.get(message.type)?.forEach((h) => {
        try {
          h(message.data);
        } catch (e) {
          console.error(`Fehler im Handler für ${message.type}`, e);
        }
      });
      listeners.get('*')?.forEach((h) => h({ type: message.type, data: message.data }));
    }
  });
}

/** Ruft eine Aktion auf der C#-Seite auf. */
export function call<T = unknown>(type: string, data?: unknown): Promise<T> {
  connect();
  if (!hasHost()) return Promise.reject(new BridgeError({ code: 'errors.offline' }));
  const id = nextId++;
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve, reject });
    host().sendMessage(JSON.stringify({ id, type, data: data ?? null }));
  });
}

/** Abonniert ein Ereignis aus C#. Gibt eine Funktion zum Abmelden zurück. */
export function on<T = any>(type: string, handler: (data: T) => void): () => void {
  connect();
  let set = listeners.get(type);
  if (!set) listeners.set(type, (set = new Set()));
  set.add(handler as Handler);
  return () => set!.delete(handler as Handler);
}

export function isHosted(): boolean {
  return hasHost();
}
