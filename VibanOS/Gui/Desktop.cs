using System;
using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.Graphics;
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
                int ny = y - dragY;
                if (ny < T.Size.MenuBar) ny = T.Size.MenuBar; // Titelleiste bleibt unter der Menüleiste erreichbar
                if (dragging.X != x - dragX || dragging.Y != ny) OS.Changed = true;
                dragging.X = x - dragX;
                dragging.Y = ny;
            }
            if (!pressed) return;

            int icon = DockIconAt(x, y);
            if (icon >= 0) { Open(icon); return; }
            for (int i = windows.Count - 1; i >= 0; i--)
            {
                Window w = windows[i];
                if (!w.Contains(x, y)) continue;
                if (w.OnClose(x, y)) { Close(w); return; }
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

        /// <summary>Öffnet eine App aus dem Dock oder holt ihr Fenster nach vorn.</summary>
        public void Open(int icon)
        {
            if (apps[icon] != null) { Focus(apps[icon]); return; }
            Window w;
            if (icon == Icons.Terminal) w = new TerminalWindow();
            else if (icon == Icons.Activity) w = new ActivityWindow();
            else w = new AboutWindow();
            // Neue Fenster leicht versetzt, damit sie sich nicht genau überdecken
            w.X = (scene.Width - w.Width) / 2 + (icon - 1) * T.Space.XXL * 3;
            w.Y = T.Size.MenuBar + T.Space.XL + icon * T.Space.XXL;
            apps[icon] = w;
            windows.Add(w);
            OS.Changed = true;
        }

        private void Focus(Window w)
        {
            Remove(w);
            windows.Add(w);
            OS.Changed = true;
        }

        private void Close(Window w)
        {
            Remove(w);
            for (int i = 0; i < apps.Length; i++)
                if (apps[i] == w) apps[i] = null;
            OS.Changed = true;
        }

        private void Remove(Window w)
        {
            for (int i = 0; i < windows.Count; i++)
                if (windows[i] == w) { windows.RemoveAt(i); return; }
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
            if (Locked && login.LockExpired(now)) OS.Changed = true;
            if (wasLocked && !Locked && windows.Count == 0) Open(Icons.Terminal);
            wasLocked = Locked;

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
            for (int i = 0; i < windows.Count; i++) windows[i].Draw(scene, windows[i] == Active);
            DrawMenuBar(now);
            DrawDock();
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
