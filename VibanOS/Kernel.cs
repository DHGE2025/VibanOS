using System;
using Cosmos.Core.Memory;
using Cosmos.System.Graphics;
using IL2CPU.API.Attribs;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Gui;
using Sys = Cosmos.System;
using T = VibanOS.Design.Tokens;

namespace VibanOS
{
    /// <summary>
    /// Einstiegspunkt unter COSMOS. Hier steht bewusst nur Verkabelung: Hardware einrichten,
    /// Tastatur und Maus an die Oberfläche weiterreichen, das fertige Bild anzeigen.
    /// Die eigentliche Logik liegt in Core/, Gui/ und den Teilsystemen.
    /// </summary>
    public class Kernel : Sys.Kernel
    {
        // Schrift-Atlanten, als eingebettete Ressourcen (siehe VibanOS.csproj und tools/make-fonts.py).
        // IL2CPU füllt die Felder beim Bauen, der C#-Compiler sieht das nicht.
#pragma warning disable CS0649
        [ManifestResourceStream(ResourceName = "VibanOS.Resources.body.vfont")] private static byte[] fontBody;
        [ManifestResourceStream(ResourceName = "VibanOS.Resources.strong.vfont")] private static byte[] fontStrong;
        [ManifestResourceStream(ResourceName = "VibanOS.Resources.caption.vfont")] private static byte[] fontCaption;
        [ManifestResourceStream(ResourceName = "VibanOS.Resources.display.vfont")] private static byte[] fontDisplay;
        [ManifestResourceStream(ResourceName = "VibanOS.Resources.mono.vfont")] private static byte[] fontMono;
#pragma warning restore CS0649

        private Canvas canvas;
        private Bitmap frame;
        private Surface front;
        private Desktop desktop;
        private int frames;

        protected override void BeforeRun()
        {
            // Dateisystem (VIB-2). Ohne angehängte FAT32-Festplatte läuft das System trotzdem,
            // Dateibefehle melden dann, dass kein Dateisystem da ist.
            try
            {
                Sys.FileSystem.VFS.VFSManager.RegisterVFS(new Sys.FileSystem.CosmosVFS());
            }
            catch (Exception) { }
            Vfs.Init(@"0:\");

            Fonts.Load(fontBody, fontStrong, fontCaption, fontDisplay, fontMono);
            OS.Boot();

            canvas = FullScreenCanvas.GetFullScreenCanvas(new Mode(T.Size.ScreenWidth, T.Size.ScreenHeight, ColorDepth.ColorDepth32));
            // Die Oberfläche zeichnet direkt in den Puffer dieser Bitmap, DrawImage kopiert ihn auf den Bildschirm.
            frame = new Bitmap(T.Size.ScreenWidth, T.Size.ScreenHeight, ColorDepth.ColorDepth32);
            front = new Surface(T.Size.ScreenWidth, T.Size.ScreenHeight, frame.RawData);
            Sys.MouseManager.ScreenWidth = T.Size.ScreenWidth;
            Sys.MouseManager.ScreenHeight = T.Size.ScreenHeight;
            desktop = new Desktop(T.Size.ScreenWidth, T.Size.ScreenHeight);
        }

        protected override void Run()
        {
            Sys.KeyEvent key;
            while (Sys.KeyboardManager.TryReadKey(out key))
                desktop.OnKey(Translate(key.Key), key.KeyChar);
            desktop.OnMouse((int)Sys.MouseManager.X, (int)Sys.MouseManager.Y, Sys.MouseManager.MouseState == Sys.MouseState.Left);

            OS.Tick();

            if (desktop.Frame(front))
            {
                canvas.DrawImage(frame, 0, 0);
                canvas.Display();
            }
            // COSMOS räumt den Heap nicht von selbst auf.
            if (++frames % 20 == 0) Heap.Collect();
        }

        private static Key Translate(Sys.ConsoleKeyEx key)
        {
            switch (key)
            {
                case Sys.ConsoleKeyEx.Enter: return Key.Enter;
                case Sys.ConsoleKeyEx.NumEnter: return Key.Enter;
                case Sys.ConsoleKeyEx.Backspace: return Key.Backspace;
                case Sys.ConsoleKeyEx.UpArrow: return Key.Up;
                case Sys.ConsoleKeyEx.DownArrow: return Key.Down;
                case Sys.ConsoleKeyEx.Tab: return Key.Tab;
                default: return Key.None;
            }
        }
    }
}
