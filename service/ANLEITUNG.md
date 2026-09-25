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

Die Tabellen legt der Dienst beim ersten Aufruf selbst an; in der Konsole ist nichts auszuführen. (`schema.sql`
enthält sie trotzdem, falls du die Datenbank lieber von Hand vorbereitest.)

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
3. Die Adresse ist im Launcher fest eingebaut (`AppInfo.AxoServiceUrl`); bei einem eigenen Worker
   dort anpassen.

## Wie es danach funktioniert

- Beim Start einer **Fabric-Instanz mit unterstützter Minecraft-Version** (26.3, 26.2, 26.1, 1.21.11,
  1.21.8, 1.21.4, 1.21.1) legt der Launcher die Mod automatisch in den mods-Ordner und meldet dich beim
  Dienst an. Bei anderen Instanzen wird nichts verändert.
- Im Spiel zeigt die Tabliste bei allen Spielern, die AxoClient nutzen, neben dem Kopf ein kleines Axolotl;
  dasselbe Symbol steht auch vor dem Namen über dem Spieler.
- Mit der **rechten Umschalttaste** öffnet sich im Spiel das Menü für die eigenen Anzeigen (FPS, Ping,
  Koordinaten, Blickrichtung, Uhrzeit, Rüstung). Dort lassen sie sich ein- und ausschalten und mit der Maus
  frei verschieben; gespeichert wird das je Instanz in `config/axoclient-hud.json`.
- Die Köpfe (und damit das Symbol) zeigt Minecraft nur auf Servern mit Online-Modus bzw. im eigenen
  LAN/Einzelspieler an.
- Wer 30 Tage nicht mit dem Launcher gespielt hat, verliert das Symbol, bis er wieder spielt.

## Update: Freunde & „Beitreten“ (wenn der Dienst schon läuft)

Für die Freundesliste auf der Startseite braucht der Dienst neuen Code:

1. **Worker → Edit code**: den Code komplett durch den aktuellen Inhalt von `worker.js` ersetzen, **Deploy**.
2. Den Launcher neu starten.

Gespeichert werden zusätzlich: ein Anmelde-Token (nur als Hash), der aktuelle Server samt Minecraft-Version,
solange du spielst, und wer wen als Freund hinzugefügt hat. Den Server sehen nur Freunde, die sich
**gegenseitig** hinzugefügt haben.

## Update: Teilen mit Freunden (Instanzen, Overlay, Mods, Server)

Damit sich Freunde Instanzen, Overlay-Einstellungen, Mods und Server schicken können, braucht der Dienst ein
Postfach. Dafür ist nur ein Schritt nötig:

1. **Worker → Edit code**: den Code komplett durch den aktuellen Inhalt von `worker.js` ersetzen, **Deploy**.

Die Tabelle `shares` legt der Dienst selbst an. Beide Seiten brauchen
danach den aktuellen Launcher – ein Freund mit einer älteren Version sieht das Postfach nicht.

Was gespeichert wird: das Paket selbst (Instanz-Rezept, Overlay-Einstellungen, Modrinth-Kennung oder Serveradresse),
wer es geschickt hat und wer es bekommt. Es liegt nur im Postfach des Empfängers, bis er es abholt oder verwirft,
**höchstens 14 Tage**. Der Dienst wertet den Inhalt nicht aus; das macht der Launcher des Empfängers und prüft ihn dabei.

Grenzen (in `worker.js` oben einstellbar): nur zwischen **gegenseitigen** Freunden, ein Paket höchstens 250 000
Zeichen, höchstens 40 offene Pakete je Postfach (davon 8 von derselben Person) und 30 gesendete Pakete pro Stunde.

## Update: Admins und Umhänge hochladen

Admins können AxoClient-Umhänge direkt im Launcher hochladen (Skins → Capes (AxoClient) → Karte mit dem **+**)
und hochgeladene Umhänge wieder löschen. Der Besitzer ernennt die Admins im Launcher über **Admins verwalten**.

1. **Worker → Edit code**: den Code komplett durch den aktuellen Inhalt von `worker.js` ersetzen, **Deploy**.
2. Deine Minecraft-UUID herausfinden: <https://api.mojang.com/users/profiles/minecraft/DEINNAME> im Browser öffnen
   (DEINNAME durch deinen Spielernamen ersetzen) und den Wert bei `"id"` kopieren.
3. Im Worker **Settings → Variables and Secrets → Add**: Typ **Text**, Name genau `OWNER_UUID`, als Wert die UUID
   aus Schritt 2 (mehrere Besitzer mit Komma getrennt). Speichern bzw. **Deploy** bestätigen.
4. Den Launcher neu starten. Unter Skins → Capes (AxoClient) erscheinen jetzt die **+**-Karte und der Knopf
   **Admins verwalten**.

Die Tabellen `admins` und `cape_images` legt der Dienst beim ersten Gebrauch selbst an. Hochgeladene Umhänge liegen
als Bild in der Datenbank (höchstens 1,5 MB, 64×32 Pixel oder ein Vielfaches bis 2048×1024). Es gibt nur noch diese
Umhänge; den früheren GitHub-Ordner `capes/` gibt es nicht mehr. Wer noch einen der alten Umhänge trägt, legt ihn
automatisch ab. Wird ein Umhang gelöscht, legen ihn ebenfalls alle ab, die ihn gerade tragen.

**Wichtig:** Launcher ab dieser Version laden die Umhänge über den Dienst. Den neuen `worker.js` deshalb vor dem
Launcher-Release deployen, sonst bleibt die Umhang-Liste leer.

## Wenn etwas nicht klappt

| Meldung | Ursache / Lösung |
|---|---|
| „Der AxoClient-Dienst kennt das Teilen noch nicht“ | Der neue `worker.js` ist noch nicht deployt (siehe „Update: Teilen mit Freunden“). |
| „Der AxoClient-Dienst kennt das Hochladen von Umhängen noch nicht“ | Der neue `worker.js` ist noch nicht deployt (siehe „Update: Admins und Umhänge hochladen“). |
| Kein **+** und kein „Admins verwalten“ zu sehen | `OWNER_UUID` fehlt oder enthält eine falsche UUID; danach den Launcher neu starten. |
| „Ihr müsst euch gegenseitig als Freunde hinzugefügt haben …“ | Teilen geht nur, wenn beide den anderen hinzugefügt haben; unter Freunde die Anfrage annehmen. |
| „Unter dieser Adresse antwortet kein Badge-Dienst“ | Adresse falsch oder Code aus Schritt 3 nicht gespeichert (Deploy vergessen). |
| „Interner Fehler: … DB …“ | Schritt 4 fehlt: Die Bindung muss genau `DB` heißen. |
| „no such table: …“ | Der Worker-Code ist veraltet: aktuellen `worker.js` deployen. |
| „Mojang hat kein Spieler-Zertifikat ausgestellt“ | Anmeldung im Launcher abgelaufen: einmal ab- und wieder anmelden. |
| „Zeitstempel ungültig – stimmt die Uhr des PCs?“ | Die Windows-Uhr weicht mehr als 5 Minuten ab: Uhrzeit automatisch stellen lassen. |
| „Spieler-Zertifikat wurde nicht von Mojang ausgestellt“ | Mojang hat seine Schlüssel geändert: `MOJANG_KEYS` in `worker.js` aktualisieren (Quelle steht im Code). |

## Kosten

Der kostenlose Tarif reicht für diesen Dienst normalerweise dauerhaft. Wird ein Tageslimit erreicht,
lehnt Cloudflare weitere Anfragen bis zum nächsten Tag ab (die Symbole fehlen dann kurz) – es entstehen
keine Kosten, solange kein bezahlter Tarif aktiviert wird.
