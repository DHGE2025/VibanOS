using System;
using System.Collections.Generic;
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

        // Bildschirm wie die VMware-SVGA-Grafik: schaltet um, lehnt aber zu große Auflösungen ab.
        public static int ScreenWidth = T.Size.ScreenWidth, ScreenHeight = T.Size.ScreenHeight;
        public static bool GermanKeyboard = true;
        public static int MaxPixels = 2048 * 1536; // so viel fasst der Grafikspeicher

        public static string ScreenName() { return "vmware-svga (Attrappe)"; }
        public static Resolution ScreenSize() { return new Resolution(ScreenWidth, ScreenHeight); }

        public static List<Resolution> ScreenModes()
        {
            int[] modes = { 640, 480, 800, 600, 1024, 768, 1280, 720, 1920, 1080, 2560, 1600 };
            List<Resolution> list = new List<Resolution>();
            for (int i = 0; i < modes.Length; i += 2) list.Add(new Resolution(modes[i], modes[i + 1]));
            return list;
        }

        public static bool SetScreenMode(int width, int height)
        {
            if (width * height > MaxPixels) return false;
            ScreenWidth = width;
            ScreenHeight = height;
            return true;
        }

        public static void SetKeyboardLayout(bool german) { GermanKeyboard = german; }

        /// <summary>Wie oft der freie Platz abgefragt wurde, und ob die Abfrage scheitern soll.</summary>
        public static int DiskSpaceCalls;
        public static bool DiskSpaceFails;

        public static bool DiskSpace(out ulong total, out ulong free)
        {
            DiskSpaceCalls++;
            total = 64UL * 1024 * 1024;
            free = 60UL * 1024 * 1024;
            return Vfs.Available && !DiskSpaceFails;
        }

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
        private static long animTime; // Uhr der Animationen, läuft nur, wenn der Test sie weiterstellt

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
            desktop.Clock = () => animTime;
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
            Type("pwd\n");
            Check(Last().Text == OS.Users.Current.HomeDirectory, "pwd zeigt den aktuellen Ordner");
            Type("cp notizen/plan.txt kopie.txt\n");
            Check(File.ReadAllText(Path.Combine(home, "kopie.txt")) == "Erster Eintrag", "cp kopiert eine Datei");
            Type("cp kopie.txt notizen\n");
            Check(File.Exists(Path.Combine(home, "notizen", "kopie.txt")), "cp in einen Ordner legt die Datei hinein");
            Type("cp kopie.txt notizen/plan.txt\n");
            Check(Last().Style == LineStyle.Error && File.ReadAllText(Path.Combine(home, "notizen", "plan.txt")) == "Erster Eintrag",
                "cp überschreibt keine vorhandene Datei");
            Type("cp notizen anderswo\n");
            Check(Last().Style == LineStyle.Error && !Directory.Exists(Path.Combine(home, "anderswo")), "cp kopiert keine Ordner");
            Type("mv kopie.txt neu.txt\n");
            Check(!File.Exists(Path.Combine(home, "kopie.txt")) && File.Exists(Path.Combine(home, "neu.txt")), "mv benennt eine Datei um");
            Type("mv notizen archiv\n");
            Check(Directory.Exists(Path.Combine(home, "archiv")) && File.Exists(Path.Combine(home, "archiv", "plan.txt")), "mv benennt einen Ordner um");
            Type("mv archiv archiv/innen\n");
            Check(Last().Style == LineStyle.Error && Directory.Exists(Path.Combine(home, "archiv")), "mv schiebt keinen Ordner in sich selbst");
            Type("cd archiv\nmv /home/root/archiv /x\n");
            Check(Last().Style == LineStyle.Error && Directory.Exists(Path.Combine(home, "archiv")), "mv verschiebt nicht den Ordner, in dem die Shell steht");
            Type("cd /\nmv /home/root /verlegt\n");
            Check(Last().Text.StartsWith("Das braucht das System") && Directory.Exists(home), "mv verschiebt keinen Home-Ordner");
            Type("mv /system /verlegt\n");
            Check(Last().Text.StartsWith("Das braucht das System") && File.Exists(userFile), "mv verschiebt den Systemordner nicht, auch nicht für root");
            Type("mv /home /verlegt\n");
            Check(Last().Text.StartsWith("Das braucht das System") && Directory.Exists(home), "mv verschiebt den Ordner home nicht");
            Type("mv /system/users.txt /home/root/users.bak\n");
            Check(Last().Text.StartsWith("Das braucht das System") && File.Exists(userFile) && !File.Exists(Path.Combine(home, "users.bak")),
                "mv holt die Nutzerdatei nicht aus dem Systemordner");
            Type("write /home/zettel.txt x\nmv /home/zettel.txt /home/root/zettel.txt\n");
            Check(File.Exists(Path.Combine(home, "zettel.txt")), "Eine Datei direkt in home lässt sich verschieben, nur die Ordner dort sind fest");
            Type("rm /home/root/zettel.txt\n");
            Type("mv / /verlegt\n");
            Check(Last().Style == LineStyle.Error && Directory.Exists(home) && !Directory.Exists(Path.Combine(disk, "verlegt")), "mv verschiebt die Wurzel nicht");
            Type("cd /home/root\n");
            Type("mv neu.txt archiv\nmv archiv notizen\n");
            Check(File.Exists(Path.Combine(home, "notizen", "neu.txt")), "mv in einen Ordner legt die Datei hinein");
            // FAT unterscheidet Groß- und Kleinschreibung nicht. Dort wäre das Ziel die Quelle selbst.
            Type("mv notizen NOTIZEN\n");
            Check(Last().Text.StartsWith("Auf der Platte ist das derselbe Name") && Directory.Exists(Path.Combine(home, "notizen")) && !Directory.Exists(Path.Combine(home, "NOTIZEN")),
                "mv auf denselben Namen in anderer Schreibweise fasst nichts an und sagt, warum");
            Type("mv notizen/neu.txt notizen\n");
            Check(Last().Text.StartsWith("Quelle und Ziel sind dasselbe") && File.Exists(Path.Combine(home, "notizen", "neu.txt")), "mv einer Datei in ihren eigenen Ordner fasst nichts an");
            Type("cp notizen/neu.txt notizen/NEU.TXT\n");
            Check(Last().Text.StartsWith("Auf der Platte ist das derselbe Name") && !File.Exists(Path.Combine(home, "notizen", "NEU.TXT")), "cp kopiert eine Datei nicht auf sich selbst");
            // Unter dem FAT-Kurznamen erkennt mv denselben Ordner nicht wieder und schöbe ihn sonst in sich selbst.
            Type("mv notizen NOTIZE~1/innen\n");
            Check(Last().Text.StartsWith("Ordner verschiebt mv nicht über einen Pfad mit ~") && Directory.Exists(Path.Combine(home, "notizen")),
                "mv verschiebt keinen Ordner über einen FAT-Kurznamen");
            Check(Vfs.Combine(Vfs.Root, "x") == Vfs.Root + "x" && Vfs.Combine(Vfs.Root + "home", "x") == Vfs.Root + "home" + Vfs.Separator + "x",
                "Ein Name hängt mit genau einem Trenner am Ordner, auch an der Wurzel");
            Type("cp notizen/plan.txt /\ncp notizen/plan.txt /\n");
            Check(File.Exists(Path.Combine(disk, "plan.txt")) && Last().Text == "Gibt es schon: " + Vfs.Root + "plan.txt", "cp in die Wurzel bildet den Pfad ohne doppelten Trenner");
            Type("rm /plan.txt\n");
            Type("mv fehlt.txt x.txt\n");
            Check(Last().Text.StartsWith("Nicht gefunden"), "mv meldet eine fehlende Quelle");
            Type("df\n");
            Check(Last().Text.Contains("64 MB gesamt") && Last().Text.Contains("60 MB frei"), "df zeigt Größe und freien Platz der Platte");
            Platform.Hardware.DiskSpaceFails = true;
            Type("df\n");
            Check(Last().Style == LineStyle.Error && Last().Text.StartsWith("Der freie Platz"), "df sagt, wenn sich der freie Platz nicht ermitteln lässt");
            Platform.Hardware.DiskSpaceFails = false;
            Check(Text.Bytes(0) == "0 Byte" && Text.Bytes(1536) == "1,5 KB" && Text.Bytes(10 * 1024) == "10 KB" && Text.Bytes(5UL << 30) == "5 GB",
                "Größen werden lesbar ausgegeben");
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
            Type("touch system\ncp system /\n");
            Check(Last().Text == "Zugriff verweigert: " + Vfs.SystemDirectory, "Auch mit der Wurzel als Ziel landet nichts unter dem Namen des Systemordners");
            Type("rm system\n");
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
            Click(DockIconX(Icons.Activity), dockRow);
            Check(desktop.Active is ActivityWindow, "Klick im Dock öffnet die App");
            Window activity = desktop.Active;
            Check(activity.Process != null && OS.Processes.Get(activity.Process.Id) == activity.Process && activity.Process.Name == "aktivität",
                "Eine geöffnete App ist ein Prozess");
            int before = OS.Processes.All.Count;
            desktop.Open(Icons.About);
            Window app = desktop.Active;
            Check(OS.Processes.All.Count == before + 1, "Eine App, die nach der Aktivitätsanzeige aufgeht, wird mitgezählt");
            Click(ControlX(app, Window.CloseButton), app.Y + T.Size.TitleBar / 2);
            Check(OS.Processes.All.Count == before && app.Process == null, "Schließen beendet den Prozess der App");
            desktop.Open(Icons.About);
            app = desktop.Active;
            OS.Processes.Kill(app.Process.Id);
            desktop.Frame(screen);
            Check(!(desktop.Active is AboutWindow) && OS.Processes.All.Count == before, "kill auf den Prozess einer App schließt ihr Fenster");
            desktop.Open(Icons.About);
            app = desktop.Active;
            app.Process.OnSignal(Signal.Term);
            desktop.Frame(screen);
            Check(!(desktop.Active is AboutWindow), "Das Signal Term an eine App schließt ihr Fenster");
            desktop.Open(Icons.About);
            app = desktop.Active;
            Click(ControlX(app, Window.MinimizeButton), app.Y + T.Size.TitleBar / 2);
            Check(OS.Processes.Get(app.Process.Id) == app.Process, "Eine App im Dock bleibt ein Prozess");
            OS.Processes.Kill(app.Process.Id);
            desktop.Frame(screen);
            desktop.Open(Icons.About);
            Check(desktop.Active != app && desktop.Active.Process != null, "kill schließt auch eine App im Dock, sie startet danach neu");
            Click(ControlX(desktop.Active, Window.CloseButton), desktop.Active.Y + T.Size.TitleBar / 2);
            terminal.X = oldX;
            terminal.Y = T.Size.ScreenHeight / 2; // reicht jetzt bis unter das Dock
            Click(DockIconX(Icons.Activity) + T.Size.DockIcon / 2 + T.Space.M / 2, dockRow);
            Check(desktop.Active is ActivityWindow, "Ein Klick zwischen die Dock-Symbole trifft kein Fenster darunter");
            Desktop small = new Desktop(800, 600);
            small.Frame(new Surface(800, 600));
            Check(small.Active != null && small.Active.X >= 0, "Auch bei 800x600 beginnt das Terminal auf dem Bildschirm");
            OS.Processes.Kill(small.Active.Process.Id); // Der zweite Schreibtisch soll im Bild keinen Prozess hinterlassen.
            small.Open(Icons.Files);
            Check(small.Active is FilesWindow && small.Active.Y >= T.Size.MenuBar && small.Active.Y + small.Active.Height <= 600 - (T.Size.ScreenHeight - dockTop),
                "Auch bei 800x600 liegt ein neues Fenster ganz über dem Dock");
            OS.Processes.Kill(small.Active.Process.Id);
            Check(ActivityWindow.Fit(Fonts.Body, "root", 82) == "root" && Fonts.Body.Measure(ActivityWindow.Fit(Fonts.Body, "max_mustermann12", 82)) <= 82,
                "Lange Nutzernamen werden für die Prozesstabelle gekürzt");
            Surface plain = new Surface(200, 120);
            plain.FillRect(0, 0, 200, 120, T.Color.TextOnAccent);
            plain.Shadow(50, 30, 100, 60, T.Radius.L, T.Shadow.DockBlur, T.Shadow.DockOffset, T.Shadow.DockAlpha, T.Color.Shadow);
            Check(plain.Pixels[32 * 200 + 100] == plain.Pixels[0] && plain.Pixels[60 * 200 + 100] == plain.Pixels[0] && plain.Pixels[95 * 200 + 100] != plain.Pixels[0],
                "Der Schatten liegt nur außerhalb der Form, auch in den Zeilen der Ecken");

            // Einstellungen: Auflösung, Akzentfarbe, Uhr, Tastatur, Mauszeiger
            Click(DockIconX(Icons.Settings), dockRow);
            Check(desktop.Active is SettingsWindow, "Das Zahnrad im Dock öffnet die Einstellungen");
            SettingsWindow settings = (SettingsWindow)desktop.Active;
            settings.X = 300;
            settings.Y = 60;
            ClickAt(settings.CenterOf(1)); // noch während des Einblendens
            bool early = desktop.Animating;
            Settle();
            Check(early && settings.CenterOf(SettingsWindow.PickAccent) != null, "Schon während des Einblendens treffen Klicks die richtige Stelle");
            ClickAt(settings.CenterOf(0));
            Shot(output, "6-einstellungen");
            Check(settings.Modes.Count == 4 && settings.Modes[0].Width == 800 && settings.Modes[3].Width == 1920,
                "Die Einstellungen bieten nur Auflösungen an, mit denen die Oberfläche etwas anfangen kann");
            Check(settings.CenterOf(SettingsWindow.PickMode + 2) == null, "Die aktuelle Auflösung ist gewählt und nicht noch einmal klickbar");
            terminal.X = T.Size.ScreenWidth - 2 * T.Size.DockIcon; // passt nur auf den großen Bildschirm
            ClickAt(settings.CenterOf(SettingsWindow.PickMode + 1));
            Check(Platform.Hardware.ScreenWidth == 1024 && Platform.Hardware.ScreenHeight == 768, "Ein Klick auf 1024 × 768 schaltet den Bildschirm um");
            ScreenChanged();
            Check(screen.Width == 1024 && terminal.X + T.Size.DockIcon <= 1024 && settings.Y + T.Size.TitleBar <= 768 - T.Size.DockIcon,
                "Nach dem Umschalten liegen alle Fenster wieder erreichbar auf dem kleineren Bildschirm");
            Check(settings.CenterOf(SettingsWindow.PickMode + 1) == null && settings.CenterOf(SettingsWindow.PickMode + 2) != null,
                "Nach dem Umschalten ist die neue Auflösung gewählt");
            ClickAt(settings.CenterOf(SettingsWindow.PickMode + 2));
            ScreenChanged();
            Check(screen.Width == T.Size.ScreenWidth && screen.Height == T.Size.ScreenHeight, "Zurück auf die Entwurfsgröße");
            Click(ControlX(terminal, Window.ZoomButton), terminal.Y + T.Size.TitleBar / 2);
            Settle();
            desktop.Open(Icons.Settings); // wieder nach vorn, das vergrößerte Terminal liegt sonst darüber
            ClickAt(settings.CenterOf(SettingsWindow.PickMode + 1));
            ScreenChanged();
            Check(terminal.Zoomed && terminal.Width == 1024 - 2 * T.Space.S && terminal.NormalX + T.Size.DockIcon <= 1024 && !desktop.Animating,
                "Ein vergrößertes Fenster füllt nach dem Umschalten die neue Fläche");
            ClickAt(settings.CenterOf(SettingsWindow.PickMode + 2));
            ScreenChanged();
            Click(ControlX(terminal, Window.ZoomButton), terminal.Y + T.Size.TitleBar / 2);
            Settle();
            desktop.Open(Icons.Settings);
            Platform.Hardware.MaxPixels = 1280 * 720;
            ClickAt(settings.CenterOf(SettingsWindow.PickMode + 3));
            ScreenChanged();
            Check(screen.Width == T.Size.ScreenWidth && settings.Error.Contains("schafft die Grafikkarte nicht"),
                "Lehnt die Grafikkarte eine Auflösung ab, bleibt alles, wie es war, und das Fenster sagt es");
            Platform.Hardware.MaxPixels = 2048 * 1536;
            string settingsFile = Path.Combine(home, Settings.FileName);
            Check(Settings.Width == T.Size.ScreenWidth && File.ReadAllText(settingsFile).Contains("breite=" + T.Size.ScreenWidth) && !File.ReadAllText(settingsFile).Contains("1920"),
                "Gespeichert wird die gewählte Auflösung, eine abgelehnte nicht");
            ClickAt(settings.CenterOf(1));
            Check(settings.CenterOf(SettingsWindow.PickMode) == null && settings.CenterOf(SettingsWindow.PickAccent) != null, "Die Seitenleiste wechselt die Seite");
            ClickAt(settings.CenterOf(SettingsWindow.PickAccent + 3));
            Check(T.Color.Accent == T.Color.AccentOrange, "Ein Farbpunkt setzt die Akzentfarbe");
            ClickAt(settings.CenterOf(SettingsWindow.PickAccent));
            Check(T.Color.Accent == T.Color.AccentBlue, "Und zurück auf Blau");
            Vfs.Available = false;
            ClickAt(settings.CenterOf(SettingsWindow.PickAccent + 3));
            Check(T.Color.Accent == T.Color.AccentOrange && settings.Note.StartsWith("Ohne Festplatte") && settings.Error == "",
                "Ohne Festplatte gilt eine Einstellung trotzdem, das Fenster weist nur darauf hin");
            Vfs.Available = true;
            File.Delete(settingsFile);
            Directory.CreateDirectory(settingsFile); // ein Ordner im Weg: Schreiben scheitert
            ClickAt(settings.CenterOf(SettingsWindow.PickAccent));
            Check(settings.Error.StartsWith("Die Einstellungen ließen sich nicht speichern") && settings.Note == "", "Scheitert das Speichern, sagt es das Fenster");
            Directory.Delete(settingsFile);
            ClickAt(settings.CenterOf(SettingsWindow.PickAccent));
            Check(settings.Error == "" && File.Exists(settingsFile), "Der nächste Klick speichert wieder");
            ClickAt(settings.CenterOf(SettingsWindow.ToggleSeconds));
            Check(Settings.ShowSeconds, "Der Schalter zeigt Sekunden in der Menüleiste");
            long withSeconds = MenuBar();
            System.Threading.Thread.Sleep(1100);
            Check(desktop.Frame(screen) && MenuBar() != withSeconds, "Mit Sekunden zeichnet die Menüleiste jede Sekunde neu");
            Shot(output, "7-einstellungen-erscheinungsbild");
            Check(File.ReadAllText(settingsFile).Contains("sekunden=1"), "Die Einstellungen landen im Home-Ordner");
            ClickAt(settings.CenterOf(SettingsWindow.PickAccent + 3));
            string annaFile = Path.Combine(disk, "home", "anna", Settings.FileName);
            Settings.Load(OS.Users.Find("anna"));
            Check(T.Color.Accent == T.Color.AccentBlue && !Settings.ShowSeconds && Settings.Width == 0, "Ein anderer Nutzer ohne eigene Einstellungen bekommt die Grundwerte");
            Check(Settings.Save(OS.Users.Find("anna")) && !File.ReadAllText(annaFile).Contains("breite"), "Wer keine Auflösung gewählt hat, bekommt auch keine gespeichert");
            File.Delete(annaFile);
            Settings.Load(OS.Users.Current);
            Check(T.Color.Accent == T.Color.AccentOrange && Settings.ShowSeconds && Settings.Width == T.Size.ScreenWidth, "Die eigenen Einstellungen kommen nach dem Anmelden zurück");
            string savedSettings = File.ReadAllText(settingsFile);
            File.WriteAllText(settingsFile, "akzent=zz\nbreite=abc\nsekunden");
            Settings.Load(OS.Users.Current);
            Check(T.Color.Accent == T.Color.AccentBlue && Platform.Hardware.ScreenWidth == T.Size.ScreenWidth, "Eine kaputte Einstellungsdatei ergibt Grundwerte statt eines Fehlers");
            // Im Home-Ordner darf bisher jeder schreiben: Aus der Datei gilt nur, was auch die Einstellungen zur Wahl stellen.
            File.WriteAllText(settingsFile, "akzent=ff123456\nbreite=640\nhoehe=480\n"); // 640x480 bietet die Karte an, die Oberfläche nicht
            Settings.Load(OS.Users.Current);
            bool refused = T.Color.Accent == T.Color.AccentBlue && Platform.Hardware.ScreenWidth == T.Size.ScreenWidth && Settings.Width == 0;
            File.WriteAllText(settingsFile, "breite=1000\nhoehe=700\n"); // groß genug, aber kein Modus der Karte
            Settings.Load(OS.Users.Current);
            Check(refused && Platform.Hardware.ScreenWidth == T.Size.ScreenWidth && Settings.Width == 0,
                "Eine Farbe oder Auflösung, die nicht zur Wahl steht, übernimmt das Laden nicht");
            File.WriteAllText(settingsFile, new string('x', 4096) + "\nsekunden=1\n");
            Settings.Load(OS.Users.Current);
            bool bounded = !Settings.ShowSeconds;
            Platform.Hardware.SetKeyboardLayout(false); // die Belegung des Vorgängers
            using (new FileStream(settingsFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Settings.Load(OS.Users.Current); // gesperrt: Lesen scheitert
            Check(bounded && T.Color.Accent == T.Color.AccentBlue && Platform.Hardware.GermanKeyboard,
                "Eine riesige oder unlesbare Einstellungsdatei ergibt Grundwerte, auch für die Tastatur");
            File.WriteAllText(settingsFile, savedSettings);
            Settings.Load(OS.Users.Current);
            ClickAt(settings.CenterOf(SettingsWindow.PickAccent));
            ClickAt(settings.CenterOf(SettingsWindow.ToggleSeconds));
            Check(!Settings.ShowSeconds && File.ReadAllText(settingsFile).Contains("sekunden=0"), "Ein zweiter Klick schaltet die Sekunden wieder aus");
            ClickAt(settings.CenterOf(2));
            ClickAt(settings.CenterOf(SettingsWindow.PickUs));
            Check(!Settings.GermanKeyboard && !Platform.Hardware.GermanKeyboard, "US-Tastaturbelegung lässt sich wählen");
            ClickAt(settings.CenterOf(SettingsWindow.PickGerman));
            Check(Settings.GermanKeyboard && Platform.Hardware.GermanKeyboard, "Und zurück auf Deutsch");
            ClickAt(settings.CenterOf(SettingsWindow.ToggleCursor));
            desktop.OnMouse(200, 200, false);
            desktop.Frame(screen);
            Shot(output, "8-einstellungen-tastatur");
            // Zeile 10 des Zeigers beginnt mit einem Randpixel. Doppelt so groß liegt es bei y + 20 statt y + 10.
            bool large = screen.Pixels[(200 + 2 * 10 + 1) * screen.Width + 200 + 1] == unchecked((int)T.Color.CursorEdge);
            ClickAt(settings.CenterOf(SettingsWindow.ToggleCursor));
            desktop.OnMouse(200, 200, false);
            desktop.Frame(screen);
            bool small2 = screen.Pixels[(200 + 2 * 10 + 1) * screen.Width + 200 + 1] != unchecked((int)T.Color.CursorEdge);
            Check(large && small2, "Der große Mauszeiger ist doppelt so groß und lässt sich wieder ausschalten");
            ClickAt(settings.CenterOf(0));
            Click(settings.X + T.Space.M + T.Size.Control / 2, settings.Y + T.Size.TitleBar / 2);

            Surface still = new Surface(100, 60);
            still.FillRect(0, 0, 100, 60, T.Color.Danger);
            plain.FillRect(0, 0, 200, 120, T.Color.TextOnAccent);
            plain.DrawScaled(still, 50, 30, 50, 30, T.Radius.M, 255);
            Check(plain.Pixels[30 * 200 + 50] == plain.Pixels[0] && plain.Pixels[45 * 200 + 75] == unchecked((int)T.Color.Danger) && plain.Pixels[45 * 200 + 100] == plain.Pixels[0],
                "Das gestreckte Standbild einer Animation hat runde Ecken und bleibt in seinem Rechteck");

            // Animationen
            terminal.X = 70;
            terminal.Y = 64;
            Settle();
            Check(!desktop.Animating, "Animationen enden nach ihrer Dauer");
            desktop.Open(Icons.About);
            Window info = desktop.Active;
            int edge = (info.Y + T.Size.TitleBar / 2) * screen.Width + info.X + T.Space.XS;
            desktop.Frame(screen);
            int hidden = screen.Pixels[edge];
            animTime += T.Motion.Open / 2;
            desktop.Frame(screen);
            Check(desktop.Animating && hidden != unchecked((int)T.Color.TitleBar) && screen.Pixels[edge] != unchecked((int)T.Color.TitleBar), "Ein neues Fenster blendet sich ein");
            animTime += T.Motion.Open;
            desktop.Frame(screen);
            Check(!desktop.Animating && screen.Pixels[edge] == unchecked((int)T.Color.TitleBar), "Nach dem Einblenden steht das Fenster fertig da");
            Click(ControlX(info, Window.ZoomButton), info.Y + T.Size.TitleBar / 2);
            Check(!desktop.Animating && info.Width == 340, "Fenster mit festem Inhalt vergrößert der grüne Knopf nicht");
            Click(ControlX(info, Window.MinimizeButton), info.Y + T.Size.TitleBar / 2);
            Check(desktop.Active != info && desktop.Animating, "Der gelbe Knopf legt das Fenster ins Dock");
            Settle();
            int dockLeft = (T.Size.ScreenWidth - (Icons.Count * T.Size.DockIcon + (Icons.Count - 1) * T.Space.M + 2 * T.Space.S)) / 2 + T.Space.S;
            Click(dockLeft + Icons.About * (T.Size.DockIcon + T.Space.M) + T.Size.DockIcon / 2, dockRow);
            Check(desktop.Active == info && desktop.Animating, "Ein Klick auf das Dock-Symbol holt dasselbe Fenster zurück");
            Click(ControlX(info, Window.CloseButton), info.Y + T.Size.TitleBar / 2);
            Settle();
            Check(desktop.Active != info && !desktop.Animating, "Der rote Knopf schließt auch ein Fenster, das noch hereinkommt");
            Click(ControlX(terminal, Window.ZoomButton), terminal.Y + T.Size.TitleBar / 2);
            Settle();
            Check(desktop.Active == terminal && terminal.Width == T.Size.ScreenWidth - 2 * T.Space.S && terminal.Y == T.Size.MenuBar + T.Space.S,
                "Der grüne Knopf vergrößert das Terminal auf die freie Fläche");
            Click(ControlX(terminal, Window.ZoomButton), terminal.Y + T.Size.TitleBar / 2);
            animTime += T.Motion.Zoom / 2;
            desktop.Frame(screen);
            Check(terminal.Width > 720 && terminal.Width < T.Size.ScreenWidth - 2 * T.Space.S, "Beim Zoomen wächst das echte Fenster schrittweise");
            Settle();
            Check(terminal.Width == 720 && terminal.X == 70 && terminal.Y == 64, "Ein zweiter Klick stellt die alte Größe wieder her");
            Click(DockIconX(Icons.Activity), dockRow); // Aktivität wieder nach vorn

            // Dateien-App (VIB-2)
            Click(DockIconX(Icons.Files), dockRow);
            Check(desktop.Active is FilesWindow, "Der Ordner im Dock öffnet die App Dateien");
            FilesWindow files = (FilesWindow)desktop.Active;
            files.X = 200;
            files.Y = 60;
            Settle();
            Check(files.Folder == OS.Users.Current.HomeDirectory && files.IndexOf("notizen") >= 0, "Dateien beginnt im eigenen Ordner und zeigt ihn");
            ClickAt(files.CenterOf(FilesWindow.NewFolder));
            Check(Directory.Exists(Path.Combine(home, "Neuer Ordner")) && files.IndexOf("Neuer Ordner") >= 0, "Neuer Ordner legt einen Ordner an");
            ClickAt(files.CenterOf(FilesWindow.NewFolder));
            Check(Directory.Exists(Path.Combine(home, "Neuer Ordner 2")), "Ein zweiter neuer Ordner bekommt eine Nummer");
            Directory.Delete(Path.Combine(home, "Neuer Ordner 2"));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("notizen")));
            Check(files.Folder == OS.Users.Current.HomeDirectory, "Ein Klick wählt nur aus");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("notizen")));
            Check(files.Folder.EndsWith("notizen") && files.IndexOf("plan.txt") >= 0, "Ein zweiter Klick öffnet den Ordner");
            File.WriteAllText(Path.Combine(home, "notizen", "liste.txt"), "\uFEFFMilch\r\nBrot\n\tButter"); // mit UTF-8-Kennung, wie sie der Windows-Editor schreibt
            int[] blank = { files.X + T.Size.Sidebar + 200, files.Y + files.Height - 80 }; // freie Fläche unter der Liste
            ClickAt(blank);
            Check(files.IndexOf("liste.txt") >= 0, "Was im Terminal entsteht, zeigt Dateien nach einem Klick ins Leere");
            File.WriteAllText(Path.Combine(home, "notizen", "weg.txt"), "x");
            ClickAt(blank);
            File.Delete(Path.Combine(home, "notizen", "weg.txt"));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("weg.txt")));
            bool stale = files.IndexOf("weg.txt") >= 0; // Auswählen liest den Ordner nicht neu, das wäre auf FAT bei jedem Klick teuer
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("weg.txt")));
            Check(stale && files.IndexOf("weg.txt") < 0 && files.OpenFile == null && files.Message.StartsWith("Datei lässt sich nicht lesen"),
                "Lässt sich ein Eintrag nicht mehr öffnen, sagt Dateien es und liest den Ordner neu");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("plan.txt")));
            files.OnKey(Key.Down, '\0');
            files.OnKey(Key.Enter, '\0');
            Check(files.OpenFile != null && files.OpenFile.EndsWith("plan.txt") && files.Lines[0] == "Erster Eintrag", "Pfeil und Eingabe öffnen eine Datei");
            Shot(output, "9-dateien-text");
            files.OnKey(Key.Backspace, '\0');
            Picture(files); // neu zeichnen, die Liste ist wieder da
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("liste.txt")));
            files.OnKey(Key.Enter, '\0');
            Check(files.Lines.Count == 3 && files.Lines[0] == "Milch" && files.Lines[1] == "Brot" && files.Lines[2] == "    Butter",
                "Die Vorschau zeigt Windows-Zeilenenden, Tabulatoren und die UTF-8-Kennung sauber");
            File.WriteAllText(Path.Combine(home, "notizen", "lang.txt"), new string('x', 10000)); // eine einzige, sehr lange Zeile
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("lang.txt")));
            files.OnKey(Key.Enter, '\0');
            Picture(files);
            Check(files.Lines.Count == 2 && files.Lines[0].Length == 8192 && files.Lines[1] == "...", "Von einer großen Datei liest die Vorschau nur den Anfang");
            ClickAt(files.CenterOf(FilesWindow.Back));
            ClickAt(files.CenterOf(FilesWindow.Back));
            Check(files.Folder == OS.Users.Current.HomeDirectory && files.OpenFile == null, "Zurück geht von der Datei zum Ordner und weiter nach oben");
            ClickAt(files.CenterOf(FilesWindow.PlaceDisk));
            Check(files.Folder == Vfs.Root && files.IndexOf("system") >= 0 && files.CenterOf(FilesWindow.Back) == null, "VIBANOS zeigt die Wurzel, weiter zurück geht es nicht");
            ClickAt(files.CenterOf(FilesWindow.PlaceHome));
            Check(files.Folder == OS.Users.Current.HomeDirectory, "Der eigene Ordner in der Seitenleiste führt nach Hause");
            Shot(output, "9-dateien");
            int spaceCalls = Platform.Hardware.DiskSpaceCalls;
            Picture(files);
            Picture(files);
            Check(Platform.Hardware.DiskSpaceCalls == spaceCalls, "Dateien fragt den freien Platz nicht bei jedem Zeichnen ab");
            OS.Shell.Submit("mkdir /home/root/wand");
            ClickAt(files.CenterOf(FilesWindow.PlaceHome)); // liest den Ordner neu
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("wand")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("wand")));
            OS.Shell.Submit("mv /home/root/wand /home/root/wand2");
            ClickAt(files.CenterOf(FilesWindow.NewFolder));
            Check(!Directory.Exists(Path.Combine(home, "wand")) && files.Folder == OS.Users.Current.HomeDirectory && files.Message.StartsWith("Ordner nicht gefunden"),
                "Ist der gezeigte Ordner inzwischen verschoben, legt Neuer Ordner ihn nicht wieder an");
            OS.Shell.Submit("rm /home/root/wand2");
            ClickAt(files.CenterOf(FilesWindow.PlaceHome));
            files.OnKey(Key.PageDown, '\0');
            files.OnKey(Key.PageDown, '\0');
            files.OnKey(Key.Enter, '\0');
            Check(files.OpenFile != null && files.OpenFile.EndsWith("windows.txt"), "Bild ab geht höchstens bis zum letzten Eintrag");
            files.OnKey(Key.Backspace, '\0');
            files.OnKey(Key.PageUp, '\0');
            files.OnKey(Key.PageUp, '\0');
            files.OnKey(Key.Enter, '\0');
            Check(files.Folder.EndsWith("Neuer Ordner"), "Bild auf geht höchstens bis zum ersten Eintrag");
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            ClickAt(files.CenterOf(FilesWindow.PlaceDisk));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("users.txt")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("users.txt")));
            Check(files.OpenFile != null && files.Lines[0].Contains("root"), "Administratoren sehen die Nutzerdatei");
            OS.Shell.Submit("logout");
            File.WriteAllText(annaFile, "akzent=" + Text.Hex(T.Color.AccentGreen, 8) + "\ntastatur=us\nbreite=1024\nhoehe=768\n");
            Type("\ttopsecret\n");
            Check(OS.Users.Current.Name == "anna", "anna meldet sich an, das Fenster von root ist noch offen");
            Check(T.Color.Accent == T.Color.AccentGreen && !Platform.Hardware.GermanKeyboard && Settings.Width == 1024
                && Platform.Hardware.ScreenWidth == 1024 && Platform.Hardware.ScreenHeight == 768,
                "Die Anmeldung übernimmt die Einstellungen des Nutzers samt Auflösung");
            ScreenChanged();
            desktop.Open(Icons.Files);
            Picture(files);
            Check(files.OpenFile == null && files.Folder == OS.Users.Current.HomeDirectory && files.Lines.Count == 0,
                "Nach einem Nutzerwechsel zeigt Dateien nicht mehr, was der Vorgänger offen hatte");
            ClickAt(files.CenterOf(FilesWindow.PlaceDisk));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            Check(files.Folder == Vfs.Root && files.Message.StartsWith("Zugriff verweigert"), "Normale Nutzer kommen auch in Dateien nicht in den Systemordner");
            Click(ControlX(files, Window.CloseButton), files.Y + T.Size.TitleBar / 2);
            Settle();
            desktop.Open(Icons.Terminal);
            Type("logout\n");
            Check(OS.Users.Current == null && Platform.Hardware.GermanKeyboard && T.Color.Accent == T.Color.AccentBlue,
                "Nach der Abmeldung gelten wieder die Grundwerte: Niemand tippt sein Passwort mit der Tastaturbelegung des Vorgängers");
            Type("\tgeheim\n");
            Check(OS.Users.Current.Name == "root", "Zurück zu root");
            ScreenChanged();
            Check(screen.Width == T.Size.ScreenWidth && screen.Height == T.Size.ScreenHeight && T.Color.Accent == T.Color.AccentBlue,
                "Nach dem Nutzerwechsel gelten wieder die eigenen Einstellungen");
            terminal = desktop.Active is TerminalWindow ? desktop.Active : terminal;

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
            Click(ControlX(terminal, Window.MinimizeButton), terminal.Y + T.Size.TitleBar / 2);
            animTime += T.Motion.Minimize / 2;
            desktop.Frame(screen);
            SavePng(screen, Path.Combine(output, "5-minimieren.png"));
            Settle();
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

        private static void ClickAt(int[] point)
        {
            if (point == null) throw new Exception("Prüfung fehlgeschlagen: Klickfläche nicht gefunden");
            Click(point[0], point[1]);
            OS.Changed = true;
            desktop.Frame(screen); // neu zeichnen, damit die Klickflächen zum neuen Stand passen
        }

        // Was Kernel.cs tut, wenn der Canvas eine neue Größe hat.
        private static void ScreenChanged()
        {
            if (screen.Width == Platform.Hardware.ScreenWidth && screen.Height == Platform.Hardware.ScreenHeight) return;
            screen = new Surface(Platform.Hardware.ScreenWidth, Platform.Hardware.ScreenHeight);
            desktop.Resize(screen.Width, screen.Height);
            desktop.Frame(screen);
        }

        // Mitte eines Dock-Symbols bei Entwurfsgröße.
        private static int DockIconX(int icon)
        {
            int dock = Icons.Count * T.Size.DockIcon + (Icons.Count - 1) * T.Space.M + 2 * T.Space.S;
            return (T.Size.ScreenWidth - dock) / 2 + T.Space.S + icon * (T.Size.DockIcon + T.Space.M) + T.Size.DockIcon / 2;
        }

        // Fasst die Menüleiste zu einer Zahl zusammen.
        private static long MenuBar()
        {
            long sum = 17;
            for (int i = 0; i < T.Size.MenuBar * screen.Width; i++) sum = sum * 31 + screen.Pixels[i];
            return sum;
        }

        // Stellt die Uhr weiter, bis jede Animation fertig ist, und zeichnet neu.
        private static void Settle()
        {
            animTime += 1000;
            desktop.Frame(screen);
        }

        // Mitte eines Fensterknopfs, waagerecht.
        private static int ControlX(Window w, int button)
        {
            return w.X + T.Space.M + button * (T.Size.Control + T.Size.ControlGap) + T.Size.Control / 2;
        }

        private static void Click(int x, int y)
        {
            desktop.OnMouse(x, y, true);
            desktop.OnMouse(x, y, false);
        }

        // Zeichnet neu und fasst den Inhalt eines Fensters zu einer Zahl zusammen, um zwei Bilder zu vergleichen.
        private static long Picture(Window w)
        {
            animTime += 1000;
            OS.Changed = true;
            desktop.Frame(screen);
            long sum = 17;
            for (int y = w.Y + T.Size.TitleBar; y < w.Y + w.Height; y++)
                for (int x = w.X; x < w.X + w.Width; x++) sum = sum * 31 + screen.Pixels[y * screen.Width + x];
            return sum;
        }

        private static void Shot(string dir, string name)
        {
            animTime += 1000;
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
