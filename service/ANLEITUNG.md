# AxoClient-Dienst einrichten

Ein Cloudflare Worker mit D1-Datenbank und einem Durable Object. Er bedient zwei Schnittstellen:

- **alt** (ohne Präfix, z. B. `POST /register`, `POST /check`): unverändert für Launcher bis 1.x
- **v2** (`/v2/…`): für den neuen Launcher und die AxoMod

Beide nutzen dieselben Tabellen. Freunde, Online-Status, Umhänge und Einladungen funktionieren also zwischen alten und
neuen Launchern. Neue Tabellen und Spalten legt der Worker beim ersten Aufruf selbst an, bestehende Daten bleiben.

## Bestehenden Worker aktualisieren

1. `worker.js` und `wrangler.toml` in einen Ordner legen.
2. In `wrangler.toml` den Namen des bisherigen Workers und die ID der bisherigen D1-Datenbank eintragen
   (Dashboard → Workers & Pages bzw. Storage & Databases → D1). `OWNER_UUID` prüfen.
3. `npx wrangler deploy`

Der erste Deploy legt das Durable Object `LiveRoom` an (Abschnitt `[[migrations]]`). Ohne diese Bindung läuft alles
außer den Emotes; `/v2/live` antwortet dann mit `503 live_unavailable`.

Im Dashboard unter *Settings → Domains & Routes* muss die `workers.dev`-Adresse aktiv sein, sonst antwortet Cloudflare
mit „error code: 1042“.

## Emotes (Echtzeit)

- Der Launcher öffnet beim Spielen auf einem Server `wss://<worker>/v2/live?server=<adresse>` (mit Token im
  `Authorization`-Header). Je Server gibt es einen Raum.
- Nachricht vom Launcher: `{"type":"emote","emote":"wave"}` bzw. `"emote":null` zum Abbrechen.
- Alle anderen AxoClient-Spieler im selben Raum bekommen `{"type":"emote","uuid":"…","emote":"wave","at":…}`.
- Höchstens ein Emote je 0,8 s und Spieler; der Worker speichert dabei nichts.
- Auf dem Minecraft-Server ist nichts nötig. Sehen können die Emotes nur Spieler mit AxoClient.

## Kurzüberblick v2

| Methode und Pfad | Zweck |
|---|---|
| `POST /v2/auth/register` | Anmeldung per Mojang-Spielerzertifikat → `{ token }` |
| `DELETE /v2/auth/token` | dieses Gerät abmelden |
| `GET /v2/me`, `DELETE /v2/me` | eigenes Konto (Rolle) / alles löschen |
| `GET /v2/sync` | Freunde, Benachrichtigungen, Postfach in einem Aufruf |
| `PUT /v2/status` | Status `offline` / `launcher` / `ingame` mit Join-Adresse |
| `POST /v2/friends/requests`, `POST|DELETE /v2/friends/{uuid}`, `DELETE /v2/friends/requests/{uuid}` | Freunde |
| `POST|DELETE /v2/blocks/{uuid}` | blockieren |
| `POST /v2/inbox`, `GET|DELETE /v2/inbox/{id}` | Pakete und Einladungen |
| `GET /v2/capes`, `GET /v2/capes/{id}.png`, `PUT /v2/capes/active` | Umhänge |
| `POST|DELETE /v2/capes/submissions` | eigenes Cape einreichen / zurückziehen |
| `/v2/admin/…` | Einreichungen prüfen, offizielle Capes, Admins (nur Besitzer) |
| `POST /v2/players` | für die Mod: wer nutzt AxoClient, welcher Umhang |
| `GET /v2/live` | WebSocket für Emotes |

Fehler kommen als `{"error":{"code":"…","message":"…"}}`; der Launcher zeigt dazu den Text `service.<code>`.
