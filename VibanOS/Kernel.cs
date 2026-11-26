using System;
using System.Diagnostics;
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
        private Resolution host;      // Bildschirm des Rechners laut Werkstatt, 0x0: unbekannt
        private int canvasHeight;     // Höhe des Canvas, zu der front gehört
        private bool stretched;       // QEMU streckt das Bild: immer das ganze anzeigen
        // Für die Zeile im Protokoll: laufende Sekunde, ihre Bilder und wie lange sie brauchten (Einheiten von Stopwatch).
        private long second, slowest, busy, shown;
        private int drawn;

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
            host = Settings.HostScreen();
            Resolution start = Settings.StartMode(Hardware.ScreenModes(), host);
            if (start.Width > 0 && (start.Width != canvas.Width || start.Height != canvas.Height))
                Hardware.SetScreenMode(start.Width, start.Height);
            // Retina: Hat der Bildschirm doppelt so viele Pixel wie der Rechner Punkte, zeichnet alles in doppelter Auflösung.
            // Die Oberfläche rechnet weiter in Punkten, nur die Flächen haben viermal so viele Pixel (Surface.Scale).
            Surface.DefaultScale = Settings.ScaleFor(new Resolution(canvas.Width, canvas.Height), host);
            Attach();
            Log.WriteString("[VibanOS] Bildschirm des Rechners " + host.Width + "x" + host.Height + ", Auflösung " + canvas.Width + "x" + canvas.Height
                + ", gezeigt " + front.PixelWidth + "x" + front.PixelHeight + (stretched ? " gestreckt" : "") + ", Faktor " + Surface.DefaultScale + "\n");
            // Deutsche Tastatur, in den Einstellungen umschaltbar.
            KeyboardManager.SetLayout(new DEStandardLayout());
            desktop = new Desktop(front.Width, front.Height);
        }

        // Legt die Zeichenfläche über den Puffer des Canvas, Hardware.Present bringt ihn auf den Bildschirm. Sie endet nach
        // den Zeilen, die ins Fenster von QEMU passen (Settings.Rows), der Rest des Puffers bleibt ungenutzt.
        private void Attach()
        {
            int scale = Surface.DefaultScale;
            int rows = Settings.Rows(new Resolution(canvas.Width, canvas.Height), host, scale);
            if (rows != canvas.Height && !Hardware.CropScreen(rows)) rows = canvas.Height;
            stretched = Settings.Stretched(canvas.Width, rows, host, scale);
            front = new Surface(canvas.GetBuffer(), canvas.Width, rows, scale);
            canvasHeight = canvas.Height;
            MouseManager.SetScreenSize(front.Width, front.Height);
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
                long began = Transition.Milliseconds();
                bool ran = false;
                try
                {
                    ran = Frame();
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
                // Nach einem Durchlauf, der selbst schon so lange gebraucht hat (ein großes Stück neu gezeichnet), sofort
                // weiter: Die Pause käme sonst zu jedem Bild dazu, und ein gezogenes Fenster ruckelt. Auch nach einem
                // Durchlauf, in dem ein Prozess die CPU hatte und der Kernel beim Wechsel schon gewartet hat (siehe Frame).
                if (Transition.Milliseconds() - began < (ran ? 1 : FramePause) || Stopwatch.Frequency <= 0) Thread.Sleep(FramePause);
            }
        }

        // Ein Durchlauf: Eingaben verteilen, bei Bedarf neu zeichnen, dann bekommt ein Prozess die CPU. true: Einer war dran.
        private bool Frame()
        {
            KeyEvent key;
            while (KeyboardManager.TryReadKey(out key))
            {
                OS.Interrupts.Raise(InterruptManager.Keyboard);
                desktop.OnKey(Translate(key.Key), key.KeyChar);
            }
            desktop.OnMouse(MouseManager.X, MouseManager.Y, MouseManager.LeftButton);
            // Die Einstellungen haben die Auflösung umgeschaltet: Der Canvas hat jetzt einen neuen, anders großen Puffer.
            if (canvas.Width != front.PixelWidth || canvas.Height != canvasHeight)
            {
                Attach();
                desktop.Resize(front.Width, front.Height);
            }
            // Nur wenn sich etwas geändert hat, geht ein neues Bild an den Bildschirm, und nur die geänderten Teile.
            long before = Stopwatch.GetTimestamp();
            if (desktop.Frame(front))
            {
                long drawnAt = Stopwatch.GetTimestamp();
                if (stretched)
                {
                    // QEMU zeichnet dann ohnehin das ganze Bild neu. Ein Aufruf mit dem Rechteck um alle Ausschnitte, sonst
                    // ginge je Ausschnitt ein ganzes Bild an die Grafikkarte.
                    int x, y, width, height;
                    desktop.DirtyBounds(out x, out y, out width, out height);
                    Hardware.Present(front, x, y, width, height, true);
                }
                else
                {
                    for (int i = 0; i < 4 * desktop.DirtyCount; i += 4)
                        Hardware.Present(front, desktop.Dirty[i], desktop.Dirty[i + 1], desktop.Dirty[i + 2], desktop.Dirty[i + 3], false);
                }
                long took = Stopwatch.GetTimestamp() - before;
                shown += Stopwatch.GetTimestamp() - drawnAt;
                busy += took;
                if (took > slowest) slowest = took;
                drawn++;
            }
            // Erst jetzt, wo das Bild steht, bekommt ein Prozess die CPU: Der Wechsel zu ihm ist ein echter Kontextwechsel,
            // und den vollzieht COSMOS erst beim nächsten Interrupt, spätestens beim Tick seines Timers nach 10 ms. So lange
            // steht der Kernel hier, die Eingabe von eben ist aber schon zu sehen. Zurück geht es sofort.
            bool ran = OS.Tick();
            // Einmal je Sekunde ins Protokoll, wie viele Bilder es gab und wie lange sie brauchten: das langsamste, alle
            // zusammen, und davon Vermessen, Zeichnen und Anzeigen. Daran sieht man in der Werkstatt, ob das Zeichnen auf
            // diesem Rechner flüssig ist und woran es hängt. Ohne neues Bild keine Zeile.
            long now = Transition.Milliseconds() / 1000;
            if (now == second) return ran;
            long f = Stopwatch.Frequency;
            if (drawn > 0 && f > 0)
                Log.WriteString("[VibanOS] " + drawn + " Bilder, das langsamste " + slowest * 1000 / f + " ms, zusammen " + busy * 1000 / f + " ms (vermessen "
                    + desktop.Measured * 1000 / f + ", zeichnen " + desktop.Painted * 1000 / f + ", anzeigen " + shown * 1000 / f + ")\n");
            second = now;
            slowest = busy = shown = desktop.Measured = desktop.Painted = 0;
            drawn = 0;
            return ran;
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
                case Sys.Input.Key.LeftArrow: return Gui.Key.Left;
                case Sys.Input.Key.RightArrow: return Gui.Key.Right;
                case Sys.Input.Key.Escape: return Gui.Key.Escape;
                case Sys.Input.Key.Delete: return Gui.Key.Delete;
                case Sys.Input.Key.F2: return Gui.Key.F2;
                default: return Gui.Key.None;
            }
        }
    }
}
