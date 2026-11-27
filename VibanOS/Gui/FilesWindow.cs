using System;
using System.Collections.Generic;
using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Platform;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>
    /// Dateien: blättert durch die Festplatte wie der Finder. Links die Orte, oben Zurück, Ordnername und
    /// "Neuer Ordner", darunter die Liste. Ein Klick wählt aus, ein zweiter Klick auf dieselbe Zeile öffnet sie:
    /// Ordner zeigen ihren Inhalt, Dateien ihren Text. Pfeiltasten wählen, Eingabe öffnet, Rücktaste geht zurück.
    /// Die gewählte Zeile lässt sich umbenennen (Knopf oder F2, Eingabefeld direkt in der Zeile) und löschen
    /// (Knopf oder Entf, mit Rückfrage). Alle Zugriffe laufen über Vfs.CanAccess, wie im Terminal, und was mv und rm
    /// nicht anfassen (Vfs.IsFixed), fasst auch Dateien nicht an.
    /// </summary>
    public class FilesWindow : Window
    {
        // Klickflächen, wie in den Einstellungen relativ zum Inhalt gemerkt.
        internal const int PlaceHome = 0, PlaceDisk = 1, Back = 10, NewFolder = 11, Rename = 12, Delete = 13,
            ConfirmDelete = 14, Cancel = 15, NameField = 16, ConfirmRename = 17, Item = 100;

        private const int IconSize = 16;       // Symbol vor einem Eintrag
        private const int SizeColumn = 190;    // Abstand der Spalte "Größe" vom rechten Rand
        private const int KindColumn = 100;    // Abstand der Spalte "Art" vom rechten Rand
        private const int ButtonWidth = 110;   // Knopf "Neuer Ordner"
        private const int PreviewBytes = 8192; // so viel einer Datei zeigt die Vorschau
        private const int SheetWidth = 340;    // Warnung vor dem Ändern einer Systemdatei, höchstens so breit

        private struct Hot { public int X, Y, W, H, Action; }

        private struct Entry { public string Name; public bool Folder; public long Size; }

        private readonly List<Hot> hot = new List<Hot>();
        private int originX, originY;
        private readonly List<Entry> entries = new List<Entry>();
        private string folder = "";
        private string file;       // geöffnete Datei oder null
        private readonly List<string> lines = new List<string>();
        // Liste und Vorschau blättern getrennt: Sonst stünde die Liste nach dem Schließen einer Datei woanders.
        private int selected = -1, scroll, visibleRows = 1; // Liste: gewählte Zeile, erste gezeigte, so viele passen
        private int top, previewRows = 1;                   // Vorschau: erste gezeigte Zeile, so viele passen
        private bool cut;                                   // Vorschau: Die Datei geht nach dem gezeigten Anfang weiter
        private string message = "";
        private string space = "";  // freier Platz für die Statuszeile, siehe ReadSpace
        private string editName;    // Umbenennen: neuer Name im Feld der gewählten Zeile, sonst null
        private int caret;          // Umbenennen: Stelle der Einfügemarke im Namen, 0 ist vor dem ersten Zeichen
        private int fieldStart;     // Umbenennen: erstes Zeichen, das im Feld zu sehen ist, wenn der Name nicht ganz passt
        private int pending;        // Rückfrage: ConfirmDelete oder ConfirmRename, 0 ohne
        private string warning;     // Rückfrage bei einer Systemdatei (Vfs.SystemFile): was ohne sie passiert, sonst null

        public FilesWindow()
        {
            Title = "Dateien";
            Width = T.Size.Sidebar + 520;
            Height = T.Size.TitleBar + 400;
            Sync();
        }

        // Für wen das Fenster gerade etwas zeigt. Fenster überleben die Abmeldung, ihr Inhalt darf das nicht:
        // Der nächste Nutzer sähe sonst den Ordner oder die Datei, die sein Vorgänger offen hatte.
        private VibanOS.Users.User owner;
        private bool hadDisk;

        private void Sync()
        {
            if (owner == OS.Users.Current && hadDisk == Vfs.Available) return;
            owner = OS.Users.Current;
            hadDisk = Vfs.Available; // nach 'format' gibt es plötzlich eine Platte
            entries.Clear();
            lines.Clear();
            folder = "";
            file = null;
            message = space = "";
            editName = null;
            pending = 0;
            selected = -1;
            scroll = 0;
            Go(Home);
        }

        public override bool CanZoom { get { return true; } }

        // Neben der Seitenleiste bleibt Platz für Name, Größe und Art.
        public override int MinWidth { get { return T.Size.Sidebar + 2 * SizeColumn; } }

        private static string Home { get { return OS.Users.Current != null ? OS.Users.Current.HomeDirectory : Vfs.Root; } }

        // Die Wurzel ist der einzige Ordner, dessen Pfad mit dem Trenner endet (so liefert Resolve sie).
        private static bool IsRoot(string path) { return path == Vfs.Root; }

        /// <summary>Für den Selbsttest: der gezeigte Ordner, die geöffnete Datei und die Meldung unten.</summary>
        internal string Folder { get { return folder; } }
        internal string OpenFile { get { return file; } }
        internal string Message { get { return message; } }
        internal int Count { get { return entries.Count; } }
        internal List<string> Lines { get { return lines; } }
        internal int Top { get { return top; } }
        internal bool Editing { get { return editName != null; } }
        internal string EditName { get { return editName; } }
        internal int Selected { get { return selected; } }
        internal bool Confirming { get { return pending != 0; } }
        internal int Caret { get { return caret; } }
        internal string Warning { get { return pending != 0 ? warning : null; } }

        /// <summary>Text der Statusleiste: was das Fenster gerade zeigt.</summary>
        internal string Info
        {
            get
            {
                if (file == null) return entries.Count + (entries.Count == 1 ? " Objekt" : " Objekte");
                // Von einer großen Datei kennt die Vorschau nur den Anfang, also auch nicht die Zahl ihrer Zeilen.
                if (cut) return "Die ersten " + Text.Bytes(PreviewBytes) + " der Datei";
                return lines.Count + (lines.Count == 1 ? " Zeile" : " Zeilen");
            }
        }

        /// <summary>Zeigt einen Ordner. Ohne Recht oder wenn es ihn nicht gibt, bleibt alles, wie es war.</summary>
        private void Go(string path)
        {
            message = "";
            if (!Vfs.Available) { entries.Clear(); folder = ""; return; }
            if (!Vfs.CanAccess(OS.Users.Current, path, Access.Read)) { message = "Zugriff verweigert: " + path; return; }
            if (!Directory.Exists(path)) { message = "Ordner nicht gefunden: " + path; return; }
            folder = path;
            file = null;
            editName = null;
            pending = 0;
            selected = -1;
            scroll = 0;
            Reload();
            ReadSpace();
        }

        /// <summary>Liest den Ordner neu, etwa nachdem im Terminal etwas angelegt wurde. Die Auswahl bleibt am Namen.</summary>
        private void Reload()
        {
            if (!Vfs.Available || folder == "" || file != null || Gone()) return;
            string keep = selected >= 0 && selected < entries.Count ? entries[selected].Name : null;
            entries.Clear();
            try
            {
                string[] dirs = Directory.GetDirectories(folder), files = Directory.GetFiles(folder);
                Add(dirs, true);
                Add(files, false);
            }
            catch (Exception e) { message = "Ordner lässt sich nicht lesen: " + e.Message; }
            selected = -1;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Name == keep) selected = i;
        }

        // Der gezeigte Ordner wurde inzwischen im Terminal verschoben oder gelöscht: nach Hause gehen und es sagen.
        // Sonst legte "Neuer Ordner" den alten Pfad stillschweigend wieder an.
        private bool Gone()
        {
            if (Directory.Exists(folder)) return false;
            string gone = folder;
            entries.Clear();
            folder = "";
            Go(Home);
            message = "Ordner nicht gefunden: " + gone;
            return true;
        }

        // Den freien Platz beim Wechsel des Ordners holen und merken, nicht bei jedem Zeichnen oder Klick:
        // Der FAT-Treiber von COSMOS liest dafür jedes Mal die ganze Belegungstabelle von der Platte.
        private void ReadSpace()
        {
            ulong total, free;
            space = Hardware.DiskSpace(out total, out free) ? ", " + Text.Bytes(free) + " verfügbar" : "";
        }

        private void Add(string[] paths, bool isFolder)
        {
            string[] names = new string[paths.Length];
            for (int i = 0; i < paths.Length; i++) names[i] = Vfs.NameOf(paths[i]);
            Array.Sort(names, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < names.Length; i++)
            {
                long size = -1;
                if (!isFolder)
                {
                    // ponytail: je Datei eine eigene Anfrage an das Dateisystem, und der FAT-Treiber von COSMOS liest dafür
                    // jedes Mal alle Ordner auf dem Pfad neu (200 Dateien: rund 1400 Lesezugriffe). Wird das bei großen
                    // Ordnern zäh, die Größe nur für die sichtbaren Zeilen holen.
                    try { size = new FileInfo(Vfs.Combine(folder, names[i])).Length; }
                    catch (Exception) { }
                }
                entries.Add(new Entry { Name = names[i], Folder = isFolder, Size = size });
            }
        }

        private string PathOf(int index) { return Vfs.Combine(folder, entries[index].Name); }

        private void Open(int index)
        {
            if (index < 0 || index >= entries.Count) return;
            string path = PathOf(index);
            if (entries[index].Folder) Go(path);
            else if (!Vfs.CanAccess(OS.Users.Current, path, Access.Read)) message = "Zugriff verweigert: " + path;
            else
            {
                lines.Clear();
                try
                {
                    bool more;
                    string[] split = Vfs.ReadStart(path, PreviewBytes, out more).Split('\n');
                    // Ein Zeilenende am Schluss beginnt keine weitere Zeile, und eine leere Datei hat keine.
                    int count = split[split.Length - 1] == "" ? split.Length - 1 : split.Length;
                    for (int i = 0; i < count; i++) lines.Add(split[i].Replace("\r", "").Replace("\t", "    "));
                    if (more) lines.Add("...");
                    cut = more;
                    file = path;
                    top = 0;
                    message = "";
                }
                catch (Exception e) { lines.Clear(); message = "Datei lässt sich nicht lesen: " + e.Message; }
            }
            // Ging es nicht, stimmt die Liste womöglich nicht mehr, etwa weil der Eintrag im Terminal gelöscht wurde.
            if (message != "") Reload();
        }

        private void GoBack()
        {
            message = "";
            if (file != null) { file = null; lines.Clear(); Reload(); return; }
            if (folder == "" || IsRoot(folder)) return;
            string from = Vfs.NameOf(folder);
            Go(Vfs.Resolve(folder, ".."));
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Folder && entries[i].Name == from) selected = i; // dort weitermachen, wo man herkam
        }

        private void MakeFolder()
        {
            message = "";
            if (folder == "" || file != null || Gone()) return;
            string name = "Neuer Ordner", path = Vfs.Combine(folder, name);
            for (int n = 2; Directory.Exists(path) || File.Exists(path); n++) path = Vfs.Combine(folder, name + " " + n);
            if (!Vfs.CanAccess(OS.Users.Current, path, Access.Write)) { message = "Zugriff verweigert: " + folder; return; }
            try { Directory.CreateDirectory(path); }
            catch (Exception e) { message = "Ordner ließ sich nicht anlegen: " + e.Message; return; }
            Reload();
            ReadSpace();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Name == Vfs.NameOf(path)) selected = i;
        }

        // Was mv und rm vor dem Umbenennen und Löschen prüfen. null: Die gewählte Zeile darf geändert werden.
        private string Refusal()
        {
            if (folder == "" || file != null || selected < 0 || selected >= entries.Count) return "";
            string path = PathOf(selected);
            if (!Vfs.CanAccess(OS.Users.Current, path, Access.Write)) return "Zugriff verweigert: " + path;
            // Ohne die Nutzerdatei begänne der nächste Start mit der Ersteinrichtung, ohne Home-Ordner fände niemand seine Dateien.
            if (Vfs.IsFixed(path)) return "Das braucht das System an seinem Platz: " + entries[selected].Name;
            // Sonst stünde das Terminal danach in einem Ordner, den es nicht mehr gibt.
            if (entries[selected].Folder && Text.HasPrefix(OS.Shell.CurrentDirectory.ToLowerInvariant() + Vfs.Separator, path.ToLowerInvariant() + Vfs.Separator))
                return "Darin steht gerade das Terminal. Erst dort mit 'cd' hinausgehen.";
            return null;
        }

        /// <summary>
        /// Öffnet das Eingabefeld in der gewählten Zeile, mit dem bisherigen Namen darin und der Einfügemarke am Ende.
        /// Bei einer Systemdatei kommt vorher eine Warnung, die man bestätigen muss. warned: Sie ist schon bestätigt.
        /// </summary>
        private void StartRename(bool warned)
        {
            pending = 0;
            string refusal = Refusal();
            if (refusal != null) { message = refusal; return; }
            message = "";
            warning = Vfs.SystemFile(PathOf(selected));
            if (warning != null && !warned) { pending = ConfirmRename; return; }
            editName = entries[selected].Name;
            caret = editName.Length;
            fieldStart = 0;
        }

        /// <summary>
        /// Übernimmt den Namen aus dem Feld. Dieselben Regeln wie bei mv: kein ~, Schreibweise allein zählt auf FAT nicht,
        /// nichts überschreiben. false: Es ging nicht, das Feld bleibt offen und die Statuszeile sagt warum.
        /// </summary>
        private bool FinishRename()
        {
            if (editName == null) return true;
            string refusal = Refusal();
            if (refusal != null) { editName = null; message = refusal; Reload(); return false; }
            string old = entries[selected].Name;
            // Wie Vfs.Resolve: FAT kennt keine Namen, die auf Punkt oder Leerzeichen enden.
            string name = editName.Trim().TrimEnd('.', ' ');
            string problem = name == "" ? "Der Name darf nicht leer sein."
                : name.IndexOfAny(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }) >= 0 ? "Diese Zeichen gehen in Namen nicht: / \\ : * ? \" < > |"
                // FAT gibt jedem langen Namen einen kurzen mit ~ (SYSTEM~1). Ein solcher Name verwechselte zwei Dateien.
                : name.IndexOf('~') >= 0 ? "Namen mit ~ gehen nicht, FAT verwechselt sie mit Kurznamen."
                : null;
            if (problem != null) { message = problem; return false; }
            if (name == old) { editName = null; message = ""; return true; }
            if (string.Equals(name, old, StringComparison.OrdinalIgnoreCase))
            {
                message = "Auf der Platte ist das derselbe Name, Groß- und Kleinschreibung zählt dort nicht.";
                return false;
            }
            string from = PathOf(selected), to = Vfs.Combine(folder, name);
            if (!Vfs.CanAccess(OS.Users.Current, to, Access.Write)) { message = "Zugriff verweigert: " + to; return false; }
            if (File.Exists(to) || Directory.Exists(to)) { message = "Gibt es schon: " + name; return false; }
            try
            {
                if (entries[selected].Folder) Directory.Move(from, to);
                else File.Move(from, to);
            }
            catch (Exception e) { editName = null; message = "Umbenennen ging nicht: " + e.Message; Reload(); return false; }
            editName = null;
            message = "";
            Reload();
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Name == name) selected = i;
            return true;
        }

        /// <summary>Fragt in der Statuszeile nach, bevor gelöscht wird. Ordner nur, wenn sie leer sind, wie bei rm.</summary>
        private void AskDelete()
        {
            editName = null;
            string refusal = Refusal();
            if (refusal == null && entries[selected].Folder && !Empty(PathOf(selected))) refusal = "Ordner ist nicht leer: " + entries[selected].Name;
            if (refusal != null) { message = refusal; pending = 0; return; }
            message = "";
            // Eine Systemdatei bekommt statt der Frage in der Statuszeile eine Warnung mitten im Fenster.
            warning = Vfs.SystemFile(PathOf(selected));
            pending = ConfirmDelete;
        }

        private static bool Empty(string path)
        {
            try { return Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length == 0; }
            catch (Exception) { return false; }
        }

        private void DeleteSelected()
        {
            pending = 0;
            // Zwischen Frage und Antwort kann im Terminal etwas passiert sein: alles noch einmal prüfen.
            string refusal = Refusal();
            if (refusal != null) { message = refusal; Reload(); return; }
            string path = PathOf(selected);
            int at = selected;
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (!Directory.Exists(path)) message = "Nicht gefunden: " + path;
                else if (!Empty(path)) message = "Ordner ist nicht leer: " + entries[selected].Name;
                else Directory.Delete(path);
            }
            catch (Exception e) { message = "Löschen ging nicht: " + e.Message; }
            Reload();
            ReadSpace();
            // Weiter mit der Zeile, die jetzt an derselben Stelle steht, wie im Finder.
            if (message == "") selected = at < entries.Count ? at : entries.Count - 1;
        }

        public override void OnClick(int px, int py)
        {
            Sync();
            // Ein Klick neben das Feld übernimmt den Namen, wie im Finder. Geht das nicht, bleibt das Feld offen. Mehr tut der
            // Klick nicht: Die Liste ist danach neu sortiert, und die Klickflächen vom letzten Zeichnen stimmen nicht mehr.
            if (editName != null && !Hits(px, py, NameField))
            {
                FinishRename();
                OS.Changed = true;
                return;
            }
            // Während der Rückfrage zählt nur ihr Knopf zum Bestätigen, Abbrechen und jeder andere Klick brechen sie ab.
            // Hier und nicht in der Schleife unten: Die Warnung liegt über den Zeilen, und deren Klickflächen kämen zuerst.
            if (pending != 0)
            {
                if (Hits(px, py, pending)) Do(pending);
                else pending = 0;
                OS.Changed = true;
                return;
            }
            for (int i = 0; i < hot.Count; i++)
            {
                Hot h = hot[i];
                h.X += X;
                h.Y += Y + T.Size.TitleBar;
                if (px < h.X || py < h.Y || px >= h.X + h.W || py >= h.Y + h.H) continue;
                Do(h.Action);
                OS.Changed = true;
                return;
            }
            // In der Vorschau gibt es nichts abzuwählen: Die Auswahl der Liste bleibt für den Weg zurück.
            if (file != null) return;
            // Ein Klick ins Leere hebt die Auswahl auf, räumt eine alte Meldung weg und holt den neuesten Stand des Ordners.
            selected = -1;
            message = "";
            Reload();
            OS.Changed = true;
        }

        private bool Hits(int px, int py, int action)
        {
            for (int i = 0; i < hot.Count; i++)
            {
                Hot h = hot[i];
                if (h.Action != action) continue;
                int hx = X + h.X, hy = Y + T.Size.TitleBar + h.Y;
                if (px >= hx && py >= hy && px < hx + h.W && py < hy + h.H) return true;
            }
            return false;
        }

        private void Do(int action)
        {
            if (action == NameField) return; // ins Feld geklickt: weiter tippen
            if (action == PlaceHome) Go(Home);
            else if (action == PlaceDisk) Go(Vfs.Root);
            else if (action == Back) GoBack();
            else if (action == NewFolder) MakeFolder();
            else if (action == Rename) StartRename(false);
            else if (action == Delete) AskDelete();
            else if (action == ConfirmDelete) DeleteSelected();
            else if (action == ConfirmRename) StartRename(true);
            else if (action == Cancel) pending = 0;
            else if (action >= Item)
            {
                int index = action - Item;
                // Nur auswählen, nicht neu lesen: Auf FAT kostet das Lesen je Datei einen eigenen Suchlauf über die Platte.
                if (index == selected) Open(index);
                else { selected = index; message = ""; }
            }
        }

        public override void OnKey(Key key, char c)
        {
            Sync();
            if (editName != null)
            {
                // Wie in einem Textfeld unter macOS: Pfeil links und rechts gehen ein Zeichen weiter, hoch und runter an Anfang und Ende.
                if (key == Key.Enter) FinishRename();
                else if (key == Key.Escape) { editName = null; message = ""; }
                else if (key == Key.Left) { if (caret > 0) caret--; }
                else if (key == Key.Right) { if (caret < editName.Length) caret++; }
                else if (key == Key.Up) caret = 0;
                else if (key == Key.Down) caret = editName.Length;
                else if (key == Key.Backspace) { if (caret > 0) { editName = editName.Remove(caret - 1, 1); caret--; } }
                else if (key == Key.Delete) { if (caret < editName.Length) editName = editName.Remove(caret, 1); }
                else if (key == Key.None && c >= ' ' && c <= 255) { editName = editName.Insert(caret, c.ToString()); caret++; }
                return;
            }
            if (pending != 0)
            {
                if (key == Key.Enter) { if (pending == ConfirmDelete) DeleteSelected(); else StartRename(true); }
                else if (key == Key.Escape) pending = 0;
                return;
            }
            if (file != null)
            {
                // Die Grenzen zieht DrawPreview: Erst dort steht fest, wie viele Zeilen es umbrochen sind.
                int page = previewRows > 1 ? previewRows - 1 : 1;
                if (key == Key.Up) top--;
                else if (key == Key.Down) top++;
                else if (key == Key.PageUp) top -= page;
                else if (key == Key.PageDown) top += page;
                else if (key == Key.Backspace) GoBack();
                return;
            }
            int step = visibleRows > 1 ? visibleRows - 1 : 1; // Bild auf und ab: eine Seite, eine Zeile bleibt als Anschluss
            if (key == Key.Up) selected--;
            else if (key == Key.Down) selected++;
            else if (key == Key.PageUp) selected -= step;
            else if (key == Key.PageDown) selected += step;
            else if (key == Key.Enter) { Open(selected); return; }
            else if (key == Key.Backspace) { GoBack(); return; }
            else if (key == Key.F2) { if (selected >= 0) StartRename(false); return; }
            else if (key == Key.Delete) { if (selected >= 0) AskDelete(); return; }
            else return;
            if (selected > entries.Count - 1) selected = entries.Count - 1;
            if (selected < 0) selected = entries.Count > 0 ? 0 : -1;
        }

        /// <summary>Für den Selbsttest: Mitte der Klickfläche einer Aktion beim letzten Zeichnen, oder null.</summary>
        internal int[] CenterOf(int action)
        {
            for (int i = 0; i < hot.Count; i++)
                if (hot[i].Action == action) return new[] { X + hot[i].X + hot[i].W / 2, Y + T.Size.TitleBar + hot[i].Y + hot[i].H / 2 };
            return null;
        }

        /// <summary>Für den Selbsttest: Index des Eintrags mit diesem Namen, oder -1.</summary>
        internal int IndexOf(string name)
        {
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].Name == name) return i;
            return -1;
        }

        private void AddHot(int x, int y, int w, int h, int action)
        {
            hot.Add(new Hot { X = x - originX, Y = y - originY, W = w, H = h, Action = action });
        }

        protected override void DrawContent(Surface s, int x, int y, int w, int h)
        {
            Sync();
            hot.Clear();
            originX = x;
            originY = y;
            DrawSidebar(s, x, y, h);

            int left = x + T.Size.Sidebar, inner = w - T.Size.Sidebar;
            int barH = T.Size.Field + 2 * T.Space.S;
            DrawToolbar(s, left, y, inner, barH);
            int ty = y + barH;
            // Die Rückfrage braucht Platz für ihre Knöpfe, sonst ist die Statuszeile so hoch wie eine Zeile Text.
            bool asking = pending == ConfirmDelete && warning == null && file == null && Vfs.Available;
            int status = asking ? T.Size.Row + 2 * T.Space.XS : Fonts.Body.LineHeight + 2 * T.Space.S;
            int bottom = y + h - status;

            if (!Vfs.Available)
            {
                Empty(s, left, inner, ty, bottom, "Keine Festplatte eingebunden.", "Im Terminal einmal 'format' ausführen (siehe README).");
            }
            else if (file != null) DrawPreview(s, left, ty, inner, bottom);
            else
            {
                DrawList(s, left, ty, inner, bottom);
                if (pending != 0 && warning != null) DrawWarning(s, left, ty, inner, bottom);
            }

            // Statusleiste wie im Finder: Anzahl und freier Platz, oder eine Meldung.
            s.FillRect(left, bottom, inner, 1, T.Color.Separator);
            int textY = bottom + (status - Fonts.Body.LineHeight) / 2;
            if (asking) DrawConfirm(s, left, bottom, inner, status);
            else if (message != "") s.DrawText(Fonts.Body, ActivityWindow.Fit(Fonts.Body, message, inner - 2 * T.Space.L), left + T.Space.L, textY, T.Color.Danger);
            else if (Vfs.Available)
            {
                s.DrawTextCentered(Fonts.Body, Info + space, left + inner / 2, textY, T.Color.TextSecondary);
            }
        }

        private void DrawSidebar(Surface s, int x, int y, int h)
        {
            // Wie in den Einstellungen: unten links rund, rechts gerade.
            int r = T.Radius.M, bar = T.Size.Sidebar;
            s.FillRoundRect(x, y - r, bar, h + r, r, T.Color.TitleBar);
            s.FillRect(x + bar - r, y, r, h, T.Color.TitleBar);
            s.FillRect(x + bar, y, 1, h, T.Color.Separator);

            int rowX = x + T.Space.S, rowW = bar - 2 * T.Space.S, ty = y + T.Space.M;
            s.DrawText(Fonts.Caption, "ORTE", rowX + T.Space.S, ty, T.Color.TextTertiary);
            ty += Fonts.Caption.LineHeight + T.Space.XS;
            string user = OS.Users.Current != null ? OS.Users.Current.Name : "Benutzer";
            ty = Place(s, rowX, ty, rowW, user, true, Vfs.Available && file == null && string.Equals(folder, Home, StringComparison.OrdinalIgnoreCase), PlaceHome);
            Place(s, rowX, ty, rowW, "VIBANOS", false, Vfs.Available && file == null && IsRoot(folder), PlaceDisk);
        }

        private int Place(Surface s, int x, int y, int w, string label, bool isFolder, bool current, int action)
        {
            if (current) s.FillRoundRect(x, y, w, T.Size.Field, T.Radius.S, T.Color.Accent);
            int iy = y + (T.Size.Field - IconSize) / 2;
            if (isFolder) Icons.Folder(s, x + T.Space.S, iy, IconSize);
            else Icons.Disk(s, x + T.Space.S, iy, IconSize);
            s.DrawText(Fonts.Body, label, x + 2 * T.Space.S + IconSize, y + (T.Size.Field - Fonts.Body.LineHeight) / 2,
                current ? T.Color.TextOnAccent : T.Color.TextPrimary);
            AddHot(x, y, w, T.Size.Field, action);
            return y + T.Size.Field + T.Space.XS;
        }

        private void DrawToolbar(Surface s, int x, int y, int w, int h)
        {
            int by = y + T.Space.S, textY = by + (T.Size.Field - Fonts.Strong.LineHeight) / 2;
            bool canBack = Vfs.Available && (file != null || (folder != "" && !IsRoot(folder)));
            int bx = x + T.Space.M;
            s.FillRoundRect(bx, by, T.Size.Field, T.Size.Field, T.Radius.S, canBack ? T.Color.Raised : T.Color.Grouped);
            s.DrawTextCentered(Fonts.Strong, "<", bx + T.Size.Field / 2, textY, canBack ? T.Color.TextPrimary : T.Color.TextTertiary);
            if (canBack) AddHot(bx, by, T.Size.Field, T.Size.Field, Back);

            int buttons = x + w; // linker Rand der Knöpfe rechts, bis dahin reicht der Titel
            if (Vfs.Available && file == null && folder != "")
            {
                buttons = Button(s, x + w - T.Space.M, by, ButtonWidth, "Neuer Ordner", NewFolder);
                // Umbenennen und Löschen gelten der gewählten Zeile, ohne Auswahl gibt es sie nicht.
                if (selected >= 0 && selected < entries.Count)
                {
                    buttons = Button(s, buttons - T.Space.S, by, 0, "Löschen", Delete);
                    buttons = Button(s, buttons - T.Space.S, by, 0, "Umbenennen", Rename);
                }
            }
            string title = file != null ? Vfs.NameOf(file) : folder == "" ? "Dateien" : IsRoot(folder) ? "VIBANOS" : Vfs.NameOf(folder);
            int titleX = bx + T.Size.Field + T.Space.M, room = buttons - T.Space.M - titleX;
            if (room > 0) s.DrawText(Fonts.Strong, ActivityWindow.Fit(Fonts.Strong, title, room), titleX, textY, T.Color.TextPrimary);
            s.FillRect(x, y + h - 1, w, 1, T.Color.Separator);
        }

        // Knopf in der Leiste, rechtsbündig an right. Breite 0: so breit wie die Beschriftung. Gibt seinen linken Rand zurück.
        private int Button(Surface s, int right, int y, int width, string label, int action)
        {
            if (width == 0) width = Fonts.Body.Measure(label) + 2 * T.Space.M;
            int bx = right - width;
            s.FillRoundRect(bx, y, width, T.Size.Field, T.Radius.S, T.Color.Raised);
            s.StrokeRoundRect(bx, y, width, T.Size.Field, T.Radius.S, T.Color.Border);
            s.DrawTextCentered(Fonts.Body, label, bx + width / 2, y + (T.Size.Field - Fonts.Body.LineHeight) / 2, T.Color.TextPrimary);
            AddHot(bx, y, width, T.Size.Field, action);
            return bx;
        }

        // Rückfrage vor dem Löschen in der Statuszeile: links die Frage, rechts Abbrechen und Löschen.
        private void DrawConfirm(Surface s, int x, int y, int w, int h)
        {
            int bh = T.Size.Row, by = y + (h - bh) / 2, textY = by + (bh - Fonts.Body.LineHeight) / 2;
            int right = x + w - T.Space.M;
            int dw = Fonts.Body.Measure("Löschen") + 2 * T.Space.M, cw = Fonts.Body.Measure("Abbrechen") + 2 * T.Space.M;
            int dx = right - dw, cx = dx - T.Space.S - cw;
            // Rot nur hier: Löschen lässt sich nicht rückgängig machen.
            s.FillRoundRect(dx, by, dw, bh, T.Radius.S, T.Color.Danger);
            s.DrawTextCentered(Fonts.Body, "Löschen", dx + dw / 2, textY, T.Color.TextOnAccent);
            AddHot(dx, by, dw, bh, ConfirmDelete);
            s.FillRoundRect(cx, by, cw, bh, T.Radius.S, T.Color.Raised);
            s.StrokeRoundRect(cx, by, cw, bh, T.Radius.S, T.Color.Border);
            s.DrawTextCentered(Fonts.Body, "Abbrechen", cx + cw / 2, textY, T.Color.TextPrimary);
            AddHot(cx, by, cw, bh, Cancel);
            string name = selected >= 0 && selected < entries.Count ? entries[selected].Name : "";
            int room = cx - T.Space.M - (x + T.Space.L);
            if (room > 0)
                s.DrawText(Fonts.Body, ActivityWindow.Fit(Fonts.Body, "\"" + name + "\" wirklich löschen?", room), x + T.Space.L, textY, T.Color.TextPrimary);
        }

        private void DrawList(Surface s, int x, int y, int w, int bottom)
        {
            int left = x + T.Space.M, right = x + w - T.Space.M;
            int head = T.Size.Row;
            int capY = y + (head - Fonts.Caption.LineHeight) / 2;
            s.DrawText(Fonts.Caption, "NAME", left + T.Space.S, capY, T.Color.TextSecondary);
            s.DrawText(Fonts.Caption, "GRÖSSE", right - SizeColumn, capY, T.Color.TextSecondary);
            s.DrawText(Fonts.Caption, "ART", right - KindColumn, capY, T.Color.TextSecondary);
            s.FillRect(left, y + head - 1, right - left, 1, T.Color.Separator);
            int ty = y + head + T.Space.XS;

            visibleRows = (bottom - ty - T.Space.XS) / T.Size.Row;
            if (visibleRows < 1) visibleRows = 1;
            // Die gewählte Zeile ist immer zu sehen. Geblättert wird hier und nicht beim Tastendruck: Erst jetzt steht fest,
            // wie viele Zeilen passen, auch nach dem Vergrößern des Fensters oder dem Weg zurück aus einem Unterordner.
            if (selected >= 0 && selected < scroll) scroll = selected;
            if (selected >= scroll + visibleRows) scroll = selected - visibleRows + 1;
            if (scroll > entries.Count - visibleRows) scroll = entries.Count - visibleRows;
            if (scroll < 0) scroll = 0;
            if (entries.Count == 0)
            {
                Empty(s, x, w, ty, bottom, "Dieser Ordner ist leer.", "");
                return;
            }

            int textY = (T.Size.Row - Fonts.Body.LineHeight) / 2;
            for (int i = scroll; i < entries.Count && i < scroll + visibleRows; i++)
            {
                Entry e = entries[i];
                bool sel = i == selected;
                if (sel) s.FillRoundRect(left, ty, right - left, T.Size.Row, T.Radius.S, T.Color.Accent);
                else if ((i & 1) == 1) s.FillRoundRect(left, ty, right - left, T.Size.Row, T.Radius.S, T.Color.Grouped);
                int iy = ty + (T.Size.Row - IconSize) / 2;
                if (e.Folder) Icons.Folder(s, left + T.Space.S, iy, IconSize);
                else Icons.Document(s, left + T.Space.S, iy, IconSize);
                int nameX = left + 2 * T.Space.S + IconSize;
                if (sel && editName != null) DrawField(s, nameX, ty, right - SizeColumn - T.Space.S - nameX, textY);
                else s.DrawText(Fonts.Body, ActivityWindow.Fit(Fonts.Body, e.Name, right - SizeColumn - nameX - T.Space.S), nameX, ty + textY,
                    sel ? T.Color.TextOnAccent : T.Color.TextPrimary);
                uint dim = sel ? T.Color.TextOnAccent : T.Color.TextSecondary;
                s.DrawText(Fonts.Body, e.Folder ? "--" : e.Size < 0 ? "?" : Text.Bytes((ulong)e.Size), right - SizeColumn, ty + textY, dim);
                s.DrawText(Fonts.Body, e.Folder ? "Ordner" : Kind(e.Name), right - KindColumn, ty + textY, dim);
                AddHot(left, ty, right - left, T.Size.Row, Item + i);
                ty += T.Size.Row;
            }
        }

        // Warnung vor dem Löschen oder Umbenennen einer Systemdatei, mitten über der Liste wie ein Hinweis unter macOS:
        // oben gelb, worum es geht, darunter die Frage, was ohne die Datei passiert, und die beiden Knöpfe.
        private void DrawWarning(Surface s, int x, int top, int w, int bottom)
        {
            s.FillRect(x, top, w, bottom - top, T.Color.Scrim);
            int pad = T.Space.L, pw = Math.Min(w - 2 * T.Space.XL, SheetWidth), textW = pw - 2 * pad;
            if (textW < 1) return;
            List<string> body = Wrap(Fonts.Body, warning, textW);
            int ph = pad + Fonts.Caption.LineHeight + T.Space.XS + Fonts.Strong.LineHeight + T.Space.S
                + body.Count * Fonts.Body.LineHeight + T.Space.L + T.Size.Field + pad;
            int px = x + (w - pw) / 2, py = top + (bottom - top - ph) / 2;
            if (py < top + T.Space.S) py = top + T.Space.S;
            s.Shadow(px, py, pw, ph, T.Radius.M, T.Shadow.WindowBlur, T.Shadow.WindowOffset, T.Shadow.WindowAlpha, T.Color.Shadow);
            s.FillRoundRect(px, py, pw, ph, T.Radius.M, T.Color.TitleBar);
            s.StrokeRoundRect(px, py, pw, ph, T.Radius.M, T.Color.Border);

            int tx = px + pad, ty = py + pad;
            s.DrawText(Fonts.Caption, "ACHTUNG: SYSTEMDATEI", tx, ty, T.Color.Warning);
            ty += Fonts.Caption.LineHeight + T.Space.XS;
            string name = selected >= 0 && selected < entries.Count ? entries[selected].Name : "";
            string question = "\"" + name + "\" " + (pending == ConfirmDelete ? "löschen?" : "umbenennen?");
            s.DrawText(Fonts.Strong, ActivityWindow.Fit(Fonts.Strong, question, textW), tx, ty, T.Color.TextPrimary);
            ty += Fonts.Strong.LineHeight + T.Space.S;
            for (int i = 0; i < body.Count; i++, ty += Fonts.Body.LineHeight) s.DrawText(Fonts.Body, body[i], tx, ty, T.Color.TextSecondary);

            // Rechts der Knopf zum Bestätigen: rot fürs Löschen, das lässt sich nicht rückgängig machen.
            string label = pending == ConfirmDelete ? "Löschen" : "Umbenennen";
            int by = py + ph - pad - T.Size.Field, textY = by + (T.Size.Field - Fonts.Body.LineHeight) / 2;
            int ow = Fonts.Body.Measure(label) + 2 * T.Space.M, cw = Fonts.Body.Measure("Abbrechen") + 2 * T.Space.M;
            int ox = px + pw - pad - ow, cx = ox - T.Space.S - cw;
            s.FillRoundRect(ox, by, ow, T.Size.Field, T.Radius.S, pending == ConfirmDelete ? T.Color.Danger : T.Color.Accent);
            s.DrawTextCentered(Fonts.Body, label, ox + ow / 2, textY, T.Color.TextOnAccent);
            AddHot(ox, by, ow, T.Size.Field, pending);
            s.FillRoundRect(cx, by, cw, T.Size.Field, T.Radius.S, T.Color.Raised);
            s.StrokeRoundRect(cx, by, cw, T.Size.Field, T.Radius.S, T.Color.Border);
            s.DrawTextCentered(Fonts.Body, "Abbrechen", cx + cw / 2, textY, T.Color.TextPrimary);
            AddHot(cx, by, cw, T.Size.Field, Cancel);
        }

        // Bricht Text an Leerzeichen in Zeilen um, die höchstens width breit sind. Ein zu langes Wort steht allein.
        private static List<string> Wrap(Font font, string text, int width)
        {
            List<string> result = new List<string>();
            string line = "";
            string[] words = text.Split(' ');
            for (int i = 0; i < words.Length; i++)
            {
                string longer = line == "" ? words[i] : line + " " + words[i];
                if (line != "" && font.Measure(longer) > width) { result.Add(line); line = words[i]; }
                else line = longer;
            }
            if (line != "") result.Add(line);
            return result;
        }

        // Eingabefeld zum Umbenennen, an der Stelle des Namens. Ist der Name zu lang, zeigt es sein Ende, wo getippt wird.
        private void DrawField(Surface s, int nameX, int ty, int width, int textY)
        {
            int fx = nameX - T.Space.XS, fw = width + T.Space.XS;
            s.FillRoundRect(fx, ty + T.Space.XS / 2, fw, T.Size.Row - T.Space.XS, T.Radius.S, T.Color.Sunken);
            s.StrokeRoundRect(fx, ty + T.Space.XS / 2, fw, T.Size.Row - T.Space.XS, T.Radius.S, T.Color.Border);
            // Passt der Name nicht ganz hinein, zeigt das Feld einen Ausschnitt, und der folgt der Einfügemarke.
            Font font = Fonts.Body;
            int room = width - T.Size.Caret - T.Space.XS;
            if (fieldStart > caret) fieldStart = caret;
            while (fieldStart < caret && font.Measure(editName.Substring(fieldStart, caret - fieldStart)) > room) fieldStart++;
            while (fieldStart > 0 && font.Measure(editName.Substring(fieldStart - 1)) <= room) fieldStart--; // nach dem Löschen wieder auffüllen
            string shown = editName.Substring(fieldStart);
            while (shown.Length > caret - fieldStart && font.Measure(shown) > room) shown = shown.Substring(0, shown.Length - 1);
            s.DrawText(font, shown, nameX, ty + textY, T.Color.TextPrimary);
            int at = nameX + font.Measure(editName.Substring(fieldStart, caret - fieldStart));
            s.FillRect(caret > fieldStart ? at - T.Size.Caret / 2 : at, ty + textY, T.Size.Caret, font.LineHeight, T.Color.Accent);
            AddHot(fx, ty, fw, T.Size.Row, NameField);
        }

        internal static string Kind(string name)
        {
            int dot = name.LastIndexOf('.');
            string ext = dot >= 0 ? name.Substring(dot + 1).ToLowerInvariant() : "";
            if (ext == "txt" || ext == "md" || ext == "log") return "Text";
            if (ext == "csv") return "Tabelle";
            // Nur kurze Endungen ohne Leerzeichen sind welche: "Bericht v1.2 final" hat keine, und die Spalte ist schmal.
            return ext == "" || ext.Length > 4 || ext.IndexOf(' ') >= 0 ? "Datei" : ext.ToUpperInvariant() + "-Datei";
        }

        private void DrawPreview(Surface s, int x, int y, int w, int bottom)
        {
            s.FillRect(x, y, w, bottom - y, T.Color.Sunken);
            int lh = Fonts.Mono.LineHeight, ty = y + T.Space.M;
            previewRows = (bottom - ty - T.Space.M) / lh;
            if (previewRows < 1) previewRows = 1;
            // Lange Zeilen brechen um wie im Terminal: Eine Datei ohne Zeilenenden ist eine einzige, sehr lange Zeile.
            int columns = (w - 2 * T.Space.L) / Fonts.Mono.Measure("M");
            if (columns < 1) columns = 1;
            int total = 0;
            for (int i = 0; i < lines.Count; i++) total += Rows(lines[i], columns);
            if (top > total - previewRows) top = total - previewRows;
            if (top < 0) top = 0;
            if (lines.Count == 0) { Empty(s, x, w, y, bottom, "Diese Datei ist leer.", ""); return; }
            int row = 0, end = top + previewRows;
            for (int i = 0; i < lines.Count && row < end; i++)
            {
                int n = Rows(lines[i], columns);
                if (row + n <= top) { row += n; continue; } // ganz über dem Ausschnitt
                for (int k = 0; k < n && row < end; k++, row++)
                {
                    if (row < top) continue;
                    int at = k * columns;
                    s.DrawText(Fonts.Mono, lines[i].Substring(at, Math.Min(columns, lines[i].Length - at)), x + T.Space.L, ty, T.Color.TextPrimary);
                    ty += lh;
                }
            }
        }

        // So viele Bildschirmzeilen braucht eine Zeile der Datei. Auch eine leere braucht eine.
        private static int Rows(string line, int columns) { return line.Length <= columns ? 1 : (line.Length + columns - 1) / columns; }

        private static void Empty(Surface s, int x, int w, int top, int bottom, string first, string second)
        {
            int cy = top + (bottom - top) / 2 - Fonts.Body.LineHeight;
            s.DrawTextCentered(Fonts.Strong, first, x + w / 2, cy, T.Color.TextTertiary);
            if (second != "") s.DrawTextCentered(Fonts.Body, second, x + w / 2, cy + Fonts.Strong.LineHeight + T.Space.XS, T.Color.TextTertiary);
        }
    }
}
