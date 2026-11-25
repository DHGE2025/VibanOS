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
    /// Alle Zugriffe laufen über Vfs.CanAccess, wie im Terminal.
    /// </summary>
    public class FilesWindow : Window
    {
        // Klickflächen, wie in den Einstellungen relativ zum Inhalt gemerkt.
        internal const int PlaceHome = 0, PlaceDisk = 1, Back = 10, NewFolder = 11, Item = 100;

        private const int IconSize = 16;       // Symbol vor einem Eintrag
        private const int SizeColumn = 190;    // Abstand der Spalte "Größe" vom rechten Rand
        private const int KindColumn = 100;    // Abstand der Spalte "Art" vom rechten Rand
        private const int ButtonWidth = 110;   // Knopf "Neuer Ordner"
        private const int PreviewBytes = 8192; // so viel einer Datei zeigt die Vorschau

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
        private string message = "";
        private string space = "";  // freier Platz für die Statuszeile, siehe ReadSpace

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
            selected = -1;
            scroll = 0;
            Go(Home);
        }

        public override bool CanZoom { get { return true; } }

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

        /// <summary>Zeigt einen Ordner. Ohne Recht oder wenn es ihn nicht gibt, bleibt alles, wie es war.</summary>
        private void Go(string path)
        {
            message = "";
            if (!Vfs.Available) { entries.Clear(); folder = ""; return; }
            if (!Vfs.CanAccess(OS.Users.Current, path, Access.Read)) { message = "Zugriff verweigert: " + path; return; }
            if (!Directory.Exists(path)) { message = "Ordner nicht gefunden: " + path; return; }
            folder = path;
            file = null;
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
                    for (int i = 0; i < split.Length; i++) lines.Add(split[i].Replace("\r", "").Replace("\t", "    "));
                    if (more) lines.Add("...");
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

        public override void OnClick(int px, int py)
        {
            Sync();
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
            // Ein Klick ins Leere hebt die Auswahl auf und holt den neuesten Stand des Ordners.
            selected = -1;
            Reload();
            OS.Changed = true;
        }

        private void Do(int action)
        {
            if (action == PlaceHome) Go(Home);
            else if (action == PlaceDisk) Go(Vfs.Root);
            else if (action == Back) GoBack();
            else if (action == NewFolder) MakeFolder();
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
            int status = Fonts.Body.LineHeight + 2 * T.Space.S;
            int bottom = y + h - status;

            if (!Vfs.Available)
            {
                Empty(s, left, inner, ty, bottom, "Keine Festplatte eingebunden.", "Im Terminal einmal 'format' ausführen (siehe README).");
            }
            else if (file != null) DrawPreview(s, left, ty, inner, bottom);
            else DrawList(s, left, ty, inner, bottom);

            // Statusleiste wie im Finder: Anzahl und freier Platz, oder eine Meldung.
            s.FillRect(left, bottom, inner, 1, T.Color.Separator);
            int textY = bottom + (status - Fonts.Body.LineHeight) / 2;
            if (message != "") s.DrawText(Fonts.Body, ActivityWindow.Fit(Fonts.Body, message, inner - 2 * T.Space.L), left + T.Space.L, textY, T.Color.Danger);
            else if (Vfs.Available)
            {
                string info = file != null ? lines.Count + (lines.Count == 1 ? " Zeile" : " Zeilen") : entries.Count + (entries.Count == 1 ? " Objekt" : " Objekte");
                s.DrawTextCentered(Fonts.Body, info + space, left + inner / 2, textY, T.Color.TextSecondary);
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

            string title = file != null ? Vfs.NameOf(file) : folder == "" ? "Dateien" : IsRoot(folder) ? "VIBANOS" : Vfs.NameOf(folder);
            int titleX = bx + T.Size.Field + T.Space.M;
            s.DrawText(Fonts.Strong, ActivityWindow.Fit(Fonts.Strong, title, w - titleX + x - ButtonWidth - 2 * T.Space.M), titleX, textY, T.Color.TextPrimary);

            if (Vfs.Available && file == null && folder != "")
            {
                int nx = x + w - T.Space.M - ButtonWidth;
                s.FillRoundRect(nx, by, ButtonWidth, T.Size.Field, T.Radius.S, T.Color.Raised);
                s.StrokeRoundRect(nx, by, ButtonWidth, T.Size.Field, T.Radius.S, T.Color.Border);
                s.DrawTextCentered(Fonts.Body, "Neuer Ordner", nx + ButtonWidth / 2, by + (T.Size.Field - Fonts.Body.LineHeight) / 2, T.Color.TextPrimary);
                AddHot(nx, by, ButtonWidth, T.Size.Field, NewFolder);
            }
            s.FillRect(x, y + h - 1, w, 1, T.Color.Separator);
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
                s.DrawText(Fonts.Body, ActivityWindow.Fit(Fonts.Body, e.Name, right - SizeColumn - nameX - T.Space.S), nameX, ty + textY,
                    sel ? T.Color.TextOnAccent : T.Color.TextPrimary);
                uint dim = sel ? T.Color.TextOnAccent : T.Color.TextSecondary;
                s.DrawText(Fonts.Body, e.Folder ? "--" : e.Size < 0 ? "?" : Text.Bytes((ulong)e.Size), right - SizeColumn, ty + textY, dim);
                s.DrawText(Fonts.Body, e.Folder ? "Ordner" : Kind(e.Name), right - KindColumn, ty + textY, dim);
                AddHot(left, ty, right - left, T.Size.Row, Item + i);
                ty += T.Size.Row;
            }
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
