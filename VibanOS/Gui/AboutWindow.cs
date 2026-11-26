using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Memory;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>„Über VibanOS“: Logo, Version und Eckdaten des Systems.</summary>
    public class AboutWindow : Window
    {
        private const int StartWidth = 340, StartHeight = 330; // kleiner passt der Inhalt nicht

        public AboutWindow()
        {
            Title = "Über " + OS.Name;
            Width = StartWidth;
            Height = StartHeight;
        }

        public override bool CanZoom { get { return true; } }
        public override int MinWidth { get { return StartWidth; } }
        public override int MinHeight { get { return StartHeight; } }

        protected override void DrawContent(Surface s, int x, int y, int w, int h)
        {
            // In einem größeren Fenster steht der Inhalt in der Mitte.
            int cx = x + w / 2, ty = y + T.Space.XL + (h - (StartHeight - T.Size.TitleBar)) / 2;
            Icons.Draw(s, Icons.About, cx - 32, ty, 64);
            ty += 64 + T.Space.M;
            s.DrawTextCentered(Fonts.Display, OS.Name, cx, ty, T.Color.TextPrimary);
            ty += Fonts.Display.LineHeight;
            s.DrawTextCentered(Fonts.Body, "Version " + OS.Version, cx, ty, T.Color.TextSecondary);
            ty += Fonts.Body.LineHeight + T.Space.L;

            s.FillRect(x + T.Space.XL, ty, w - 2 * T.Space.XL, 1, T.Color.Separator);
            ty += T.Space.M;
            ty = Row(s, x, w, ty, "Kernel", "COSMOS 3 (C#)");
            // Die Zeichenfläche ist so groß wie der Bildschirm, das ist die tatsächliche Auflösung.
            ty = Row(s, x, w, ty, "Anzeige", s.Width + " × " + s.Height);
            ty = Row(s, x, w, ty, "Simulierter RAM", PhysicalMemory.FrameCount * PhysicalMemory.FrameSize / 1024 + " KB");
            ty = Row(s, x, w, ty, "Dateisystem", Vfs.Available ? "FAT auf " + Vfs.Root : "nicht eingebunden");
            Row(s, x, w, ty, "Angemeldet als", OS.Users.Current != null ? OS.Users.Current.Name : "");
        }

        // Beschriftung rechtsbündig vor der Mitte, Wert linksbündig dahinter.
        private static int Row(Surface s, int x, int w, int y, string label, string value)
        {
            int middle = x + w / 2;
            s.DrawText(Fonts.Body, label, middle - T.Space.XS - Fonts.Body.Measure(label), y, T.Color.TextSecondary);
            s.DrawText(Fonts.Body, value, middle + T.Space.XS, y, T.Color.TextPrimary);
            return y + Fonts.Body.LineHeight + T.Space.XS;
        }
    }
}
