import { call } from '../bridge/bridge';
import { app } from '../stores/app.svelte';
import type { FileRef } from '../types';

export async function toBase64(file: Blob): Promise<string> {
  const buffer = new Uint8Array(await file.arrayBuffer());
  let binary = '';
  for (let i = 0; i < buffer.length; i += 0x8000) binary += String.fromCharCode(...buffer.subarray(i, i + 0x8000));
  return btoa(binary);
}

/** Datei aus Drag & Drop an C# geben; zurück kommt eine Referenz (nie ein Pfad). */
export async function upload(file: File): Promise<FileRef> {
  return call<FileRef>('files.upload', { name: file.name, base64: await toBase64(file) });
}

/**
 * Bild über die native Dateiauswahl des Launchers wählen (wie alle anderen Dateien auch).
 * Die Auswahl des Browsers (<input type=file>) gehört in der WebView nicht zum Fenster und ging dahinter auf.
 */
export async function pickImage(): Promise<{ name: string; base64: string; url: string } | null> {
  let picked: { name: string; base64: string } | null;
  try {
    picked = await call<{ name: string; base64: string } | null>('dialog.openImage');
  } catch (e) {
    app.error(e);
    return null;
  }
  if (!picked) return null;
  const bytes = Uint8Array.from(atob(picked.base64), (c) => c.charCodeAt(0));
  const type = bytes[0] === 0xff ? 'image/jpeg' : 'image/png';
  return { ...picked, url: URL.createObjectURL(new Blob([bytes], { type })) };
}
