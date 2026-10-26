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
   git checkout -b feature/VIB-2-dateisystem
   ```
2. Der Branchname muss mit einem dieser Präfixe beginnen, sonst greift das Ruleset nicht:
   - `feature/` für neue Funktionen
   - `fix/` für Fehlerbehebungen
   - `rework/` für Umbauten und Refactorings
   Nach dem Präfix folgt die Kennung der Aufgabe aus Linear (z. B. `VIB-2`), siehe unten.
3. Änderungen pushen und einen Pull Request gegen `development` öffnen.
4. Der PR wird von @TheScoutSky reviewed und erst nach dessen Freigabe gemergt. Direkt auf `development` oder `main` wird nicht gepusht.

## Aufgaben und Fehler in Linear

Alle Aufgaben und Fehler werden im Linear-Projekt [VibanOS](https://linear.app/cxdex/project/vibanos-b87d853f1795) verwaltet, nicht als GitHub-Issues. Jede Aufgabe hat eine Kennung wie `VIB-2`.

### Aufgabe anlegen

1. In Linear im Team **VibanOS** und Projekt VibanOS mit **C** (oder „New issue“) eine neue Aufgabe anlegen.
2. Kurzen, eindeutigen Titel wählen, z. B. `Shell stürzt bei leerer Eingabe ab`.
3. Im Text beschreiben:
   - **Bei Fehlern:** Was wurde gemacht (Schritte zum Nachstellen)? Was sollte passieren? Was ist stattdessen passiert? Fehlermeldung oder Screenshot, VM (VMware/QEMU) und Branch.
   - **Bei neuen Funktionen:** Was soll das Feature können und warum wird es gebraucht?
4. Label setzen: das Thema (z. B. `Speicherverwaltung`) und bei Fehlern zusätzlich `Bug`. Projekt `VibanOS` auswählen und die Aufgabe jemandem zuweisen.

### Von der Aufgabe zum Pull Request

1. Aufgabe auf „In Progress“ setzen.
2. Branch von `development` erstellen, mit Präfix und Kennung im Namen:
   - Fehler → `fix/VIB-23-leere-eingabe`
   - Feature → `feature/VIB-2-dateisystem`
   - Umbau → `rework/VIB-1-shell-befehle`

   Der genaue Branchname und PR-Titel steht in jeder Aufgabe unter „Workflow“. Den von Linear über „Copy git branch name“ vorgeschlagenen Namen nicht verwenden, er hat nicht das nötige Präfix.
3. PR gegen `development` öffnen. Linear erkennt die Kennung im Branchnamen und verknüpft Branch und PR automatisch mit der Aufgabe. Nach dem Merge wird die Aufgabe automatisch auf „Done“ gesetzt.

### Fehler, die erst nach dem Merge auffallen

Ein bereits gemergter PR wird nicht wieder geöffnet oder weiterverwendet.

1. Neue Aufgabe mit Label `Bug` in Linear anlegen und darin den PR verlinken, der den Fehler eingeführt hat. Falls bekannt, die ursprüngliche Aufgabe unter „Related“ verknüpfen.
2. Einen neuen `fix/`-Branch mit der neuen Kennung vom aktuellen `development` erstellen, den Fehler beheben und einen neuen PR öffnen.
3. Ist der Fehler schwerwiegend und `development` dadurch unbenutzbar, kann der ursprüngliche PR auf GitHub über **Revert** zurückgenommen werden. Auch dieser Revert läuft als PR mit Review.
4. Ist der Fehler schon auf `main`, wird der Fix zuerst nach `development` gemergt und danach wie gewohnt von `development` nach `main` übernommen.

## Linear mit GitHub verbinden (einmalig)

1. **GitHub-Integration:** In Linear unter *Settings → Integrations → GitHub* verbinden, die Linear-App für die Organisation `DHGE2025` installieren und das Repo `VibanOS` auswählen. Dafür braucht man Owner-Rechte in der GitHub-Organisation.
2. **Persönliches Konto:** Jedes Teammitglied verbindet in Linear zusätzlich sein eigenes GitHub-Konto, damit PRs und Reviews der richtigen Person zugeordnet werden.
3. **Status-Automatik:** Im Team VibanOS unter *Settings → Teams → VibanOS → Workflow* die Git-Automationen einstellen, z. B. PR geöffnet → „In Review“, PR gemergt → „Done“.

## Weiterführend

- Doku: https://cosmosos.github.io/
- Dateisystem (`Sys.FileSystem.CosmosVFS`), Grafik (`Cosmos.System.Graphics`) und Netzwerk sind in `Cosmos.System2` enthalten.
