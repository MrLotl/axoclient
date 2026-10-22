import { call } from '../bridge/bridge';

/** In die Zwischenablage; fällt auf die C#-Seite zurück, falls der Browser es nicht erlaubt. */
export async function copyText(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
  } catch {
    await call('clipboard.write', { text });
  }
}
