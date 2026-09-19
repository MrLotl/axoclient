# Symbol-Dienst bei Cloudflare einrichten (kostenlos, ca. 5 Minuten)

Der Dienst merkt sich, welche Spieler AxoClient nutzen, damit die Mod im Spiel neben ihnen das
Axolotl anzeigen kann. Gespeichert werden nur Minecraft-UUID, Spielername und „zuletzt gesehen“.

Alles passiert im Browser, du musst nichts installieren. (Die Beschriftungen im Cloudflare-Dashboard
können sich leicht ändern; sie sind hier auf Englisch angegeben, wie Cloudflare sie anzeigt.)

## 1. Konto anlegen

Auf <https://dash.cloudflare.com/sign-up> ein kostenloses Konto erstellen und die E-Mail bestätigen.
Eine eigene Domain wird **nicht** benötigt.

## 2. Datenbank anlegen

1. Im Dashboard links **Storage & Databases → D1 SQL Database** öffnen.
2. **Create** klicken, als Namen `mclauncher-badge` eingeben, **Create** bestätigen.
3. In der neuen Datenbank den Tab **Console** öffnen.
4. Die beiden Zeilen aus `schema.sql` (liegt neben dieser Anleitung) **einzeln nacheinander** einfügen und
   jeweils **Execute** klicken. Mit `/tables` lässt sich prüfen, ob die Tabelle `users` angelegt wurde.

   Gespeichert werden je Spieler: `uuid` (Minecraft-UUID ohne Bindestriche), `name` (Spielername, nur zur
   Übersicht) und `last_seen` (letzte Anmeldung in Millisekunden).

## 3. Worker (den eigentlichen Dienst) anlegen

1. Links **Compute (Workers) → Workers & Pages** öffnen und **Create** klicken.
2. **Start with Hello World!** wählen, als Namen `mclauncher-badge` eingeben und **Deploy** klicken.
3. Auf **Edit code** klicken, den vorhandenen Code komplett löschen und den Inhalt von `worker.js` einfügen.
4. Oben rechts **Deploy** klicken.

## 4. Datenbank mit dem Worker verbinden

1. Im Worker `mclauncher-badge` auf **Settings → Bindings** gehen und **Add** klicken.
2. **D1 database** wählen.
3. Bei **Variable name** genau `DB` eintragen (Großbuchstaben), als Datenbank `mclauncher-badge` wählen.
4. Speichern bzw. **Deploy** bestätigen.

## 5. Testen

1. Die Adresse des Workers kopieren. Sie steht oben beim Worker und sieht so aus:
   `https://mclauncher-badge.DEIN-NAME.workers.dev`
2. Im Browser öffnen. Es muss erscheinen: `{"ok":true,"service":"mclauncher-badge"}`
3. Im Launcher unter **Einstellungen → Axolotl-Symbol in der Tabliste** die Adresse eintragen und
   **Verbindung testen** klicken. Bei Erfolg steht dort „Alles bereit“.

## Wie es danach funktioniert

- Beim Start einer **Fabric-Instanz mit Minecraft 26.2** legt der Launcher die Mod automatisch in den
  mods-Ordner und meldet dich beim Dienst an. Bei anderen Instanzen wird nichts verändert.
- Im Spiel zeigt die Tabliste bei allen Spielern, die den Launcher (mit derselben Dienst-Adresse) nutzen,
  oben rechts am Kopf ein kleines Axolotl.
- Die Köpfe (und damit das Symbol) zeigt Minecraft nur auf Servern mit Online-Modus bzw. im eigenen
  LAN/Einzelspieler an.
- Wer 30 Tage nicht mit dem Launcher gespielt hat, verliert das Symbol, bis er wieder spielt.

## Update: Freunde & „Beitreten“ (wenn der Dienst schon läuft)

Für die Freundesliste auf der Startseite braucht der Dienst neuen Code und drei neue Tabellen:

1. **Worker → Edit code**: den Code komplett durch den aktuellen Inhalt von `worker.js` ersetzen, **Deploy**.
2. **D1 → mclauncher-badge → Console**: die Zeilen 3 bis 6 aus `schema.sql` einzeln ausführen
   (`tokens`, `status`, `friends` und der Index). Alle Zeilen können gefahrlos erneut ausgeführt werden.
3. Den Launcher neu starten.

Gespeichert werden zusätzlich: ein Anmelde-Token (nur als Hash), der aktuelle Server samt Minecraft-Version,
solange du spielst, und wer wen als Freund hinzugefügt hat. Den Server sehen nur Freunde, die sich
**gegenseitig** hinzugefügt haben.

## Wenn etwas nicht klappt

| Meldung | Ursache / Lösung |
|---|---|
| „Unter dieser Adresse antwortet kein Badge-Dienst“ | Adresse falsch oder Code aus Schritt 3 nicht gespeichert (Deploy vergessen). |
| „Interner Fehler: … DB …“ | Schritt 4 fehlt: Die Bindung muss genau `DB` heißen. |
| „no such table: users“ | Schritt 2.4 (schema.sql ausführen) fehlt. |
| „Mojang hat kein Spieler-Zertifikat ausgestellt“ | Anmeldung im Launcher abgelaufen: einmal ab- und wieder anmelden. |
| „Zeitstempel ungültig – stimmt die Uhr des PCs?“ | Die Windows-Uhr weicht mehr als 5 Minuten ab: Uhrzeit automatisch stellen lassen. |
| „Spieler-Zertifikat wurde nicht von Mojang ausgestellt“ | Mojang hat seine Schlüssel geändert: `MOJANG_KEYS` in `worker.js` aktualisieren (Quelle steht im Code). |

## Kosten

Der kostenlose Tarif reicht für diesen Dienst normalerweise dauerhaft. Wird ein Tageslimit erreicht,
lehnt Cloudflare weitere Anfragen bis zum nächsten Tag ab (die Symbole fehlen dann kurz) – es entstehen
keine Kosten, solange kein bezahlter Tarif aktiviert wird.
