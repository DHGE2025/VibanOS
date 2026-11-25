using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Platform;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>
    /// Systemeinstellungen: links die Seiten, rechts ihr Inhalt in Gruppen. Alles gilt sofort.
    /// Anzeige (Auflösung), Erscheinungsbild (Akzentfarbe, Sekunden), Tastatur und Maus (Belegung, großer Zeiger).
    /// </summary>
    public class SettingsWindow : Window
    {
        private const int Display = 0, Appearance = 1, Input = 2;
        private static readonly string[] Pages = { "Anzeige", "Erscheinungsbild", "Tastatur & Maus" };
        private static readonly uint[] PageColors = { T.Color.WallBlob4, T.Color.WallBlob1, T.Color.Raised };
        private static readonly uint[] Accents =
        {
            T.Color.AccentBlue, T.Color.AccentPurple, T.Color.AccentPink, T.Color.AccentOrange, T.Color.AccentGreen, T.Color.AccentGraphite
        };

        // Was klickbar ist, merkt sich DrawContent, relativ zum Inhaltsbereich. So stimmt es auch, wenn eine Animation
        // das Fenster an anderer Stelle in ein Standbild zeichnet. Seiten 0..2, sonst Bereich plus Index.
        internal const int PickMode = 100, PickAccent = 200, ToggleSeconds = 300, ToggleCursor = 301, PickGerman = 400, PickUs = 401;

        // Auswahl der Auflösungen: kleiner passen die Fenster nicht, größer wird das Zeichnen ohne Grafikbeschleunigung zäh.
        private const int MinWidth = 800, MinHeight = 600, MaxPixels = 1920 * 1200;
        private const int ModeColumns = 3;
        private const int SegmentWidth = 72;  // ein Feld der Auswahl Deutsch/US
        private const int RowHeight = T.Size.Field + T.Space.M;

        private struct Hot { public int X, Y, W, H, Action; }

        private readonly List<Hot> hot = new List<Hot>();
        private int originX, originY; // linke obere Ecke des Inhalts beim Zeichnen
        private readonly List<Resolution> modes = new List<Resolution>();
        private int page;
        private string error = "";

        public SettingsWindow()
        {
            Title = "Systemeinstellungen";
            Width = T.Size.Sidebar + 460;
            Height = T.Size.TitleBar + 380;
            List<Resolution> all = Hardware.ScreenModes();
            // Kann die Grafik nicht umschalten, steht hier nur die eine Auflösung. Die zeigen wir immer.
            for (int i = 0; i < all.Count; i++)
            {
                Resolution r = all[i];
                if (all.Count == 1 || (r.Width >= MinWidth && r.Height >= MinHeight && r.Width * r.Height <= MaxPixels)) modes.Add(r);
            }
        }

        public override void OnClick(int px, int py)
        {
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
        }

        private void Do(int action)
        {
            error = "";
            if (action < Pages.Length) { page = action; return; }
            if (action >= PickAccent && action < PickAccent + Accents.Length) T.Color.Accent = Accents[action - PickAccent];
            else if (action >= PickMode && action < PickMode + modes.Count)
            {
                Resolution r = modes[action - PickMode];
                if (!Hardware.SetScreenMode(r.Width, r.Height)) error = "Diese Auflösung schafft die Grafikkarte nicht.";
            }
            else if (action == ToggleSeconds) Settings.ShowSeconds = !Settings.ShowSeconds;
            else if (action == ToggleCursor) Settings.LargeCursor = !Settings.LargeCursor;
            else if (action == PickGerman || action == PickUs)
            {
                Settings.GermanKeyboard = action == PickGerman;
                Hardware.SetKeyboardLayout(Settings.GermanKeyboard);
            }
            else return;
            if (!Settings.Save(OS.Users.Current) && error == "") error = Vfs.Available
                ? "Die Einstellungen ließen sich nicht speichern." : "Ohne Festplatte gelten die Einstellungen nur bis zum Ausschalten.";
        }

        /// <summary>Für den Selbsttest: Mitte der Klickfläche einer Aktion beim letzten Zeichnen, oder null.</summary>
        internal int[] CenterOf(int action)
        {
            for (int i = 0; i < hot.Count; i++)
                if (hot[i].Action == action) return new[] { X + hot[i].X + hot[i].W / 2, Y + T.Size.TitleBar + hot[i].Y + hot[i].H / 2 };
            return null;
        }

        /// <summary>Für den Selbsttest: die Fehlermeldung unter den Auflösungen.</summary>
        internal string Error { get { return error; } }

        /// <summary>Für den Selbsttest: die angebotenen Auflösungen.</summary>
        internal List<Resolution> Modes { get { return modes; } }

        private void AddHot(int x, int y, int w, int h, int action)
        {
            hot.Add(new Hot { X = x - originX, Y = y - originY, W = w, H = h, Action = action });
        }

        protected override void DrawContent(Surface s, int x, int y, int w, int h)
        {
            hot.Clear();
            originX = x;
            originY = y;
            DrawSidebar(s, x, y, h);

            int left = x + T.Size.Sidebar + T.Space.XL, inner = w - T.Size.Sidebar - 2 * T.Space.XL;
            int ty = y + T.Space.XL;
            s.DrawText(Fonts.Display, Pages[page], left, ty, T.Color.TextPrimary);
            ty += Fonts.Display.LineHeight + T.Space.L;

            if (page == Display) DrawDisplay(s, left, ty, inner);
            else if (page == Appearance)
            {
                Group(s, left, ty, inner, 2);
                Label(s, "Akzentfarbe", left, ty);
                DrawAccents(s, left + inner - T.Space.L, ty + RowHeight / 2);
                Label(s, "Sekunden in der Menüleiste", left, ty + RowHeight);
                Toggle(s, left + inner - T.Space.L - T.Size.ToggleWidth, ty + RowHeight, Settings.ShowSeconds, ToggleSeconds);
            }
            else
            {
                Group(s, left, ty, inner, 2);
                Label(s, "Tastaturbelegung", left, ty);
                Segments(s, left + inner - T.Space.L - 2 * SegmentWidth, ty, "Deutsch", "US", Settings.GermanKeyboard);
                Label(s, "Großer Mauszeiger", left, ty + RowHeight);
                Toggle(s, left + inner - T.Space.L - T.Size.ToggleWidth, ty + RowHeight, Settings.LargeCursor, ToggleCursor);
            }
            if (error != "") s.DrawText(Fonts.Body, error, left, y + h - T.Space.XL - Fonts.Body.LineHeight, T.Color.Danger);
        }

        private void DrawSidebar(Surface s, int x, int y, int h)
        {
            // Unten links rund wie das Fenster, rechts gerade. Der obere Teil liegt außerhalb des Ausschnitts.
            int r = T.Radius.M, bar = T.Size.Sidebar;
            s.FillRoundRect(x, y - r, bar, h + r, r, T.Color.TitleBar);
            s.FillRect(x + bar - r, y, r, h, T.Color.TitleBar);
            s.FillRect(x + bar, y, 1, h, T.Color.Separator);

            int rowX = x + T.Space.S, rowW = bar - 2 * T.Space.S, ty = y + T.Space.S;
            for (int i = 0; i < Pages.Length; i++)
            {
                bool selected = i == page;
                if (selected) s.FillRoundRect(rowX, ty, rowW, T.Size.Field, T.Radius.S, T.Color.Accent);
                int icon = T.Size.Swatch;
                s.FillRoundRect(rowX + T.Space.S, ty + (T.Size.Field - icon) / 2, icon, icon, T.Radius.S, PageColors[i]);
                s.StrokeRoundRect(rowX + T.Space.S, ty + (T.Size.Field - icon) / 2, icon, icon, T.Radius.S, T.Color.Border);
                s.DrawText(Fonts.Body, Pages[i], rowX + 2 * T.Space.S + icon, ty + (T.Size.Field - Fonts.Body.LineHeight) / 2,
                    selected ? T.Color.TextOnAccent : T.Color.TextPrimary);
                AddHot(rowX, ty, rowW, T.Size.Field, i);
                ty += T.Size.Field + T.Space.XS;
            }
        }

        private void DrawDisplay(Surface s, int left, int ty, int inner)
        {
            s.DrawText(Fonts.Caption, "AUFLÖSUNG", left, ty, T.Color.TextSecondary);
            ty += Fonts.Caption.LineHeight + T.Space.S;

            // Nicht die Zeichenfläche fragen: Beim Öffnen zeichnet die Animation das Fenster in ein Standbild seiner Größe.
            Resolution screen = Hardware.ScreenSize();
            int chipW = (inner - (ModeColumns - 1) * T.Space.S) / ModeColumns;
            for (int i = 0; i < modes.Count; i++)
            {
                Resolution r = modes[i];
                int cx = left + i % ModeColumns * (chipW + T.Space.S), cy = ty + i / ModeColumns * (T.Size.Field + T.Space.S);
                bool current = r.Width == screen.Width && r.Height == screen.Height;
                string label = r.Width + " × " + r.Height;
                int textY = cy + (T.Size.Field - Fonts.Body.LineHeight) / 2;
                if (current)
                {
                    s.FillRoundRect(cx, cy, chipW, T.Size.Field, T.Radius.S, T.Color.Accent);
                    s.DrawTextCentered(Fonts.Strong, label, cx + chipW / 2, textY, T.Color.TextOnAccent);
                }
                else
                {
                    s.FillRoundRect(cx, cy, chipW, T.Size.Field, T.Radius.S, T.Color.Grouped);
                    s.StrokeRoundRect(cx, cy, chipW, T.Size.Field, T.Radius.S, T.Color.Border);
                    s.DrawTextCentered(Fonts.Body, label, cx + chipW / 2, textY, T.Color.TextPrimary);
                    AddHot(cx, cy, chipW, T.Size.Field, PickMode + i);
                }
            }
            ty += (modes.Count + ModeColumns - 1) / ModeColumns * (T.Size.Field + T.Space.S) + T.Space.S;

            s.DrawText(Fonts.Body, "Grafik: " + Hardware.ScreenName(), left, ty, T.Color.TextSecondary);
            ty += Fonts.Body.LineHeight + T.Space.XS;
            if (modes.Count <= 1)
            {
                s.DrawText(Fonts.Body, "Diese Grafikkarte kann die Auflösung nicht umschalten.", left, ty, T.Color.TextSecondary);
                ty += Fonts.Body.LineHeight;
                s.DrawText(Fonts.Body, "Dazu VibanOS mit -vga vmware starten (siehe README).", left, ty, T.Color.TextSecondary);
                ty += Fonts.Body.LineHeight + T.Space.XS;
            }
        }

        private void DrawAccents(Surface s, int right, int centerY)
        {
            int d = T.Size.Swatch, step = d + T.Space.S;
            int x0 = right - Accents.Length * step + T.Space.S;
            for (int i = 0; i < Accents.Length; i++)
            {
                int cx = x0 + i * step + d / 2;
                if (Accents[i] == T.Color.Accent)
                {
                    int ring = d + T.Space.S;
                    s.StrokeRoundRect(cx - ring / 2, centerY - ring / 2, ring, ring, ring / 2, T.Color.TextPrimary);
                }
                s.FillCircle(cx, centerY, d, Accents[i]);
                AddHot(cx - step / 2, centerY - step / 2, step, step, PickAccent + i);
            }
        }

        private static void Group(Surface s, int x, int y, int w, int rows)
        {
            s.FillRoundRect(x, y, w, rows * RowHeight, T.Radius.M, T.Color.Grouped);
            s.StrokeRoundRect(x, y, w, rows * RowHeight, T.Radius.M, T.Color.Border);
            for (int i = 1; i < rows; i++) s.FillRect(x + T.Space.L, y + i * RowHeight, w - 2 * T.Space.L, 1, T.Color.Separator);
        }

        private static void Label(Surface s, string text, int left, int rowY)
        {
            s.DrawText(Fonts.Body, text, left + T.Space.L, rowY + (RowHeight - Fonts.Body.LineHeight) / 2, T.Color.TextPrimary);
        }

        private void Toggle(Surface s, int x, int rowY, bool on, int action)
        {
            int w = T.Size.ToggleWidth, h = T.Size.ToggleHeight, y = rowY + (RowHeight - h) / 2, knob = h - T.Space.XS;
            s.FillRoundRect(x, y, w, h, h / 2, on ? T.Color.Accent : T.Color.Track);
            s.FillCircle(on ? x + w - h / 2 : x + h / 2, y + h / 2, knob, T.Color.TextOnAccent);
            AddHot(x, y, w, h, action);
        }

        // Zwei Felder nebeneinander, das gewählte ist erhöht. Links steht für PickGerman, rechts für PickUs.
        private void Segments(Surface s, int x, int rowY, string a, string b, bool first)
        {
            int h = T.Size.ToggleHeight, y = rowY + (RowHeight - h) / 2, textY = y + (h - Fonts.Body.LineHeight) / 2;
            s.FillRoundRect(x, y, 2 * SegmentWidth, h, T.Radius.S, T.Color.Sunken);
            s.FillRoundRect(first ? x : x + SegmentWidth, y, SegmentWidth, h, T.Radius.S, T.Color.Raised);
            s.DrawTextCentered(first ? Fonts.Strong : Fonts.Body, a, x + SegmentWidth / 2, textY, first ? T.Color.TextPrimary : T.Color.TextSecondary);
            s.DrawTextCentered(first ? Fonts.Body : Fonts.Strong, b, x + SegmentWidth * 3 / 2, textY, first ? T.Color.TextSecondary : T.Color.TextPrimary);
            AddHot(x, y, SegmentWidth, h, PickGerman);
            AddHot(x + SegmentWidth, y, SegmentWidth, h, PickUs);
        }
    }
}
