# VibanOS

Ein Betriebssystem in C#, gebaut mit [COSMOS](https://www.gocosmos.org/) (C# Open Source Managed Operating System).

## Voraussetzungen (Windows)

1. **Visual Studio 2022** mit dem Workload **".NET-Desktopentwicklung"**
2. **.NET 6 SDK** (das Kernel-Projekt nutzt `net6.0`)
3. **COSMOS User Kit** – neueste Version von https://github.com/CosmosOS/Cosmos/releases installieren.
   Das Kit bringt die NuGet-Pakete (`Cosmos.Build`, `Cosmos.System2`, …) und die Visual-Studio-Integration mit.
4. Eine VM zum Testen: **VMware Workstation Player** (Standard) oder **QEMU**

## Bauen und starten

1. `VibanOS.sln` in Visual Studio öffnen.
2. **F5** drücken. COSMOS kompiliert den Kernel, erzeugt eine bootfähige ISO
   (`VibanOS/bin/Debug/net6.0/VibanOS.iso`) und startet sie in VMware.

### Mit QEMU statt VMware

ISO wie oben bauen (oder `dotnet build`), dann:

```sh
qemu-system-x86_64 -cdrom VibanOS/bin/Debug/net6.0/VibanOS.iso -m 512M
```

Um F5 dauerhaft auf ein anderes Ziel zu stellen, in `VibanOS/VibanOS.csproj` die Werte `<Launch>` und `<Profile>` ändern (z. B. `ISO`, `Bochs`, `HyperV`).

## Projektstruktur

```
VibanOS.sln
VibanOS/
  VibanOS.csproj   COSMOS-Kernelprojekt
  Kernel.cs        Einstiegspunkt: BeforeRun() einmal beim Boot, Run() in Endlosschleife
```

`Kernel.cs` enthält eine kleine Shell mit den Befehlen `help`, `echo`, `clear`, `time`, `reboot`, `shutdown`.
Neue Befehle einfach als weiteren `case` im `switch` ergänzen.

## Branch-Workflow

1. Neue Branches immer von `development` abzweigen:
   ```sh
   git checkout development
   git pull
   git checkout -b feature/mein-feature
   ```
2. Der Branchname muss mit einem dieser Präfixe beginnen, sonst greift das Ruleset nicht:
   - `feature/` für neue Funktionen
   - `fix/` für Fehlerbehebungen
   - `rework/` für Umbauten und Refactorings
3. Änderungen pushen und einen Pull Request gegen `development` öffnen.
4. Der PR wird von @TheScoutSky reviewed und erst nach dessen Freigabe gemergt. Direkt auf `development` oder `main` wird nicht gepusht.

## Weiterführend

- Doku: https://cosmosos.github.io/
- Dateisystem (`Sys.FileSystem.CosmosVFS`), Grafik (`Cosmos.System.Graphics`) und Netzwerk sind in `Cosmos.System2` enthalten.
