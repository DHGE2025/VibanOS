# Regeln für KI-Assistenten in VibanOS

Diese Datei gilt für jede KI, die an VibanOS mitarbeitet (Claude, ChatGPT/Codex, Copilot, Cursor und andere). Claude Code liest sie über `CLAUDE.md`, Codex liest sie direkt. Wer im Browser mit Claude oder ChatGPT arbeitet, gibt sie dem Chat am Anfang mit.

VibanOS ist ein Betriebssystem in C# auf COSMOS 3.0.99 (.NET 10, NativeAOT). Antworte auf Deutsch.

## Vor dem Programmieren

1. Lies die Aufgabe aus Linear (Team VIB, Kennung wie `VIB-23`). Bau nur, was dort steht. Ist sie von einer anderen Aufgabe geblockt („Blocked by“), die noch nicht fertig ist, fang nicht an und sag es dem Menschen.
2. Lies [`docs/architektur.md`](docs/architektur.md). Dort steht, wo was liegt und wie man Befehle, Prozesse, Fenster und Treiber hinzufügt.
3. Ändert die Aufgabe etwas Sichtbares, lies vorher das [Designsheet](docs/design/designsheet.md).

## Branches und Pull Requests

- Neue Branches immer von `development` abzweigen, nie direkt auf `development` oder `main` committen.
- Branchname: Präfix + Kennung + kurzer Name, klein und mit Bindestrichen.
  - `feature/VIB-23-kurzer-name` für neue Funktionen
  - `fix/VIB-23-kurzer-name` für Fehler
  - `rework/VIB-23-kurzer-name` für Umbauten
- Steht in der Linear-Aufgabe unter „Workflow“ ein Branchname oder PR-Titel, nimm genau diesen. Nicht den Namen aus „Copy git branch name“ von Linear, dem fehlt das Präfix.
- **Ausnahme Cloud-Sitzungen:** Läuft die KI in einer Cloud-Sitzung (etwa Claude Code im Web), die nur auf einen vorgegebenen Branch wie `claude/...` pushen darf, nimmt sie diesen Branch. Die VIB-Kennung steht dann vorn im PR-Titel (`VIB-23: Papierkorb im Dock`), damit Linear den PR zuordnet. Alle anderen Regeln gelten weiter.
- Commit-Nachrichten auf Deutsch, beginnend mit der Kennung: `VIB-23: Papierkorb im Dock`.
- Pull Request immer gegen `development`. Antonio (@TheScoutSky) reviewt und merged. Nie selbst mergen.

## Designsheet (verbindlich)

- Alle Farben, Abstände, Radien, Maße und Schriften kommen aus `VibanOS/Design/Tokens.cs` (`using T = VibanOS.Design.Tokens;`).
- In `VibanOS/Gui/` stehen **keine rohen Farbwerte** und keine erfundenen Abstände. Fehlt ein Wert, erst ein neues Token in `Tokens.cs` anlegen und im Designsheet eintragen, dann benutzen.
- Stil: dunkel, ruhig, an macOS angelehnt. Blau (`Accent`) nur für das, womit man gerade arbeitet. Rot, Gelb, Grün nur für Fehler, Warnung, Erfolg.
- Abstände im 4-px-Raster (`T.Space.…`).
- Nach Änderungen am Aussehen die Bilder in `docs/design/` aus `tools/Preview` erneuern.

## Code-Regeln für COSMOS

- COSMOS-Klassen nur in `VibanOS/Kernel.cs` und `VibanOS/Platform/Hardware.cs`. Alles andere ist normales C#. Neue Hardware-Funktion = Methode in `Hardware.cs` plus Attrappe in `tools/Preview/Program.cs`.
- Keine Reflection. Neue Shell-Befehle von Hand in `VibanOS/Shell/CommandList.cs` eintragen.
- Keine Kultur- und Ländereinstellungen. Datum und Zahlen über `Core/Text.cs` formatieren.
- Shell-Ausgaben über `shell.WriteLine` und `shell.Error`, nie `Console`. Befehle warten nicht auf Eingaben, Rückfragen über `shell.Ask(...)`.
- Keine neuen NuGet-Pakete ohne Rücksprache.
- Beispiele aus dem Netz zu `Cosmos.System2` oder IL2CPU gelten für die alte COSMOS-Generation und passen nicht. Im Zweifel https://cosmosos.github.io/ prüfen.

## Prüfen vor dem Pull Request

1. `dotnet run --project tools/Preview` muss ohne Fehler durchlaufen. Wer ein Teilsystem erweitert, ergänzt dort eine Prüfung.
2. Im Ordner `VibanOS/`: `cosmos build` und `cosmos run --disk disk.img`, oder in der Werkstatt (`tools/Werkstatt`) „Bauen und starten“. Das Feature in der VM ausprobieren.
3. Im PR kurz schreiben, was geändert wurde und wie es getestet ist, bei sichtbaren Änderungen mit Bildschirmfoto.

## Was die KI nicht tut

- Nicht auf `development` oder `main` pushen, nicht mergen, keine Branch-Regeln umgehen.
- Keine Dateien außerhalb der Aufgabe umbauen oder umformatieren.
- Nichts behaupten, was nicht gebaut oder getestet wurde. Lief `cosmos build` nicht, das offen sagen.
