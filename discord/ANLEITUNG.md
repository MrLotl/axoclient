# Discord-Status „Spielt AxoClient“ einrichten (kostenlos, ca. 3 Minuten)

Discord zeigt als Spielnamen den Namen einer „Anwendung“ an. Die legst du einmal im Discord Developer
Portal an; danach trägt der Launcher deren ID ein und Discord zeigt **AxoClient** statt Minecraft.

## 1. Anwendung anlegen

1. <https://discord.com/developers/applications> öffnen und mit deinem Discord-Konto anmelden.
2. Oben rechts **New Application** klicken, als Namen **AxoClient** eingeben, den Bedingungen zustimmen,
   **Create** klicken. (Dieser Name erscheint später in Discord hinter „Spielt“.)
3. Unter **General Information** als **App Icon** die Datei `axolotl-512.png` aus diesem Ordner hochladen
   und **Save Changes** klicken.
4. Die **Application ID** kopieren (Zahl unter „General Information“).

## 2. Bild für den Status hochladen

1. Links **Rich Presence → Art Assets** öffnen.
2. **Add Image(s)** klicken, wieder `axolotl-512.png` hochladen.
3. Als Namen genau **axolotl** eintragen (klein geschrieben) und **Save Changes** klicken.
   Discord braucht manchmal ein paar Minuten, bis neue Bilder angezeigt werden.

## 3. Im Launcher eintragen

Im Launcher unter **Einstellungen → Discord-Status** die Application ID einfügen.
Beim nächsten Spielstart zeigt Discord:

- **Spielt AxoClient** mit dem Axolotl-Bild
- darunter den Namen der Instanz, Mod-Loader und Version (bzw. den Server oder „Einzelspieler“)
- die Spielzeit seit dem Start

Der Status bleibt, solange das Spiel läuft. Der Launcher muss dafür geöffnet bleiben (minimiert reicht).

## Falls zusätzlich „Minecraft“ angezeigt wird

Discord erkennt Minecraft außerdem selbst am laufenden Programm und zeigt es dann ggf. zusätzlich an.
Das lässt sich in Discord abschalten: **Einstellungen → Registrierte Spiele** (bzw. „Aktivitätsprivatsphäre“),
dort bei Minecraft bzw. Java die Statusanzeige ausschalten oder den Eintrag entfernen.
