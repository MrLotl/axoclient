# AxoClient – Ordnerstruktur

Oct 16, 2026 · @Finn

```
AxoClient/
├─ launcher.json
├─ accounts.json
├─ state.json
│
├─ instances/
│  └─ <instance-id>/
│     ├─ instance.json
│     ├─ content.json
│     ├─ playtime.json
│     ├─ icon.png
│     ├─ banner.png
│     ├─ backups/
│     └─ minecraft/
│        ├─ mods/
│        ├─ resourcepacks/
│        ├─ shaderpacks/
│        ├─ saves/
│        ├─ screenshots/
│        ├─ config/
│        ├─ logs/
│        ├─ crash-reports/
│        ├─ options.txt
│        └─ servers.dat
│
├─ servers/
│  └─ <server-id>/
│     ├─ server.json
│     ├─ uptime.json
│     ├─ icon.png
│     └─ data/
│
├─ skins/
│  ├─ library.json
│  └─ <skin-id>.png
│
├─ capes/
│  ├─ library.json
│  ├─ <cape-id>.png
│  └─ <cape-id>.edit.json
│
├─ game/
│  ├─ versions/
│  ├─ libraries/
│  └─ assets/
│
├─ java/
├─ cache/
│  ├─ api/
│  ├─ downloads/
│  ├─ heads/
│  └─ server-icons/
├─ updates/
├─ logs/
└─ tmp/
```
