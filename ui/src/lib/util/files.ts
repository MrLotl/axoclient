import { call } from '../bridge/bridge';
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

/** Öffnet die Dateiauswahl des Browsers für Bilder und gibt Base64 zurück. */
export function pickImage(accept = 'image/png,image/jpeg'): Promise<{ name: string; base64: string; url: string } | null> {
  return new Promise((resolve) => {
    const input = document.createElement('input');
    input.type = 'file';
    input.accept = accept;
    input.onchange = async () => {
      const file = input.files?.[0];
      if (!file) return resolve(null);
      resolve({ name: file.name, base64: await toBase64(file), url: URL.createObjectURL(file) });
    };
    input.click();
  });
}
