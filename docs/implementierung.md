---
layout: default
title: Implementierung
---

# Implementierung

So baust du ein neues Feature in VibanOS ein, vom Klonen bis zum Merge. Mehr Details stehen im [README](https://github.com/DHGE2025/VibanOS#readme).

Arbeitest du mit Claude oder ChatGPT? Dann lies zuerst den Abschnitt [Mit Claude oder ChatGPT arbeiten](#mit-claude-oder-chatgpt-arbeiten) unten.

## 1. Einmalig: Werkzeuge installieren

Du brauchst **.NET SDK 10**, **COSMOS 3.0.99** und **git**. Auf dem Mac zusätzlich QEMU aus Homebrew:

```sh
dotnet tool install -g Cosmos.Tools
cosmos install
cosmos update --version 3.0.99
brew install qemu        # nur Mac
```

Unter Windows installierst du COSMOS mit `CosmosSetup-<version>-windows.exe` von den [COSMOS-Releases](https://github.com/CosmosOS/Cosmos/releases). `cosmos check` zeigt, ob alles da ist.

## 2. Repo klonen

```sh
git clone https://github.com/DHGE2025/VibanOS.git
cd VibanOS
git checkout development
```

Wichtig: Der Pfad zum Klon darf **keine Leerzeichen** enthalten, sonst scheitert `cosmos build`.

## 3. Werkstatt starten

Die Werkstatt ist eine kleine Web-App auf deinem Rechner. Sie holt Branches von GitHub, baut sie und startet sie in QEMU.

- **Mac:** `tools/Werkstatt/start.command` im Finder doppelklicken
- **Windows:** `tools\Werkstatt\start.cmd` doppelklicken
- **Terminal:** `dotnet run --project tools/Werkstatt`

Der Browser öffnet sich mit http://127.0.0.1:5099. Dort siehst du alle Branches. Mit **Holen, bauen und starten** probierst du jeden Branch aus, auch die der anderen. Das Terminalfenster der Werkstatt bleibt offen, solange du sie brauchst.

## 4. Aufgabe aus Linear nehmen

1. In Linear im Team **VIB** eine Aufgabe nehmen, die dir zugewiesen ist.
2. **Nur Aufgaben, die nicht geblockt sind.** Steht in der Aufgabe „Blocked by“ mit einer anderen Aufgabe, die noch nicht fertig ist, lass sie liegen und nimm eine andere. Sie baut auf etwas auf, das es noch nicht gibt.
3. Auf **In Progress** setzen.
4. Unter „Workflow“ in der Aufgabe stehen Branchname und PR-Titel. Nimm genau diese, nicht „Copy git branch name“ von Linear.

## 5. Branch von development anlegen

```sh
git checkout development
git pull
git checkout -b feature/VIB-23-kurzer-name
```

Der Name beginnt mit `feature/` (neue Funktion), `fix/` (Fehler) oder `rework/` (Umbau), danach kommt die VIB-Kennung. Direkt auf `development` oder `main` pushst du nie.

## 6. Programmieren

- Für alles Sichtbare gilt das [Designsheet](design/designsheet.html): keine rohen Farben in `VibanOS/Gui/`, nur Tokens aus `VibanOS/Design/Tokens.cs`. Fehlt ein Wert, legst du erst ein Token an.
- Nur `Kernel.cs` und `Platform/Hardware.cs` sprechen COSMOS direkt an.
- Ein neuer Shell-Befehl ist eine Klasse in `VibanOS/Shell/Commands/` plus eine Zeile in `Shell/CommandList.cs`.
- Wo was hingehört, steht in der [Architektur](architektur.html).

## 7. Bauen und testen

In der Werkstatt unter **Dein Klon**:

1. **Selbsttest** prüft Kern und Oberfläche ohne VM und macht Bildschirmfotos. Das geht schnell.
2. **Bauen und starten** übersetzt mit COSMOS und startet VibanOS in QEMU. Probier dein Feature dort aus.
3. Zum Beenden im System `shutdown` tippen, dann ist die Festplatte sauber.

Ohne Werkstatt geht dasselbe im Terminal: `dotnet run --project tools/Preview` für den Selbsttest, im Ordner `VibanOS/` dann `cosmos build` und `cosmos run --disk disk.img`.

## 8. Pull Request öffnen

```sh
git add .
git commit -m "VIB-23: kurze Beschreibung"
git push -u origin feature/VIB-23-kurzer-name
```

Auf GitHub einen Pull Request **gegen `development`** öffnen (nicht gegen `main`), mit dem Titel aus der Linear-Aufgabe. Schreib kurz rein, was du geändert und wie du es getestet hast. Hast du etwas am Aussehen geändert, häng ein Bildschirmfoto an. Linear verknüpft den PR über die Kennung von selbst.

## 9. Auf Antonios Review warten

Antonio ([TheScoutSky](https://github.com/TheScoutSky)) prüft jeden PR. Wünscht er Änderungen, machst du sie auf demselben Branch und pushst noch einmal, der PR aktualisiert sich von selbst. Nach seiner Freigabe merged er, und die Aufgabe in Linear springt auf „Done“.

Fällt nach dem Merge ein Fehler auf, gibt es dafür eine neue Aufgabe und einen neuen `fix/`-Branch. Ein gemergter PR wird nicht wieder geöffnet.

## Mit Claude oder ChatGPT arbeiten

Im Repo liegt [`AGENTS.md`](https://github.com/DHGE2025/VibanOS/blob/development/AGENTS.md) mit den Regeln für jede KI: Branchnamen, Designsheet, Code-Regeln für COSMOS, Prüfen vor dem PR. So bekommt die KI sie mit:

| Werkzeug | Was du tust |
| -- | -- |
| **Claude Code** (Terminal, App, Web) | Nichts. Es liest `CLAUDE.md`, und die verweist auf `AGENTS.md`. |
| **Codex** (ChatGPT) | Nichts. Codex liest `AGENTS.md` von selbst. |
| **Claude oder ChatGPT im Browser** | Lege ein Projekt an und kopiere den Inhalt von `AGENTS.md` in die Projektanweisungen. Oder häng die Datei am Anfang jedes Chats an. |
| **Cursor, Copilot und andere** | Die meisten lesen `AGENTS.md` auch. Sonst wie im Browser die Regeln mitgeben. |

### So klappt es gut

1. **Branch selbst anlegen.** Leg den Branch wie in Schritt 5 an, bevor die KI loslegt. Dann landet nichts auf `development`. Ausnahme: Claude in der Cloud (Claude Code im Web) darf nur auf seinen eigenen `claude/...`-Branch pushen. Das ist erlaubt, dann gehört die VIB-Kennung vorn in den PR-Titel.
2. **Die Aufgabe ganz mitgeben.** Kopiere Titel und Text der Linear-Aufgabe in den Chat, mit Kennung. Zum Beispiel:
   > Aufgabe VIB-23: Papierkorb im Dock. *(Text aus Linear)* Halte dich an AGENTS.md und das Designsheet. Erklär mir erst kurz deinen Plan, dann bau es.
3. **Erst den Plan, dann den Code.** Lass dir sagen, welche Dateien sie ändern will. Passt das nicht zur [Architektur](architektur.html), korrigier sie früh.
4. **Kleine Schritte.** Ein Feature pro Chat. Nach jedem Schritt bauen und testen (Schritt 7). Fehlermeldungen aus `cosmos build` oder der seriellen Ausgabe kopierst du einfach in den Chat.
5. **Selbst prüfen.** Schau dir den Code an, bevor du committest. Achte vor allem auf rohe Farbwerte in `VibanOS/Gui/`, COSMOS-Aufrufe außerhalb von `Kernel.cs` und `Hardware.cs` und Code aus alten COSMOS-Beispielen (`Cosmos.System2`).
6. **Ehrlich im PR.** Schreib rein, dass du mit KI gearbeitet hast und wie du getestet hast. Die KI merged nie selbst, das macht Antonio nach dem Review.
