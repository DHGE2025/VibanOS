using System;
using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.Graphics;
using VibanOS.Processes;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>
    /// Der Schreibtisch: Hintergrund, Menüleiste, Dock, Fensterverwaltung und Mauszeiger.
    /// Der Kernel reicht nur Tastatur und Maus herein und holt mit Frame() das fertige Bild ab.
    /// </summary>
    public class Desktop
    {
        private const int CursorSize = 20;
        private static readonly string[] Cursor =
        {
            "X           ",
            "XX          ",
            "X.X         ",
            "X..X        ",
            "X...X       ",
            "X....X      ",
            "X.....X     ",
            "X......X    ",
            "X.......X   ",
            "X........X  ",
            "X.........X ",
            "X......XXXXX",
            "X...X..X    ",
            "X..X X..X   ",
            "X.X  X..X   ",
            "XX    X..X  ",
            "X     X..X  ",
            "       XX   ",
        };

        // Doppelpfeile über einem Fensterrand, die Maus zeigt auf ihre Mitte. Reihenfolge wie die Werte von Shape.
        private const int ArrowShape = 0, ResizeHorizontal = 1, ResizeVertical = 2, ResizeFalling = 3, ResizeRising = 4;
        private static readonly string[][] ResizeCursors =
        {
            new string[]
            {
                "   XXX     XXX   ",
                "  XX.X     X.XX  ",
                " XX..X     X..XX ",
                "XX...XXXXXXX...XX",
                "X...............X",
                "XX...XXXXXXX...XX",
                " XX..X     X..XX ",
                "  XX.X     X.XX  ",
                "   XXX     XXX   ",
            },
            new string[]
            {
                "    XXX    ",
                "   XX.XX   ",
                "  XX...XX  ",
                " XX.....XX ",
                " X.......X ",
                " XXXX.XXXX ",
                "    X.X    ",
                "    X.X    ",
                "    X.X    ",
                "    X.X    ",
                "    X.X    ",
                " XXXX.XXXX ",
                " X.......X ",
                " XX.....XX ",
                "  XX...XX  ",
                "   XX.XX   ",
                "    XXX    ",
            },
            new string[]
            {
                "XXXXXXX          ",
                "X.....X          ",
                "X....XX          ",
                "X...XX           ",
                "X..X.XX          ",
                "X.XXX.XX         ",
                "XXX XX.XX        ",
                "     XX.XX       ",
                "      XX.XX      ",
                "       XX.XX     ",
                "        XX.XX XXX",
                "         XX.XXX.X",
                "          XX.X..X",
                "           XX...X",
                "          XX....X",
                "          X.....X",
                "          XXXXXXX",
            },
            new string[]
            {
                "          XXXXXXX",
                "          X.....X",
                "          XX....X",
                "           XX...X",
                "          XX.X..X",
                "         XX.XXX.X",
                "        XX.XX XXX",
                "       XX.XX     ",
                "      XX.XX      ",
                "     XX.XX       ",
                "XXX XX.XX        ",
                "X.XXX.XX         ",
                "X..X.XX          ",
                "X...XX           ",
                "X....XX          ",
                "X.....X          ",
                "XXXXXXX          ",
            },
        };

        private Surface wallpaper;
        private Surface scene;
        private readonly LoginScreen login = new LoginScreen();

        /// <summary>Der Anmeldebildschirm. Der Selbsttest fragt darüber, ob die Nutzerwahl offen ist.</summary>
        public LoginScreen Login { get { return login; } }
        private readonly Window[] apps = new Window[Icons.Count];   // offenes Fenster je App oder null
        private readonly List<Window> windows = new List<Window>(); // hinten nach vorn, das letzte ist aktiv
        private readonly List<Transition> transitions = new List<Transition>();

        /// <summary>Uhr der Animationen in Millisekunden. Der Selbsttest setzt hier eine eigene ein.</summary>
        public Func<long> Clock = Transition.Milliseconds;
        private long ms;

        private int mouseX, mouseY, drawnX = -1, drawnY = -1;
        private bool drawnLarge; // so groß steht der Mauszeiger gerade auf dem Bildschirm
        private int shape, drawnShape; // Pfeil oder Doppelpfeil (*Shape, Resize*): gewünscht und auf dem Bildschirm

        /// <summary>
        /// Nach Frame() mit true: Nur diese Ausschnitte haben sich geändert und müssen an den Bildschirm.
        /// Je Ausschnitt vier Werte: x, y, Breite, Höhe in Punkten. Der letzte gehört dem Mauszeiger.
        /// </summary>
        public readonly int[] Dirty = new int[4 * (Surface.MaxDamage + 1)];
        public int DirtyCount;

        /// <summary>Das kleinste Rechteck um alle Ausschnitte in Dirty. Nur nach Frame() mit true.</summary>
        public void DirtyBounds(out int x, out int y, out int width, out int height)
        {
            int right = Dirty[0] + Dirty[2], bottom = Dirty[1] + Dirty[3];
            x = Dirty[0];
            y = Dirty[1];
            for (int i = 4; i < 4 * DirtyCount; i += 4)
            {
                x = Math.Min(x, Dirty[i]);
                y = Math.Min(y, Dirty[i + 1]);
                right = Math.Max(right, Dirty[i] + Dirty[i + 2]);
                bottom = Math.Max(bottom, Dirty[i + 1] + Dirty[i + 3]);
            }
            width = right - x;
            height = bottom - y;
        }

        /// <summary>So lange hat Frame bisher vermessen und gezeichnet, in Einheiten von Stopwatch. Der Kernel schreibt das ins Protokoll.</summary>
        public long Measured, Painted;
        private bool mouseDown;
        private Window dragging;
        private int dragX, dragY;
        private Window resizing;   // wird gerade an einem Rand gezogen
        private int snapEdges;      // beim Loslassen dockt das gezogene Fenster an diese Ränder an (Window.Edge*), 0: nicht
        private Window snapPartner; // liegt schon in der Hälfte und teilt sie sich dann mit dem gezogenen Fenster
        private int resizeEdge, resizeX, resizeY, startX, startY, startWidth, startHeight;
        private int lastSecond = -1, lastMinute = -1;
        private bool wasLocked = true;

        public Desktop(int width, int height)
        {
            login.Clock = () => Clock(); // auch eine später eingesetzte Uhr (Selbsttest) gilt für die Anmeldung
            wallpaper = new Surface(width, height);
            scene = new Surface(width, height);
            Wallpaper.Render(wallpaper);
        }

        /// <summary>Das nächste Frame zeichnet alles neu. Der Selbsttest prüft damit, ob das stückweise Neuzeichnen dasselbe Bild ergibt.</summary>
        public void Redraw()
        {
            scene.Invalidate();
            OS.Changed = true;
        }

        /// <summary>true, solange eine Fenster-Animation läuft.</summary>
        public bool Animating { get { return transitions.Count > 0; } }

        /// <summary>
        /// Neue Bildschirmgröße (Auflösung in den Einstellungen umgeschaltet): Hintergrund neu, laufende Animationen
        /// beenden, alle Fenster der Apps zurück auf den Schirm, auch minimierte. Vergrößerte füllen die neue Fläche.
        /// </summary>
        public void Resize(int width, int height)
        {
            for (int i = 0; i < transitions.Count; i++) transitions[i].Finish();
            transitions.Clear();
            wallpaper = new Surface(width, height);
            scene = new Surface(width, height);
            Wallpaper.Render(wallpaper);
            for (int i = 0; i < apps.Length; i++)
            {
                Window w = apps[i];
                if (w == null) continue;
                if (w.Zoomed || w.Snapped != 0)
                {
                    if (w.Zoomed) ZoomArea(out w.X, out w.Y, out w.Width, out w.Height);
                    else SnapArea(w.Snapped, out w.X, out w.Y, out w.Width, out w.Height);
                    Keep(ref w.NormalX, ref w.NormalY, w.NormalWidth); // auch die Größe davor muss auf den Schirm passen
                }
                else Place(w, w.X, w.Y);
            }
            drawnX = drawnY = -1;
            OS.Changed = true;
        }

        private void Place(Window w, int nx, int ny)
        {
            Keep(ref nx, ref ny, w.Width);
            if (w.X != nx || w.Y != ny) OS.Changed = true;
            w.X = nx;
            w.Y = ny;
        }

        // Ein Stück der Titelleiste bleibt erreichbar: seitlich, über dem Dock und unter der Menüleiste.
        private void Keep(ref int x, ref int y, int width)
        {
            int keep = T.Size.DockIcon;
            if (x < keep - width) x = keep - width;
            if (x > scene.Width - keep) x = scene.Width - keep;
            if (y > DockY - T.Size.TitleBar) y = DockY - T.Size.TitleBar;
            if (y < T.Size.MenuBar) y = T.Size.MenuBar;
        }

        // Die Fläche, die der grüne Knopf füllt: zwischen Menüleiste und Dock.
        private void ZoomArea(out int x, out int y, out int width, out int height)
        {
            x = T.Space.S;
            y = T.Size.MenuBar + T.Space.S;
            width = scene.Width - 2 * T.Space.S;
            height = DockY - T.Space.S - y;
        }

        // Der Teil der Zoom-Fläche, an den ein Fenster andockt (Window.Edge*): ein Rand die Hälfte, eine Ecke ein Viertel.
        // Zwischen den Teilen Abstand S wie am Rand.
        private void SnapArea(int edges, out int x, out int y, out int width, out int height)
        {
            ZoomArea(out x, out y, out width, out height);
            if ((edges & (Window.EdgeLeft | Window.EdgeRight)) != 0)
            {
                int half = (width - T.Space.S) / 2;
                if ((edges & Window.EdgeRight) != 0) x += width - half;
                width = half;
            }
            if ((edges & (Window.EdgeTop | Window.EdgeBottom)) != 0)
            {
                int half = (height - T.Space.S) / 2;
                if ((edges & Window.EdgeBottom) != 0) y += height - half;
                height = half;
            }
        }

        // An welche Ränder dockt ein Fenster an, das an der Stelle der Maus losgelassen wird? Ganz am Bildschirmrand
        // eine Hälfte, in einer Ecke ein Viertel. Die Ecke greift SnapCorner weit, damit man sie nicht genau treffen muss.
        private int SnapEdgesAt(int x, int y)
        {
            int edge = T.Size.SnapEdge, corner = T.Size.SnapCorner;
            int horizontal = x < edge ? Window.EdgeLeft : x >= scene.Width - edge ? Window.EdgeRight : 0;
            int vertical = y < edge ? Window.EdgeTop : y >= scene.Height - edge ? Window.EdgeBottom : 0;
            if (horizontal != 0 && vertical == 0) vertical = y < corner ? Window.EdgeTop : y >= scene.Height - corner ? Window.EdgeBottom : 0;
            if (vertical != 0 && horizontal == 0) horizontal = x < corner ? Window.EdgeLeft : x >= scene.Width - corner ? Window.EdgeRight : 0;
            return horizontal | vertical;
        }

        private static bool Locked { get { return OS.Users.Current == null; } }

        /// <summary>Das vorderste Fenster. Es bekommt die Tastatur. null, wenn keines offen ist.</summary>
        public Window Active { get { return windows.Count > 0 ? windows[windows.Count - 1] : null; } }

        public void OnKey(Key key, char c)
        {
            VibanOS.Users.User was = OS.Users.Current;
            if (Locked) login.OnKey(key, c);
            else if (Active != null) Active.OnKey(key, c);
            // Gerade an- oder abgemeldet oder mit su oder login den Nutzer gewechselt: Akzentfarbe, Uhr, Tastatur und
            // Auflösung des Nutzers übernehmen, nach der Abmeldung die Grundwerte. Sonst tippte der Nächste sein Passwort
            // mit der Tastaturbelegung des Vorgängers.
            // Hier und nicht erst beim Zeichnen: So ist eine andere Auflösung da, bevor das nächste Bild entsteht, wie
            // nach einem Klick in den Einstellungen. Mitten im Zeichnen bekäme der Bildschirm einen neuen Puffer.
            if (was != OS.Users.Current) Settings.Load(OS.Users.Current);
            OS.Changed = true;
        }

        public void OnMouse(int x, int y, bool down)
        {
            bool pressed = down && !mouseDown;
            mouseX = x;
            mouseY = y;
            mouseDown = down;
            if (Locked)
            {
                // Am Anmeldebildschirm: Das Nutzerbild öffnet die Nutzerwahl. Anmelden geht nur mit der Tastatur.
                login.OnMove(x, y);
                if (pressed) { login.OnClick(x, y); OS.Changed = true; }
                shape = ArrowShape;
                return;
            }

            // Erst an die Stelle der Maus, dann loslassen: Bewegung und Loslassen können im selben Durchlauf ankommen,
            // das Fenster bliebe sonst einen Schritt hinter der Maus zurück.
            if (dragging != null) DragTo(x, y);
            if (resizing != null) ResizeTo(x, y);
            if (!down)
            {
                if (dragging != null && snapEdges != 0) Snap(dragging, snapEdges);
                dragging = null;
                resizing = null;
                snapEdges = 0;
                snapPartner = null;
            }
            if (pressed) Press(x, y);
            // Über einem Rand, der sich ziehen lässt, zeigt der Zeiger einen Doppelpfeil, und während des Ziehens bleibt er.
            // Mit gedrückter Taste aus einem Fenster heraus nicht: Dann zieht man gerade etwas anderes.
            shape = resizing != null ? ShapeOf(resizeEdge) : down ? ArrowShape : ShapeOf(EdgeUnder(x, y));
        }

        // Ein Klick: Dock, Fensterrand, Fensterknopf, Titelleiste oder Inhalt.
        private void Press(int x, int y)
        {
            int icon = DockIconAt(x, y);
            if (icon >= 0) { Open(icon); return; }
            // Ein Klick auf den Rand des Docks trifft kein Fenster darunter.
            if (x >= DockX && x < DockX + DockWidth && y >= DockY && y < DockY + DockHeight) return;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window w = windows[i];
                int edge = w.EdgeAt(x, y);
                if (edge != 0) { StartResize(w, edge, x, y); return; }
                if (!w.Contains(x, y)) continue;
                int control = w.ControlAt(x, y);
                if (control == Window.CloseButton) { Close(w); return; }
                if (control == Window.MinimizeButton) { Minimize(w); return; }
                if (control == Window.ZoomButton && w.CanZoom) { Zoom(w); return; }
                Focus(w);
                if (w.InTitleBar(x, y))
                {
                    dragging = w;
                    dragX = x - w.X;
                    dragY = y - w.Y;
                    startX = w.X; startY = w.Y; // dorthin stellt der grüne Knopf ein angedocktes Fenster zurück
                }
                else w.OnClick(x, y);
                return;
            }
        }

        // Der Rand unter der Maus, an dem ein Klick das Fenster größer und kleiner zöge. 0: keiner.
        // Wie beim Klick gilt das vorderste Fenster, ein Rand unter einem anderen Fenster oder dem Dock zählt nicht.
        private int EdgeUnder(int x, int y)
        {
            if (x >= DockX && x < DockX + DockWidth && y >= DockY && y < DockY + DockHeight) return 0;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                int edge = windows[i].EdgeAt(x, y);
                if (edge != 0) return edge;
                if (windows[i].Contains(x, y)) return 0;
            }
            return 0;
        }

        private static int ShapeOf(int edge)
        {
            bool left = (edge & Window.EdgeLeft) != 0, right = (edge & Window.EdgeRight) != 0;
            bool top = (edge & Window.EdgeTop) != 0, bottom = (edge & Window.EdgeBottom) != 0;
            if ((left || right) && !top && !bottom) return ResizeHorizontal;
            if ((top || bottom) && !left && !right) return ResizeVertical;
            if ((left && top) || (right && bottom)) return ResizeFalling;
            if ((right && top) || (left && bottom)) return ResizeRising;
            return ArrowShape;
        }

        // Ein Fenster an der Titelleiste folgt der Maus. Ein angedocktes bekommt dabei seine alte Größe zurück, die Maus
        // bleibt an derselben Stelle der Titelleiste (anteilig). Ganz am Bildschirmrand zeigt eine Fläche, wohin es beim
        // Loslassen andockt.
        private void DragTo(int x, int y)
        {
            Window w = dragging;
            if (w.Snapped != 0 && (x != w.X + dragX || y != w.Y + dragY))
            {
                Cancel(w);
                dragX = dragX * w.NormalWidth / w.Width;
                w.Width = w.NormalWidth;
                w.Height = w.NormalHeight;
                w.Snapped = 0;
                OS.Changed = true;
            }
            Place(w, x - dragX, y - dragY);
            int side = w.CanResize ? SnapEdgesAt(x, y) : 0;
            // Liegt in der Hälfte schon ein Fenster, teilen sich beide sie: Das gezogene kommt auf die Seite der Maus,
            // das andere rückt auf die andere.
            snapPartner = null;
            if (side == Window.EdgeLeft || side == Window.EdgeRight || side == Window.EdgeTop || side == Window.EdgeBottom)
            {
                snapPartner = SnappedTo(side, w);
                if (snapPartner != null)
                {
                    if (side == Window.EdgeLeft || side == Window.EdgeRight) side |= y < scene.Height / 2 ? Window.EdgeTop : Window.EdgeBottom;
                    else side |= x < scene.Width / 2 ? Window.EdgeLeft : Window.EdgeRight;
                }
            }
            if (side != snapEdges) { snapEdges = side; OS.Changed = true; }
        }

        // Das offene Fenster außer except, das genau an diesen Rändern angedockt ist, sonst null.
        private Window SnappedTo(int edges, Window except)
        {
            for (int i = windows.Count - 1; i >= 0; i--)
                if (windows[i] != except && windows[i].Snapped == edges) return windows[i];
            return null;
        }

        // Dockt w an eine Bildschirmhälfte oder ein Viertel an, mit derselben Bewegung wie der grüne Knopf.
        private void Snap(Window w, int side)
        {
            Cancel(w);
            if (!w.Zoomed && w.Snapped == 0)
            {
                w.NormalX = startX; w.NormalY = startY; w.NormalWidth = w.Width; w.NormalHeight = w.Height;
            }
            w.Zoomed = false;
            w.Snapped = side;
            int x, y, width, height;
            SnapArea(side, out x, out y, out width, out height);
            transitions.Add(new Transition(w, Motion.Zoom, Clock(), true, x, y, width, height));
            // Das Fenster, das die Hälfte vorher allein hatte, rückt in das andere Viertel.
            Window partner = snapPartner;
            if (partner != null && IndexOf(partner) >= 0)
            {
                int taken = side & ~partner.Snapped; // das Viertel des gezogenen Fensters, etwa oben
                int other = taken == Window.EdgeTop ? Window.EdgeBottom : taken == Window.EdgeBottom ? Window.EdgeTop
                    : taken == Window.EdgeLeft ? Window.EdgeRight : Window.EdgeLeft;
                Cancel(partner);
                partner.Snapped |= other;
                SnapArea(partner.Snapped, out x, out y, out width, out height);
                transitions.Add(new Transition(partner, Motion.Zoom, Clock(), false, x, y, width, height));
            }
            OS.Changed = true;
        }

        private void StartResize(Window w, int edge, int x, int y)
        {
            Focus(w);
            Cancel(w); // ein laufender Zoom springt an sein Ziel, gezogen wird von dort aus
            w.Zoomed = false; // von Hand gezogen ist es nicht mehr vergrößert, der grüne Knopf vergrößert wieder
            w.Snapped = 0;
            resizing = w;
            resizeEdge = edge;
            resizeX = x; resizeY = y;
            startX = w.X; startY = w.Y; startWidth = w.Width; startHeight = w.Height;
        }

        // Der gezogene Rand folgt der Maus. Der gegenüberliegende bleibt stehen, auch wenn die Mindestgröße erreicht ist.
        private void ResizeTo(int x, int y)
        {
            Window w = resizing;
            bool left = (resizeEdge & Window.EdgeLeft) != 0, top = (resizeEdge & Window.EdgeTop) != 0;
            int width = startWidth, height = startHeight;
            if (left) width = startWidth - (x - resizeX);
            else if ((resizeEdge & Window.EdgeRight) != 0) width = startWidth + x - resizeX;
            if (top) height = startHeight - (y - resizeY);
            else if ((resizeEdge & Window.EdgeBottom) != 0) height = startHeight + y - resizeY;
            if (top && startY + startHeight - height < T.Size.MenuBar) height = startY + startHeight - T.Size.MenuBar; // nicht unter die Menüleiste
            if (width < w.MinWidth) width = w.MinWidth;
            if (height < w.MinHeight) height = w.MinHeight;
            int nx = left ? startX + startWidth - width : startX, ny = top ? startY + startHeight - height : startY;
            if (nx == w.X && ny == w.Y && width == w.Width && height == w.Height) return;
            w.X = nx; w.Y = ny; w.Width = width; w.Height = height;
            OS.Changed = true;
        }

        /// <summary>Öffnet eine App aus dem Dock, holt ihr Fenster nach vorn oder aus dem Dock zurück.</summary>
        public void Open(int icon)
        {
            Window open = apps[icon];
            if (open != null && IndexOf(open) >= 0) { Focus(open); return; }
            if (open != null)
            {
                windows.Add(open);
                Animate(open, Motion.Restore, icon);
                return;
            }
            Window w;
            string name;
            if (icon == Icons.Files) { w = new FilesWindow(); name = "dateien"; }
            else if (icon == Icons.Terminal) { w = new TerminalWindow(); name = "terminal"; }
            else if (icon == Icons.Activity) { w = new ActivityWindow(); name = "aktivität"; }
            else if (icon == Icons.Settings) { w = new SettingsWindow(); name = "optionen"; } // ps zeigt Namen bis 10 Zeichen
            else { w = new AboutWindow(); name = "info"; }
            // Jede offene App ist ein Prozess, sonst fehlt sie in ps und in der Aktivitätsanzeige.
            w.Process = OS.Processes.Start(new AppProcess(name));
            // Neue Fenster leicht versetzt, damit sie sich nicht genau überdecken. Das Terminal steht vorn,
            // Dateien (ganz links im Dock) bekommt den letzten Platz.
            int slot = icon == Icons.Files ? Icons.Count - 1 : icon - Icons.Terminal;
            w.X = (scene.Width - w.Width) / 2 + (slot - 1) * T.Space.XXL * 3;
            w.Y = T.Size.MenuBar + T.Space.XL + slot * T.Space.XXL;
            // Auf einem kleinen Bildschirm bleiben Fensterknöpfe und Zeilenanfänge sichtbar.
            if (w.X + w.Width > scene.Width - T.Space.M) w.X = scene.Width - T.Space.M - w.Width;
            if (w.X < T.Space.M) w.X = T.Space.M;
            // Und nach unten: Ein hohes Fenster reichte sonst unter das Dock, sein Fuß wäre verdeckt.
            if (w.Y + w.Height > DockY - T.Space.S) w.Y = DockY - T.Space.S - w.Height;
            if (w.Y < T.Size.MenuBar + T.Space.S) w.Y = T.Size.MenuBar + T.Space.S;
            apps[icon] = w;
            windows.Add(w);
            Animate(w, Motion.Open, icon);
        }

        private void Focus(Window w)
        {
            Remove(w);
            windows.Add(w);
            OS.Changed = true;
        }

        private void Close(Window w)
        {
            if (w.Process != null) OS.Processes.Kill(w.Process.Id);
            w.Process = null;
            int icon = IconOf(w);
            Animate(w, Motion.Close, icon);
            Remove(w);
            if (icon >= 0) apps[icon] = null;
        }

        /// <summary>Gelber Knopf: Das Fenster wandert ins Dock, ein Klick auf das Symbol holt es zurück.</summary>
        private void Minimize(Window w)
        {
            Animate(w, Motion.Minimize, IconOf(w));
            Remove(w);
        }

        /// <summary>Grüner Knopf: füllt die Fläche zwischen Menüleiste und Dock, ein zweiter Klick stellt die alte Größe her.</summary>
        private void Zoom(Window w)
        {
            Focus(w);
            Cancel(w);
            int x, y, width, height;
            if (w.Zoomed)
            {
                x = w.NormalX; y = w.NormalY; width = w.NormalWidth; height = w.NormalHeight;
            }
            else
            {
                // Ein angedocktes Fenster hat seine Größe davor schon gemerkt, zurück geht es dann auf diese.
                if (w.Snapped == 0) { w.NormalX = w.X; w.NormalY = w.Y; w.NormalWidth = w.Width; w.NormalHeight = w.Height; }
                ZoomArea(out x, out y, out width, out height);
            }
            w.Zoomed = !w.Zoomed;
            w.Snapped = 0;
            transitions.Add(new Transition(w, Motion.Zoom, Clock(), true, x, y, width, height));
            OS.Changed = true;
        }

        // Startet eine Animation für w. Eine laufende desselben Fensters endet sofort. icon: App des Fensters, für den Weg ins Dock.
        private void Animate(Window w, Motion kind, int icon)
        {
            Cancel(w);
            int x = icon >= 0 ? DockX + T.Space.S + icon * (T.Size.DockIcon + T.Space.M) : scene.Width / 2;
            transitions.Add(new Transition(w, kind, Clock(), kind == Motion.Open || kind == Motion.Restore || w == Active,
                x, DockY + T.Space.S, T.Size.DockIcon, T.Size.DockIcon));
            OS.Changed = true;
        }

        // Beendet eine laufende Animation von w sofort.
        private void Cancel(Window w)
        {
            for (int i = transitions.Count - 1; i >= 0; i--)
                if (transitions[i].Window == w) { transitions[i].Finish(); transitions.RemoveAt(i); }
        }

        private int IconOf(Window w)
        {
            for (int i = 0; i < apps.Length; i++)
                if (apps[i] == w) return i;
            return -1;
        }

        private int IndexOf(Window w)
        {
            for (int i = 0; i < windows.Count; i++)
                if (windows[i] == w) return i;
            return -1;
        }

        private void Remove(Window w)
        {
            int i = IndexOf(w);
            if (i >= 0) windows.RemoveAt(i);
            OS.Changed = true;
        }

        /// <summary>
        /// Bringt front auf den aktuellen Stand. Gibt false zurück, wenn sich nichts geändert hat,
        /// dann muss der Kernel nichts an den Bildschirm schicken.
        /// </summary>
        public bool Frame(Surface front)
        {
            // Uhr und Live-Fenster ändern sich ohne Eingabe.
            DateTime now = DateTime.Now;
            if (now.Minute != lastMinute) { lastMinute = now.Minute; OS.Changed = true; }
            if (now.Second != lastSecond)
            {
                lastSecond = now.Second;
                if (Settings.ShowSeconds && !Locked) OS.Changed = true;
                for (int i = 0; i < windows.Count; i++)
                    if (windows[i].Live) OS.Changed = true;
            }
            // Wurde der Prozess einer App beendet (kill, Signal), geht ihr Fenster zu, auch wenn es im Dock liegt.
            for (int i = 0; i < apps.Length; i++)
            {
                Process p = apps[i] != null ? apps[i].Process : null;
                if (p != null && (p.State == ProcessState.Terminated || OS.Processes.Get(p.Id) != p)) Close(apps[i]);
            }
            if (Locked && login.LockExpired(now)) OS.Changed = true;
            if (!wasLocked && Locked) login.Appear();
            if (Locked && login.NeedsFrame(Clock())) OS.Changed = true;
            if (wasLocked && !Locked && windows.Count == 0) Open(Icons.Terminal);
            wasLocked = Locked;

            // Laufende Animationen brauchen jedes Mal ein neues Bild, auch das erste nach ihrem Ende.
            ms = Clock();
            for (int i = transitions.Count - 1; i >= 0; i--)
            {
                transitions[i].Step(ms);
                if (transitions[i].Done(ms)) transitions.RemoveAt(i);
                OS.Changed = true;
            }

            // Als bewegt gilt auch ein Zeiger, der anders aussehen soll, als er auf dem Bildschirm steht (Größe umgeschaltet):
            // Er liegt nur auf front und geht in keine Kachel ein, sein Ausschnitt muss also eigens an den Bildschirm.
            // Gezeichnet wird ab der linken oberen Ecke des Zeigers: Beim Pfeil ist das die Spitze, beim Doppelpfeil liegt die Maus in der Mitte.
            int k = Settings.LargeCursor ? T.Size.CursorScale : 1;
            int cursorX = mouseX, cursorY = mouseY;
            if (shape != ArrowShape)
            {
                string[] rows = ResizeCursors[shape - 1];
                cursorX -= rows[0].Length / 2 * k;
                cursorY -= rows.Length / 2 * k;
            }
            bool moved = cursorX != drawnX || cursorY != drawnY || Settings.LargeCursor != drawnLarge || shape != drawnShape;
            if (!OS.Changed && !moved) return false;
            DirtyCount = 0;
            if (OS.Changed)
            {
                OS.Changed = false;
                // Erst vermessen, was sich gegenüber dem letzten Bild geändert hat, dann nur diese Bereiche neu zeichnen.
                // Ein getippter Buchstabe oder ein gezogenes Fenster kostet so einen Bruchteil des ganzen Bildes.
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                scene.Measure();
                Draw(now);
                DirtyCount = scene.Damage(Dirty);
                long middle = System.Diagnostics.Stopwatch.GetTimestamp();
                Measured += middle - start;
                for (int i = 0; i < 4 * DirtyCount; i += 4)
                {
                    scene.CopyRect(wallpaper, Dirty[i], Dirty[i + 1], Dirty[i + 2], Dirty[i + 3]);
                    scene.Limit(Dirty[i], Dirty[i + 1], Dirty[i + 2], Dirty[i + 3]);
                    Draw(now);
                    front.CopyRect(scene, Dirty[i], Dirty[i + 1], Dirty[i + 2], Dirty[i + 3]);
                }
                scene.Unlimit();
                Painted += System.Diagnostics.Stopwatch.GetTimestamp() - middle;
            }
            // Der Mauszeiger liegt nur auf front. Ist er gewandert oder wurde unter ihm neu gezeichnet, verschwindet er
            // an der alten Stelle und entsteht an der neuen.
            int size = CursorSize * T.Size.CursorScale; // reicht für beide Größen, auch direkt nach dem Umschalten
            bool hit = moved;
            for (int i = 0; i < 4 * DirtyCount && !hit; i += 4)
                hit = Dirty[i] < drawnX + size && Dirty[i] + Dirty[i + 2] > drawnX && Dirty[i + 1] < drawnY + size && Dirty[i + 1] + Dirty[i + 3] > drawnY;
            if (hit)
            {
                front.CopyRect(scene, drawnX, drawnY, size, size);
                DrawCursor(front, cursorX, cursorY, shape);
            }
            if (moved)
            {
                // Alte und neue Stelle in einem Ausschnitt. Steht er still, liegt alles Geänderte schon in den Bereichen oben.
                int n = 4 * DirtyCount++;
                // Ein Doppelpfeil am Rand des Bildschirms beginnt links oder oben davor, der Ausschnitt nicht.
                Dirty[n] = Math.Max(0, Math.Min(drawnX, cursorX));
                Dirty[n + 1] = Math.Max(0, Math.Min(drawnY, cursorY));
                Dirty[n + 2] = Math.Max(drawnX, cursorX) + size - Dirty[n];
                Dirty[n + 3] = Math.Max(drawnY, cursorY) + size - Dirty[n + 1];
                drawnX = cursorX;
                drawnY = cursorY;
                drawnLarge = Settings.LargeCursor;
                drawnShape = shape;
            }
            return DirtyCount > 0;
        }

        // Zeichnet alles über dem Hintergrund auf scene. Beim Vermessen entsteht dabei kein Pixel.
        private void Draw(DateTime now)
        {
            if (Locked)
            {
                login.Draw(scene);
                return;
            }
            for (int i = 0; i < windows.Count; i++)
            {
                if (windows[i] == dragging && snapEdges != 0) DrawSnapPreview();
                Transition t = IncomingFor(windows[i]);
                if (t != null) t.Draw(scene, ms);
                else windows[i].Draw(scene, windows[i] == Active);
            }
            DrawMenuBar(now);
            DrawDock();
            // Was geht, liegt obenauf: Beim Minimieren taucht das Fenster so sichtbar ins Dock.
            for (int i = 0; i < transitions.Count; i++)
                if (!transitions[i].Incoming) transitions[i].Draw(scene, ms);
        }

        // Die Fläche, an die das gezogene Fenster beim Loslassen andockt: durchscheinend in der Akzentfarbe, unter dem Fenster.
        private void DrawSnapPreview()
        {
            int x, y, width, height;
            SnapArea(snapEdges, out x, out y, out width, out height);
            scene.FillRoundRect(x, y, width, height, T.Radius.M, (T.Color.Accent & 0x00FFFFFF) | (T.Color.SnapAlpha << 24));
            scene.StrokeRoundRect(x, y, width, height, T.Radius.M, T.Color.Border);
        }

        private Transition IncomingFor(Window w)
        {
            for (int i = 0; i < transitions.Count; i++)
                if (transitions[i].Window == w && transitions[i].Incoming) return transitions[i];
            return null;
        }

        private void DrawMenuBar(DateTime now)
        {
            Surface s = scene;
            int textY = (T.Size.MenuBar - Fonts.Strong.LineHeight) / 2;
            s.FillRect(0, 0, s.Width, T.Size.MenuBar, T.Color.MenuBar);

            int logo = T.Size.MenuBar - 2 * T.Space.XS - 4;
            Icons.Draw(s, Icons.About, T.Space.L, (T.Size.MenuBar - logo) / 2, logo);
            string title = Active != null ? Active.Title : "Schreibtisch";
            s.DrawText(Fonts.Strong, title, T.Space.L + logo + T.Space.M, textY, T.Color.TextPrimary);

            string clock = Text.Date(now) + "  " + Text.Time(now);
            if (Settings.ShowSeconds) clock += ":" + Text.Two(now.Second);
            int x = s.Width - T.Space.L - Fonts.Body.Measure(clock);
            s.DrawText(Fonts.Body, clock, x, textY, T.Color.TextPrimary);
            string user = OS.Users.Current.Name;
            s.DrawText(Fonts.Body, user, x - T.Space.L - Fonts.Body.Measure(user), textY, T.Color.TextSecondary);
        }

        // Dock-Maße: Symbole mit Abstand M, innen S Rand, unten Platz für den Punkt „App läuft“.
        private int DockWidth { get { return Icons.Count * T.Size.DockIcon + (Icons.Count - 1) * T.Space.M + 2 * T.Space.S; } }
        private int DockHeight { get { return T.Size.DockIcon + T.Space.S + T.Space.M; } }
        private int DockX { get { return (scene.Width - DockWidth) / 2; } }
        private int DockY { get { return scene.Height - DockHeight - T.Space.S; } }

        private void DrawDock()
        {
            Surface s = scene;
            s.Shadow(DockX, DockY, DockWidth, DockHeight, T.Radius.L, T.Shadow.DockBlur, T.Shadow.DockOffset, T.Shadow.DockAlpha, T.Color.Shadow);
            s.FillRoundRect(DockX, DockY, DockWidth, DockHeight, T.Radius.L, T.Color.Dock);
            s.StrokeRoundRect(DockX, DockY, DockWidth, DockHeight, T.Radius.L, T.Color.Border);
            for (int i = 0; i < Icons.Count; i++)
            {
                int x = DockX + T.Space.S + i * (T.Size.DockIcon + T.Space.M);
                Icons.Draw(s, i, x, DockY + T.Space.S, T.Size.DockIcon);
                if (apps[i] != null)
                    s.FillCircle(x + T.Size.DockIcon / 2, DockY + T.Space.S + T.Size.DockIcon + T.Space.M / 2, T.Size.DockIndicator, T.Color.TextPrimary);
            }
        }

        private int DockIconAt(int x, int y)
        {
            if (y < DockY + T.Space.S || y >= DockY + T.Space.S + T.Size.DockIcon) return -1;
            for (int i = 0; i < Icons.Count; i++)
            {
                int ix = DockX + T.Space.S + i * (T.Size.DockIcon + T.Space.M);
                if (x >= ix && x < ix + T.Size.DockIcon) return i;
            }
            return -1;
        }

        private static void DrawCursor(Surface s, int x, int y, int shape)
        {
            int k = Settings.LargeCursor ? T.Size.CursorScale : 1;
            string[] rows = shape == ArrowShape ? Cursor : ResizeCursors[shape - 1];
            for (int j = 0; j < rows.Length; j++)
            {
                string row = rows[j];
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] == ' ') continue;
                    s.FillRect(x + i * k, y + j * k, k, k, row[i] == 'X' ? T.Color.CursorEdge : T.Color.CursorFill);
                }
            }
        }
    }
}
