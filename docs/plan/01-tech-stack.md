# AxoClient – Tech-Stack und Module

Oct 16, 2026 · @Finn

Die Logik läuft in C# (.NET), das Fenster in Photino.NET und die Oberfläche in Svelte 5 mit TypeScript, gebaut mit Vite (ohne SvelteKit).

## Tech-Stack

| Schicht | Technik | Aufgabe |
| --- | --- | --- |
| Logik | C# (.NET) | Downloads, Start-Pipeline, Dateien, JSON |
| Fenster | Photino.NET | Fenster, Browser-Unterbau, Nachrichten zwischen C# und UI |
| Oberfläche | Svelte 5 + TypeScript, Vite | Alle Ansichten aus dem Design |

## Projektstruktur

```
AxoClient/
├─ src/
│  ├─ AxoClient.Common/              Hilfsklassen und Infrastruktur (Pfade, JSON, Log, Downloads, Jobs, Prozesse)
│  ├─ AxoClient.DataModel/           alle Modelle (Records, Enums, Dokumente), je Bereich ein Ordner
│  ├─ AxoClient.Accounts.Contract/   Interfaces des Bereichs
│  ├─ AxoClient.Accounts/            Umsetzung des Bereichs
│  ├─ AxoClient.Settings(.Contract)/
│  ├─ AxoClient.Game(.Contract)/
│  ├─ AxoClient.Instances(.Contract)/
│  ├─ AxoClient.Content(.Contract)/
│  ├─ AxoClient.Import(.Contract)/
│  ├─ AxoClient.Launch(.Contract)/
│  ├─ AxoClient.Servers(.Contract)/
│  ├─ AxoClient.Skins(.Contract)/
│  ├─ AxoClient.Social(.Contract)/
│  ├─ AxoClient.Integrations(.Contract)/
│  ├─ AxoClient.Diagnostics(.Contract)/
│  ├─ AxoClient.Updates(.Contract)/
│  ├─ AxoClient.App/                 Photino-Fenster, Bridge-Handler, Startup (Dependency Injection)
│  └─ AxoClient.Tests/
└─ ui/
   └─ src/
      ├─ lib/components/
      ├─ lib/bridge/
      ├─ lib/types/
      ├─ lib/i18n/
      └─ pages/
```

Jeder Bereich besteht aus zwei Projekten: `AxoClient.<Bereich>.Contract` enthält nur die Interfaces (z. B. `IInstanceManager`),
`AxoClient.<Bereich>` die Klassen dazu. Ein Bereich kennt andere Bereiche nur über deren Contract, nie über die Umsetzung –
so gibt es keine Kreise zwischen den Projekten. Modelle, die mehrere Bereiche teilen, liegen in `AxoClient.DataModel`.

`AxoClient.App` ist die Kompositionswurzel: `Startup.ConfigureServices` meldet alle Dienste beim Service-Container an, die
Klassen bekommen ihre Abhängigkeiten über den Konstruktor. Statische Klassen und Methoden gibt es nicht (Ausnahmen:
`Main` und die Windows-API-Deklarationen); Hilfsfunktionen stecken in eigenen Hilfsklassen mit Interface
(`ITextHelper`, `IPathGuard`, `IGameVersionHelper` …) oder in Basisklassen (`LaunchCheck`, `ServerSoftwareBase`, `BridgeHandler`).

Code-Stil (über `.editorconfig`, wird beim Bauen geprüft): `if`, `else`, Schleifen immer mit geschweiften Klammern,
Namespaces als Block, private Methoden `lowerCamelCase`, private Felder `_camelCase`, keine Methodennamen mit `Async`.

## Module

| Modul | Inhalt |
| --- | --- |
| Common | Gemeinsame Bausteine, siehe DRY-Bausteine |
| Settings | `SettingsService` (launcher.json, state.json), `Appearance` |
| Accounts | `AccountManager`, `IAuthProvider` (vorerst Entwicklungs-Provider, Microsoft später), `TokenStore` über DPAPI |
| Game | `VersionManager`, `JavaManager`, `LoaderManager` mit `ILoaderInstaller` je Loader (Fabric, Forge, NeoForge, Quilt) |
| Instances | `InstanceManager`, `WorldManager` (Welten, Backups), `ScreenshotManager`, `ServerListManager` (`servers.dat`) |
| Content | `ContentManager` (Mods, Ressourcepacks, Shader), `IContentProvider` mit `ModrinthProvider` und `CurseForgeProvider`, `ModpackManager` (`.mrpack`) |
| Launch | `LaunchPipeline` mit `ILaunchCheck`-Prüfungen, `CrashAnalyzer` mit Regeln für bekannte Fehlermuster |
| Servers | `LocalServerManager`, `IServerSoftware` je Software (Vanilla, Paper, Fabric, Velocity), UPnP |
| Skins | `SkinManager`, `CapeManager` |
| Social | Client für den Online-Service: Freunde, Status, Senden, Benachrichtigungen (später) |
| Integrations | Discord-Status, e4mc |
| Updates | Selbst-Update des Launchers |

## DRY-Bausteine

| Baustein | Wird genutzt für |
| --- | --- |
| `JsonStore<T>` | Jede JSON-Datei: atomar schreiben (`.tmp`, dann umbenennen), Migration über `formatVersion` |
| `AppPaths` | Alle Pfade an einer Stelle |
| `DownloadManager` | Minecraft-Dateien, Mods, Java, Modpacks, Server-JARs: parallel, mit Hash-Prüfung und Wiederholung |
| `ProcessRunner` | Java-Prozesse von Instanzen und lokalen Servern, liefert Log-Zeilen live an die Konsole |
| `ActivityTracker` | `playtime.json` und `uptime.json` |
| `ContentManager` | Mods, Ressourcepacks und Shader über `ContentType` |
| `LibraryStore<T>` | `skins/library.json` und `capes/library.json` |
| `ILaunchCheck` | Jede Prüfung vor dem Start: prüfen, Ergebnis, optional reparieren |

## Bridge zwischen C# und Svelte

- C#: `MessageRouter` in `AxoClient.App` nimmt Nachrichten wie `{ "type": "instance.launch", "id": "…" }` an und leitet sie an den passenden Manager weiter. Ereignisse wie Fortschritt, Log-Zeilen oder Freundesstatus schickt er an die UI.
- Svelte: eine `bridge.ts` mit `call(type, data)` und `on(type, handler)`. Keine Seite spricht Photino direkt an.
- Typen in `ui/src/lib/types/` spiegeln die C#-Klassen und sollen später automatisch daraus erzeugt werden.

## UI-Komponenten

`TitleBar`, `Sidebar`, `InstanceTabs`, `Dialog`, `Button`, `Toggle`, `Select`, `SearchBox`, `GridListToggle`, `FriendPicker` (alle Senden-Dialoge), `ProgressBar`, `LogView` (Konsole und Absturz-Dialog)
