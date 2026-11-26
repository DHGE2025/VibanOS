using System;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using Cosmos.Kernel.System.Input.Layouts;
using VibanOS.Core;
using VibanOS.Design;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Gui;
using VibanOS.Interrupts;
using VibanOS.Platform;
using Sys = Cosmos.Kernel.System;

namespace VibanOS
{
    /// <summary>
    /// Einstiegspunkt unter COSMOS 3. Hier steht bewusst nur Verkabelung: Hardware einrichten,
    /// Tastatur und Maus an die Oberfläche weiterreichen, das fertige Bild anzeigen.
    /// Die eigentliche Logik liegt in Core/, Gui/ und den Teilsystemen.
    /// </summary>
    public class Kernel : Sys.Kernel
    {
        // So viele Durchläufe dürfen nacheinander scheitern, bevor der Kernel aufgibt.
        private const int MaxFailures = 3;

        // Pause je Durchlauf in Millisekunden. Gibt die CPU frei, statt sie in der Schleife zu verheizen.
        private const int FramePause = 10;

        private Canvas canvas;
        private Surface front;
        private Desktop desktop;

        protected override void BeforeRun()
        {
            // Dateisystem (VIB-2). Ohne formatierte Festplatte läuft das System trotzdem,
            // Dateibefehle melden dann, dass kein Dateisystem da ist. 'format' richtet eine leere Platte ein.
            Hardware.MountDisk();
            Vfs.Init(Hardware.DiskRoot);

            Fonts.Load();
            OS.Boot();

            // Auf dem Startbild von Limine legt der Bootloader die Auflösung fest (Bootloader/limine.conf). Eine umschaltbare
            // Grafik (VMware SVGA) startet mit 1024x768. Sie bekommt die Auflösung, die zum Bildschirm des Rechners passt,
            // den die Werkstatt QEMU mitgibt, sonst die Entwurfsgröße. Danach immer die echte Größe nehmen, nicht annehmen.
            canvas = Canvas.GetFullScreen();
            Resolution host = Settings.HostScreen();
            Resolution start = Settings.StartMode(Hardware.ScreenModes(), host);
            if (start.Width > 0 && (start.Width != canvas.Width || start.Height != canvas.Height))
                Hardware.SetScreenMode(start.Width, start.Height);
            Log.WriteString("[VibanOS] Bildschirm des Rechners " + host.Width + "x" + host.Height + ", Auflösung " + canvas.Width + "x" + canvas.Height + "\n");
            // Die Zeichenfläche schreibt direkt in den Puffer des Canvas, Display() bringt ihn auf den Bildschirm.
            front = new Surface(canvas.Width, canvas.Height, canvas.GetBuffer());
            MouseManager.SetScreenSize(canvas.Width, canvas.Height);
            // Deutsche Tastatur, in den Einstellungen umschaltbar.
            KeyboardManager.SetLayout(new DEStandardLayout());
            desktop = new Desktop(canvas.Width, canvas.Height);
        }

        /// <summary>
        /// Die Kernel-Schleife. COSMOS 3 ruft Run() zwar selbst immer wieder auf, schreibt dabei aber jedes
        /// Mal zwei Zeilen ins Protokoll. Deshalb läuft die Schleife hier und Run() kehrt erst beim Beenden zurück.
        /// </summary>
        protected override void Run()
        {
            int failures = 0;
            while (!Stopped)
            {
                try
                {
                    Frame();
                    failures = 0;
                }
                catch (Exception e)
                {
                    // Ein Fehler in einem Befehl oder Fenster soll nicht das ganze System anhalten.
                    Log.WriteString("[VibanOS] Fehler in der Kernel-Schleife: " + e.Message + "\n");
                    // Scheitert jeder Durchlauf, hilft Weitermachen nicht: COSMOS zeigt den Fehler dann an und hält an.
                    if (++failures >= MaxFailures) throw;
                    OS.Shell.Error("Interner Fehler: " + e.Message);
                }
                Thread.Sleep(FramePause);
            }
        }

        // Ein Durchlauf: Eingaben verteilen, Kern weiterschalten, bei Bedarf neu zeichnen.
        private void Frame()
        {
            KeyEvent key;
            while (KeyboardManager.TryReadKey(out key))
            {
                OS.Interrupts.Raise(InterruptManager.Keyboard);
                desktop.OnKey(Translate(key.Key), key.KeyChar);
            }
            desktop.OnMouse(MouseManager.X, MouseManager.Y, MouseManager.LeftButton);
            OS.Tick();
            // Die Einstellungen haben die Auflösung umgeschaltet: Der Canvas hat jetzt einen neuen, anders großen Puffer.
            if (canvas.Width != front.Width || canvas.Height != front.Height)
            {
                front = new Surface(canvas.Width, canvas.Height, canvas.GetBuffer());
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                desktop.Resize(canvas.Width, canvas.Height);
            }
            // Nur wenn sich etwas geändert hat, geht ein neues Bild an den Bildschirm, und nur der geänderte Teil.
            if (desktop.Frame(front)) Hardware.Present(front, desktop.DirtyX, desktop.DirtyY, desktop.DirtyWidth, desktop.DirtyHeight);
        }

        private static Gui.Key Translate(Sys.Input.Key key)
        {
            switch (key)
            {
                case Sys.Input.Key.Enter: return Gui.Key.Enter;
                case Sys.Input.Key.NumEnter: return Gui.Key.Enter;
                case Sys.Input.Key.Backspace: return Gui.Key.Backspace;
                case Sys.Input.Key.UpArrow: return Gui.Key.Up;
                case Sys.Input.Key.DownArrow: return Gui.Key.Down;
                case Sys.Input.Key.PageUp: return Gui.Key.PageUp;
                case Sys.Input.Key.PageDown: return Gui.Key.PageDown;
                case Sys.Input.Key.Tab: return Gui.Key.Tab;
                default: return Gui.Key.None;
            }
        }
    }
}
