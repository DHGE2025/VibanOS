using VibanOS.Graphics;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>Sondertasten, unabhängig von COSMOS. Kernel.cs übersetzt die Tastatur hierher.</summary>
    public enum Key { None, Enter, Backspace, Up, Down, Tab, PageUp, PageDown }

    /// <summary>
    /// Basisklasse aller Fenster: Rahmen, Titelleiste, Schatten und die drei Fensterknöpfe.
    /// Eine App leitet davon ab und zeichnet nur ihren Inhalt.
    /// </summary>
    public abstract class Window
    {
        public int X, Y, Width, Height;
        public string Title = "";

        /// <summary>true: Inhalt ändert sich von selbst und wird jede Sekunde neu gezeichnet.</summary>
        public virtual bool Live { get { return false; } }

        protected virtual uint Background { get { return T.Color.Window; } }

        /// <summary>Zeichnet den Inhalt in den Bereich unter der Titelleiste. Der Bereich ist abgeschnitten.</summary>
        protected abstract void DrawContent(Surface s, int x, int y, int w, int h);

        public virtual void OnKey(Key key, char c) { }

        /// <summary>Mausklick in den Inhalt (Bildschirmkoordinaten). Titelleiste und Knöpfe erledigt der Schreibtisch.</summary>
        public virtual void OnClick(int px, int py) { }

        public bool Contains(int px, int py)
        {
            return px >= X && py >= Y && px < X + Width && py < Y + Height;
        }

        public bool InTitleBar(int px, int py)
        {
            return Contains(px, py) && py < Y + T.Size.TitleBar;
        }

        /// <summary>Trifft der Punkt den roten Schließen-Knopf?</summary>
        public bool OnClose(int px, int py)
        {
            int cx = ControlX(0) + T.Size.Control / 2, cy = Y + T.Size.TitleBar / 2;
            int dx = px - cx, dy = py - cy, r = T.Size.Control / 2 + 2;
            return dx * dx + dy * dy <= r * r;
        }

        private int ControlX(int index)
        {
            return X + T.Space.M + index * (T.Size.Control + T.Size.ControlGap);
        }

        public void Draw(Surface s, bool active)
        {
            int r = T.Radius.M, bar = T.Size.TitleBar;
            s.Shadow(X, Y, Width, Height, r, T.Shadow.WindowBlur, T.Shadow.WindowOffset,
                active ? T.Shadow.WindowAlpha : T.Shadow.WindowAlphaInactive, T.Color.Shadow);

            // Titelleiste und Inhalt: erst die ganze Form in Leistenfarbe, dann der Inhalt darüber.
            s.FillRoundRect(X, Y, Width, Height, r, T.Color.TitleBar);
            s.FillRoundRect(X, Y + bar, Width, Height - bar, r, Background);
            s.FillRect(X, Y + bar, Width, r, Background);
            s.FillRect(X, Y + bar - 1, Width, 1, T.Color.Separator);
            s.StrokeRoundRect(X, Y, Width, Height, r, T.Color.Border);

            int cy = Y + bar / 2;
            s.FillCircle(ControlX(0) + T.Size.Control / 2, cy, T.Size.Control, active ? T.Color.Close : T.Color.ControlInactive);
            s.FillCircle(ControlX(1) + T.Size.Control / 2, cy, T.Size.Control, active ? T.Color.Minimize : T.Color.ControlInactive);
            s.FillCircle(ControlX(2) + T.Size.Control / 2, cy, T.Size.Control, active ? T.Color.Zoom : T.Color.ControlInactive);
            s.DrawTextCentered(Fonts.Strong, Title, X + Width / 2, Y + (bar - Fonts.Strong.LineHeight) / 2,
                active ? T.Color.TextPrimary : T.Color.TextTertiary);

            s.SetClip(X + 1, Y + bar, Width - 2, Height - bar - 1);
            DrawContent(s, X, Y + bar, Width, Height - bar);
            s.ResetClip();
        }
    }
}
