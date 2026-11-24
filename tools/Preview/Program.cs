using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using VibanOS.Cli;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Gui;
using VibanOS.Memory;
using VibanOS.Processes;
using VibanOS.Syscalls;
using VibanOS.Users;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Platform
{
    /// <summary>Attrappe für Platform/Hardware.cs: merkt sich nur, was aufgerufen wurde.</summary>
    public static class Hardware
    {
        public static bool ShutdownCalled;
        public static string SerialOut = "";

        public static void Shutdown() { ShutdownCalled = true; }
        public static void Reboot() { }
        public static byte In(int port) { return 0x20; } // COM1: Senderegister frei, nichts empfangen
        public static void Out(int port, byte value) { if (port == 0x3F8) SerialOut += (char)value; }
    }
}

namespace VibanOS.Preview
{
    /// <summary>
    /// Selbsttest und Vorschau. Fährt VibanOS ohne COSMOS hoch, spielt eine Sitzung durch,
    /// prüft dabei die Grundfunktionen und speichert Bildschirmfotos und das Designsheet als PNG.
    /// Aufruf: dotnet run --project tools/Preview [zielordner]
    /// </summary>
    public static class Program
    {
        private static Desktop desktop;
        private static Surface screen;
        private static int checks;

        public static int Main(string[] args)
        {
            string output = Path.GetFullPath(args.Length > 0 ? args[0] : "preview");
            Directory.CreateDirectory(output);
            string fonts = FindResources();

            // Wegwerf-Festplatte: ein Ordner namens "0", damit die Pfade fast wie unter COSMOS aussehen (0:\home).
            string temp = Path.Combine(Path.GetTempPath(), "vibanos-" + Guid.NewGuid().ToString("N"));
            string disk = Path.Combine(temp, "0");
            Directory.CreateDirectory(disk);
            Directory.SetCurrentDirectory(temp);
            Vfs.Init("0" + Path.DirectorySeparatorChar);
            Fonts.Load(Read(fonts, "body"), Read(fonts, "strong"), Read(fonts, "caption"), Read(fonts, "display"), Read(fonts, "mono"));
            OS.Boot();
            screen = new Surface(T.Size.ScreenWidth, T.Size.ScreenHeight);
            desktop = new Desktop(T.Size.ScreenWidth, T.Size.ScreenHeight);
            desktop.OnMouse(760, 470, false);

            // Erster Start: root vergibt sein Passwort
            Check(OS.Users.NeedsSetup && OS.Users.Current == null, "Erster Start verlangt die Einrichtung");
            Shot(output, "1-anmeldung");
            Type("geheim\n");
            Check(OS.Users.Current != null && OS.Users.Current.Name == "root", "root ist nach der Einrichtung angemeldet");
            string userFile = File.ReadAllText(Path.Combine(disk, "system", "users.txt"));
            Check(userFile.Contains("root") && !userFile.Contains("geheim"), "Nutzerdatei enthält kein Klartext-Passwort");
            desktop.Frame(screen);
            Check(desktop.Active is TerminalWindow, "Nach der Anmeldung öffnet sich das Terminal");

            // Shell (VIB-1)
            Type("echo Hallo VibanOS\n");
            Check(Last().Text == "Hallo VibanOS", "echo gibt den Text aus");
            Type("gibtsnicht\n");
            Check(Last().Style == LineStyle.Error, "Unbekannter Befehl meldet einen Fehler");
            Type("clear\n");

            // Dateisystem (VIB-2)
            Type("mkdir notizen\ncd notizen\nwrite plan.txt Erster Eintrag\ncat plan.txt\n");
            Check(Last().Text == "Erster Eintrag", "write und cat arbeiten zusammen");
            Type("cd ..\nrm notizen\n");
            Check(Last().Style == LineStyle.Error && Directory.Exists(Path.Combine(disk, "home", "root", "notizen")), "rm löscht keinen vollen Ordner");
            Type("cd ..\ncd ..\ncd ..\n");
            Check(OS.Shell.CurrentDirectory == Vfs.Root, "cd .. verlässt die Wurzel nicht");
            Type("cd home/root\n");
            Check(Vfs.Resolve(Vfs.Root + "home", "../a/./b") == Vfs.Root + "a" + Vfs.Separator + "b", "Pfade werden aufgelöst");

            // Prozesse und Scheduler (VIB-3)
            Type("start counter\nstart counter\nstart clock\n");
            for (int i = 0; i < 30; i++) OS.Tick();
            Check(OS.Processes.Get(1).Steps == 10 && OS.Processes.Get(2).Steps == 10 && OS.Processes.Get(3).Steps == 10,
                "Round Robin verteilt die Schritte gleichmäßig");
            int framesBefore = OS.Memory.Frames.FreeCount;
            Type("kill 2\n");
            Check(OS.Processes.Get(2) == null && OS.Memory.Frames.FreeCount == framesBefore + 1, "kill beendet den Prozess und gibt seinen Speicher frei");

            // Speicher (VIB-5, VIB-7)
            Type("alloc 1 5\n");
            OS.Memory.WriteByte(1, 4096 + 7, 42);
            Check(OS.Memory.ReadByte(1, 4096 + 7) == 42, "Schreiben und Lesen über die Seitentabelle");
            Check(Throws(() => OS.Memory.ReadByte(1, 40 * 4096)), "Zugriff auf eine nicht zugeordnete Seite ist ein Page Fault");
            Check(Throws(() => OS.Memory.ReadByte(99, 0)), "Fremder Prozess ohne Seitentabelle wird abgewiesen");
            OS.Memory.TableOf(1).Pages[0].Access = PageAccess.Read;
            Check(Throws(() => OS.Memory.WriteByte(1, 0, 1)) && OS.Memory.Log.Count == 3, "Schreibschutz greift und wird protokolliert");

            // Systemaufrufe (VIB-11, VIB-12)
            Syscall.ShellMode = ProcessMode.User;
            Check(Syscall.Invoke(SyscallNumber.Shutdown).Error == SyscallResult.Denied && !Platform.Hardware.ShutdownCalled,
                "Usermode darf nicht herunterfahren");
            Check(Syscall.Invoke(SyscallNumber.GetTime).Error == SyscallResult.Ok, "Usermode darf die Zeit lesen");
            Syscall.ShellMode = ProcessMode.Kernel;

            // Treiber (VIB-9, VIB-10)
            Platform.Hardware.SerialOut = ""; // die Initialisierung schreibt den Baudraten-Teiler auf denselben Port
            Type("send ping\n");
            Check(Platform.Hardware.SerialOut == "ping\n", "send schreibt die Nachricht auf COM1");

            // Nutzer und Rechte (VIB-16, VIB-17)
            Type("useradd anna\ntopsecret\n");
            Check(OS.Users.Find("anna") != null, "useradd legt nach der Passwortabfrage einen Nutzer an");
            UserManager reloaded = new UserManager();
            reloaded.Load();
            Check(reloaded.Find("anna") != null && reloaded.CheckPassword(reloaded.Find("anna"), "topsecret")
                && !reloaded.CheckPassword(reloaded.Find("anna"), "falsch"), "Nutzer überstehen einen Neustart");
            Type("logout\n");
            Check(OS.Users.Current == null, "logout sperrt den Schreibtisch");
            Type("\tfalsch\n");
            Check(OS.Users.Current == null, "Falsches Passwort wird abgelehnt");
            Shot(output, "2-anmeldung-fehler");
            Type("topsecret\n");
            Check(OS.Users.Current.Name == "anna", "Tab wechselt den Nutzer, anna meldet sich an");
            Type("shutdown\n");
            Check(Last().Style == LineStyle.Error && !Platform.Hardware.ShutdownCalled, "Normale Nutzer dürfen nicht herunterfahren");
            Type("kill 1\n");
            Check(OS.Processes.Get(1) != null, "Normale Nutzer dürfen fremde Prozesse nicht beenden");
            Type("logout\n\tgeheim\n");
            Check(OS.Users.Current.Name == "root", "root meldet sich wieder an");

            // Fenster und Maus
            desktop.Open(Icons.About);
            Window about = desktop.Active;
            Click(about.X + T.Space.M + T.Size.Control / 2, about.Y + T.Size.TitleBar / 2);
            Check(desktop.Active is TerminalWindow, "Der rote Knopf schließt das Fenster");
            Window terminal = desktop.Active;
            int oldX = terminal.X;
            desktop.OnMouse(terminal.X + 300, terminal.Y + 10, true);
            desktop.OnMouse(terminal.X + 240, terminal.Y + 10, true);
            desktop.OnMouse(0, 0, false);
            Check(terminal.X == oldX - 60, "Fenster lassen sich an der Titelleiste ziehen");
            Click(T.Size.ScreenWidth / 2, T.Size.ScreenHeight - T.Space.S - T.Space.M - T.Size.DockIcon / 2);
            Check(desktop.Active is ActivityWindow, "Klick im Dock öffnet die App");

            // Bild für die Vorschau herrichten
            Type("");
            terminal.X = 70;
            terminal.Y = 64;
            desktop.Active.X = 560;
            desktop.Active.Y = 250;
            OS.Shell.Clear();
            desktop.OnMouse(terminal.X + 300, terminal.Y + 100, true);
            desktop.OnMouse(terminal.X + 300, terminal.Y + 100, false);
            Type("help\n");
            Shot(output, "3-terminal");
            OS.Shell.Clear();
            Type("start busy\nps\nmem\nwhoami\n");
            desktop.Open(Icons.Activity);
            desktop.OnMouse(980, 600, false);
            Shot(output, "4-schreibtisch");
            DesignSheet(output);

            Directory.SetCurrentDirectory(output);
            Directory.Delete(temp, true);
            Console.WriteLine(checks + " Prüfungen bestanden. Bilder liegen in " + output);
            return 0;
        }

        private static void Check(bool ok, string what)
        {
            if (!ok) throw new Exception("Prüfung fehlgeschlagen: " + what);
            checks++;
        }

        private static bool Throws(Action action)
        {
            try { action(); } catch (MemoryAccessException) { return true; }
            return false;
        }

        private static ShellLine Last() { return OS.Shell.Lines[OS.Shell.Lines.Count - 1]; }

        // Tippt Text wie auf der Tastatur: \n ist die Eingabetaste, \t der Tabulator.
        private static void Type(string text)
        {
            foreach (char c in text)
                desktop.OnKey(c == '\n' ? Key.Enter : c == '\t' ? Key.Tab : Key.None, c);
        }

        private static void Click(int x, int y)
        {
            desktop.OnMouse(x, y, true);
            desktop.OnMouse(x, y, false);
        }

        private static string FindResources()
        {
            string dir = Directory.GetCurrentDirectory();
            while (dir != null && !Directory.Exists(Path.Combine(dir, "VibanOS", "Resources"))) dir = Path.GetDirectoryName(dir);
            if (dir == null) throw new Exception("VibanOS/Resources nicht gefunden. Bitte im Repo-Ordner starten.");
            return Path.Combine(dir, "VibanOS", "Resources");
        }

        private static byte[] Read(string dir, string role) { return File.ReadAllBytes(Path.Combine(dir, role + ".vfont")); }

        private static void Shot(string dir, string name)
        {
            OS.Changed = true;
            desktop.Frame(screen);
            SavePng(screen, Path.Combine(dir, name + ".png"));
        }

        // Das Designsheet als Bild: alle Tokens, gezeichnet mit der Zeichenfläche von VibanOS selbst.
        private static void DesignSheet(string dir)
        {
            Surface s = new Surface(T.Size.ScreenWidth, T.Size.ScreenHeight);
            int left = T.Space.XXL + T.Space.L, y = T.Space.XXL;
            s.FillRect(0, 0, s.Width, s.Height, T.Color.Window);
            Icons.Draw(s, Icons.About, left, y, T.Size.DockIcon);
            s.DrawText(Fonts.Display, OS.Name + " Designsheet", left + T.Size.DockIcon + T.Space.L, y, T.Color.TextPrimary);
            s.DrawText(Fonts.Body, "Alle Werte stammen aus VibanOS/Design/Tokens.cs. Erklärung in docs/design/designsheet.md.",
                left + T.Size.DockIcon + T.Space.L, y + Fonts.Display.LineHeight, T.Color.TextSecondary);
            y += T.Size.DockIcon + T.Space.XL;

            y = Heading(s, "FARBEN", left, y);
            FieldInfo[] colors = typeof(T.Color).GetFields();
            const int perRow = 6, cellW = 196, cellH = 50, swatch = 36;
            for (int i = 0; i < colors.Length; i++)
            {
                uint value = (uint)colors[i].GetValue(null);
                int x = left + i % perRow * cellW, cy = y + i / perRow * cellH;
                // Heller Grund unter der Farbe, damit man durchscheinende Farben erkennt
                s.FillRoundRect(x, cy, swatch, swatch, T.Radius.S, T.Color.TextSecondary);
                s.FillRoundRect(x, cy, swatch, swatch, T.Radius.S, value);
                s.StrokeRoundRect(x, cy, swatch, swatch, T.Radius.S, T.Color.Border);
                s.DrawText(Fonts.Strong, colors[i].Name, x + swatch + T.Space.S, cy + 2, T.Color.TextPrimary);
                s.DrawText(Fonts.Caption, "#" + value.ToString("X8"), x + swatch + T.Space.S, cy + 2 + Fonts.Strong.LineHeight, T.Color.TextSecondary);
            }
            y += (colors.Length + perRow - 1) / perRow * cellH + T.Space.L;

            int column = left;
            int ty = Heading(s, "SCHRIFT", column, y);
            ty = Sample(s, Fonts.Display, "Display", "Inter Display SemiBold 26", column, ty);
            ty = Sample(s, Fonts.Strong, "Strong", "Inter SemiBold 13", column, ty);
            ty = Sample(s, Fonts.Body, "Body", "Inter Regular 13", column, ty);
            ty = Sample(s, Fonts.Caption, "CAPTION", "Inter Medium 11, Großbuchstaben", column, ty);
            Sample(s, Fonts.Mono, "Mono", "JetBrains Mono Regular 13", column, ty);

            column = left + 2 * cellW;
            ty = Heading(s, "ABSTÄNDE", column, y);
            foreach (FieldInfo f in typeof(T.Space).GetFields())
            {
                int v = (int)f.GetValue(null);
                s.FillRoundRect(column, ty + 3, v, T.Space.M, T.Radius.XS, T.Color.Accent);
                s.DrawText(Fonts.Body, f.Name + "  " + v + " px", column + T.Space.XXL + T.Space.M, ty, T.Color.TextPrimary);
                ty += Fonts.Body.LineHeight + T.Space.S;
            }

            column = left + 3 * cellW + T.Space.XXL;
            ty = Heading(s, "RADIEN", column, y);
            int rx = column;
            foreach (FieldInfo f in typeof(T.Radius).GetFields())
            {
                int v = (int)f.GetValue(null);
                s.FillRoundRect(rx, ty, 64, 64, v, T.Color.Raised);
                s.StrokeRoundRect(rx, ty, 64, 64, v, T.Color.Border);
                s.DrawTextCentered(Fonts.Strong, f.Name, rx + 32, ty + 64 + T.Space.S, T.Color.TextPrimary);
                s.DrawTextCentered(Fonts.Caption, v + " px", rx + 32, ty + 64 + T.Space.S + Fonts.Strong.LineHeight, T.Color.TextSecondary);
                rx += 64 + T.Space.XL;
            }

            ty = Heading(s, "BAUSTEINE", column, y + 64 + 4 * T.Space.XL);
            int cx = column;
            uint[] controls = { T.Color.Close, T.Color.Minimize, T.Color.Zoom };
            foreach (uint c in controls)
            {
                s.FillCircle(cx + T.Size.Control / 2, ty + T.Size.Field / 2, T.Size.Control, c);
                cx += T.Size.Control + T.Size.ControlGap;
            }
            cx += T.Space.L;
            s.FillRoundRect(cx, ty, 200, T.Size.Field, T.Size.Field / 2, T.Color.Sunken);
            s.StrokeRoundRect(cx, ty, 200, T.Size.Field, T.Size.Field / 2, T.Color.Border);
            s.DrawText(Fonts.Body, "Eingabefeld", cx + T.Space.M, ty + (T.Size.Field - Fonts.Body.LineHeight) / 2, T.Color.TextSecondary);
            cx += 200 + T.Space.L;
            s.FillRoundRect(cx, ty, 110, T.Size.Field, T.Radius.S, T.Color.Accent);
            s.DrawTextCentered(Fonts.Strong, "Knopf", cx + 55, ty + (T.Size.Field - Fonts.Strong.LineHeight) / 2, T.Color.TextOnAccent);
            SavePng(s, Path.Combine(dir, "designsheet.png"));
        }

        private static int Heading(Surface s, string text, int x, int y)
        {
            s.DrawText(Fonts.Caption, text, x, y, T.Color.TextSecondary);
            return y + Fonts.Caption.LineHeight + T.Space.M;
        }

        private static int Sample(Surface s, Font font, string role, string description, int x, int y)
        {
            s.DrawText(font, role, x, y, T.Color.TextPrimary);
            s.DrawText(Fonts.Caption, description, x + 140, y + (font.LineHeight - Fonts.Caption.LineHeight) / 2, T.Color.TextSecondary);
            return y + font.LineHeight + T.Space.S;
        }

        // Minimaler PNG-Schreiber (RGB, 8 Bit), damit die Vorschau ohne Zusatzpakete auskommt.
        private static void SavePng(Surface s, string path)
        {
            MemoryStream raw = new MemoryStream();
            using (ZLibStream z = new ZLibStream(raw, CompressionLevel.Optimal, true))
            {
                byte[] line = new byte[1 + s.Width * 3]; // erstes Byte: Filter 0
                for (int y = 0; y < s.Height; y++)
                {
                    for (int x = 0; x < s.Width; x++)
                    {
                        int p = s.Pixels[y * s.Width + x];
                        line[1 + x * 3] = (byte)(p >> 16);
                        line[2 + x * 3] = (byte)(p >> 8);
                        line[3 + x * 3] = (byte)p;
                    }
                    z.Write(line, 0, line.Length);
                }
            }
            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), s.Width);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), s.Height);
            header[8] = 8; // Bittiefe
            header[9] = 2; // Farbtyp RGB
            using (FileStream f = File.Create(path))
            {
                f.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
                Chunk(f, "IHDR", header);
                Chunk(f, "IDAT", raw.ToArray());
                Chunk(f, "IEND", new byte[0]);
            }
        }

        private static void Chunk(Stream f, string type, byte[] data)
        {
            byte[] body = new byte[4 + data.Length];
            for (int i = 0; i < 4; i++) body[i] = (byte)type[i];
            data.CopyTo(body, 4);
            uint crc = 0xFFFFFFFF;
            foreach (byte b in body)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
            byte[] n = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(n, data.Length);
            f.Write(n);
            f.Write(body);
            BinaryPrimitives.WriteUInt32BigEndian(n, ~crc);
            f.Write(n);
        }
    }
}
