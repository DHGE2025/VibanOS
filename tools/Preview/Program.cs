using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using VibanOS.Cli;
using VibanOS.Core;
using VibanOS.Drivers;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Gui;
using VibanOS.Interrupts;
using VibanOS.Memory;
using VibanOS.Processes;
using VibanOS.Sync;
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

        /// <summary>null: format gelingt und die "Festplatte" (ein Ordner) entsteht. Sonst die Fehlermeldung.</summary>
        public static string FormatError;

        public static void Shutdown() { ShutdownCalled = true; }
        public static void Reboot() { }
        public static byte In(int port) { return 0x20; } // COM1: Senderegister frei, nichts empfangen
        public static void Out(int port, byte value) { if (port == 0x3F8) SerialOut += (char)value; }

        public static string FormatDisk()
        {
            if (FormatError == null) Directory.CreateDirectory(Vfs.Root);
            return FormatError;
        }
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

            // Wegwerf-Festplatte: ein Ordner namens "mnt", damit die Pfade fast wie unter COSMOS aussehen (/mnt/home).
            // Er entsteht erst mit dem Befehl format. So beginnt der Selbsttest wie ein erster Start mit leerer Platte.
            string temp = Path.Combine(Path.GetTempPath(), "vibanos-" + Guid.NewGuid().ToString("N"));
            string disk = Path.Combine(temp, "mnt");
            string home = Path.Combine(disk, "home", "root");
            string userFile = Path.Combine(disk, "system", "users.txt");
            Directory.CreateDirectory(temp);
            Directory.SetCurrentDirectory(temp);
            Vfs.Init("mnt/");
            Fonts.Load();
            OS.Boot();
            screen = new Surface(T.Size.ScreenWidth, T.Size.ScreenHeight);
            desktop = new Desktop(T.Size.ScreenWidth, T.Size.ScreenHeight);
            desktop.OnMouse(760, 470, false);

            // Erster Start: root vergibt sein Passwort und tippt es zweimal ein
            Check(!Vfs.Available && OS.Users.NeedsSetup && OS.Users.Current == null, "Erster Start verlangt die Einrichtung");
            Shot(output, "1-anmeldung");
            Type("geheim\nanders\n");
            Check(OS.Users.Current == null && OS.Users.NeedsSetup, "Zwei verschiedene Passwörter richten nichts ein");
            Type("geheim\ngeheim\n");
            Check(OS.Users.Current != null && OS.Users.Current.Name == "root", "root ist nach der Einrichtung angemeldet");
            desktop.Frame(screen);
            Check(desktop.Active is TerminalWindow, "Nach der Anmeldung öffnet sich das Terminal");

            // Leere Festplatte einrichten (VIB-2)
            Type("ls\n");
            Check(Last().Style == LineStyle.Error, "Ohne Dateisystem melden Dateibefehle einen Fehler");
            Platform.Hardware.FormatError = "Keine Festplatte gefunden.";
            Type("format\nja\n");
            Check(Last().Text == "Keine Festplatte gefunden." && !Vfs.Available, "format meldet, warum es nicht geht");
            Platform.Hardware.FormatError = null;
            Type("format\nnein\n");
            Check(!Vfs.Available, "format fragt nach und lässt sich abbrechen");
            Type("format\nja\n");
            Check(Vfs.Available && File.ReadAllText(userFile).Contains("root") && !File.ReadAllText(userFile).Contains("geheim"),
                "format richtet die Platte ein und sichert die Nutzer ohne Klartext-Passwort");
            Check(Directory.Exists(home) && OS.Shell.CurrentDirectory == OS.Users.Current.HomeDirectory, "Nach format steht die Shell im eigenen Ordner");
            Type("format\n");
            Check(Last().Style == LineStyle.Error && !OS.Shell.Asking, "Eine eingerichtete Platte fasst format nicht an");

            // Shell (VIB-1)
            Type("echo Hallo VibanOS\n");
            Check(Last().Text == "Hallo VibanOS", "echo gibt den Text aus");
            Type("gibtsnicht\n");
            Check(Last().Style == LineStyle.Error, "Unbekannter Befehl meldet einen Fehler");
            Type("xyz");
            Press(Key.Down);
            Type("\n");
            Check(Last().Text.Contains("xyz"), "Pfeil ab lässt die getippte Eingabe stehen");
            Type("clear\n");

            // Dateisystem (VIB-2)
            Type("mkdir notizen\ncd notizen\nwrite plan.txt Erster Eintrag\ncat plan.txt\n");
            Check(Last().Text == "Erster Eintrag", "write und cat arbeiten zusammen");
            Type("write plan.txt\n");
            Check(Last().Style == LineStyle.Error && File.ReadAllText(Path.Combine(home, "notizen", "plan.txt")) == "Erster Eintrag",
                "write ohne Text leert die Datei nicht");
            Type("write leer.txt \"\"\n");
            Check(File.ReadAllText(Path.Combine(home, "notizen", "leer.txt")) == "", "Ein leerer Text in Anführungszeichen ist ein Argument");
            Type("cd ..\nrm notizen\n");
            Check(Last().Style == LineStyle.Error && Directory.Exists(Path.Combine(home, "notizen")), "rm löscht keinen vollen Ordner");
            Type("mkdir leer\ncd leer\nrm .\n");
            Check(Last().Style == LineStyle.Error && Directory.Exists(Path.Combine(home, "leer")), "rm löscht nicht den aktuellen Ordner");
            Type("cd ..\nrm leer\n");
            Check(!Directory.Exists(Path.Combine(home, "leer")), "rm löscht einen leeren Ordner");
            File.WriteAllText(Path.Combine(home, "windows.txt"), "eins\r\n\tzwei");
            Type("cat windows.txt\n");
            Check(Last().Text == "    zwei" && OS.Shell.Lines[OS.Shell.Lines.Count - 2].Text == "eins", "cat zeigt Windows-Zeilenenden und Tabulatoren sauber");
            Type("cd ..\ncd ..\ncd ..\n");
            Check(OS.Shell.CurrentDirectory == Vfs.Root, "cd .. verlässt die Wurzel nicht");
            Type("cd home/root\n");
            Check(Vfs.Resolve(Vfs.Root + "home", "../a/./b") == Vfs.Root + "a" + Vfs.Separator + "b", "Pfade werden aufgelöst");
            Check(Vfs.Resolve(Vfs.Root + "home", Vfs.Root.TrimEnd('/')) == Vfs.Root, "Die Wurzel gilt auch ohne abschließenden Trenner");
            Check(Vfs.Resolve(Vfs.Root, "system./users.txt") == Vfs.SystemDirectory + Vfs.Separator + "users.txt", "Punkte am Namensende ergeben keinen zweiten Namen");

            // Prozesse und Scheduler (VIB-3)
            // PID 1 ist das Terminal: Jede offene App ist ein Prozess und bekommt ihren Anteil.
            Type("start counter\nstart counter\nstart clock\n");
            for (int i = 0; i < 40; i++) OS.Tick();
            Check(OS.Processes.Get(1).Name == "terminal" && OS.Processes.Get(1).Steps == 10 && OS.Processes.Get(2).Steps == 10
                && OS.Processes.Get(3).Steps == 10 && OS.Processes.Get(4).Steps == 10,
                "Round Robin verteilt die Schritte gleichmäßig");
            Type("ps\n");
            Check(OS.Shell.Lines[OS.Shell.Lines.Count - 3].Text.Contains("Zähler 10"), "ps zeigt die Ausgabe der Prozesse");
            int framesBefore = OS.Memory.Frames.FreeCount;
            Type("kill 3\n");
            Check(OS.Processes.Get(3) == null && OS.Memory.Frames.FreeCount == framesBefore + 1, "kill beendet den Prozess und gibt seinen Speicher frei");
            Type("start counter abc\n");
            Check(Last().Style == LineStyle.Error && OS.Processes.All.Count == 3, "start lehnt eine Priorität ab, die keine Zahl ist");

            // Signale (VIB-14)
            Process clock = OS.Processes.Get(4);
            clock.OnSignal(Signal.Stop);
            int stoppedAt = clock.Steps;
            for (int i = 0; i < 10; i++) OS.Tick();
            Check(clock.Steps == stoppedAt && clock.StateText == "angehalten", "Stop hält einen Prozess an");
            clock.OnSignal(Signal.Cont);
            for (int i = 0; i < 10; i++) OS.Tick();
            Check(clock.Steps > stoppedAt, "Cont lässt ihn weiterlaufen");
            framesBefore = OS.Memory.Frames.FreeCount;
            clock.OnSignal(Signal.Term);
            clock.OnSignal(Signal.Cont);
            Check(clock.State == ProcessState.Terminated && OS.Memory.Frames.FreeCount == framesBefore + 1,
                "Term beendet den Prozess endgültig und gibt seinen Speicher frei");
            Check(!Ipc.Mailbox.Send(2, 4, "hallo") && Ipc.Mailbox.Send(4, 2, "hallo"), "Nachrichten gehen nur an lebende Prozesse");
            Type("kill 4\n");

            // Speicher (VIB-5, VIB-7)
            Type("alloc 2 5\n");
            OS.Memory.WriteByte(2, 4096 + 7, 42);
            Check(OS.Memory.ReadByte(2, 4096 + 7) == 42, "Schreiben und Lesen über die Seitentabelle");
            Check(Throws(() => OS.Memory.ReadByte(2, 40 * 4096)), "Zugriff auf eine nicht zugeordnete Seite ist ein Page Fault");
            Check(Throws(() => OS.Memory.ReadByte(99, 0)), "Fremder Prozess ohne Seitentabelle wird abgewiesen");
            OS.Memory.TableOf(2).Pages[0].Access = PageAccess.Read;
            Check(Throws(() => OS.Memory.WriteByte(2, 0, 1)) && OS.Memory.Log.Count == 3, "Schreibschutz greift und wird protokolliert");
            OS.Memory.FreePage(2, 0);
            OS.Memory.FreePage(2, 1);
            Type("alloc 2 2\n");
            Check(!Throws(() => OS.Memory.WriteByte(2, 0, 1)) && OS.Memory.ReadByte(2, 4096 + 7) == 0, "Eine neu vergebene Seite ist leer und beschreibbar");
            Type("alloc 2 0\n");
            Check(Last().Style == LineStyle.Error, "alloc verlangt mindestens eine Seite");
            Type("start counter\n");
            int hog = OS.Processes.All[OS.Processes.All.Count - 1].Id;
            Type("alloc " + hog + " 999\n");
            Check(Last().Style == LineStyle.Error && OS.Memory.Frames.FreeCount == 0, "alloc meldet, wenn der Speicher voll ist");
            Type("start clock\n");
            Check(Last().Style == LineStyle.Error && OS.Processes.All.Count == 3, "Ohne freien Rahmen startet kein Prozess");
            Type("kill " + hog + "\n");
            Check(OS.Memory.Frames.FreeCount > 0, "kill gibt alle Seiten des Prozesses frei");

            // Sperren (VIB-8)
            Type("start counter\nstart counter\nstart counter\n");
            int a = OS.Processes.All[2].Id, b = OS.Processes.All[3].Id, c = OS.Processes.All[4].Id;
            Mutex mutex = new Mutex("test");
            Check(mutex.Lock(a) && !mutex.Lock(b) && !mutex.Lock(c) && OS.Processes.Get(b).State == ProcessState.Blocked,
                "Eine belegte Sperre blockiert den nächsten Prozess");
            OS.Processes.Kill(b);
            mutex.Unlock(a);
            Check(mutex.Owner == c && OS.Processes.Get(c).State == ProcessState.Ready, "Die Sperre geht an den ersten Wartenden, den es noch gibt");
            OS.Processes.Kill(c);
            Check(mutex.Lock(a), "Stirbt der Besitzer, wird die Sperre beim nächsten Versuch frei");
            Semaphore semaphore = new Semaphore("test", 0);
            bool waited = !semaphore.Wait(a);
            semaphore.Signal();
            Check(waited && OS.Processes.Get(a).State == ProcessState.Ready && semaphore.Wait(a) && semaphore.Count == 0,
                "Eine Freigabe der Semaphore geht nicht verloren");
            OS.Processes.Kill(a);

            // Systemaufrufe (VIB-11, VIB-12)
            Syscall.ShellMode = ProcessMode.User;
            Check(Syscall.Invoke(SyscallNumber.Shutdown).Error == SyscallResult.Denied && !Platform.Hardware.ShutdownCalled,
                "Usermode darf nicht herunterfahren");
            Check(Syscall.Invoke(SyscallNumber.GetTime).Error == SyscallResult.Ok, "Usermode darf die Zeit lesen");
            Syscall.ShellMode = ProcessMode.Kernel;

            // Treiber und Interrupts (VIB-9, VIB-10, VIB-14)
            Platform.Hardware.SerialOut = ""; // die Initialisierung schreibt den Baudraten-Teiler auf denselben Port
            Type("send ping\n");
            Check(Platform.Hardware.SerialOut == "ping\n", "send schreibt die Nachricht auf COM1");
            Driver broken = new BrokenDriver();
            Check(!broken.Start() && broken.Status == DriverStatus.Stopped, "Ein Treiber ohne seine Hardware bleibt gestoppt");
            int interrupts = OS.Interrupts.Entries.Count;
            OS.Interrupts.Register(InterruptManager.Timer, "Timer", null);
            Check(OS.Interrupts.Entries.Count == interrupts, "Ein Interrupt wird nicht doppelt eingetragen");

            // Nutzer und Rechte (VIB-16, VIB-17)
            Type("useradd Anna!\n");
            Check(Last().Style == LineStyle.Error && !OS.Shell.Asking, "useradd prüft den Namen, bevor es nach dem Passwort fragt");
            Type("useradd bert\n");
            Press(Key.Up);
            Type("\n");
            Check(OS.Users.Find("bert") == null, "In einer Passwortabfrage gibt es keinen Verlauf");
            Type("useradd anna\ntopsecret\n");
            Check(OS.Users.Find("anna") != null, "useradd legt nach der Passwortabfrage einen Nutzer an");
            Check(!Shown("topsecret"), "Ein eingetipptes Passwort steht weder im Terminal noch im Verlauf");
            UserManager reloaded = new UserManager();
            reloaded.Load();
            Check(reloaded.Find("anna") != null && reloaded.CheckPassword(reloaded.Find("anna"), "topsecret")
                && !reloaded.CheckPassword(reloaded.Find("anna"), "falsch"), "Nutzer überstehen einen Neustart");
            string users = File.ReadAllText(userFile);
            File.WriteAllText(userFile, "kaputt\nx;y;z;;;\n" + users + "7;;s;h;mnt/home/;users\n9;anna;s;h;mnt/home/anna;users\n");
            reloaded = new UserManager();
            reloaded.Load();
            Check(reloaded.All.Count == 2 && reloaded.CheckPassword(reloaded.Find("root"), "geheim")
                && reloaded.CheckPassword(reloaded.Find("anna"), "topsecret"), "Kaputte Zeilen der Nutzerdatei sperren niemanden aus");
            File.WriteAllText(userFile, users);
            File.Move(userFile, userFile + ".new");
            reloaded = new UserManager();
            reloaded.Load();
            Check(!reloaded.NeedsSetup && reloaded.Find("anna") != null, "Ein unterbrochenes Speichern der Nutzerdatei führt nicht zur Ersteinrichtung");
            File.Move(userFile + ".new", userFile);
            using (new FileStream(userFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) // gesperrt: Lesen scheitert
            {
                reloaded = new UserManager();
                reloaded.Load();
            }
            Check(reloaded.LoadError != null && !reloaded.NeedsSetup && !reloaded.Save() && File.ReadAllText(userFile) == users,
                "Eine unlesbare Nutzerdatei führt nicht zur Ersteinrichtung und wird nicht überschrieben");
            Directory.CreateDirectory(userFile + ".new"); // ein Ordner im Weg: Schreiben scheitert
            Check(!OS.Users.Save() && File.ReadAllText(userFile) == users, "Scheitert das Schreiben, bleibt die bisherige Nutzerdatei unversehrt");
            Directory.Delete(userFile + ".new");
            Check(OS.Users.Save() && File.ReadAllText(userFile) == users, "Danach lässt sich die Nutzerdatei wieder schreiben");
            Check(Tail(UserManager.Hash("a", "salz")) != Tail(UserManager.Hash("b", "salz")) || Tail(UserManager.Hash("b", "salz")) != Tail(UserManager.Hash("c", "salz")),
                "Auch die letzten Stellen des Passwort-Hashs hängen vom Passwort ab");
            Type("logout\n");
            Check(OS.Users.Current == null, "logout sperrt den Schreibtisch");
            Check(OS.Shell.Lines.Count == 0 && OS.Shell.History.Count == 0, "logout räumt Ausgaben und Verlauf für den nächsten Nutzer weg");
            LoginScreen login = new LoginScreen();
            for (int i = 0; i < 3; i++) { login.OnKey(Key.None, 'x'); login.OnKey(Key.Enter, '\n'); }
            foreach (char ch in "geheim\n") login.OnKey(ch == '\n' ? Key.Enter : Key.None, ch);
            Check(OS.Users.Current == null, "Während der Sperre nach drei Fehlversuchen hilft auch das richtige Passwort nicht");
            DateTime now = DateTime.Now;
            Check(!login.LockExpired(now) && login.LockExpired(now.AddSeconds(6)) && !login.LockExpired(now.AddSeconds(7)),
                "Die Sperre nach drei Fehlversuchen läuft ab und meldet das genau einmal");
            Type("\tfalsch\n");
            Check(OS.Users.Current == null, "Falsches Passwort wird abgelehnt");
            Shot(output, "2-anmeldung-fehler");
            Type("topsecret\n");
            Check(OS.Users.Current.Name == "anna", "Tab wechselt den Nutzer, anna meldet sich an");
            Type("shutdown\n");
            Check(Last().Style == LineStyle.Error && !Platform.Hardware.ShutdownCalled, "Normale Nutzer dürfen nicht herunterfahren");
            Type("useradd eve\n");
            Check(Last().Text.StartsWith("Keine Berechtigung") && !OS.Shell.Asking && OS.Users.Find("eve") == null, "Normale Nutzer legen keine Nutzer an");
            Type("format\n");
            Check(Last().Text.StartsWith("Keine Berechtigung") && !OS.Shell.Asking, "Normale Nutzer dürfen format nicht aufrufen");
            Type("passwd\nfalsch\n");
            Check(Last().Style == LineStyle.Error && !OS.Shell.Asking && OS.Users.CheckPassword(OS.Users.Current, "topsecret"),
                "passwd ändert nichts ohne das alte Passwort");
            Type("kill 1\n");
            Check(OS.Processes.Get(1) != null, "Normale Nutzer dürfen fremde Prozesse nicht beenden");
            int pagesBefore = OS.Memory.TableOf(1).Pages.Count;
            Type("alloc 1 1\n");
            Check(Last().Style == LineStyle.Error && OS.Memory.TableOf(1).Pages.Count == pagesBefore, "Normale Nutzer geben fremden Prozessen keinen Speicher");
            Type("cat /system/users.txt\n");
            Check(Last().Text.StartsWith("Zugriff verweigert"), "Normale Nutzer lesen die Nutzerdatei nicht");
            Type("write ../../SYSTEM/users.txt x\n");
            Check(Last().Text.StartsWith("Zugriff verweigert") && File.ReadAllText(userFile) == users, "Der Systemordner ist auch in anderer Schreibweise gesperrt");
            Type("cat /system~1/users.txt\n");
            Check(Last().Text.StartsWith("Zugriff verweigert"), "Der FAT-Kurzname des Systemordners (SYSTEM~1) ist normalen Nutzern gesperrt");
            Type("rm /system/users.txt\n");
            Check(Last().Text.StartsWith("Zugriff verweigert") && File.Exists(userFile), "Normale Nutzer löschen nichts im Systemordner");
            Type("logout\n\tgeheim\n");
            Check(OS.Users.Current.Name == "root", "root meldet sich wieder an");
            Type("cat /system/users.txt\n");
            Check(Last().Style != LineStyle.Error, "Administratoren dürfen in den Systemordner");

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
            int dockRow = T.Size.ScreenHeight - T.Space.S - T.Space.M - T.Size.DockIcon / 2, dockTop = dockRow - T.Size.DockIcon / 2 - T.Space.S;
            desktop.OnMouse(terminal.X + terminal.Width - 5, terminal.Y + 10, true);
            desktop.OnMouse(0, T.Size.ScreenHeight - 1, true);
            desktop.OnMouse(0, 0, false);
            Check(terminal.X + terminal.Width >= T.Size.DockIcon && terminal.Y + T.Size.TitleBar <= dockTop,
                "Ein Fenster lässt sich nicht ganz aus dem Bildschirm oder unter das Dock ziehen");
            Click(T.Size.ScreenWidth / 2, dockRow);
            Check(desktop.Active is ActivityWindow, "Klick im Dock öffnet die App");
            Window activity = desktop.Active;
            Check(activity.Process != null && OS.Processes.Get(activity.Process.Id) == activity.Process && activity.Process.Name == "aktivität",
                "Eine geöffnete App ist ein Prozess");
            int before = OS.Processes.All.Count;
            desktop.Open(Icons.About);
            Window info = desktop.Active;
            Check(OS.Processes.All.Count == before + 1, "Eine App, die nach der Aktivitätsanzeige aufgeht, wird mitgezählt");
            Click(info.X + T.Space.M + T.Size.Control / 2, info.Y + T.Size.TitleBar / 2);
            Check(OS.Processes.All.Count == before && OS.Processes.Get(info.Process == null ? -1 : info.Process.Id) == null,
                "Schließen beendet den Prozess der App");
            desktop.Open(Icons.About);
            info = desktop.Active;
            OS.Processes.Kill(info.Process.Id);
            desktop.Frame(new Surface(T.Size.ScreenWidth, T.Size.ScreenHeight));
            Check(!(desktop.Active is AboutWindow) && OS.Processes.All.Count == before, "kill auf den Prozess einer App schließt ihr Fenster");
            desktop.Open(Icons.About);
            info = desktop.Active;
            info.Process.OnSignal(Signal.Term);
            desktop.Frame(new Surface(T.Size.ScreenWidth, T.Size.ScreenHeight));
            Check(!(desktop.Active is AboutWindow), "Das Signal Term an eine App schließt ihr Fenster");
            terminal.X = oldX;
            terminal.Y = T.Size.ScreenHeight / 2; // reicht jetzt bis unter das Dock
            Click(T.Size.ScreenWidth / 2 + T.Size.DockIcon / 2 + T.Space.M / 2, dockRow);
            Check(desktop.Active is ActivityWindow, "Ein Klick zwischen die Dock-Symbole trifft kein Fenster darunter");
            Desktop small = new Desktop(800, 600);
            small.Frame(new Surface(800, 600));
            Check(small.Active != null && small.Active.X >= 0, "Auch bei 800x600 beginnt das Terminal auf dem Bildschirm");
            OS.Processes.Kill(small.Active.Process.Id); // Der zweite Schreibtisch soll im Bild keinen Prozess hinterlassen.
            Check(ActivityWindow.Fit(Fonts.Body, "root", 82) == "root" && Fonts.Body.Measure(ActivityWindow.Fit(Fonts.Body, "max_mustermann12", 82)) <= 82,
                "Lange Nutzernamen werden für die Prozesstabelle gekürzt");
            Surface plain = new Surface(200, 120);
            plain.FillRect(0, 0, 200, 120, T.Color.TextOnAccent);
            plain.Shadow(50, 30, 100, 60, T.Radius.L, T.Shadow.DockBlur, T.Shadow.DockOffset, T.Shadow.DockAlpha, T.Color.Shadow);
            Check(plain.Pixels[32 * 200 + 100] == plain.Pixels[0] && plain.Pixels[60 * 200 + 100] == plain.Pixels[0] && plain.Pixels[95 * 200 + 100] != plain.Pixels[0],
                "Der Schatten liegt nur außerhalb der Form, auch in den Zeilen der Ecken");

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
            long bottom = Picture(terminal);
            Press(Key.PageUp);
            long top = Picture(terminal);
            Press(Key.PageDown);
            Check(top != bottom && Picture(terminal) == bottom, "Bild auf und Bild ab blättern im Terminal");
            Shot(output, "3-terminal");
            OS.Shell.Clear();
            Type("start clock\nstart busy\n");
            for (int i = 0; i < 90; i++) OS.Tick();
            Type("ps\nmem\nwhoami\n");
            desktop.Open(Icons.Activity);
            desktop.OnMouse(980, 600, false);
            Shot(output, "4-schreibtisch");
            DesignSheet(output);

            Directory.SetCurrentDirectory(output);
            Directory.Delete(temp, true);
            Console.WriteLine(checks + " Prüfungen bestanden. Bilder liegen in " + output);
            return 0;
        }

        private class BrokenDriver : Driver
        {
            public override string Name { get { return "kaputt"; } }
            public override string Description { get { return "Treiber, dessen Hardware fehlt"; } }
            protected override void Init() { throw new Exception("keine Hardware"); }
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

        // Steht der Text irgendwo im Terminal oder im Verlauf der Shell?
        private static bool Shown(string text)
        {
            foreach (ShellLine line in OS.Shell.Lines)
                if (line.Text.Contains(text)) return true;
            return OS.Shell.History.Contains(text);
        }

        // Die letzten drei Hex-Stellen eines Hashs, also seine unteren 12 Bit.
        private static string Tail(string hash) { return hash.Substring(hash.Length - 3); }

        // Tippt Text wie auf der Tastatur: \n ist die Eingabetaste, \t der Tabulator.
        private static void Type(string text)
        {
            foreach (char c in text)
                desktop.OnKey(c == '\n' ? Key.Enter : c == '\t' ? Key.Tab : Key.None, c);
        }

        private static void Press(Key key) { desktop.OnKey(key, '\0'); }

        private static void Click(int x, int y)
        {
            desktop.OnMouse(x, y, true);
            desktop.OnMouse(x, y, false);
        }

        // Zeichnet neu und fasst den Inhalt eines Fensters zu einer Zahl zusammen, um zwei Bilder zu vergleichen.
        private static long Picture(Window w)
        {
            OS.Changed = true;
            desktop.Frame(screen);
            long sum = 17;
            for (int y = w.Y + T.Size.TitleBar; y < w.Y + w.Height; y++)
                for (int x = w.X; x < w.X + w.Width; x++) sum = sum * 31 + screen.Pixels[y * screen.Width + x];
            return sum;
        }

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
