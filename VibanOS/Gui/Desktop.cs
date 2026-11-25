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

        private readonly Surface wallpaper;
        private readonly Surface scene;
        private readonly LoginScreen login = new LoginScreen();
        private readonly Window[] apps = new Window[Icons.Count];   // offenes Fenster je App oder null
        private readonly List<Window> windows = new List<Window>(); // hinten nach vorn, das letzte ist aktiv
        private readonly List<Transition> transitions = new List<Transition>();

        /// <summary>Uhr der Animationen in Millisekunden. Der Selbsttest setzt hier eine eigene ein.</summary>
        public Func<long> Clock = Transition.Milliseconds;
        private long ms;

        private int mouseX, mouseY, drawnX = -1, drawnY = -1;
        private bool mouseDown;
        private Window dragging;
        private int dragX, dragY;
        private int lastSecond = -1, lastMinute = -1;
        private bool wasLocked = true;

        public Desktop(int width, int height)
        {
            wallpaper = new Surface(width, height);
            scene = new Surface(width, height);
            Wallpaper.Render(wallpaper);
        }

        /// <summary>true, solange eine Fenster-Animation läuft.</summary>
        public bool Animating { get { return transitions.Count > 0; } }

        private static bool Locked { get { return OS.Users.Current == null; } }

        /// <summary>Das vorderste Fenster. Es bekommt die Tastatur. null, wenn keines offen ist.</summary>
        public Window Active { get { return windows.Count > 0 ? windows[windows.Count - 1] : null; } }

        public void OnKey(Key key, char c)
        {
            if (Locked) login.OnKey(key, c);
            else if (Active != null) Active.OnKey(key, c);
            OS.Changed = true;
        }

        public void OnMouse(int x, int y, bool down)
        {
            bool pressed = down && !mouseDown;
            mouseX = x;
            mouseY = y;
            mouseDown = down;
            if (Locked) return;

            if (!down) dragging = null;
            if (dragging != null)
            {
                // Ein Stück der Titelleiste bleibt immer erreichbar: seitlich, über dem Dock und unter der Menüleiste.
                int nx = x - dragX, ny = y - dragY, keep = T.Size.DockIcon;
                if (nx < keep - dragging.Width) nx = keep - dragging.Width;
                if (nx > scene.Width - keep) nx = scene.Width - keep;
                if (ny > DockY - T.Size.TitleBar) ny = DockY - T.Size.TitleBar;
                if (ny < T.Size.MenuBar) ny = T.Size.MenuBar;
                if (dragging.X != nx || dragging.Y != ny) OS.Changed = true;
                dragging.X = nx;
                dragging.Y = ny;
            }
            if (!pressed) return;

            int icon = DockIconAt(x, y);
            if (icon >= 0) { Open(icon); return; }
            // Ein Klick auf den Rand des Docks trifft kein Fenster darunter.
            if (x >= DockX && x < DockX + DockWidth && y >= DockY && y < DockY + DockHeight) return;
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window w = windows[i];
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
                }
                return;
            }
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
            if (icon == Icons.Terminal) { w = new TerminalWindow(); name = "terminal"; }
            else if (icon == Icons.Activity) { w = new ActivityWindow(); name = "aktivität"; }
            else { w = new AboutWindow(); name = "info"; }
            // Jede offene App ist ein Prozess, sonst fehlt sie in ps und in der Aktivitätsanzeige.
            w.Process = OS.Processes.Start(new AppProcess(name));
            // Neue Fenster leicht versetzt, damit sie sich nicht genau überdecken
            w.X = (scene.Width - w.Width) / 2 + (icon - 1) * T.Space.XXL * 3;
            w.Y = T.Size.MenuBar + T.Space.XL + icon * T.Space.XXL;
            // Auf einem kleinen Bildschirm bleiben Fensterknöpfe und Zeilenanfänge sichtbar.
            if (w.X < T.Space.M) w.X = T.Space.M;
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
                w.NormalX = w.X; w.NormalY = w.Y; w.NormalWidth = w.Width; w.NormalHeight = w.Height;
                x = T.Space.S;
                y = T.Size.MenuBar + T.Space.S;
                width = scene.Width - 2 * T.Space.S;
                height = DockY - T.Space.S - y;
            }
            w.Zoomed = !w.Zoomed;
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

            if (!OS.Changed && mouseX == drawnX && mouseY == drawnY) return false;
            if (OS.Changed)
            {
                OS.Changed = false;
                Render(now);
                front.CopyFrom(scene);
            }
            else
            {
                front.CopyRect(scene, drawnX, drawnY, CursorSize, CursorSize); // alten Mauszeiger entfernen
            }
            DrawCursor(front, mouseX, mouseY);
            drawnX = mouseX;
            drawnY = mouseY;
            return true;
        }

        private void Render(DateTime now)
        {
            scene.CopyFrom(wallpaper);
            if (Locked)
            {
                login.Draw(scene);
                return;
            }
            for (int i = 0; i < windows.Count; i++)
            {
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

        private static void DrawCursor(Surface s, int x, int y)
        {
            for (int j = 0; j < Cursor.Length; j++)
            {
                string row = Cursor[j];
                for (int i = 0; i < row.Length; i++)
                {
                    if (row[i] == ' ') continue;
                    s.FillRect(x + i, y + j, 1, 1, row[i] == 'X' ? T.Color.CursorEdge : T.Color.CursorFill);
                }
            }
        }
    }
}
