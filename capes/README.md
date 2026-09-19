# AxoClient-Umhänge

Neuen Umhang hinzufügen:

1. Bild als `capes/<id>.png` ablegen. Die ID nur aus Kleinbuchstaben, Ziffern, `-` und `_` (max. 32 Zeichen),
   z.B. `sommer-2026.png`.
2. In `capes.json` eintragen: `{ "id": "sommer-2026", "name": "Sommer 2026" }`
3. Committen und pushen (`git add -A`, `git commit -m "Neuer Umhang"`, `git push`) – ein neues Release ist
   nicht nötig. Nach ein paar Minuten (GitHub zwischenspeichert) erscheint er im Launcher unter Skins.

Bildformat wie ein normaler Minecraft-Umhang: 64×32 Pixel oder ein Vielfaches (128×64, 256×128 ...).

| Bereich (in 64×32-Pixeln) | Inhalt |
|---|---|
| x 1–10, y 1–16 | Außenseite (sieht man von hinten) |
| x 12–21, y 1–16 | Innenseite |
| x 0 und x 11, y 1–16 | Seitenkanten |
| x 1–10 / 11–20, y 0 | Ober- / Unterkante |
| x 22–45, y 0–21 | Elytra |

Einen Umhang entfernen: aus `capes.json` löschen (das Bild kann liegen bleiben, damit ihn niemand
plötzlich „verliert“, oder ebenfalls gelöscht werden – dann trägt ihn im Spiel niemand mehr).
