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

## Issues erstellen

Jede Aufgabe und jeder Fehler bekommt zuerst ein Issue. So bleibt nachvollziehbar, was wann und warum geändert wurde.

1. Auf GitHub unter **Issues → New issue** ein neues Issue anlegen.
2. Einen kurzen, eindeutigen Titel wählen, z. B. `Shell stürzt bei leerer Eingabe ab`.
3. Im Text beschreiben:
   - **Bei Fehlern:** Was wurde gemacht (Schritte zum Nachstellen)? Was sollte passieren? Was ist stattdessen passiert? Fehlermeldung oder Screenshot, VM (VMware/QEMU) und Commit/Branch.
   - **Bei neuen Funktionen:** Was soll das Feature können und warum wird es gebraucht?
4. Ein passendes Label setzen (`bug`, `enhancement`, `documentation`) und sich selbst oder jemand anderen zuweisen.

### Vom Issue zum Pull Request

1. Branch passend zum Issue von `development` erstellen, mit Issue-Nummer im Namen:
   - Fehler → `fix/12-leere-eingabe`
   - Feature → `feature/15-dateisystem`
   - Umbau → `rework/20-shell-aufraeumen`
2. Im PR-Text das Issue verlinken, z. B. `Closes #12`. Dann wird das Issue beim Merge automatisch geschlossen.

### Fehler, die erst nach dem Merge auffallen

Ein bereits gemergter PR wird nicht wieder geöffnet oder weiterverwendet.

1. Neues Issue anlegen und darin den PR verlinken, der den Fehler eingeführt hat (z. B. `Entstanden durch #8`).
2. Einen neuen `fix/`-Branch vom aktuellen `development` erstellen, den Fehler beheben und einen neuen PR mit `Closes #<Issue>` öffnen.
3. Ist der Fehler schwerwiegend und `development` dadurch unbenutzbar, kann der ursprüngliche PR auf GitHub über **Revert** zurückgenommen werden. Auch dieser Revert läuft als PR mit Review.
4. Ist der Fehler schon auf `main`, wird der Fix zuerst nach `development` gemergt und danach wie gewohnt von `development` nach `main` übernommen.

## Weiterführend

- Doku: https://cosmosos.github.io/
- Dateisystem (`Sys.FileSystem.CosmosVFS`), Grafik (`Cosmos.System.Graphics`) und Netzwerk sind in `Cosmos.System2` enthalten.
