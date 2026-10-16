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
│  ├─ AxoClient.Core/
│  │  ├─ Common/
│  │  ├─ Accounts/
│  │  ├─ Game/
│  │  ├─ Instances/
│  │  ├─ Content/
│  │  ├─ Launch/
│  │  ├─ Servers/
│  │  ├─ Skins/
│  │  ├─ Social/
│  │  ├─ Integrations/
│  │  ├─ Updates/
│  │  └─ Diagnostics/
│  ├─ AxoClient.App/
│  └─ AxoClient.Tests/
└─ ui/
   └─ src/
      ├─ lib/components/
      ├─ lib/bridge/
      ├─ lib/types/
      ├─ lib/i18n/
      └─ pages/
```

`AxoClient.Core` enthält die gesamte Logik ohne UI, `AxoClient.App` das Photino-Fenster und die Bridge. Die Module sind Ordner und Namespaces, keine eigenen Projekte.

## Module

| Modul | Inhalt |
| --- | --- |
| Common | Gemeinsame Bausteine, siehe DRY-Bausteine |
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
