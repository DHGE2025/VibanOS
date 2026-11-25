using System.Diagnostics;
using VibanOS.Graphics;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    public enum Motion { Open, Close, Minimize, Restore, Zoom }

    /// <summary>
    /// Eine laufende Fenster-Animation. Öffnen, Schließen, Minimieren und Zurückholen zeichnen ein Standbild
    /// des Fensters, das einmal am Anfang entsteht, gestreckt und durchscheinend. Das kostet je Bild nur ein
    /// Umkopieren, der Fensterinhalt wird nicht neu gezeichnet. Zoom verschiebt dagegen das echte Fenster,
    /// damit der Inhalt mitwächst.
    /// </summary>
    public class Transition
    {
        public readonly Window Window;
        public readonly Motion Kind;
        private readonly long start;
        private readonly int duration;
        // a: fester Anfang (Fenster beim Start oder Dock-Symbol), b: festes Ziel (Dock-Symbol oder Zoom-Fläche).
        private readonly int ax, ay, aw, ah, bx, by, bw, bh;
        private readonly Surface still;
        private readonly bool active;

        /// <summary>x, y, width, height: Dock-Symbol bei Minimize und Restore, Zielfläche bei Zoom, sonst ohne Bedeutung.</summary>
        public Transition(Window w, Motion kind, long now, bool active, int x, int y, int width, int height)
        {
            Window = w;
            Kind = kind;
            start = now;
            this.active = active;
            duration = kind == Motion.Open ? T.Motion.Open : kind == Motion.Close ? T.Motion.Close
                : kind == Motion.Zoom ? T.Motion.Zoom : T.Motion.Minimize;
            ax = w.X; ay = w.Y; aw = w.Width; ah = w.Height;
            bx = x; by = y; bw = width; bh = height;
            if (kind == Motion.Restore) { ax = x; ay = y; aw = width; ah = height; }
            if (kind == Motion.Zoom) return;

            still = new Surface(w.Width, w.Height);
            int wx = w.X, wy = w.Y;
            w.X = 0;
            w.Y = 0;
            w.DrawBody(still, active);
            w.X = wx;
            w.Y = wy;
        }

        /// <summary>Uhr in Millisekunden. COSMOS 3 rechnet Stopwatch über den Zeitstempelzähler der CPU.</summary>
        public static long Milliseconds()
        {
            long f = Stopwatch.Frequency, t = Stopwatch.GetTimestamp();
            if (f <= 0) return fallback += 50; // ohne Uhr: jede Animation ist nach wenigen Bildern fertig
            return t / f * 1000 + t % f * 1000 / f;
        }
        private static long fallback;

        /// <summary>Kommt das Fenster gerade erst dazu? Dann zeichnet die Animation es an seiner Stelle in der Fensterliste.</summary>
        public bool Incoming { get { return Kind == Motion.Open || Kind == Motion.Restore; } }

        public bool Done(long now) { return now - start >= duration; }

        // Fortschritt 0..1, schon mit Beschleunigung: Öffnen, Schließen und Zoom bremsen sanft ab,
        // der Weg ins Dock beschleunigt und bremst.
        private float Eased(long now)
        {
            float t = (float)(now - start) / duration;
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            if (Kind == Motion.Minimize || Kind == Motion.Restore)
                return t < 0.5f ? 4 * t * t * t : 1 - 4 * (1 - t) * (1 - t) * (1 - t);
            return 1 - (1 - t) * (1 - t) * (1 - t);
        }

        private static int Lerp(int a, int b, float t) { return a + (int)((b - a) * t + (b > a ? 0.5f : -0.5f)); }

        /// <summary>Zoom: setzt das Fenster auf die Zwischengröße. Bei done genau aufs Ziel.</summary>
        public void Step(long now)
        {
            if (Kind != Motion.Zoom) return;
            float e = Done(now) ? 1 : Eased(now);
            Window.X = Lerp(ax, bx, e);
            Window.Y = Lerp(ay, by, e);
            Window.Width = Lerp(aw, bw, e);
            Window.Height = Lerp(ah, bh, e);
        }

        /// <summary>Bricht ab, etwa weil das Fenster gleich eine neue Animation bekommt. Zoom springt ans Ziel.</summary>
        public void Finish() { Step(long.MaxValue / 2); }

        public void Draw(Surface s, long now)
        {
            if (still == null) return;
            float e = Eased(now);
            int x, y, w, h, alpha;
            if (Kind == Motion.Open || Kind == Motion.Close)
            {
                // Um die Mitte wachsen bzw. schrumpfen. Beim Öffnen zählt die aktuelle Lage, das Fenster kann schon gezogen werden.
                int cx = Kind == Motion.Open ? Window.X : ax, cy = Kind == Motion.Open ? Window.Y : ay;
                float grow = Kind == Motion.Open ? e : 1 - e;
                float scale = (T.Motion.StartScale + (100 - T.Motion.StartScale) * grow) / 100f;
                w = (int)(aw * scale);
                h = (int)(ah * scale);
                x = cx + (aw - w) / 2;
                y = cy + (ah - h) / 2;
                alpha = (int)(255 * grow);
            }
            else
            {
                // Minimize: vom Fenster zum Dock-Symbol. Restore: vom Dock-Symbol zur aktuellen Lage des Fensters.
                int tx = Kind == Motion.Minimize ? bx : Window.X, ty = Kind == Motion.Minimize ? by : Window.Y;
                int tw = Kind == Motion.Minimize ? bw : Window.Width, th = Kind == Motion.Minimize ? bh : Window.Height;
                x = Lerp(ax, tx, e);
                y = Lerp(ay, ty, e);
                w = Lerp(aw, tw, e);
                h = Lerp(ah, th, e);
                float toDock = Kind == Motion.Minimize ? e : 1 - e;
                alpha = 255 - (int)((255 - T.Motion.DockAlpha) * toDock);
            }
            int r = T.Radius.M * w / still.Width;
            int shadow = (active ? T.Shadow.WindowAlpha : T.Shadow.WindowAlphaInactive) * alpha / 255;
            s.Shadow(x, y, w, h, r, T.Shadow.WindowBlur, T.Shadow.WindowOffset, shadow, T.Color.Shadow);
            s.DrawScaled(still, x, y, w, h, r, alpha);
        }
    }
}
