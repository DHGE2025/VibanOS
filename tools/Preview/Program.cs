using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using VibanOS.Cli;
using VibanOS.Cli.Commands;
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
        public static byte In(int port)
        {
            if (port == 0x511) return FwCfgFiles == null ? (byte)0xFF : fwCfgPos < fwCfgData.Length ? fwCfgData[fwCfgPos++] : (byte)0;
            return 0x20; // COM1: Senderegister frei, nichts empfangen
        }

        public static void Out(int port, byte value) { if (port == 0x3F8) SerialOut += (char)value; }

        public static void Out16(int port, ushort value) { if (port == 0x510) FwCfgSelect(value); }

        // fw_cfg wie in QEMU: Schlüssel an 0x510, Daten byteweise an 0x511. null: kein QEMU, der Port liefert 0xFF.
        public static List<KeyValuePair<string, string>> FwCfgFiles;
        private static byte[] fwCfgData = new byte[0];
        private static int fwCfgPos;
        private const int FwCfgFirstFile = 0x20;

        private static void FwCfgSelect(ushort key)
        {
            List<byte> data = new List<byte>();
            if (key == 0) data.AddRange(System.Text.Encoding.ASCII.GetBytes("QEMU"));
            else if (key == 0x19 && FwCfgFiles != null)
            {
                AddBigEndian(data, (uint)FwCfgFiles.Count, 4);
                for (int i = 0; i < FwCfgFiles.Count; i++)
                {
                    AddBigEndian(data, (uint)FwCfgFiles[i].Value.Length, 4);
                    AddBigEndian(data, (uint)(FwCfgFirstFile + i), 2);
                    AddBigEndian(data, 0, 2);
                    byte[] name = new byte[56];
                    System.Text.Encoding.ASCII.GetBytes(FwCfgFiles[i].Key).CopyTo(name, 0);
                    data.AddRange(name);
                }
            }
            else if (FwCfgFiles != null && key >= FwCfgFirstFile && key < FwCfgFirstFile + FwCfgFiles.Count)
                data.AddRange(System.Text.Encoding.ASCII.GetBytes(FwCfgFiles[key - FwCfgFirstFile].Value));
            fwCfgData = data.ToArray();
            fwCfgPos = 0;
        }

        private static void AddBigEndian(List<byte> data, uint value, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) data.Add((byte)(value >> (8 * i)));
        }

        // Nur Kernel.cs bringt das Bild an den Bildschirm, und den übersetzt die Vorschau nicht.
        public static void Present(Surface screen, int x, int y, int width, int height, bool whole) { }

        // Nur virtio-gpu kann weniger Zeilen zeigen, als es hat. Kernel.cs fragt danach.
        public static bool CropScreen(int height) { return false; }

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

        // Den Thread, wie COSMOS ihn führt, gibt es hier nicht. Auf Wunsch liefert die Attrappe erfundene Register,
        // damit sich der Befehl context prüfen lässt.
        public static bool FakeThreads;

        public static object CurrentThread() { return null; }

        public static ThreadInfo InspectThread(object thread)
        {
            if (!FakeThreads) return null;
            ThreadInfo info = new ThreadInfo();
            info.Id = 7;
            info.StackBase = 0x1000000;
            info.StackSize = Context.StackSize;
            info.Registers = new ulong[ThreadInfo.RegisterNames.Length];
            for (int i = 0; i < info.Registers.Length; i++) info.Registers[i] = 0x1000UL + (ulong)i;
            return info;
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
            // Windows schriebe die Umlaute sonst in der Codepage der Konsole, die Werkstatt liest UTF-8.
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (IOException) { }
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
            Draw();
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
            Type("rm /system/users.txt\n");
            Check(Last().Text.StartsWith("Das braucht das System") && File.Exists(userFile), "rm löscht die Nutzerdatei nicht, auch nicht für root");
            Type("write /home/zettel.txt x\nmv /home/zettel.txt /home/root/zettel.txt\n");
            Check(File.Exists(Path.Combine(home, "zettel.txt")), "Eine Datei direkt in home lässt sich verschieben, fest sind dort nur die Home-Ordner der Nutzer");
            Type("rm /home/root/zettel.txt\n");
            Type("rm /home/root\n");
            Check(Last().Text.StartsWith("Das braucht das System") && Directory.Exists(home), "rm löscht keinen Home-Ordner");
            Type("mkdir /home/fremd\nmv /home/fremd /home/fremd2\nrm /home/fremd2\n");
            Check(Last().Style != LineStyle.Error && !Directory.Exists(Path.Combine(disk, "home", "fremd")) && !Directory.Exists(Path.Combine(disk, "home", "fremd2")),
                "Ein Ordner in home, der keinem Nutzer gehört, lässt sich umbenennen und löschen");
            // Die Meldung muss von der Schutzregel kommen: Im Selbsttest scheiterte das Verschieben der Wurzel auch am Wirtssystem.
            Type("mv / /verlegt\n");
            Check(Last().Text.StartsWith("Das braucht das System") && Directory.Exists(home) && !Directory.Exists(Path.Combine(disk, "verlegt")), "mv verschiebt die Wurzel nicht");
            Type("rm /\n");
            Check(Last().Text.StartsWith("Das braucht das System"), "rm fasst die Wurzel nicht an");
            Type("cd /home/root\n");
            Type("mv neu.txt archiv\nmv archiv notizen\n");
            Check(File.Exists(Path.Combine(home, "notizen", "neu.txt")), "mv in einen Ordner legt die Datei hinein");
            // FAT unterscheidet Groß- und Kleinschreibung nicht. Dort wäre das Ziel die Quelle selbst.
            Type("mv notizen NOTIZEN\n");
            Check(Last().Text.StartsWith("Auf der Platte ist das derselbe Name") && Listed(home, "notizen") && !Listed(home, "NOTIZEN"),
                "mv auf denselben Namen in anderer Schreibweise fasst nichts an und sagt, warum");
            Type("mv notizen/neu.txt notizen\n");
            Check(Last().Text.StartsWith("Quelle und Ziel sind dasselbe") && File.Exists(Path.Combine(home, "notizen", "neu.txt")), "mv einer Datei in ihren eigenen Ordner fasst nichts an");
            Type("cp notizen/neu.txt notizen/NEU.TXT\n");
            // Der Inhalt zählt mit: Wo die Schreibweise nicht zählt, schriebe ein Kopieren auf sich selbst die Quelle leer.
            Check(Last().Text.StartsWith("Auf der Platte ist das derselbe Name") && !Listed(Path.Combine(home, "notizen"), "NEU.TXT")
                && File.ReadAllText(Path.Combine(home, "notizen", "neu.txt")) == "Erster Eintrag", "cp kopiert eine Datei nicht auf sich selbst");
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
            Type("mv notizen/neu.txt neu kopie.txt\n");
            Check(Last().Text.StartsWith("Aufruf: mv") && File.Exists(Path.Combine(home, "notizen", "neu.txt")) && !File.Exists(Path.Combine(home, "neu")),
                "mv mit einem Argument zu viel fasst nichts an, etwa bei einem Namen mit Leerzeichen ohne Anführungszeichen");
            Type("mkdir \"zwei worte\"\nmv \"zwei worte\" \"drei kurze worte\"\n");
            bool renamed = Directory.Exists(Path.Combine(home, "drei kurze worte")) && !Directory.Exists(Path.Combine(home, "zwei worte"));
            Type("rm \"drei kurze worte\"\n");
            Check(renamed && !Directory.Exists(Path.Combine(home, "drei kurze worte")), "In Anführungszeichen gehen auch Namen mit Leerzeichen");
            Type("touch Neuer\nmkdir \"Neuer Ordner\"\nrm Neuer Ordner\n");
            Check(Last().Text.StartsWith("Zu viele Namen") && File.Exists(Path.Combine(home, "Neuer")) && Directory.Exists(Path.Combine(home, "Neuer Ordner")),
                "rm mit einem Namen zu viel löscht nichts: Ohne Anführungszeichen träfe es sonst still die Datei Neuer");
            Type("rm Neuer\nrm \"Neuer Ordner\"\n");
            // Die Sperre sitzt in FileCommand.Target und gilt damit für jeden Befehl, der seine Argumente ganz hereinreicht, nicht nur für rm.
            bool allRefuse = true;
            foreach (string command in new[] { "ls", "cd", "mkdir", "touch", "cat" })
            {
                Type(command + " eins zwei\n");
                allRefuse &= Last().Text.StartsWith("Zu viele Namen");
            }
            Check(allRefuse && !File.Exists(Path.Combine(home, "eins")) && !Directory.Exists(Path.Combine(home, "eins")),
                "Auch ls, cd, mkdir, touch und cat nehmen keinen Namen zu viel");

            // Ordner samt Inhalt kopieren und löschen (VIB-26). Beides arbeitet in Schritten, je Durchlauf der Kernel-Schleife einer (OS.Tick).
            Type("mkdir baum\nmkdir baum/ast\nmkdir baum/ast/zweig\nmkdir baum/leer\nwrite baum/wurzel.txt Wurzel\nwrite baum/ast/zweig/spitze.txt Spitze\n");
            byte[] big = new byte[200 * 1024]; // größer als ein Schritt von cp -r
            new Random(26).NextBytes(big);
            File.WriteAllBytes(Path.Combine(home, "baum", "ast", "gross.bin"), big);
            Type("cp -r baum kopie\n");
            bool stepwise = OS.Shell.Busy && OS.Shell.Prompt.StartsWith("cp -r: ") && !Directory.Exists(Path.Combine(home, "kopie"));
            Type("ls\n");
            Check(stepwise && OS.Shell.History[OS.Shell.History.Count - 1] == "cp -r baum kopie",
                "cp -r kehrt sofort zurück und arbeitet in Schritten, bis dahin nimmt die Shell keine Befehle an");
            Finish();
            Check(!OS.Shell.Busy && File.ReadAllText(Path.Combine(home, "kopie", "wurzel.txt")) == "Wurzel"
                && File.ReadAllText(Path.Combine(home, "kopie", "ast", "zweig", "spitze.txt")) == "Spitze" && Directory.Exists(Path.Combine(home, "kopie", "leer"))
                && File.ReadAllBytes(Path.Combine(home, "kopie", "ast", "gross.bin")).AsSpan().SequenceEqual(big)
                && Last().Text == "Kopiert nach " + Vfs.Combine(OS.Users.Current.HomeDirectory, "kopie") + " mit 3 Dateien und 3 Unterordnern.",
                "cp -r kopiert verschachtelte Ordner samt Inhalt, auch leere Ordner und große Dateien");
            Type("cp -r baum kopie\n");
            Finish();
            Check(File.Exists(Path.Combine(home, "kopie", "baum", "ast", "zweig", "spitze.txt")), "cp -r in einen vorhandenen Ordner legt die Kopie hinein");
            Type("cp -r baum kopie\n");
            Check(Last().Text.StartsWith("Gibt es schon") && !OS.Shell.Busy, "cp -r überschreibt nichts");
            Type("cp -r baum baum/ast/innen\n");
            Check(Last().Text == "Ein Ordner passt nicht in sich selbst." && !OS.Shell.Busy && !Directory.Exists(Path.Combine(home, "baum", "ast", "innen")),
                "cp -r kopiert keinen Ordner in sich selbst");
            Type("cp -r baum BAUM/innen\n");
            Check(Last().Text == "Ein Ordner passt nicht in sich selbst." && !OS.Shell.Busy, "cp -r erkennt sich selbst auch in anderer Schreibweise, die auf FAT nicht zählt");
            Type("cp -r baum BAUM~1/innen\n");
            Check(Last().Text.StartsWith("Ordner kopiert cp -r nicht über einen Pfad mit ~") && !OS.Shell.Busy, "cp -r nimmt keinen Pfad mit ~");
            Type("cp -r /system /home/root/sicherung\n");
            Check(Last().Text.StartsWith("Das braucht das System") && !OS.Shell.Busy && !Directory.Exists(Path.Combine(home, "sicherung")),
                "cp -r kopiert den Systemordner mit der Nutzerdatei nicht, auch nicht für root");
            Type("cp -r /home/root /sicherung\n");
            Check(Last().Text.StartsWith("Das braucht das System") && !OS.Shell.Busy && !Directory.Exists(Path.Combine(disk, "sicherung")), "cp -r fasst keinen Home-Ordner an");
            Type("cp -r baum teil\n");
            Until(() => Directory.Exists(Path.Combine(home, "teil", "ast")));
            Press(Key.Escape);
            Finish();
            Check(!Directory.Exists(Path.Combine(home, "teil")) && Last().Text == "Die angefangene Kopie ist wieder entfernt, nichts kopiert.",
                "Escape bricht cp -r ab und entfernt die angefangene Kopie");
            // Verschwindet eine Datei, bevor sie dran ist (etwa in der App Dateien gelöscht), bleibt keine halbe Kopie zurück.
            Type("cp -r kopie fehl\n");
            Until(() => Directory.Exists(Path.Combine(home, "fehl")));
            File.Delete(Path.Combine(home, "kopie", "ast", "zweig", "spitze.txt"));
            Finish();
            ShellLine failed = OS.Shell.Lines[OS.Shell.Lines.Count - 2];
            Check(!Directory.Exists(Path.Combine(home, "fehl")) && failed.Style == LineStyle.Error && failed.Text.StartsWith("cp: ")
                && Last().Text == "Die angefangene Kopie ist wieder entfernt, nichts kopiert.", "Scheitert cp -r mittendrin, entfernt es wieder, was es angelegt hat");
            Type("rm kopie\n");
            Check(Last().Text.EndsWith("Mit 'rm -r' samt Inhalt löschen.") && Directory.Exists(Path.Combine(home, "kopie")), "rm ohne -r sagt bei einem vollen Ordner, wie es geht");
            Type("rm -r kopie\n");
            Finish();
            Check(OS.Shell.Asking && OS.Shell.Prompt == "Ordner " + Vfs.Combine(OS.Users.Current.HomeDirectory, "kopie")
                + " mit 5 Dateien und 7 Unterordnern löschen? Zum Bestätigen 'ja' eingeben: " && Directory.Exists(Path.Combine(home, "kopie")),
                "rm -r liest erst alles ein und fragt dann nach, wie viel es löscht");
            Type("nein\n");
            Check(!OS.Shell.Busy && Directory.Exists(Path.Combine(home, "kopie")) && Last().Text == "Abgebrochen, nichts gelöscht.", "rm -r lässt sich bei der Rückfrage abbrechen");
            Type("rm -r kopie\n");
            Finish();
            Type("ja\n");
            OS.Tick();
            OS.Tick();
            Press(Key.Escape);
            Check(!OS.Shell.Busy && Directory.Exists(Path.Combine(home, "kopie")) && Last().Text == "Abgebrochen. 2 von 13 Einträgen sind schon gelöscht.",
                "Escape hält rm -r an und sagt, wie weit es gekommen ist");
            Type("rm -r kopie\n");
            Finish();
            Type("ja\n");
            Finish();
            Check(!Directory.Exists(Path.Combine(home, "kopie")) && Last().Text == "Gelöscht: " + Vfs.Combine(OS.Users.Current.HomeDirectory, "kopie") + " mit 3 Dateien und 7 Unterordnern.",
                "rm -r löscht verschachtelte Ordner samt Inhalt");
            Type("cd baum/ast\nrm -r /home/root/baum\n");
            Check(Last().Text.StartsWith("Darin steht gerade die Shell") && !OS.Shell.Busy && Directory.Exists(Path.Combine(home, "baum")),
                "rm -r löscht nicht den Ordner, in dem die Shell steht, auch nicht von weiter innen");
            Type("cd /home/root\nrm -r BAUM~1\n");
            Check(Last().Text.StartsWith("rm -r nimmt keinen Pfad mit ~") && !OS.Shell.Busy, "rm -r nimmt keinen Pfad mit ~, darunter erkennt es feste Ordner nicht");
            Type("rm -r " + Vfs.Root + "home\n");
            Check(Last().Text.StartsWith("Das braucht das System") && !OS.Shell.Busy && Directory.Exists(home), "rm -r /mnt/home wird abgelehnt");
            Type("rm -r /home/root\n");
            Check(Last().Text.StartsWith("Das braucht das System") && !OS.Shell.Busy && Directory.Exists(home), "rm -r löscht keinen Home-Ordner");
            Type("rm -r /system\n");
            Check(Last().Text.StartsWith("Das braucht das System") && !OS.Shell.Busy && File.Exists(userFile), "rm -r löscht den Systemordner nicht, auch nicht für root");
            Type("rm -r /\n");
            Check(Last().Text.StartsWith("Das braucht das System") && !OS.Shell.Busy, "rm -r fasst die Wurzel nicht an");
            Type("rm -r baum\n");
            Finish();
            Type("ja\n");
            Finish();
            Check(!Directory.Exists(Path.Combine(home, "baum")), "rm -r räumt auf");
            // Wie in der App Dateien: Die Einstellungen eines Nutzers gehen erst nach einer Warnung.
            Check(!File.Exists(Path.Combine(home, Settings.FileName)), "Vor der Prüfung gibt es noch keine Einstellungen von root");
            Type("write settings.txt x=y\nrm settings.txt\n");
            Check(OS.Shell.Asking && Shown("Achtung, Systemdatei: Darin stehen die Einstellungen von root") && File.Exists(Path.Combine(home, Settings.FileName)),
                "rm warnt vor dem Löschen der Einstellungen und fragt nach");
            Type("nein\n");
            Check(Last().Text == "Abgebrochen." && File.Exists(Path.Combine(home, Settings.FileName)), "Ohne 'ja' bleiben die Einstellungen");
            Type("rm -r settings.txt\nja\n");
            Check(!File.Exists(Path.Combine(home, Settings.FileName)), "Nach der Warnung löscht rm die Einstellungen, auch mit -r");
            Type("df\n");
            Check(Last().Text.Contains("64 MB gesamt") && Last().Text.Contains("60 MB frei"), "df zeigt Größe und freien Platz der Platte");
            Platform.Hardware.DiskSpaceFails = true;
            Type("df\n");
            Check(Last().Style == LineStyle.Error && Last().Text.StartsWith("Der freie Platz"), "df sagt, wenn sich der freie Platz nicht ermitteln lässt");
            Platform.Hardware.DiskSpaceFails = false;
            Check(Text.Bytes(0) == "0 Byte" && Text.Bytes(1023) == "1023 Byte" && Text.Bytes(1536) == "1,5 KB" && Text.Bytes(10239) == "9,9 KB"
                && Text.Bytes(10 * 1024) == "10 KB" && Text.Bytes(5UL << 30) == "5 GB" && Text.Bytes(ulong.MaxValue) == "16777216 TB",
                "Größen werden lesbar ausgegeben");
            Check(Text.Bytes((1UL << 20) - 1) == "1 MB" && Text.Bytes((1UL << 30) - 1) == "1 GB", "Knapp unter der nächsten Einheit steht 1 MB und nicht 1024 KB");
            Check(Text.Bytes(10968105) == "10 MB" && Text.Bytes(11010048) == "11 MB" && Text.Bytes(1073112678) == "1023 MB" && Text.Bytes(1073217536) == "1 GB",
                "Größen werden einmal gerundet, nicht zweimal: 10,46 MB sind 10 MB, erst 10,5 MB sind 11 MB");
            Type("cd ..\ncd ..\ncd ..\n");
            Check(OS.Shell.CurrentDirectory == Vfs.Root, "cd .. verlässt die Wurzel nicht");
            Type("cd home/root\n");
            Check(Vfs.Resolve(Vfs.Root + "home", "../a/./b") == Vfs.Root + "a" + Vfs.Separator + "b", "Pfade werden aufgelöst");
            Check(Vfs.Resolve(Vfs.Root + "home", Vfs.Root.TrimEnd('/')) == Vfs.Root, "Die Wurzel gilt auch ohne abschließenden Trenner");
            Check(Vfs.Resolve(Vfs.Root, "system./users.txt") == Vfs.SystemDirectory + Vfs.Separator + "users.txt", "Punkte am Namensende ergeben keinen zweiten Namen");

            // Prozesse und Scheduler (VIB-3)
            // PID 1 ist das Terminal: Jede offene App ist ein Prozess und bekommt ihren Anteil. Allein war es schon bei rm -r und cp -r dran.
            int terminalSteps = OS.Processes.Get(1).Steps;
            Type("start counter\nstart counter\nstart clock\n");
            for (int i = 0; i < 40; i++) OS.Tick();
            Check(OS.Processes.Get(1).Name == "terminal" && OS.Processes.Get(1).Steps == terminalSteps + 10 && OS.Processes.Get(2).Steps == 10
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
            int frame1 = OS.Memory.TableOf(2).Pages[1].Frame;
            Type("translate 2 4103\n");
            Check(Last().Text.Contains("= " + (frame1 * 4096 + 7)) && Last().Style != LineStyle.Error, "translate rechnet Seite, Offset und Rahmen zur physischen Adresse");
            Type("translate 2 0x1007\n");
            Check(Last().Text.Contains("= " + (frame1 * 4096 + 7)), "translate versteht Adressen mit 0x");
            int logBefore = OS.Memory.Log.Count;
            Type("translate 2 " + (40 * 4096) + "\n");
            Check(Last().Style == LineStyle.Error && Last().Text.Contains("Page Fault") && OS.Memory.Log.Count == logBefore,
                "translate meldet einen Page Fault, ohne ihn als Zugriff zu protokollieren");
            Type("translate 2 -5\n");
            Check(Last().Style == LineStyle.Error, "translate lehnt negative Adressen ab");
            Type("pagetable 2\n");
            Check(Last().Text.StartsWith(OS.Memory.TableOf(2).Pages.Count - 1 + " "), "pagetable listet alle Seiten des Prozesses");
            int freeBefore = OS.Memory.Frames.FreeCount;
            Type("free 2 1\n");
            Check(!OS.Memory.TableOf(2).Pages[1].Present && OS.Memory.Frames.FreeCount == freeBefore + 1
                && OS.Memory.Frames.Owner[frame1] == FrameTable.Free, "free gibt die Seite und ihren Rahmen frei");
            Type("free 2 1\n");
            Check(Last().Style == LineStyle.Error && OS.Memory.Frames.FreeCount == freeBefore + 1, "free meldet eine Seite, die es nicht gibt");
            Type("pagetable 2\n");
            Check(OS.Shell.Lines[OS.Shell.Lines.Count - OS.Memory.TableOf(2).Pages.Count + 1].Text.Contains("nein"),
                "pagetable zeigt eine freigegebene Seite als nicht vorhanden");
            Type("translate 2 4103\n");
            Check(Last().Text.Contains("Page Fault"), "Nach free ist die Adresse ein Page Fault");
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

            // Skripte (VIB-28): sh führt je Durchlauf der Kernel-Schleife eine Zeile aus, mit Variablen, if, for und sleep.
            long fakeClock = 0;
            Shell.Clock = () => fakeClock += 50; // jede Abfrage 50 ms später: sleep wartet ohne echte Zeit
            Type("cd /home/root\n");
            string rootHome = Path.Combine(disk, "home", "root");
            File.WriteAllText(Path.Combine(rootHome, "probe.sh"), "#!/bin/sh\n# Kommentar\r\nname=Welt\n\necho \"Hallo $name\"\nfor x in a \"b c\"\n  echo Runde $x\ndone\n"
                + "if $name == Welt\necho gleich\nelse\necho ungleich\nfi\nif ! exists gibtsnicht\nif \"\" != \"$leer\"\necho falsch\nfi\necho fehlt\nfi\nfor y in\necho LEER\ndone\n");
            Type("sh probe.sh\n");
            Check(OS.Shell.Busy && OS.Shell.Prompt == "sh probe.sh: Zeile 1 von 22, Escape bricht ab", "sh kehrt sofort zurück und zeigt, in welcher Zeile das Skript steht");
            OS.Tick();
            Check(!Shown("Hallo Welt") && OS.Shell.Variable("name") == "Welt" && OS.Shell.Prompt.StartsWith("sh probe.sh: Zeile 3 von"),
                "sh überspringt Kommentare und leere Zeilen und führt je Durchlauf eine Zeile aus");
            OS.Tick();
            Check(Last().Text == "Hallo Welt", "Variablen werden in der nächsten Zeile ersetzt");
            int linesBefore = OS.Shell.Lines.Count;
            Finish();
            List<string> printed = new List<string>();
            for (int i = linesBefore; i < OS.Shell.Lines.Count; i++) printed.Add(OS.Shell.Lines[i].Text);
            Check(string.Join("|", printed) == "Runde a|Runde b c|gleich|fehlt", "for, if, else, exists und ! laufen wie erwartet: " + string.Join("|", printed));

            File.WriteAllText(Path.Combine(rootHome, "fehler.sh"), "echo eins\n\nunsinn\necho drei\n");
            Type("sh fehler.sh\n");
            Finish();
            Check(Last().Text == "sh fehler.sh: abgebrochen nach dem Fehler in Zeile 3." && Last().Style == LineStyle.Error && OS.Shell.Lines[OS.Shell.Lines.Count - 3].Text == "eins" && !OS.Shell.Busy,
                "Ein Fehler bricht das Skript mit der Nummer seiner Zeile ab");
            File.WriteAllText(Path.Combine(rootHome, "kaputt.sh"), "echo x\nfor i in 1 2\nif a == a\ndone\n");
            Type("sh kaputt.sh\n");
            Check(Last().Text == "sh kaputt.sh, Zeile 4: done ohne passendes for." && !OS.Shell.Busy, "sh prüft vorher, ob if/fi und for/done zusammenpassen");
            File.WriteAllText(Path.Combine(rootHome, "kaputt.sh"), "if a == a\necho x\n");
            Type("sh kaputt.sh\n");
            Check(Last().Text == "sh kaputt.sh, Zeile 1: Zu diesem if fehlt fi." && !OS.Shell.Busy, "Ein offenes if meldet sh mit seiner Zeile");
            File.WriteAllText(Path.Combine(rootHome, "kaputt.sh"), "if a = a\necho UNERREICHT\nfi\n");
            Type("sh kaputt.sh\n");
            Finish();
            Check(Last().Text == "sh kaputt.sh: abgebrochen nach dem Fehler in Zeile 1." && !Shown("UNERREICHT"), "Eine falsch geschriebene Bedingung ist ein Fehler");
            Type("sh fehlt.sh\n");
            Check(Last().Text.StartsWith("Skript nicht gefunden: ") && !OS.Shell.Busy, "sh sagt, wenn es das Skript nicht gibt");

            // Rückfragen und Arbeit in Schritten mitten im Skript: Das Skript wartet, bis sie fertig sind.
            Directory.CreateDirectory(Path.Combine(rootHome, "weg", "innen"));
            File.WriteAllText(Path.Combine(rootHome, "frage.sh"), "write settings.txt x=y\nrm settings.txt\nrm -r weg\necho weiter\n");
            Type("sh frage.sh\n");
            Finish();
            Check(OS.Shell.Asking && OS.Shell.Busy && Shown("Achtung, Systemdatei"), "sh wartet auf die Antwort einer Rückfrage");
            Type("nein\n");
            Finish();
            Check(OS.Shell.Asking && OS.Shell.Prompt.Contains("weg") && File.Exists(Path.Combine(rootHome, Settings.FileName)),
                "Eine abgelehnte Rückfrage ist kein Fehler, das Skript geht weiter bis zum nächsten Befehl mit Rückfrage");
            Type("ja\n");
            Finish();
            Check(!OS.Shell.Busy && !Directory.Exists(Path.Combine(rootHome, "weg")) && Last().Text == "weiter",
                "rm -r läuft im Skript in Schritten zu Ende, danach geht das Skript weiter");
            File.Delete(Path.Combine(rootHome, Settings.FileName));

            // sleep und Escape
            Check(SleepCommand.Parse("2", out long ms2) && ms2 == 2000 && SleepCommand.Parse("0.5", out long ms3) && ms3 == 500 && SleepCommand.Parse(".25", out long ms4) && ms4 == 250
                && !SleepCommand.Parse("x", out _) && !SleepCommand.Parse("601", out _) && !SleepCommand.Parse("-1", out _) && !SleepCommand.Parse("1,5", out _) && !SleepCommand.Parse(".", out _),
                "sleep liest Sekunden mit Punkt, ohne Ländereinstellung");
            File.WriteAllText(Path.Combine(rootHome, "warten.sh"), "sleep 5\necho NIEMALS\n");
            Type("sh warten.sh\n");
            OS.Tick();
            OS.Tick();
            Check(OS.Shell.Busy && OS.Shell.Prompt.StartsWith("sleep: noch "), "sleep wartet, ohne die Kernel-Schleife anzuhalten, und zeigt die Restzeit");
            Press(Key.Escape);
            Check(!OS.Shell.Busy && Last().Text == "sh warten.sh: abgebrochen in Zeile 1." && !Shown("NIEMALS"), "Escape bricht sleep und das Skript dahinter ab");
            Type("sleep 1\n");
            Check(OS.Shell.Busy, "sleep geht auch ohne Skript");
            Finish();
            Check(!OS.Shell.Busy, "sleep ist nach der Zeit fertig");

            // Variablen in der Shell
            Type("gruss=\"Hallo Welt\"\necho $gruss und ${gruss}! \\$gruss kostet 5$\n");
            Check(Last().Text == "Hallo Welt und Hallo Welt! $gruss kostet 5$", "Variablen gelten auch im Terminal, \\$ ist ein echtes Dollarzeichen");
            Type("echo [$unbekannt] $USER $HOME\n");
            Check(Last().Text == "[] root " + OS.Users.Current.HomeDirectory, "Unbekannte Variablen sind leer, USER und HOME kommen vom System");
            Type("USER=mallory\n");
            Check(Last().Style == LineStyle.Error && OS.Shell.Variable("USER") == "root", "Feste Variablen lassen sich nicht setzen");
            Type("set\n");
            Check(Shown("gruss=Hallo Welt"), "set zeigt die Variablen");
            Type("start counter\n");
            int started = OS.Processes.All[OS.Processes.All.Count - 1].Id;
            Type("echo $!\nkill $!\n");
            Check(Shown(started + "") && Last().Text == "Prozess " + started + " beendet.", "$! ist der zuletzt gestartete Prozess");

            // set -x, sh in sh, Grenze
            File.WriteAllText(Path.Combine(rootHome, "innen.sh"), "set -x\necho x\nset +x\necho still\n");
            File.WriteAllText(Path.Combine(rootHome, "aussen.sh"), "sh innen.sh\necho zurück\n");
            Type("sh aussen.sh\n");
            Finish();
            Check(Shown(OS.Shell.CommandPrompt + "echo x") && !Shown(OS.Shell.CommandPrompt + "echo still") && Last().Text == "zurück",
                "set -x zeigt Befehle wie getippt, ein Skript kann ein anderes starten");
            File.WriteAllText(Path.Combine(rootHome, "selbst.sh"), "sh selbst.sh\n");
            Type("sh selbst.sh\n");
            Finish();
            Check(!OS.Shell.Busy && Shown("Zu viele Skripte ineinander") && Last().Text == "sh selbst.sh: abgebrochen nach dem Fehler in Zeile 1.",
                "Ein Skript, das sich selbst startet, endet nach " + ScriptJob.MaxDepth + " Ebenen");

            // Nutzerwechsel mitten im Skript
            Type("sh warten.sh\n");
            OS.Tick();
            VibanOS.Users.User me = OS.Users.Current;
            OS.Users.Current = OS.Users.Find("anna");
            fakeClock += 10000;
            Finish();
            OS.Users.Current = me;
            Check(!OS.Shell.Busy && Shown("sh warten.sh beendet, weil der Nutzer gewechselt hat.") && !Shown("NIEMALS"), "Ein Skript läuft nicht als anderer Nutzer weiter");

            // Die Vorführung (VIB-4)
            int errors = OS.Shell.Errors;
            Type("sh -p demo.sh\n");
            Check(Shown("# demo.sh: Vorführung von VibanOS"), "sh -p zeigt das eingebaute Skript");
            int processes = OS.Processes.All.Count;
            Type("cd /\nsh demo.sh\n");
            Finish();
            Check(OS.Shell.Errors == errors && Last().Text.StartsWith("Fertig.") && !Directory.Exists(Path.Combine(rootHome, "vorfuehrung"))
                && OS.Shell.CurrentDirectory == OS.Users.Current.HomeDirectory, "sh demo.sh läuft ohne Fehler durch und räumt auf");
            Check(Shown(OS.Shell.CommandPrompt + "kill ") && Shown("== 5. Treiber"), "demo.sh zeigt die Befehle und alle Teile");
            Shot(output, "9-skript-demo");
            Type("geheimnis=42\n"); // sieht der nächste Nutzer nicht

            Type("logout\n");
            Check(OS.Users.Current == null, "logout sperrt den Schreibtisch");
            Check(OS.Shell.Lines.Count == 0 && OS.Shell.History.Count == 0, "logout räumt Ausgaben und Verlauf für den nächsten Nutzer weg");
            LoginScreen login = new LoginScreen();
            for (int i = 0; i < 3; i++) { login.OnKey(Key.None, 'x'); login.OnKey(Key.Enter, '\n'); }
            foreach (char ch in "geheim\n") login.OnKey(ch == '\n' ? Key.Enter : Key.None, ch);
            Check(OS.Users.Current == null, "Während der Sperre nach drei Fehlversuchen hilft auch das richtige Passwort nicht");
            Check(!login.CaretShown, "Während der Sperre fehlt die Einfügemarke, das Feld nimmt nichts an");
            DateTime now = DateTime.Now;
            Check(!login.LockExpired(now) && login.LockExpired(now.AddSeconds(6)) && !login.LockExpired(now.AddSeconds(7)),
                "Die Sperre nach drei Fehlversuchen läuft ab und meldet das genau einmal");
            Check(login.CaretShown, "Nach der Sperre ist die Einfügemarke gleich wieder da");
            Type("\tfalsch\n");
            Check(OS.Users.Current == null, "Falsches Passwort wird abgelehnt");
            Shot(output, "2-anmeldung-fehler");
            File.WriteAllText(Path.Combine(disk, "home", "anna", ShCommand.Autostart), "gruss=Hallo\necho \"$gruss $USER [$geheimnis]\"\n");
            Type("topsecret\n");
            Check(OS.Users.Current.Name == "anna", "Tab wechselt den Nutzer, anna meldet sich an");
            Finish();
            Check(Last().Text == "Hallo anna []" && !OS.Shell.Busy, "Nach der Anmeldung läuft autostart.sh, Variablen des Vorgängers sind weg");
            File.Delete(Path.Combine(disk, "home", "anna", ShCommand.Autostart));
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
            Type("block 1\n");
            Check(Last().Text.StartsWith("Keine Berechtigung") && OS.Processes.Get(1).State != ProcessState.Blocked,
                "Normale Nutzer blockieren fremde Prozesse nicht");
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
            File.WriteAllText(Path.Combine(disk, "home", "root", ShCommand.Autostart), "write untergeschoben.txt x\n");
            Type("logout\n\tgeheim\n");
            Check(OS.Users.Current.Name == "root", "root meldet sich wieder an");
            Check(!OS.Shell.Busy && Last().Text.StartsWith(ShCommand.Autostart + " übersprungen") && !File.Exists(Path.Combine(disk, "home", "root", "untergeschoben.txt")),
                "autostart.sh läuft nicht für root, solange jeder in fremde Home-Ordner schreiben darf");
            File.Delete(Path.Combine(disk, "home", "root", ShCommand.Autostart));
            Type("cat /system/users.txt\n");
            Check(Last().Style != LineStyle.Error, "Administratoren dürfen in den Systemordner");

            // Gruppen, Rechte und sudo (VIB-17)
            string groupFile = Path.Combine(Vfs.SystemDirectory, "groups.txt");
            Type("groups\n");
            Check(Last().Text == "admin", "groups zeigt die Gruppen des angemeldeten Nutzers");
            Type("perms anna\n");
            Check(Last().Text == "anna hat keine Systemrechte.", "Normale Nutzer haben keine Systemrechte");
            Type("groupadd team\ngroupadd team\n");
            Check(Last().Style == LineStyle.Error && OS.Users.FindGroup("team") != null && File.Exists(groupFile), "groupadd legt eine Gruppe an, aber nur einmal");
            Type("usermod -aG team anna\nusermod -aG admin anna\ngroups anna\n");
            Check(Last().Text == "users team admin", "usermod -aG nimmt in Gruppen auf");
            Type("usermod -aG niemand anna\n");
            Check(Last().Style == LineStyle.Error, "usermod meldet eine unbekannte Gruppe");
            Type("usermod -rG admin root\n");
            Check(Last().Style == LineStyle.Error && UserManager.InGroup(OS.Users.Find("root"), UserManager.AdminGroup), "root bleibt in admin");
            Type("groupdel admin\n");
            Check(Last().Style == LineStyle.Error && OS.Users.FindGroup("admin") != null, "Die Gruppe admin lässt sich nicht löschen");
            File.AppendAllText(groupFile, "kaputt\n5;admin;\n");
            reloaded = new UserManager();
            reloaded.Load();
            Check(reloaded.FindGroup("team") != null && UserManager.InGroup(reloaded.Find("anna"), "team") && UserManager.InGroup(reloaded.Find("anna"), "admin")
                && reloaded.HasPermission(reloaded.Find("anna"), Permission.Shutdown), "Gruppen überstehen einen Neustart, admin behält alle Rechte");
            Type("su anna\nshutdown\n");
            Check(Last().Text.Contains("sudo shutdown") && !Platform.Hardware.ShutdownCalled, "Auch Admins fahren ohne sudo nicht herunter und erfahren, wie es geht");
            Type("cat /system/users.txt\n");
            Check(Last().Text.StartsWith("Zugriff verweigert"), "Ohne sudo kommen Admins nicht in den Systemordner");
            Type("perms\n");
            Check(Last().Text == "Diese Rechte gelten nur mit sudo." && Shown("Shutdown            über admin"), "perms zeigt Rechte mit ihrer Gruppe");
            Type("sudo nichts\n");
            Check(Last().Style == LineStyle.Error && !OS.Shell.Asking, "sudo meldet einen unbekannten Befehl, ohne nach dem Passwort zu fragen");
            Type("sudo useradd eve\nfalsch\n");
            Check(Last().Text == "Falsches Passwort." && !OS.Shell.Asking && OS.Users.Find("eve") == null, "sudo macht nichts ohne das richtige Passwort");
            Type("sudo useradd eve\ntopsecret\nevepw\n");
            Check(OS.Users.Find("eve") != null && !OS.Users.Elevated && !Shown("evepw"), "Mit sudo legt ein Admin Nutzer an");
            Type("useradd fritz\n");
            Check(Last().Text.StartsWith("Keine Berechtigung") && OS.Users.Find("fritz") == null, "sudo gilt nur für den einen Befehl");
            Type("sudo cat \"/system/users.txt\"\ntopsecret\n");
            Check(Last().Style != LineStyle.Error, "Mit sudo kommt ein Admin in den Systemordner");
            // rm -r arbeitet in Schritten (VIB-26). Namen mit ~ sind Administratoren vorbehalten, und sudo gilt für alle Schritte.
            string kram = Path.Combine(disk, "home", "anna", "kram");
            Directory.CreateDirectory(kram);
            File.WriteAllText(Path.Combine(kram, "ALT~1.TXT"), "x");
            Type("rm -r /home/anna/kram\n");
            Finish();
            Check(!OS.Shell.Busy && Last().Text.StartsWith("Zugriff verweigert: ") && Last().Text.EndsWith("Nichts gelöscht.") && File.Exists(Path.Combine(kram, "ALT~1.TXT")),
                "rm -r prüft jede Datei einzeln und löscht nichts, wenn eine nicht erlaubt ist");
            Type("sudo rm -r /home/anna/kram\ntopsecret\n");
            Finish();
            bool askedElevated = OS.Shell.Asking && !OS.Users.Elevated;
            Type("ja\n");
            Finish();
            Check(askedElevated && !Directory.Exists(kram) && !OS.Users.Elevated && Last().Text.StartsWith("Gelöscht: "),
                "sudo gilt für alle Schritte von rm -r, dazwischen und danach nicht");
            Type("su eve\nevepw\nsudo useradd fritz\n");
            Check(OS.Users.Current.Name == "eve" && Last().Text.Contains("nicht in der Gruppe admin") && !OS.Shell.Asking, "Wer nicht in admin ist, darf sudo nicht benutzen");
            Type("groupadd bande\n");
            Check(Last().Text.StartsWith("Keine Berechtigung") && OS.Users.FindGroup("bande") == null, "Normale Nutzer legen keine Gruppen an");
            Type("su\ngeheim\n");
            string eveHome = OS.Users.Find("eve").HomeDirectory;
            Type("userdel eve\nja\nusermod -rG admin anna\ngroupdel team\n");
            Check(OS.Users.FindGroup("team") == null && !UserManager.InGroup(OS.Users.Find("anna"), "team") && !UserManager.InGroup(OS.Users.Find("anna"), "admin")
                && !File.ReadAllText(groupFile).Contains("team"), "groupdel löscht die Gruppe und trägt ihre Mitglieder aus");
            Directory.Delete(eveHome);

            // su, login, userdel (VIB-16)
            Type("su niemand\n");
            Check(Last().Style == LineStyle.Error && !OS.Shell.Asking, "su meldet einen unbekannten Nutzer, ohne nach dem Passwort zu fragen");
            string suFolder = OS.Shell.CurrentDirectory;
            Type("su anna\n");
            Check(OS.Users.Current.Name == "anna" && !OS.Shell.Asking, "root wechselt mit su ohne Passwort");
            Type("userdel root\n");
            Check(Last().Text.StartsWith("Keine Berechtigung") && !OS.Shell.Asking, "Normale Nutzer löschen keine Nutzer");
            Type("su\nfalsch\n");
            Check(OS.Users.Current.Name == "anna" && Last().Style == LineStyle.Error, "su wechselt nicht ohne das richtige Passwort");
            Type("su\ngeheim\n");
            Check(OS.Users.Current.Name == "root" && OS.Shell.CurrentDirectory == suFolder && !Shown("geheim"),
                "su ohne Namen wechselt zu root, der Ordner bleibt, das Passwort steht nirgends");
            Type("login anna\nfalsch\n");
            Check(OS.Users.Current.Name == "root" && Last().Text == "Anmeldung fehlgeschlagen.", "login mit falschem Passwort ändert nichts");
            Type("login niemand\n");
            Check(OS.Shell.Asking && OS.Shell.Masked, "login fragt auch bei einem unbekannten Namen nach dem Passwort");
            Type("x\nlogin\nanna\ntopsecret\n");
            Check(OS.Users.Current.Name == "anna" && OS.Shell.Lines.Count == 1 && OS.Shell.History.Count == 0
                && OS.Shell.CurrentDirectory == OS.Users.Current.HomeDirectory, "login meldet neu an: leeres Terminal, kein Verlauf, eigener Ordner");
            Type("login root\ngeheim\n");
            Check(OS.Users.Current.Name == "root", "login zurück zu root");
            Type("useradd carl\nkarl\nuserdel carl\nnein\n");
            Check(OS.Users.Find("carl") != null && Last().Text == "Abgebrochen.", "userdel löscht erst nach einem ja");
            Type("userdel root\n");
            Check(Last().Style == LineStyle.Error && OS.Users.Find("root") != null, "root lässt sich nicht löschen");
            Type("userdel nobody\n");
            Check(Last().Style == LineStyle.Error && !OS.Shell.Asking, "userdel meldet einen unbekannten Nutzer");
            string carlHome = OS.Users.Find("carl").HomeDirectory;
            Type("userdel carl\nja\n");
            reloaded = new UserManager();
            reloaded.Load();
            Check(OS.Users.Find("carl") == null && reloaded.Find("carl") == null && Directory.Exists(carlHome),
                "userdel löscht den Nutzer dauerhaft, sein Ordner bleibt");
            Directory.Delete(carlHome);

            // Nutzerwahl am Anmeldebildschirm: Klick aufs Bild, Nutzer wechseln, neuen Nutzer anlegen
            Type("logout\n");
            LoginScreen screenLogin = desktop.Login;
            Draw();
            Check(screenLogin.Animating(animTime), "Nach dem Abmelden blendet die Anmeldung ein");
            animTime += T.Motion.Appear;
            Check(!screenLogin.Animating(animTime), "Danach steht die Anmeldung still");

            // Einfügemarke im Feld: blinkt, steht beim Tippen still und wandert mit, fehlt in der Nutzerwahl
            int caretPixels = T.Size.Caret * Fonts.Body.LineHeight * screen.Scale * screen.Scale;
            Draw();
            int caretX, typedX, unused, empty = Caret(out caretX);
            Check(screenLogin.CaretShown && empty == caretPixels, "Im leeren Feld steht die Einfügemarke");
            animTime += T.Motion.Blink;
            bool blinked = Draw();
            Check(blinked && !screenLogin.CaretShown && Caret(out unused) == 0 && dirtyArea * 100 < (long)screen.Width * screen.Height,
                "Die Einfügemarke blinkt, dabei entsteht nur ihr kleiner Ausschnitt neu (" + dirtyArea + " Punkte)");
            animTime += T.Motion.Blink;
            Check(Draw() && screenLogin.CaretShown && Caret(out unused) == caretPixels, "Nach einem Takt ist sie wieder da");
            animTime += T.Motion.Blink / 2;
            Type("ab");
            animTime += T.Motion.Blink - 1;
            Draw();
            int typed = Caret(out typedX);
            Check(screenLogin.CaretShown && typed == caretPixels, "Beim Tippen steht die Einfügemarke still und blinkt erst danach weiter");
            Check(typedX == caretX + T.Size.Caret + 2 * T.Space.M, "Die Einfügemarke wandert hinter den letzten Punkt");
            Press(Key.Escape);

            ClickAt(screenLogin.AvatarCenter());
            Check(screenLogin.Picking && screenLogin.Animating(animTime), "Ein Klick auf das Nutzerbild fächert die Nutzerwahl auf");
            Check(!screenLogin.CaretShown, "In der Nutzerwahl hat das Feld keine Einfügemarke");
            animTime += T.Motion.Picker / 3;
            OS.Changed = true;
            Draw();
            SavePng(screen, Path.Combine(output, "2-nutzerwahl-halb.png"));
            Shot(output, "2-nutzerwahl");
            Check(!screenLogin.Animating(animTime), "Danach steht die Nutzerwahl still");
            Press(Key.Escape);
            Check(!screenLogin.Picking && OS.Users.Current == null && screenLogin.Animating(animTime), "Esc schließt die Nutzerwahl, die Bilder fallen zurück");
            Settle();
            ClickAt(screenLogin.AvatarCenter());
            int[] annaTile = screenLogin.TileCenter(1);
            desktop.OnMouse(annaTile[0], annaTile[1], false);
            Press(Key.Enter);
            Type("topsecret\n");
            Check(OS.Users.Current.Name == "anna", "Die Maus markiert ein Nutzerbild, die Eingabetaste wählt es");
            Type("logout\n");
            Settle();
            ClickAt(screenLogin.AvatarCenter());
            ClickAt(screenLogin.TileCenter(1));
            Check(!screenLogin.Picking, "Ein Klick auf einen Nutzer schließt die Nutzerwahl");
            Type("topsecret\n");
            Check(OS.Users.Current.Name == "anna", "Der gewählte Nutzer meldet sich an");
            Type("logout\n");
            ClickAt(screenLogin.AvatarCenter());
            ClickAt(screenLogin.TileCenter(OS.Users.All.Count));
            Check(screenLogin.Creating, "Das Plus in der Nutzerwahl legt einen neuen Nutzer an");
            Settle();
            Type("Dora");
            Draw();
            int nameX, named = Caret(out nameX);
            Check(screenLogin.CaretShown && named == caretPixels && nameX == caretX + T.Size.Caret + Fonts.Body.Measure("dora"),
                "Beim Anlegen steht die Einfügemarke hinter dem getippten Namen");
            Press(Key.Escape);
            Check(!screenLogin.Creating && !screenLogin.Picking, "Esc bricht das Anlegen ab");
            ClickAt(screenLogin.AvatarCenter());
            Press(Key.Right); // markiert ist anna, das Feld daneben ist das Plus
            Press(Key.Enter);
            Type("Anna\n");
            Check(screenLogin.Creating && OS.Users.All.Count == 2, "Einen vergebenen Namen nimmt die Anmeldung nicht an");
            Press(Key.Escape);
            ClickAt(screenLogin.AvatarCenter());
            ClickAt(screenLogin.TileCenter(OS.Users.All.Count));
            Settle();
            Type("Dora\nblume\nblüte\n");
            Check(screenLogin.Creating && OS.Users.Find("dora") == null, "Zwei verschiedene Passwörter legen niemanden an");
            Check(screenLogin.Animating(animTime), "Bei ungleichen Passwörtern schüttelt sich das Feld");
            animTime += T.Motion.Shake / 4;
            OS.Changed = true;
            Draw();
            SavePng(screen, Path.Combine(output, "2-schuetteln.png"));
            Settle();
            Type("blume\n");
            Shot(output, "2-neuer-nutzer");
            Type("blume\n");
            User dora = OS.Users.Find("dora");
            Check(dora != null && OS.Users.Current == dora && UserManager.InGroup(dora, UserManager.UserGroup) && !UserManager.InGroup(dora, UserManager.AdminGroup)
                && OS.Shell.CurrentDirectory == dora.HomeDirectory && Directory.Exists(dora.HomeDirectory),
                "Am Anmeldebildschirm entsteht ohne Admin ein normaler Nutzer mit eigenem Ordner und ist gleich angemeldet");
            Check(!File.ReadAllText(userFile).Contains("blume"), "In der Nutzerdatei steht kein Passwort im Klartext");
            reloaded = new UserManager();
            reloaded.Load();
            Check(reloaded.CheckPassword(reloaded.Find("dora"), "blume"), "Der neue Nutzer übersteht einen Neustart");
            Type("logout\n");
            ClickAt(screenLogin.AvatarCenter());
            ClickAt(screenLogin.TileCenter(0));
            Type("geheim\n");
            Check(OS.Users.Current.Name == "root", "Über die Nutzerwahl zurück zu root");
            Type("userdel dora\nja\n");
            Directory.Delete(dora.HomeDirectory, true);
            Check(OS.Users.All.Count == 2, "dora ist wieder weg");

            // Fenster und Maus
            desktop.Open(Icons.About);
            Window about = desktop.Active;
            Click(about.X + T.Space.M + T.Size.Control / 2, about.Y + T.Size.TitleBar / 2);
            Check(desktop.Active is TerminalWindow, "Der rote Knopf schließt das Fenster");
            Window terminal = desktop.Active;
            int oldX = terminal.X;
            int gripX = terminal.X + 300, gripY = terminal.Y + 10;
            desktop.OnMouse(gripX, gripY, true);
            desktop.OnMouse(gripX - 50, gripY, true);
            desktop.OnMouse(gripX - 60, gripY, false); // Bewegung und Loslassen kommen im selben Durchlauf an
            Check(terminal.X == oldX - 60, "Fenster lassen sich an der Titelleiste ziehen, auch der letzte Schritt beim Loslassen zählt");
            desktop.OnMouse(gripX, gripY, false);
            Check(terminal.X == oldX - 60, "Ein losgelassenes Fenster folgt der Maus nicht mehr");
            int dockRow = T.Size.ScreenHeight - T.Space.S - T.Space.M - T.Size.DockIcon / 2, dockTop = dockRow - T.Size.DockIcon / 2 - T.Space.S;
            desktop.OnMouse(terminal.X + terminal.Width - 5, terminal.Y + 10, true);
            desktop.OnMouse(0, T.Size.ScreenHeight - 1, true);
            Check(terminal.X + terminal.Width >= T.Size.DockIcon && terminal.Y + T.Size.TitleBar <= dockTop,
                "Ein Fenster lässt sich nicht ganz aus dem Bildschirm oder unter das Dock ziehen");
            desktop.OnMouse(300, 300, false); // nicht in der Ecke loslassen, sonst dockt es dort an
            Check(terminal.Snapped == 0, "Mitten auf dem Bildschirm losgelassen dockt ein Fenster nicht an");

            // Fenstergröße an Rändern und Ecken ziehen
            terminal.X = 200; terminal.Y = 100;
            int termW = terminal.Width, termH = terminal.Height;
            int edgeX = terminal.X + termW + 2, edgeY = terminal.Y + 200; // knapp außerhalb des rechten Rands
            desktop.OnMouse(edgeX, edgeY, false);
            Draw();
            Check(screen.Pixels[edgeY * screen.Width + edgeX - 8] == unchecked((int)T.Color.CursorEdge)
                && screen.Pixels[edgeY * screen.Width + edgeX] == unchecked((int)T.Color.CursorFill),
                "Über dem rechten Rand zeigt der Zeiger einen Doppelpfeil mit der Maus in der Mitte");
            desktop.OnMouse(edgeX, edgeY, true);
            desktop.OnMouse(edgeX + 40, edgeY + 30, true);
            desktop.OnMouse(edgeX + 50, edgeY + 30, false);
            Check(terminal.Width == termW + 50 && terminal.Height == termH && terminal.X == 200,
                "Am rechten Rand wird das Fenster breiter, auch der letzte Schritt beim Loslassen zählt");
            Draw();
            desktop.OnMouse(edgeX, edgeY, false);
            Check(terminal.Width == termW + 50, "Ein losgelassener Rand folgt der Maus nicht mehr");
            desktop.OnMouse(400, 300, false);
            Draw();
            Check(screen.Pixels[300 * screen.Width + 400] == unchecked((int)T.Color.CursorEdge), "Im Fenster ist der Zeiger wieder ein Pfeil");
            int cornerX = terminal.X + 1, cornerY = terminal.Y + 1;
            desktop.OnMouse(cornerX, cornerY, true);
            desktop.OnMouse(cornerX + 2000, cornerY + 2000, false);
            Check(terminal.Width == terminal.MinWidth && terminal.Height == terminal.MinHeight
                && terminal.X + terminal.Width == 200 + termW + 50 && terminal.Y + terminal.Height == 100 + termH,
                "An der linken oberen Ecke schrumpft das Fenster bis zur Mindestgröße, rechts unten bleibt es stehen");
            Draw();
            cornerX = terminal.X - 2; cornerY = terminal.Y + 3;
            desktop.OnMouse(cornerX, cornerY, true);
            desktop.OnMouse(cornerX - 100, 0, false);
            Check(terminal.Y == T.Size.MenuBar && terminal.X == 200 + termW + 50 - terminal.MinWidth - 100 && terminal.Y + terminal.Height == 100 + termH,
                "Nach oben wächst ein Fenster höchstens bis an die Menüleiste");
            int bottomEdge = terminal.Y + terminal.Height - 2;
            desktop.OnMouse(terminal.X + terminal.Width / 2, bottomEdge, true);
            desktop.OnMouse(terminal.X + terminal.Width / 2, bottomEdge + 40, false);
            Check(terminal.Height == 100 + termH - T.Size.MenuBar + 40, "Am unteren Rand wird das Fenster höher");
            Draw();
            int infoBefore = OS.Processes.All.Count;
            desktop.Open(Icons.About);
            Window aboutBox = desktop.Active;
            Settle();
            int aboutEdge = aboutBox.X + aboutBox.Width + 2;
            desktop.OnMouse(aboutEdge, aboutBox.Y + 100, true);
            desktop.OnMouse(aboutEdge + 60, aboutBox.Y + 100, false);
            Check(aboutBox.Width == 400, "Auch Info lässt sich an den Rändern größer ziehen");
            Draw();
            aboutEdge = aboutBox.X + aboutBox.Width + 2;
            desktop.OnMouse(aboutEdge, aboutBox.Y + 100, true);
            desktop.OnMouse(aboutEdge - 300, aboutBox.Y + 100, false);
            Check(aboutBox.Width == 340, "Kleiner als zu Beginn wird Info nicht, sonst passt der Inhalt nicht");
            Click(ControlX(aboutBox, Window.CloseButton), aboutBox.Y + T.Size.TitleBar / 2);
            Settle();
            Check(OS.Processes.All.Count == infoBefore, "Info ist wieder zu");
            Click(ControlX(terminal, Window.ZoomButton), terminal.Y + T.Size.TitleBar / 2);
            Settle();
            int zoomedW = terminal.Width;
            desktop.OnMouse(terminal.X + terminal.Width - 1, terminal.Y + 200, true);
            desktop.OnMouse(terminal.X + terminal.Width - 101, terminal.Y + 200, false);
            Check(!terminal.Zoomed && terminal.Width == zoomedW - 100, "Ein vergrößertes Fenster lässt sich verkleinern und gilt dann nicht mehr als vergrößert");
            terminal.X = oldX - 60; terminal.Y = 100; terminal.Width = termW; terminal.Height = termH;
            OS.Changed = true;
            Draw();

            // Andocken: an einen Bildschirmrand gezogen füllt ein Fenster diese Hälfte, in einer Ecke ein Viertel
            int areaX = T.Space.S, areaY = T.Size.MenuBar + T.Space.S, areaW = T.Size.ScreenWidth - 2 * T.Space.S;
            int areaH = T.Size.ScreenHeight - (T.Size.DockIcon + T.Space.S + T.Space.M) - T.Space.S - T.Space.S - areaY;
            int halfW = (areaW - T.Space.S) / 2, halfH = (areaH - T.Space.S) / 2;
            int probe = (areaY + areaH - 20) * screen.Width + 100; // links unten in der Hälfte, nicht unter dem Fenster
            int beforeSnap = screen.Pixels[probe];
            desktop.OnMouse(terminal.X + 300, terminal.Y + 10, true);
            desktop.OnMouse(0, 60, true);
            Draw();
            Check(screen.Pixels[probe] != beforeSnap, "Am linken Rand zeigt eine Fläche, wohin das Fenster andockt");
            SavePng(screen, Path.Combine(output, "5-andocken.png"));
            desktop.OnMouse(0, 60, false);
            Settle();
            Check(terminal.Snapped == Window.EdgeLeft && terminal.X == areaX && terminal.Y == areaY && terminal.Width == halfW && terminal.Height == areaH,
                "Am linken Rand losgelassen füllt das Fenster die linke Hälfte");
            Check(screen.Pixels[probe] != beforeSnap, "Nach dem Andocken liegt das Fenster dort, wo die Fläche war");
            desktop.OnMouse(terminal.X + 300, terminal.Y + 10, true);
            desktop.OnMouse(700, 300, true);
            Check(terminal.Snapped == 0 && terminal.Width == termW && terminal.Height == termH && terminal.X < 700 && terminal.X + terminal.Width > 700,
                "Weggezogen bekommt ein angedocktes Fenster seine alte Größe zurück und bleibt unter der Maus");
            desktop.OnMouse(T.Size.ScreenWidth - 1, 0, false);
            Settle();
            Check(terminal.Snapped == (Window.EdgeRight | Window.EdgeTop) && terminal.X + terminal.Width == areaX + areaW && terminal.Y == areaY
                && terminal.Width == halfW && terminal.Height == halfH, "In der Ecke rechts oben losgelassen füllt das Fenster dieses Viertel");
            desktop.OnMouse(terminal.X + 200, terminal.Y + 10, true);
            desktop.OnMouse(T.Size.ScreenWidth / 2, T.Size.ScreenHeight - 1, false);
            Settle();
            Check(terminal.Snapped == Window.EdgeBottom && terminal.Y + terminal.Height == areaY + areaH && terminal.Width == areaW && terminal.Height == halfH,
                "Am unteren Rand losgelassen füllt das Fenster die untere Hälfte");
            desktop.OnMouse(terminal.X + 200, terminal.Y + 10, true);
            desktop.OnMouse(T.Size.ScreenWidth / 2, 0, false);
            Settle();
            Check(terminal.Snapped == Window.EdgeTop && terminal.Y == areaY && terminal.Height == halfH, "Am oberen Rand losgelassen füllt das Fenster die obere Hälfte");
            Click(ControlX(terminal, Window.ZoomButton), terminal.Y + T.Size.TitleBar / 2);
            Settle();
            Check(terminal.Zoomed && terminal.Snapped == 0 && terminal.Width == areaW, "Der grüne Knopf vergrößert auch ein angedocktes Fenster");
            Click(ControlX(terminal, Window.ZoomButton), terminal.Y + T.Size.TitleBar / 2);
            Settle();
            Check(!terminal.Zoomed && terminal.Width == termW && terminal.Height == termH, "Danach stellt er die Größe von vor dem Andocken her");
            desktop.Open(Icons.About);
            Settle();
            Window snapAbout = desktop.Active;
            desktop.OnMouse(snapAbout.X + 200, snapAbout.Y + 10, true);
            desktop.OnMouse(0, 300, false);
            Settle();
            Check(snapAbout.Snapped == Window.EdgeLeft && snapAbout.Width == halfW, "Auch Info dockt an");

            // Liegt in einer Hälfte schon ein Fenster, teilen sich beide sie
            desktop.Open(Icons.Terminal); // nach vorn, damit der Griff an der Titelleiste es trifft
            desktop.OnMouse(terminal.X + 200, terminal.Y + 10, true);
            desktop.OnMouse(T.Size.ScreenWidth - 1, T.Size.ScreenHeight / 2 + 100, false);
            Settle();
            Check(terminal.Snapped == Window.EdgeRight && terminal.Height == areaH, "Rechts losgelassen füllt das Terminal die rechte Hälfte");
            desktop.Open(Icons.About);
            desktop.OnMouse(snapAbout.X + 200, snapAbout.Y + 10, true);
            desktop.OnMouse(T.Size.ScreenWidth - 1, T.Size.ScreenHeight / 2 - 100, true);
            Draw();
            desktop.OnMouse(T.Size.ScreenWidth - 1, T.Size.ScreenHeight / 2 - 100, false);
            Settle();
            Check(snapAbout.Snapped == (Window.EdgeRight | Window.EdgeTop) && snapAbout.Y == areaY && snapAbout.Height == halfH
                && terminal.Snapped == (Window.EdgeRight | Window.EdgeBottom) && terminal.Y + terminal.Height == areaY + areaH && terminal.Height == halfH
                && terminal.X == snapAbout.X && terminal.Width == halfW,
                "Ein zweites Fenster rechts teilt sich die Hälfte: es kommt nach oben, das andere rückt nach unten");
            desktop.Open(Icons.About);
            desktop.OnMouse(snapAbout.X + 200, snapAbout.Y + 10, true);
            desktop.OnMouse(T.Size.ScreenWidth / 2, 200, false);
            Settle();
            desktop.Open(Icons.Terminal); // nach vorn, damit der Griff an der Titelleiste es trifft
            desktop.OnMouse(terminal.X + 200, terminal.Y + 10, true);
            desktop.OnMouse(T.Size.ScreenWidth / 2, 0, false);
            Settle();
            Check(terminal.Snapped == Window.EdgeTop && terminal.Width == areaW, "Das Terminal füllt die obere Hälfte");
            desktop.Open(Icons.About);
            desktop.OnMouse(snapAbout.X + 200, snapAbout.Y + 10, true);
            desktop.OnMouse(T.Size.ScreenWidth / 2 + 100, 0, false);
            Settle();
            Check(snapAbout.Snapped == (Window.EdgeTop | Window.EdgeRight) && terminal.Snapped == (Window.EdgeTop | Window.EdgeLeft)
                && terminal.Width == halfW && snapAbout.Width == halfW && terminal.Height == halfH,
                "Oben geht das genauso: das zweite Fenster kommt auf die Seite der Maus, das erste rückt daneben");
            Click(ControlX(snapAbout, Window.CloseButton), snapAbout.Y + T.Size.TitleBar / 2);
            Settle();
            terminal.X = oldX - 60; terminal.Y = 100; terminal.Width = termW; terminal.Height = termH; terminal.Snapped = 0;
            OS.Changed = true;
            Draw();
            Click(DockIconX(Icons.Activity), dockRow);
            Check(desktop.Active is ActivityWindow, "Klick im Dock öffnet die App");
            Window activity = desktop.Active;
            Settle();
            int activityW = activity.Width;
            Click(ControlX(activity, Window.ZoomButton), activity.Y + T.Size.TitleBar / 2);
            Settle();
            Shot(output, "9-aktivitaet-gross");
            Check(activity.Zoomed && activity.Width > activityW, "Auch die Aktivitätsanzeige lässt sich vergrößern, Tabelle und Speicherkarte wachsen mit");
            Click(ControlX(activity, Window.ZoomButton), activity.Y + T.Size.TitleBar / 2);
            Settle();
            Check(!activity.Zoomed && activity.Width == activityW, "Und wieder zurück auf die Größe davor");
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
            Draw();
            Check(!(desktop.Active is AboutWindow) && OS.Processes.All.Count == before, "kill auf den Prozess einer App schließt ihr Fenster");
            desktop.Open(Icons.About);
            app = desktop.Active;
            app.Process.OnSignal(Signal.Term);
            Draw();
            Check(!(desktop.Active is AboutWindow), "Das Signal Term an eine App schließt ihr Fenster");
            desktop.Open(Icons.About);
            app = desktop.Active;
            Click(ControlX(app, Window.MinimizeButton), app.Y + T.Size.TitleBar / 2);
            Check(OS.Processes.Get(app.Process.Id) == app.Process, "Eine App im Dock bleibt ein Prozess");
            OS.Processes.Kill(app.Process.Id);
            Draw();
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
            Check(ShadowMatches(1, 20, 20, 100, 60, T.Radius.L, T.Shadow.DockBlur, T.Shadow.DockOffset, T.Shadow.DockAlpha)
                && ShadowMatches(2, 30, 30, 81, 45, T.Radius.M, T.Shadow.WindowBlur, T.Shadow.WindowOffset, T.Shadow.WindowAlpha)
                && ShadowMatches(2, 40, 50, 60, 20, 0, 12, 0, 255) && ShadowMatches(1, 50, 50, 40, 20, 18, 9, 4, 70)
                && ShadowMatches(2, -15, 100, 70, 60, 12, 25, 10, 130),
                "Der Schatten aus der Tabelle gleicht Pixel für Pixel dem mit Kommazahlen gerechneten");
            // Dieselbe Form mit je einem anderen Wert: Für jeden gilt eine eigene Tabelle. Mit denen der Prüfung davor sind
            // es mehr, als gemerkt werden: Die Tabelle des Docks ist bis zum letzten Aufruf verdrängt und entsteht dort neu.
            Check(ShadowMatches(1, 20, 20, 100, 60, T.Radius.L, T.Shadow.DockBlur, T.Shadow.DockOffset, 200)
                && ShadowMatches(1, 20, 20, 100, 60, T.Radius.M, T.Shadow.DockBlur, T.Shadow.DockOffset, 200)
                && ShadowMatches(1, 20, 20, 100, 60, T.Radius.M, T.Shadow.WindowBlur, T.Shadow.DockOffset, 200)
                && ShadowMatches(1, 20, 20, 100, 60, 3, 7, 2, 90)
                && ShadowMatches(1, 20, 20, 100, 60, T.Radius.L, T.Shadow.DockBlur, T.Shadow.DockOffset, T.Shadow.DockAlpha),
                "Schatten mit anderem Radius, anderer Unschärfe oder anderer Deckkraft teilen sich keine Tabelle");
            Tiles();

            // Auflösung passend zum Bildschirm des Rechners, nur geänderte Ausschnitte an den Bildschirm (VIB-37)
            Platform.Hardware.FwCfgFiles = null;
            Check(Settings.HostScreen().Width == 0, "Ohne QEMU gibt es keine Bildschirmgröße des Rechners");
            Platform.Hardware.FwCfgFiles = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("etc/boot-fail-wait", "\0\0\0\0"),
                new KeyValuePair<string, string>("opt/vibanos/screens", "1x1"),
                new KeyValuePair<string, string>("opt/vibanos/screen", "1512x945"),
            };
            Resolution hostScreen = Settings.HostScreen();
            Platform.Hardware.FwCfgFiles = null;
            Check(hostScreen.Width == 1512 && hostScreen.Height == 945, "Die Bildschirmgröße kommt aus der fw_cfg-Datei von QEMU mit genau diesem Namen");
            // Die Liste der VMware-SVGA-Grafik in COSMOS 3.0.99 (VmwareSvgaState.s_modes)
            int[] svgaList = { 320, 200, 320, 240, 640, 480, 720, 480, 800, 600, 1024, 768, 1152, 768, 1280, 720, 1280, 768, 1280, 800,
                1280, 1024, 1360, 768, 1440, 900, 1400, 1050, 1600, 1200, 1680, 1050, 1920, 1080, 1920, 1200, 2048, 1536, 2560, 1080,
                2560, 1600, 2560, 2048, 3200, 2048, 3200, 2400, 3840, 2400 };
            List<Resolution> svga = new List<Resolution>();
            for (int i = 0; i < svgaList.Length; i += 2) svga.Add(new Resolution(svgaList[i], svgaList[i + 1]));
            string picks = "";
            int[] hosts = { 1512, 945, 1512, 982, 1728, 1080, 1440, 900, 1920, 1080, 2560, 1440, 1024, 700, 0, 0 };
            for (int i = 0; i < hosts.Length; i += 2)
            {
                Resolution pick = Settings.StartMode(svga, new Resolution(hosts[i], hosts[i + 1]));
                picks += pick.Width + "x" + pick.Height + " ";
            }
            Check(picks == "1440x900 1440x900 1680x1050 1440x900 1920x1080 1920x1080 800x600 1280x720 ",
                "Zum Start wählt VibanOS die größte Auflösung mit dem Seitenverhältnis des Rechners, ohne Angabe die Entwurfsgröße (" + picks + ")");
            Check(Settings.ParseSize("1512x945\n").Width == 1512 && Settings.ParseSize("x945").Width == 0 && Settings.ParseSize("-5x3").Width == 0
                && Settings.ParseSize(null).Width == 0, "Nur eine Angabe wie 1512x945 gilt als Bildschirmgröße");
            Draw(); // die Maus steht jetzt, wo sie gezeichnet ist
            int minute = DateTime.Now.Minute;
            desktop.Redraw();
            Check(desktop.Frame(screen) && desktop.DirtyCount == 1 && desktop.Dirty[0] == 0 && desktop.Dirty[1] == 0
                && desktop.Dirty[2] >= screen.Width && desktop.Dirty[3] >= screen.Height, "Nach einem ganzen Neuzeichnen geht der ganze Bildschirm hinaus");
            OS.Changed = true;
            bool same = !desktop.Frame(screen);
            // Springt genau zwischen den zwei Bildern die Minute um, zeichnet die Menüleiste die Uhr neu: dann zählt erst das nächste Bild.
            if (!same && DateTime.Now.Minute != minute) { OS.Changed = true; same = !desktop.Frame(screen); }
            Check(same, "Sieht das neue Bild aus wie das alte, geht nichts an den Bildschirm");
            Settle();
            Check(CursorOnly(screen), "Bewegt sich nur die Maus, geht nur ihr Ausschnitt an den Bildschirm");

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
            OS.Users.Logout(); // Nutzerwechsel bei offenem Fenster, hier ohne den Weg über den Anmeldebildschirm
            OS.Users.Login("anna", "topsecret");
            Picture(settings);
            bool forgotten = settings.Error == "";
            OS.Users.Logout();
            OS.Users.Login(UserManager.Root, "geheim");
            Check(forgotten && OS.Users.Current.Name == UserManager.Root, "Die Meldung des Vorgängers steht nach einem Nutzerwechsel nicht mehr in den Einstellungen");
            // Klickt der Neue, bevor das Fenster für ihn gezeichnet wurde (Anmeldung und Klick im selben Bild), gilt die Meldung ihm.
            string annaFile = Path.Combine(disk, "home", "anna", Settings.FileName);
            Directory.CreateDirectory(annaFile); // auch bei anna scheitert das Speichern
            Picture(settings);
            int[] accent = settings.CenterOf(SettingsWindow.PickAccent);
            OS.Users.Logout();
            OS.Users.Login("anna", "topsecret");
            Click(accent[0], accent[1]);
            Picture(settings);
            bool own = settings.Error != "";
            OS.Users.Logout();
            OS.Users.Login(UserManager.Root, "geheim");
            Directory.Delete(annaFile);
            Picture(settings);
            Check(own && settings.Error == "", "Die eigene Meldung übersteht das erste Zeichnen nach dem Nutzerwechsel");
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
            // Die Maus steht schon auf dem Schalter, wenn der Klick kommt: Der Zeiger wechselt die Größe, ohne sich zu
            // bewegen. Draw prüft, dass auch dann alles Geänderte in einem gemeldeten Ausschnitt liegt.
            int[] toggle = settings.CenterOf(SettingsWindow.ToggleCursor);
            desktop.OnMouse(toggle[0], toggle[1], false);
            Draw();
            ClickAt(toggle);
            desktop.OnMouse(200, 200, false);
            Draw();
            Shot(output, "8-einstellungen-tastatur");
            // Zeile 10 des Zeigers beginnt mit einem Randpixel. Doppelt so groß liegt es bei y + 20 statt y + 10.
            bool large = screen.Pixels[(200 + 2 * 10 + 1) * screen.Width + 200 + 1] == unchecked((int)T.Color.CursorEdge);
            toggle = settings.CenterOf(SettingsWindow.ToggleCursor);
            desktop.OnMouse(toggle[0], toggle[1], false);
            Draw();
            ClickAt(toggle);
            desktop.OnMouse(200, 200, false);
            Draw();
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
            Draw();
            int hidden = screen.Pixels[edge];
            animTime += T.Motion.Open / 2;
            Draw();
            Check(desktop.Animating && hidden != unchecked((int)T.Color.TitleBar) && screen.Pixels[edge] != unchecked((int)T.Color.TitleBar), "Ein neues Fenster blendet sich ein");
            animTime += T.Motion.Open;
            Draw();
            Check(!desktop.Animating && screen.Pixels[edge] == unchecked((int)T.Color.TitleBar), "Nach dem Einblenden steht das Fenster fertig da");
            Click(ControlX(info, Window.ZoomButton), info.Y + T.Size.TitleBar / 2);
            Settle();
            Check(info.Zoomed && info.Width == T.Size.ScreenWidth - 2 * T.Space.S, "Der grüne Knopf vergrößert auch Info");
            Click(ControlX(info, Window.ZoomButton), info.Y + T.Size.TitleBar / 2);
            Settle();
            Check(!info.Zoomed && info.Width == 340, "Und ein zweiter Klick stellt die Größe davor her");
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
            Draw();
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
            File.WriteAllText(Path.Combine(home, "notizen", "ende.txt"), "eins\r\nzwei\r\n");
            File.WriteAllText(Path.Combine(home, "notizen", "nichts.txt"), "");
            File.WriteAllText(Path.Combine(home, "notizen", "zeile.txt"), "\n"); // eine einzige, leere Zeile
            File.WriteAllText(Path.Combine(home, "notizen", "rand.txt"), new string('x', 8191) + "\nweiter"); // der gelesene Anfang endet am Zeilenende
            // Ausschnitte des Fensters (links, oben, rechts, unten): die Statusleiste und die Mitte der Vorschau.
            int[] statusBar = { files.X + T.Size.Sidebar + 10, files.Y + files.Height - 26, files.X + files.Width - 10, files.Y + files.Height - 3 };
            int[] middle = { files.X + T.Size.Sidebar + 20, files.Y + T.Size.TitleBar + 60, files.X + files.Width - 20, files.Y + files.Height - 50 };
            ShowFile(files, "ende.txt");
            bool two = files.Lines.Count == 2 && files.Lines[1] == "zwei" && files.Info == "2 Zeilen";
            long twoLines = Region(statusBar);
            int twoInk = Ink(statusBar);
            ShowFile(files, "zeile.txt");
            long oneEmptyLine = Region(middle);
            ShowFile(files, "nichts.txt");
            for (int i = 0; i < 3; i++) files.OnKey(Key.PageDown, '\0');
            Picture(files);
            Check(two && files.OpenFile != null && files.Lines.Count == 0 && files.Info == "0 Zeilen" && files.Top == 0,
                "Ein Zeilenende am Schluss zählt nicht als weitere Zeile, und eine leere Datei hat keine");
            Check(Region(middle) != oneEmptyLine, "Eine leere Datei sagt in der Vorschau, dass sie leer ist");
            ShowFile(files, "rand.txt");
            Check(files.Lines.Count == 2 && files.Lines[0].Length == 8191 && files.Lines[1] == "...",
                "Endet der gelesene Anfang an einem Zeilenende, steht keine Leerzeile vor den Auslassungspunkten");
            File.WriteAllText(Path.Combine(home, "notizen", "lang.txt"), new string('x', 10000)); // eine einzige, sehr lange Zeile
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("lang.txt")));
            files.OnKey(Key.Enter, '\0');
            Picture(files);
            Check(files.Lines.Count == 2 && files.Lines[0].Length == 8192 && files.Lines[1] == "..." && files.Info == "Die ersten 8 KB der Datei",
                "Von einer großen Datei liest die Vorschau nur den Anfang und sagt das in der Statusleiste");
            // Anders als bei "2 Zeilen" und mit mehr Farbe: Der längere Text steht wirklich da, eine leere Statusleiste fiele hier auf.
            Check(Region(statusBar) != twoLines && Ink(statusBar) > twoInk, "Die Statusleiste zeigt bei einer großen Datei den gelesenen Anfang, nicht die Zeilenzahl");
            long lineStart = Picture(files);
            for (int i = 0; i < 100; i++) files.OnKey(Key.PageDown, '\0');
            Check(Picture(files) != lineStart && files.Top > 0, "Eine sehr lange Zeile bricht in der Vorschau um: Mit Bild ab kommt man bis an ihr Ende");
            string[] sixty = new string[60];
            for (int i = 0; i < sixty.Length; i++) sixty[i] = "Zeile " + (i + 1);
            File.WriteAllText(Path.Combine(home, "notizen", "zeilen.txt"), string.Join("\n", sixty));
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("zeilen.txt")));
            files.OnKey(Key.Enter, '\0');
            Picture(files);
            Check(files.Lines.Count == 60 && files.Top == 0, "Jede Datei beginnt in der Vorschau oben, auch wenn die vorige weit geblättert war");
            files.OnKey(Key.Down, '\0');
            Picture(files);
            bool oneDown = files.Top == 1;
            for (int i = 0; i < 100; i++) files.OnKey(Key.PageDown, '\0');
            Picture(files);
            int lastTop = files.Top;
            files.OnKey(Key.Down, '\0');
            Picture(files);
            bool held = files.Top == lastTop && lastTop > 1 && lastTop < sixty.Length;
            for (int i = 0; i < 100; i++) files.OnKey(Key.PageUp, '\0');
            Picture(files);
            Check(oneDown && held && files.Top == 0, "In der Vorschau blättern Pfeiltasten und Bild auf und ab, aber nicht über Anfang und Ende hinaus");
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
            // Ein Ordner mit mehr Einträgen, als ins Fenster passen: 20 Ordner, dahinter 10 Dateien.
            string many = Path.Combine(home, "viele");
            for (int i = 1; i <= 20; i++) Directory.CreateDirectory(Path.Combine(many, "o" + Text.Two(i)));
            for (int i = 1; i <= 10; i++) File.WriteAllText(Path.Combine(many, "d" + Text.Two(i) + ".txt"), "Inhalt " + i);
            ClickAt(files.CenterOf(FilesWindow.PlaceHome)); // liest den Ordner neu
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("viele")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("viele")));
            for (int i = 0; i < 28; i++) files.OnKey(Key.Down, '\0');
            Picture(files); // wie im Kernel: Zwischen den Tasten wird gezeichnet
            for (int i = 0; i < 5; i++) files.OnKey(Key.Up, '\0');
            Picture(files);
            int firstRow = FirstRow(files);
            Check(firstRow > 0 && files.CenterOf(FilesWindow.Item + files.IndexOf("d03.txt")) != null, "Die Liste blättert mit der Auswahl mit");
            files.OnKey(Key.Enter, '\0');
            Picture(files);
            bool opened = files.OpenFile != null && files.OpenFile.EndsWith("d03.txt") && files.Top == 0 && files.Lines[0] == "Inhalt 3";
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            Check(opened && FirstRow(files) == firstRow, "Nach dem Schließen einer Datei steht die Liste, wo sie war");
            for (int i = 0; i < 5; i++) files.OnKey(Key.PageUp, '\0');
            for (int i = 0; i < 17; i++) files.OnKey(Key.Down, '\0');
            files.OnKey(Key.Enter, '\0');
            bool inside = files.Folder.EndsWith("o18");
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            bool shown = files.CenterOf(FilesWindow.Item + files.IndexOf("o18")) != null;
            files.OnKey(Key.Enter, '\0');
            Check(inside && shown && files.Folder.EndsWith("o18"), "Zurück aus einem Unterordner ist er ausgewählt und zu sehen, auch weit unten in der Liste");
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            bool scrolled = FirstRow(files) > 0;
            for (int i = 0; i < 17; i++) files.OnKey(Key.Up, '\0');
            Picture(files);
            Check(scrolled && FirstRow(files) == 0, "Die Liste blättert auch nach oben mit der Auswahl mit");
            Directory.Delete(many, true);
            ClickAt(files.CenterOf(FilesWindow.PlaceHome));
            Check(FilesWindow.Kind("bild.png") == "PNG-Datei" && FilesWindow.Kind("NOTIZ.TXT") == "Text" && FilesWindow.Kind("ohne") == "Datei"
                && FilesWindow.Kind("a.jpeg") == "JPEG-Datei" && FilesWindow.Kind("sicherung.backup2024") == "Datei" && FilesWindow.Kind("Bericht v1. neu") == "Datei",
                "Die Spalte Art nennt nur kurze Endungen, alles andere heißt Datei");
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
            OS.Shell.Submit("mkdir /home/root/wand");
            ClickAt(files.CenterOf(FilesWindow.PlaceHome));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("wand")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("wand")));
            OS.Shell.Submit("rm /home/root/wand");
            ClickAt(blank);
            Check(files.Folder == OS.Users.Current.HomeDirectory && files.Message.StartsWith("Ordner nicht gefunden"),
                "Ist der gezeigte Ordner weg, führt auch ein Klick ins Leere nach Hause und sagt es");
            ClickAt(blank);
            Check(files.Message == "", "Der nächste Klick ins Leere räumt die alte Meldung weg");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("windows.txt")));
            files.OnKey(Key.Enter, '\0');
            Picture(files);
            ClickAt(blank); // mitten in den Text
            files.OnKey(Key.Backspace, '\0');
            Picture(files);
            files.OnKey(Key.Enter, '\0');
            Check(files.OpenFile != null && files.OpenFile.EndsWith("windows.txt"), "Ein Klick in die Vorschau hebt die Auswahl der Liste nicht auf");
            files.OnKey(Key.Backspace, '\0');
            Picture(files);

            // Umbenennen und Löschen in Dateien (VIB-25)
            File.WriteAllText(Path.Combine(home, "alt.txt"), "alt");
            Directory.CreateDirectory(Path.Combine(home, "leer"));
            ClickAt(files.CenterOf(FilesWindow.PlaceHome)); // liest den Ordner neu
            Check(files.CenterOf(FilesWindow.Rename) == null && files.CenterOf(FilesWindow.Delete) == null, "Ohne Auswahl gibt es Umbenennen und Löschen nicht");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("alt.txt")));
            Picture(files);
            ClickAt(files.CenterOf(FilesWindow.Rename));
            bool editing = files.Editing && files.EditName == "alt.txt";
            for (int i = 0; i < 7; i++) files.OnKey(Key.Backspace, '\0');
            foreach (char ch in "neu.txt") files.OnKey(Key.None, ch);
            files.OnKey(Key.Down, '\0'); // im Feld wählen Pfeile keine andere Zeile
            Shot(output, "9-dateien-umbenennen");
            files.OnKey(Key.Enter, '\0');
            Check(editing && !files.Editing && File.Exists(Path.Combine(home, "neu.txt")) && !File.Exists(Path.Combine(home, "alt.txt"))
                && files.IndexOf("neu.txt") >= 0 && files.Selected == files.IndexOf("neu.txt") && files.Message == "",
                "Umbenennen öffnet ein Feld in der Zeile, Eingabe übernimmt den Namen, die Zeile bleibt gewählt");
            files.OnKey(Key.F2, '\0');
            for (int i = 0; i < 7; i++) files.OnKey(Key.Backspace, '\0');
            foreach (char ch in "NEU.TXT") files.OnKey(Key.None, ch);
            files.OnKey(Key.Enter, '\0');
            bool caseOnly = files.Editing && files.Message.StartsWith("Auf der Platte ist das derselbe Name");
            files.OnKey(Key.Escape, '\0');
            Check(caseOnly && !files.Editing && files.Message == "" && File.Exists(Path.Combine(home, "neu.txt")),
                "Nur andere Schreibweise lehnt Dateien ab wie mv, Escape bricht ab");
            files.OnKey(Key.F2, '\0');
            bool atEnd = files.Caret == 7;
            for (int i = 0; i < 4; i++) files.OnKey(Key.Left, '\0');
            files.OnKey(Key.None, '2');
            bool inserted = files.EditName == "neu2.txt" && files.Caret == 4;
            files.OnKey(Key.Up, '\0');
            files.OnKey(Key.Left, '\0');
            files.OnKey(Key.Delete, '\0');
            files.OnKey(Key.None, 'N');
            bool front = files.EditName == "Neu2.txt" && files.Caret == 1;
            files.OnKey(Key.Right, '\0');
            files.OnKey(Key.Backspace, '\0');
            bool back = files.EditName == "Nu2.txt" && files.Caret == 1;
            files.OnKey(Key.Down, '\0');
            files.OnKey(Key.Right, '\0');
            files.OnKey(Key.Delete, '\0');
            Check(atEnd && inserted && front && back && files.EditName == "Nu2.txt" && files.Caret == 7,
                "Im Feld gehen Pfeil links und rechts zwischen den Buchstaben, hoch und runter an Anfang und Ende, Entf löscht rechts");
            foreach (char ch in "-ein-sehr-langer-name-der-nicht-ins-feld-passt-und-blaettern-muss") files.OnKey(Key.None, ch);
            long longEnd = Picture(files);
            files.OnKey(Key.Up, '\0');
            long longStart = Picture(files);
            files.OnKey(Key.Escape, '\0');
            Check(longEnd != longStart && !files.Editing && File.Exists(Path.Combine(home, "neu.txt")),
                "Ein langer Name blättert im Feld mit der Einfügemarke, Escape lässt den alten stehen");
            string[] bad = { "a~1.txt", "a/b", "windows.txt", "  ..", "x:y" };
            string[] reason = { "Namen mit ~", "Diese Zeichen", "Gibt es schon", "Der Name darf nicht leer", "Diese Zeichen" };
            for (int k = 0; k < bad.Length; k++)
            {
                files.OnKey(Key.F2, '\0');
                for (int i = 0; i < 7; i++) files.OnKey(Key.Backspace, '\0');
                foreach (char ch in bad[k]) files.OnKey(Key.None, ch);
                files.OnKey(Key.Enter, '\0');
                Check(files.Editing && files.Message.StartsWith(reason[k]) && File.Exists(Path.Combine(home, "neu.txt")),
                    "Umbenennen lehnt \"" + bad[k] + "\" ab: " + reason[k]);
                files.OnKey(Key.Escape, '\0');
            }
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("leer")));
            files.OnKey(Key.F2, '\0');
            foreach (char ch in "2.") files.OnKey(Key.None, ch);
            ClickAt(blank); // daneben klicken übernimmt den Namen wie im Finder
            Check(!files.Editing && Directory.Exists(Path.Combine(home, "leer2")) && !Directory.Exists(Path.Combine(home, "leer"))
                && files.Selected == files.IndexOf("leer2"), "Ordner lassen sich umbenennen, ein Klick daneben übernimmt den Namen, Punkte am Ende fallen weg");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("neu.txt")));
            files.OnKey(Key.Delete, '\0');
            bool asked = files.Confirming;
            files.OnKey(Key.Escape, '\0');
            Check(asked && !files.Confirming && File.Exists(Path.Combine(home, "neu.txt")), "Entf fragt erst nach, Escape lässt die Datei stehen");
            files.OnKey(Key.Delete, '\0');
            Check(files.Confirming && files.Warning == null, "Gewöhnliche Dateien fragen nur in der Statuszeile, ohne Warnung");
            files.OnKey(Key.Escape, '\0');
            int at = files.Selected;
            ClickAt(files.CenterOf(FilesWindow.Delete));
            Shot(output, "9-dateien-loeschen");
            ClickAt(files.CenterOf(FilesWindow.ConfirmDelete));
            Check(!File.Exists(Path.Combine(home, "neu.txt")) && files.IndexOf("neu.txt") < 0 && !files.Confirming && files.Message == "" && files.Selected == at,
                "Löschen mit Rückfrage entfernt die Datei, die Auswahl rückt auf die nächste Zeile");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("leer2")));
            files.OnKey(Key.Delete, '\0');
            Picture(files); // erst das Zeichnen legt die Knöpfe der Rückfrage an
            ClickAt(files.CenterOf(FilesWindow.Cancel));
            bool stays = Directory.Exists(Path.Combine(home, "leer2")) && !files.Confirming;
            files.OnKey(Key.Delete, '\0');
            ClickAt(blank);
            stays = stays && Directory.Exists(Path.Combine(home, "leer2")) && !files.Confirming;
            files.OnKey(Key.Delete, '\0');
            files.OnKey(Key.Enter, '\0');
            Check(stays && !Directory.Exists(Path.Combine(home, "leer2")), "Abbrechen und ein Klick daneben lassen den Ordner stehen, Eingabe löscht den leeren Ordner");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("notizen")));
            files.OnKey(Key.Delete, '\0');
            Check(!files.Confirming && files.Message.StartsWith("Ordner ist nicht leer") && Directory.Exists(Path.Combine(home, "notizen")),
                "Volle Ordner löscht Dateien nicht, wie rm");
            string rootSettings = Path.Combine(home, "settings.txt"), settingsText = File.ReadAllText(rootSettings);
            string homeDir = OS.Users.Current.HomeDirectory;
            Check(Vfs.SystemFile(Vfs.Combine(homeDir, "SETTINGS.TXT")) != null && Vfs.SystemFile(Vfs.Combine(Vfs.Combine(homeDir, "notizen"), "settings.txt")) == null
                && Vfs.SystemFile(Vfs.Combine(homeDir, "neu.txt")) == null, "Systemdatei ist die settings.txt direkt im Home-Ordner, in jeder Schreibweise");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("settings.txt")));
            files.OnKey(Key.Delete, '\0');
            bool warned = files.Confirming && files.Warning != null && files.Warning.Contains("Einstellungen von root");
            Shot(output, "9-dateien-warnung");
            ClickAt(files.CenterOf(FilesWindow.Cancel));
            bool cancelled = !files.Confirming && File.Exists(rootSettings);
            files.OnKey(Key.Delete, '\0');
            Picture(files);
            int sel = files.Selected;
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("notizen"))); // ein Klick neben die Warnung bricht ab
            Check(warned && cancelled && !files.Confirming && files.Selected == sel && files.Folder == homeDir && File.Exists(rootSettings),
                "Vor dem Löschen der Einstellungen kommt eine Warnung, Abbrechen und ein Klick daneben lassen die Datei stehen");
            files.OnKey(Key.F2, '\0');
            bool renameWarned = files.Confirming && !files.Editing && files.Warning != null;
            files.OnKey(Key.Escape, '\0');
            ClickAt(files.CenterOf(FilesWindow.Rename));
            ClickAt(files.CenterOf(FilesWindow.ConfirmRename));
            bool fieldOpen = files.Editing && files.EditName == "settings.txt" && !files.Confirming;
            for (int i = 0; i < 3; i++) files.OnKey(Key.Backspace, '\0');
            foreach (char ch in "alt") files.OnKey(Key.None, ch);
            files.OnKey(Key.Enter, '\0');
            bool settingsRenamed = !File.Exists(rootSettings) && File.Exists(Path.Combine(home, "settings.alt"));
            files.OnKey(Key.F2, '\0'); // settings.alt ist keine Systemdatei mehr: gleich das Feld
            bool direct = files.Editing && !files.Confirming;
            for (int i = 0; i < 3; i++) files.OnKey(Key.Backspace, '\0');
            foreach (char ch in "txt") files.OnKey(Key.None, ch);
            files.OnKey(Key.Enter, '\0');
            Check(renameWarned && fieldOpen && settingsRenamed && direct && File.Exists(rootSettings),
                "Auch vor dem Umbenennen der Einstellungen kommt die Warnung, erst nach dem Bestätigen das Feld");
            files.OnKey(Key.Delete, '\0');
            files.OnKey(Key.Enter, '\0');
            bool gone = !File.Exists(rootSettings) && !files.Confirming;
            File.WriteAllText(rootSettings, settingsText);
            Check(gone, "Nach dem Bestätigen der Warnung lassen sich die Einstellungen löschen");
            ClickAt(files.CenterOf(FilesWindow.PlaceHome));
            Directory.CreateDirectory(Path.Combine(home, "hier"));
            OS.Shell.Submit("cd /home/root/hier");
            ClickAt(files.CenterOf(FilesWindow.PlaceHome));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("hier")));
            files.OnKey(Key.Delete, '\0');
            bool cwdDelete = !files.Confirming && files.Message.StartsWith("Darin steht gerade das Terminal");
            files.OnKey(Key.F2, '\0');
            Check(cwdDelete && !files.Editing && files.Message.StartsWith("Darin steht gerade das Terminal") && Directory.Exists(Path.Combine(home, "hier")),
                "Den Ordner, in dem das Terminal steht, lässt Dateien in Ruhe");
            OS.Shell.Submit("cd /home/root");
            Directory.Delete(Path.Combine(home, "hier"));
            ClickAt(files.CenterOf(FilesWindow.PlaceDisk));
            foreach (string fixedName in new[] { "system", "home" })
            {
                ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf(fixedName)));
                files.OnKey(Key.F2, '\0');
                bool noRename = !files.Editing && files.Message.StartsWith("Das braucht das System");
                files.OnKey(Key.Delete, '\0');
                Check(noRename && !files.Confirming && files.Message.StartsWith("Das braucht das System") && Directory.Exists(Path.Combine(disk, fixedName)),
                    "Auch root kann " + fixedName + " in Dateien weder umbenennen noch löschen");
            }
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("home")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("root")));
            ClickAt(files.CenterOf(FilesWindow.Delete));
            bool homeKept = !files.Confirming && files.Message.StartsWith("Das braucht das System");
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("anna")));
            ClickAt(files.CenterOf(FilesWindow.Rename));
            Check(homeKept && !files.Editing && files.Message.StartsWith("Das braucht das System"), "Home-Ordner lassen sich in Dateien nicht anfassen");
            ClickAt(files.CenterOf(FilesWindow.PlaceDisk));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("users.txt")));
            files.OnKey(Key.Delete, '\0');
            bool userFileKept = !files.Confirming && files.Message.StartsWith("Das braucht das System");
            files.OnKey(Key.F2, '\0');
            Check(userFileKept && !files.Editing && File.Exists(Path.Combine(disk, "system", "users.txt")), "Die Nutzerdatei lässt sich in Dateien nicht anfassen");
            ClickAt(files.CenterOf(FilesWindow.PlaceHome));
            ClickAt(files.CenterOf(FilesWindow.PlaceDisk));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("users.txt")));
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("users.txt")));
            Check(files.OpenFile != null && files.Lines[0].Contains("root"), "Administratoren sehen die Nutzerdatei");
            string annaHome = Path.Combine(disk, "home", "anna");
            OS.Shell.Submit("mv /home/anna /home/anna2");
            bool kept = Last().Text.StartsWith("Das braucht das System") && Directory.Exists(annaHome);
            OS.Shell.Submit("rm /home/anna");
            Check(kept && Last().Text.StartsWith("Das braucht das System") && Vfs.IsFixed(Vfs.Root + "HOME/ANNA"),
                "Auch der Home-Ordner eines anderen Nutzers ist fest, in jeder Schreibweise");
            Directory.Move(annaHome, annaHome + ".weg");
            File.WriteAllText(annaHome, "x"); // eine Datei liegt, wo der Home-Ordner hingehört
            OS.Shell.Submit("rm /home/anna");
            bool removed = !File.Exists(annaHome);
            File.Delete(annaHome);
            Directory.Move(annaHome + ".weg", annaHome);
            Check(removed, "Eine Datei an der Stelle eines Home-Ordners ist nicht fest und lässt sich löschen");
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
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf("system")));
            files.OnKey(Key.F2, '\0');
            bool annaRename = !files.Editing && files.Message.StartsWith("Zugriff verweigert");
            files.OnKey(Key.Delete, '\0');
            Check(annaRename && !files.Confirming && files.Message.StartsWith("Zugriff verweigert"), "Normale Nutzer können den Systemordner in Dateien nicht umbenennen oder löschen");
            Click(ControlX(files, Window.CloseButton), files.Y + T.Size.TitleBar / 2);
            Settle();
            desktop.Open(Icons.Terminal);
            Type("passwd\n");
            bool askedGerman = OS.Shell.Masked && Platform.Hardware.GermanKeyboard;
            Type("topsecret\n");
            askedGerman &= OS.Shell.Masked && Platform.Hardware.GermanKeyboard; // auch bei der zweiten Frage
            Type("topsecret\n");
            Check(askedGerman && !Platform.Hardware.GermanKeyboard && Last().Text == "Passwort geändert.",
                "Passwörter tippt man auch mit US-Belegung deutsch wie am Anmeldebildschirm, danach gilt wieder die eigene");
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
            Retina(output);
            desktop.OnMouse(980, 600, false);
            Click(ControlX(terminal, Window.MinimizeButton), terminal.Y + T.Size.TitleBar / 2);
            animTime += T.Motion.Minimize / 2;
            Draw();
            SavePng(screen, Path.Combine(output, "5-minimieren.png"));
            Settle();
            DesignSheet(output);
            Pieces();
            Contexts();

            Directory.SetCurrentDirectory(output);
            // Aufräumen kippt den bestandenen Test nicht: Unter Windows lässt sich ein Ordner nicht löschen, solange ein
            // anderes Programm (Virenscanner) darin noch eine Datei offen hält.
            try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            Console.WriteLine(checks + " Prüfungen bestanden. Bilder liegen in " + output);
            return 0;
        }

        private class BrokenDriver : Driver
        {
            public override string Name { get { return "kaputt"; } }
            public override string Description { get { return "Treiber, dessen Hardware fehlt"; } }
            protected override void Init() { throw new Exception("keine Hardware"); }
        }

        // Retina (Folge zu VIB-37): derselbe Schreibtisch in doppelter Auflösung, wie auf einem Mac im Vollbild.
        private static void Retina(string output)
        {
            Check(Settings.ScaleFor(new Resolution(2940, 1846), new Resolution(1470, 923)) == 2
                && Settings.ScaleFor(new Resolution(1440, 900), new Resolution(1470, 923)) == 1
                && Settings.ScaleFor(new Resolution(2940, 1846), new Resolution(0, 0)) == 1
                && Settings.ScaleFor(new Resolution(1920, 1080), new Resolution(1920, 1080)) == 1
                && Settings.ScaleFor(new Resolution(1280, 800), new Resolution(640, 400)) == 1,
                "Doppelt gezeichnet wird nur, wenn der Bildschirm doppelt so viele Pixel hat wie der Rechner Punkte, und groß genug ist");

            Surface box = new Surface(50, 30, 2);
            box.FillRect(5, 5, 10, 4, T.Color.Danger);
            box.StrokeRoundRect(20, 5, 10, 10, 0, T.Color.Danger);
            int red = unchecked((int)T.Color.Danger), filled = 0, stroked = 0;
            for (int i = 0; i < box.Pixels.Length; i++)
            {
                if (box.Pixels[i] != red) continue;
                int x = i % box.PixelWidth, y = i / box.PixelWidth;
                if (x >= 10 && x < 30 && y >= 10 && y < 18) filled++;
                else if (x >= 40 && x < 60 && y >= 10 && y < 30 && (x < 42 || x >= 58 || y < 12 || y >= 28)) stroked++;
                else filled = -10000;
            }
            Check(box.PixelWidth == 100 && filled == 20 * 8 && stroked == 20 * 20 - 16 * 16,
                "Bei Retina deckt jeder Punkt genau 2x2 Pixel, auch Rahmen sind zwei Pixel dick");
            Check(box.DrawText(Fonts.Body, "Hallo VibanOS", 0, 0, T.Color.TextPrimary) == Fonts.Body.Measure("Hallo VibanOS"),
                "Text ist bei Retina genauso breit wie sonst, nur schärfer");

            OS.Changed = true;
            Draw();
            Surface.DefaultScale = 2;
            desktop.Resize(T.Size.ScreenWidth, T.Size.ScreenHeight);
            Surface retina = new Surface(T.Size.ScreenWidth, T.Size.ScreenHeight);
            OS.Changed = true;
            desktop.Frame(retina);
            long far = 0;
            for (int y = 0; y < screen.Height; y++)
                for (int x = 0; x < screen.Width; x++)
                {
                    int a = screen.Pixels[y * screen.Width + x];
                    for (int c = 0; c < 24; c += 8)
                    {
                        int sum = 0;
                        for (int k = 0; k < 4; k++) sum += (retina.Pixels[(2 * y + k / 2) * retina.PixelWidth + 2 * x + k % 2] >> c) & 255;
                        if (Math.Abs(sum / 4 - ((a >> c) & 255)) > 64) { far++; break; }
                    }
                }
            Check(far * 100 < screen.Width * screen.Height, "Bei Retina liegt alles an derselben Stelle wie sonst (" + far + " Pixel weichen ab, an Textkanten)");
            Check(CursorOnly(retina), "Auch bei Retina geht beim Bewegen der Maus nur ihr Ausschnitt an den Bildschirm");
            SavePng(retina, Path.Combine(output, "4-schreibtisch-retina.png"));

            Surface.DefaultScale = 1;
            desktop.Resize(T.Size.ScreenWidth, T.Size.ScreenHeight);
            OS.Changed = true;
            Draw();
        }

        // Bewegt sich nur die Maus, ändert Frame nur Pixel im gemeldeten Ausschnitt (in Punkten, bei Retina mal 2).
        private static bool CursorOnly(Surface front)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                // Mehrere Versuche: Wechselt dazwischen die Sekunde, zeichnet die Aktivität ihre Zahlen neu.
                desktop.OnMouse(400, 300, false);
                desktop.Frame(front);
                int[] unmoved = (int[])front.Pixels.Clone();
                desktop.OnMouse(405, 297, false);
                if (!desktop.Frame(front) || desktop.DirtyCount != 1) continue;
                int size = 20 * T.Size.CursorScale, k = front.Scale;
                int[] d = desktop.Dirty;
                for (int y = 0; y < front.PixelHeight; y++)
                    for (int x = 0; x < front.PixelWidth; x++)
                    {
                        bool inDirty = x >= d[0] * k && x < (d[0] + d[2]) * k && y >= d[1] * k && y < (d[1] + d[3]) * k;
                        if (!inDirty && front.Pixels[y * front.PixelWidth + x] != unmoved[y * front.PixelWidth + x]) return false;
                    }
                if (d[0] != 400 || d[1] != 297 || d[2] != size + 5 || d[3] != size + 3) return false;
                // Und das Bild ist dasselbe wie nach einem ganzen Neuzeichnen: Der alte Zeiger ist weg, der neue da.
                int[] moved = (int[])front.Pixels.Clone();
                desktop.Redraw();
                desktop.Frame(front);
                if (front.Pixels.AsSpan().SequenceEqual(moved)) return true;
                // sonst hat dazwischen die Sekunde gewechselt: noch einmal
            }
            return false;
        }

        // Zeichnet wie der Kernel und vergleicht jedes Mal: Das stückweise neu gezeichnete Bild muss dasselbe sein wie ein
        // ganz neu gezeichnetes. Uhr und Aktivität hängen an der Sekunde, wechselt sie dazwischen, zählt der Vergleich nicht.
        private static int compared;
        private static long dirtyArea; // so viele Punkte hat das letzte Draw neu an den Bildschirm geschickt

        private static bool Draw() { return Draw(screen); }

        private static bool Draw(Surface front)
        {
            int second = DateTime.Now.Second;
            int[] before = (int[])front.Pixels.Clone();
            bool shown = desktop.Frame(front);
            dirtyArea = 0;
            for (int i = 0; shown && i < 4 * desktop.DirtyCount; i += 4)
                dirtyArea += (long)Math.Min(desktop.Dirty[i + 2], front.Width - desktop.Dirty[i]) * Math.Min(desktop.Dirty[i + 3], front.Height - desktop.Dirty[i + 1]);
            // Der Kernel schickt nur die gemeldeten Ausschnitte an den Bildschirm. Was Frame außerhalb davon ändert, käme
            // dort nie an.
            int scale = front.Scale;
            for (int i = 0; i < before.Length; i++)
            {
                if (before[i] == front.Pixels[i]) continue;
                int px = i % front.PixelWidth, py = i / front.PixelWidth;
                bool inside = false;
                for (int d = 0; shown && d < 4 * desktop.DirtyCount && !inside; d += 4)
                    inside = px >= desktop.Dirty[d] * scale && px < (desktop.Dirty[d] + desktop.Dirty[d + 2]) * scale
                        && py >= desktop.Dirty[d + 1] * scale && py < (desktop.Dirty[d + 1] + desktop.Dirty[d + 3]) * scale;
                if (!inside)
                    throw new Exception("Prüfung fehlgeschlagen: Frame hat ein Pixel geändert, das in keinem gemeldeten Ausschnitt liegt (" + px + "," + py + ")");
            }
            int[] pieces = (int[])front.Pixels.Clone();
            desktop.Redraw();
            desktop.Frame(front);
            if (DateTime.Now.Second != second) return shown;
            for (int i = 0; i < pieces.Length; i++)
                if (pieces[i] != front.Pixels[i])
                    throw new Exception("Prüfung fehlgeschlagen: Stückweise neu gezeichnet sieht das Bild anders aus als ganz neu gezeichnet (Vergleich "
                        + compared + ", Pixel " + i % front.PixelWidth + "," + i / front.PixelWidth + ")");
            compared++;
            return shown;
        }

        // Surface.Shadow liest die Deckung je Abstand aus einer Tabelle. Hier dieselbe Rechnung für jedes Pixel einzeln mit
        // Kommazahlen, so wie Shadow sie früher machte.
        private static bool ShadowMatches(int scale, int x, int y, int w, int h, int r, int blur, int offsetY, int alpha)
        {
            Surface s = new Surface(160, 140, scale);
            uint color = T.Color.Shadow;
            s.FillRect(0, 0, s.Width, s.Height, T.Color.TextOnAccent);
            int paper = s.Pixels[0];
            s.Shadow(x, y, w, h, r, blur, offsetY, alpha, color);
            x *= scale; y *= scale; w *= scale; h *= scale; r *= scale; blur *= scale; offsetY *= scale;
            int sy = y + offsetY;
            float halfW = w / 2f - r, halfH = h / 2f - r, cx = x + w / 2f, cy = sy + h / 2f;
            for (int py = 0; py < s.PixelHeight; py++)
                for (int px = 0; px < s.PixelWidth; px++)
                {
                    int expected = paper, j = py - y;
                    bool covered = false; // unter der Form selbst bleibt alles stehen
                    if (j >= 0 && j < h)
                    {
                        int inset = 0;
                        if (j < r || j >= h - r)
                        {
                            float dy = j < r ? r - j - 0.5f : j - (h - r) + 0.5f;
                            float reach = (r - 0.5f) * (r - 0.5f) - dy * dy;
                            inset = reach <= 0 ? r : r - (int)((float)Math.Sqrt(reach) + 0.5f);
                        }
                        covered = px >= x + inset && px < x + w - inset;
                    }
                    float qx = Math.Abs(px + 0.5f - cx) - halfW, qy = Math.Abs(py + 0.5f - cy) - halfH;
                    float d = qx > 0 && qy > 0 ? (float)Math.Sqrt(qx * qx + qy * qy) - r : Math.Max(qx, qy) - r;
                    if (!covered && d < blur)
                    {
                        float t = d <= 0 ? 1 : 1 - d / blur;
                        int a = (int)(color >> 24) * (int)(alpha * t * t) / 255, u = 255 - a;
                        if (a > 0)
                            expected = (255 << 24) | ((((int)(color >> 16) & 255) * a + ((paper >> 16) & 255) * u) / 255 << 16)
                                | ((((int)(color >> 8) & 255) * a + ((paper >> 8) & 255) * u) / 255 << 8)
                                | (((int)color & 255) * a + (paper & 255) * u) / 255;
                    }
                    if (s.Pixels[py * s.PixelWidth + px] != expected) return false;
                }
            return true;
        }

        // Was sich geändert hat, merkt sich die Zeichenfläche je Kachel (Surface.Measure, Surface.Damage). Hier an einer
        // kleinen Fläche durchgespielt: 10 x 8 Kacheln.
        private static void Tiles()
        {
            int[] r = new int[4 * Surface.MaxDamage];
            Surface s = new Surface(320, 256, 1);
            Func<Action, int> damage = draw => { s.Measure(); draw(); return s.Damage(r); };
            uint a = T.Color.Accent, b = T.Color.Danger;
            Check(damage(() => { }) == 1 && r[0] == 0 && r[1] == 0 && r[2] == 320 && r[3] == 256 && damage(() => { }) == 0,
                "Das erste Bild gilt ganz als geändert, ein gleiches danach gar nicht");
            Check(damage(() => s.FillRect(40, 40, 100, 100, a)) == 1 && r[0] == 32 && r[1] == 32 && r[2] == 128 && r[3] == 128
                && damage(() => s.FillRect(40, 40, 100, 100, a)) == 0 && s.Pixels[50 * 320 + 50] == 0,
                "Geändert sind genau die Kacheln unter einer neuen Form, und beim Vermessen entsteht kein Pixel");
            Check(damage(() => s.FillRect(40, 40, 100, 100, b)) == 1 && damage(() => s.FillRect(41, 40, 100, 100, b)) == 1
                && damage(() => { s.DrawText(Fonts.Body, "ab", 8, 8, a); }) == 2 && damage(() => { s.DrawText(Fonts.Body, "ad", 8, 8, a); }) == 1 && r[1] == 0 && r[3] == 32,
                "Eine andere Farbe, eine andere Lage oder ein anderer Buchstabe gelten als Änderung");
            damage(() => { s.FillRect(0, 0, 64, 64, a); s.FillRect(32, 32, 64, 64, b); });
            Check(damage(() => { s.FillRect(32, 32, 64, 64, b); s.FillRect(0, 0, 64, 64, a); }) == 1 && r[0] == 32 && r[1] == 32 && r[2] == 32 && r[3] == 32,
                "Liegen zwei Formen in anderer Reihenfolge übereinander, ist nur ihre Überschneidung geändert");
            damage(() => { s.SetClip(0, 0, 64, 40); s.FillRect(0, 0, 64, 64, a); s.ResetClip(); });
            Check(damage(() => { s.SetClip(0, 0, 64, 48); s.FillRect(0, 0, 64, 64, a); s.ResetClip(); }) == 1,
                "Dieselbe Form anders beschnitten gilt als Änderung");
            damage(() => { s.SetClip(0, 0, 64, 64); s.FillRoundRect(-8, -40, 100, 400, 0, a, b); s.ResetClip(); });
            Check(damage(() => { s.SetClip(0, 0, 64, 64); s.FillRoundRect(-8, -48, 100, 400, 0, a, b); s.ResetClip(); }) == 1,
                "Ein Verlauf, der unter demselben Zuschnitt verschoben ist, gilt als Änderung");
            damage(() => { });
            Check(damage(() => { for (int i = 0; i < 6; i++) s.FillRect(i * 32, i * 32, 8, 8, a); }) == 6 && r[20] == 160 && r[21] == 160 && r[22] == 32,
                "Bis zu sechs getrennte Bereiche werden einzeln gemeldet");
            damage(() => { });
            Check(damage(() => { for (int i = 0; i < 7; i++) s.FillRect(i * 32, i * 32, 8, 8, a); }) == 1 && r[0] == 0 && r[1] == 0 && r[2] == 224 && r[3] == 224,
                "Mehr als sechs Bereiche werden zu einem, der alle umfasst");
            // Die Ecken kommen aus einer Tabelle für die linke obere, die anderen drei sind gespiegelt.
            Surface round = new Surface(64, 48, 2);
            round.FillRoundRect(3, 2, 57, 43, 9, a);
            round.StrokeRoundRect(3, 2, 57, 43, 9, b);
            bool mirrored = round.Pixels[4 * 128 + 6] == 0 && round.Pixels[5 * 128 + 6 + 18] != 0; // die Ecke selbst bleibt frei
            for (int j = 0; j < 86 && mirrored; j++)
                for (int i = 0; i < 114 && mirrored; i++)
                    mirrored = round.Pixels[(4 + j) * 128 + 6 + i] == round.Pixels[(4 + 85 - j) * 128 + 6 + 113 - i]
                        && round.Pixels[(4 + j) * 128 + 6 + i] == round.Pixels[(4 + j) * 128 + 6 + 113 - i];
            for (int j = 0; j < 18 && mirrored; j++) // und jede Ecke in sich, entlang ihrer Schräge
                for (int i = 0; i < j && mirrored; i++)
                    mirrored = round.Pixels[(4 + j) * 128 + 6 + i] == round.Pixels[(4 + i) * 128 + 6 + j];
            Check(mirrored, "Abgerundete Ecken und ihr Rahmen sind oben und unten, links und rechts gleich und in sich gespiegelt");
            Surface twice = new Surface(64, 64, 2);
            twice.Measure();
            twice.Damage(r);
            twice.Measure();
            twice.FillRect(33, 1, 1, 1, a);
            Check(twice.Damage(r) == 1 && r[0] == 32 && r[1] == 0 && r[2] == 32 && r[3] == 32, "Bei Retina sind Kacheln in Punkten gleich groß, die Bereiche kommen in Punkten");
        }

        // Stückweises Neuzeichnen in der Größe von Antonios Mac im Vollbild (1470x923 Punkte, doppelt gezeichnet):
        // Tippen und Fensterziehen zeichnen nur ihre Umgebung neu. Am Ende die Zeiten, zum Vergleichen vor und nach einem Umbau.
        private static void Pieces()
        {
            Check(Settings.Rows(new Resolution(2940, 1848), new Resolution(1470, 923), 2) == 1846
                && Settings.Rows(new Resolution(2940, 1848), new Resolution(1470, 924), 2) == 1848
                && Settings.Rows(new Resolution(2940, 1848), new Resolution(1470, 950), 2) == 1848
                && Settings.Rows(new Resolution(1440, 900), new Resolution(1470, 923), 1) == 900
                && Settings.Rows(new Resolution(2940, 1848), new Resolution(1400, 900), 2) == 1848
                && Settings.Rows(new Resolution(2940, 1848), new Resolution(0, 0), 1) == 1848,
                "Meldet QEMU mehr Zeilen, als sein Fenster hat, zeigt VibanOS nur die Zeilen des Fensters");
            Check(!Settings.Stretched(2940, 1846, new Resolution(1470, 923), 2) && Settings.Stretched(2940, 1848, new Resolution(1470, 923), 2)
                && Settings.Stretched(1440, 900, new Resolution(1470, 923), 1) && !Settings.Stretched(1440, 900, new Resolution(0, 0), 1),
                "Nur wenn das Bild nicht genau auf den Bildschirm des Rechners passt, gilt es als gestreckt");

            Surface.DefaultScale = 2;
            desktop.Resize(1470, 923);
            Surface front = new Surface(1470, 923);
            desktop.Open(Icons.Terminal);
            animTime += 1000;
            Draw(front);
            Window w = desktop.Active;
            Check(w is TerminalWindow && compared > 40, "Nach jedem Schritt des Selbsttests ergab stückweises Neuzeichnen dasselbe Bild wie ein ganzes (" + compared + " Vergleiche)");

            long all = (long)front.Width * front.Height;
            Type("x");
            Check(Draw(front) && dirtyArea * 20 < all, "Ein getippter Buchstabe zeichnet nur seine Umgebung neu (" + dirtyArea * 100 / all + " % des Bildes)");
            int gripX = w.X + w.Width / 2, gripY = w.Y + T.Size.TitleBar / 2;
            desktop.OnMouse(gripX, gripY, true);
            Draw(front);
            desktop.OnMouse(gripX + 12, gripY + 7, true);
            Check(Draw(front) && dirtyArea >= (long)w.Width * w.Height && dirtyArea < (long)(w.Width + 200) * (w.Height + 200),
                "Ein gezogenes Fenster zeichnet nur sich und seinen Schatten neu (" + dirtyArea * 100 / all + " % des Bildes)");
            int[] three = { 10, 20, 30, 40, 5, 50, 10, 10, 100, 0, 20, 30 };
            Array.Copy(three, desktop.Dirty, three.Length);
            desktop.DirtyCount = 3;
            int bx, by, bw, bh;
            desktop.DirtyBounds(out bx, out by, out bw, out bh);
            Check(bx == 5 && by == 0 && bw == 115 && bh == 60, "Das Rechteck um alle geänderten Ausschnitte umfasst genau sie");

            // Zeiten: je 20 Bilder
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 20; i++) { desktop.Redraw(); desktop.Frame(front); }
            double whole = watch.Elapsed.TotalMilliseconds / 20;
            watch.Restart();
            for (int i = 0; i < 20; i++) { desktop.OnMouse(gripX + 12 + 5 * (i + 1), gripY + 7 + 3 * (i + 1), true); desktop.Frame(front); }
            double drag = watch.Elapsed.TotalMilliseconds / 20;
            desktop.OnMouse(gripX, gripY, false);
            desktop.Frame(front);
            watch.Restart();
            for (int i = 0; i < 20; i++) { Type("x"); desktop.Frame(front); }
            double type = watch.Elapsed.TotalMilliseconds / 20;
            watch.Restart();
            for (int i = 0; i < 20; i++) { desktop.OnMouse(300 + 7 * i, 200 + 5 * i, false); desktop.Frame(front); }
            double mouse = watch.Elapsed.TotalMilliseconds / 20;
            Console.WriteLine("Zeit je Bild bei " + front.PixelWidth + "x" + front.PixelHeight + ": alles neu " + whole.ToString("0.0") + " ms, Fenster ziehen "
                + drag.ToString("0.0") + " ms, Buchstabe tippen " + type.ToString("0.00") + " ms, nur Maus " + mouse.ToString("0.000") + " ms");
            Surface.DefaultScale = 1;
        }

        // Kontextwechsel (VIB-3). Steht am Ende, damit die Prozessnummern in den Bildern davor dieselben bleiben.
        private static void Contexts()
        {
            int kernelThread = Environment.CurrentManagedThreadId;
            int before = OS.Processes.All.Count;

            // Zwei Prozesse rechnen rekursiv und geben auf jeder Ebene die CPU ab.
            int threadA = 0, threadB = 0;
            Script a = Spawn(p => { threadA = Environment.CurrentManagedThreadId; p.Output = Sum(p, 20).ToString(); });
            Script b = Spawn(p => { threadB = Environment.CurrentManagedThreadId; p.Output = Sum(p, 30).ToString(); });
            Until(() => a.Steps == 5);
            Check(a.State == ProcessState.Ready && a.Output == "" && a.Context != null && OS.Processes.Current == null,
                "Ein Prozess bleibt mitten in seiner Aufrufkette stehen, wenn er die CPU abgibt");
            Until(() => a.State == ProcessState.Terminated && b.State == ProcessState.Terminated);
            Check(a.Output == "210" && b.Output == "465" && a.Steps == 21 && b.Steps == 31,
                "Zwei Prozesse rechnen abwechselnd in der Tiefe weiter: Jeder hat seinen eigenen Stack");
            Check(threadA != kernelThread && threadB != kernelThread && threadA != threadB && a.Context == null && b.Context == null,
                "Jeder Prozess läuft in seinem eigenen Thread und gibt ihn mit dem Ende ab");
            OS.Processes.Yield();
            Check(OS.Processes.Current == null, "Yield aus dem Kernel tut nichts");

            // block und unblock
            OS.Shell.Execute("start counter");
            Process counter = OS.Processes.All[OS.Processes.All.Count - 1];
            Until(() => counter.Steps == 3);
            OS.Shell.Execute("block " + counter.Id);
            Check(counter.State == ProcessState.Blocked && counter.StateText == "blockiert" && Last().Style == LineStyle.Normal, "block blockiert einen Prozess");
            for (int i = 0; i < 100; i++) OS.Tick();
            Check(counter.Steps == 3 && counter.Output == "Zähler 3", "Ein blockierter Prozess bekommt die CPU nicht");
            counter.Stopped = true; // Auch dann ist er vor allem blockiert, nicht "angehalten".
            OS.Shell.Execute("block " + counter.Id);
            counter.Stopped = false;
            Check(Last().Style == LineStyle.Error && Last().Text == "Prozess " + counter.Id + " ist schon blockiert.", "block meldet, wenn der Prozess schon blockiert ist");
            OS.Shell.Execute("block " + a.Id);
            Check(Last().Style == LineStyle.Error && Last().Text == "Prozess " + a.Id + " ist beendet.", "block meldet, wenn der Prozess beendet ist");
            OS.Shell.Execute("unblock " + counter.Id);
            Check(counter.State == ProcessState.Ready && Last().Style == LineStyle.Normal, "unblock macht ihn wieder bereit");
            Until(() => counter.Steps == 4);
            Check(counter.Output == "Zähler 4", "Nach unblock zählt er dort weiter, wo er stand");
            OS.Shell.Execute("unblock " + counter.Id);
            Check(Last().Style == LineStyle.Error && counter.State == ProcessState.Ready, "unblock meldet, wenn der Prozess nicht blockiert ist");
            OS.Shell.Execute("block 9999");
            Check(Last().Text == "Kein Prozess mit der ID 9999.", "block meldet, wenn es den Prozess nicht gibt");
            OS.Shell.Execute("unblock");
            Check(Last().Text == "Aufruf: unblock <pid>", "unblock ohne Nummer nennt den Aufruf");

            // context
            List<ShellLine> lines = OS.Shell.Lines;
            OS.Shell.Execute("context " + counter.Id);
            Check(lines[lines.Count - 3].Text == "Prozess " + counter.Id + " (counter), bereit, 4 mal an der CPU"
                && lines[lines.Count - 2].Text == "Thread " + counter.Context.ThreadId + ", eigener Stack mit 256 KB", "context zeigt Thread und Stack eines Prozesses");
            Platform.Hardware.FakeThreads = true;
            OS.Shell.Execute("context " + counter.Id);
            Platform.Hardware.FakeThreads = false;
            Check(lines[lines.Count - 11].Text == "Thread 7, eigener Stack mit 256 KB ab 0x0000000001000000"
                && lines[lines.Count - 9].Text == "RIP    0x0000000000001000  RSP    0x0000000000001001"
                && Last().Text == "R14    0x0000000000001010  R15    0x0000000000001011", "context zeigt die gesicherten Register");
            Process fresh = OS.Processes.Start(new CounterProcess());
            OS.Shell.Execute("context " + fresh.Id);
            Check(Last().Text.StartsWith("Noch nicht gelaufen"), "context sagt, wenn ein Prozess noch keinen Stack hat");

            // Blockieren mitten im Programm
            Script sleeper = Spawn(p => { p.Output = "vor"; OS.Processes.Block(p); p.Output = "nach"; });
            Until(() => sleeper.Steps == 1);
            for (int i = 0; i < 100; i++) OS.Tick();
            Check(sleeper.State == ProcessState.Blocked && sleeper.Output == "vor" && sleeper.Steps == 1,
                "Blockiert sich ein Prozess selbst, bleibt er an dieser Stelle stehen");
            OS.Processes.Unblock(sleeper);
            Until(() => sleeper.State == ProcessState.Terminated);
            Check(sleeper.Output == "nach" && sleeper.Steps == 2, "Geweckt läuft er hinter dem Block weiter");

            Mutex door = new Mutex("tür");
            Script first = Spawn(p => { door.Lock(p.Id); p.Pause(); p.Pause(); door.Unlock(p.Id); });
            Until(() => first.Steps == 1);
            bool waited = false;
            Script second = Spawn(p => { while (!door.Lock(p.Id)) { waited = true; p.Pause(); } p.Output = "drin"; door.Unlock(p.Id); });
            Until(() => second.Steps == 1);
            Check(second.State == ProcessState.Blocked && door.Owner == first.Id, "Ein Prozess wartet mitten im Programm auf eine Sperre");
            Until(() => second.State == ProcessState.Terminated);
            Check(waited && second.Output == "drin" && second.Steps == 2 && door.Owner == Mutex.Nobody,
                "Mit der Freigabe läuft der Wartende weiter und bekommt die Sperre");
            // Dasselbe mit Block statt Yield: Die Sperre hat den Prozess schon blockiert, die CPU muss er trotzdem abgeben.
            Script holder = Spawn(p => { door.Lock(p.Id); p.Pause(); p.Pause(); door.Unlock(p.Id); });
            Until(() => holder.Steps == 1);
            int tries = 0;
            Script blocker = Spawn(p => { while (!door.Lock(p.Id) && tries < 100) { tries++; OS.Processes.Block(p); } door.Unlock(p.Id); });
            Until(() => blocker.State == ProcessState.Terminated);
            Check(tries == 1 && blocker.Steps == 2, "Block gibt die CPU auch ab, wenn der Prozess schon blockiert ist");

            // Beenden: Der Stack wird abgebaut, der Kontext geht an den nächsten Prozess.
            Script leaver = Spawn(p => { Syscall.Invoke(SyscallNumber.Exit); p.Pause(); p.Output = "lebt noch"; });
            Until(() => leaver.State == ProcessState.Terminated);
            Check(leaver.Output == "" && leaver.Steps == 1 && leaver.Context == null, "Nach dem Systemaufruf exit läuft das Programm nicht weiter");
            bool cleaned = false;
            Script victim = Spawn(p => { try { while (true) p.Pause(); } finally { cleaned = true; } });
            Until(() => victim.Steps == 2);
            int threads = Context.Count;
            OS.Processes.Kill(victim.Id);
            OS.Tick();
            Check(cleaned && victim.Context == null, "kill baut den Stack eines wartenden Prozesses ab, seine finally-Blöcke laufen");
            cleaned = false;
            victim = Spawn(p => { try { while (true) p.Pause(); } finally { cleaned = true; p.Pause(); cleaned = false; } });
            Until(() => victim.Steps == 1);
            victim.OnSignal(Signal.Term);
            OS.Tick();
            Check(cleaned && victim.Context == null && OS.Processes.Get(victim.Id) == victim,
                "Auch nach dem Signal Term räumt ein wartender Prozess auf, warten kann er dabei nicht mehr");
            Check(Context.Count == threads, "Ein neuer Prozess übernimmt den Kontext eines beendeten");
            Script hoarder = Spawn(p => { try { while (true) p.Pause(); } finally { Syscall.Invoke(SyscallNumber.Alloc); } });
            Until(() => hoarder.Steps == 1);
            OS.Processes.Kill(hoarder.Id);
            OS.Tick();
            Check(hoarder.Context == null && OS.Memory.TableOf(hoarder.Id) == null, "Speicher, den ein Prozess beim Aufräumen noch anfordert, wird frei");
            Script stubborn = Spawn(p => { while (true) { try { p.Pause(); } catch (ProcessKilled) { p.Output = "abgefangen"; } } });
            Until(() => stubborn.Steps == 1);
            OS.Processes.Kill(stubborn.Id);
            OS.Tick();
            Check(stubborn.Output == "abgefangen" && stubborn.Context == null && OS.Tick(), "Ein Prozess, der sein Ende abfängt, hält das System nicht auf");
            Script broken = Spawn(p => { p.Pause(); throw new InvalidOperationException("kaputt"); });
            Until(() => broken.State == ProcessState.Terminated);
            Check(broken.Output == "Abbruch: kaputt" && broken.Context == null, "Ein Fehler mitten im Programm beendet nur diesen Prozess");

            // Obergrenze: Jeder Prozess braucht einen Thread.
            while (OS.Processes.All.Count < ProcessManager.MaxProcesses) OS.Processes.Start(new CounterProcess());
            OS.Shell.Execute("start counter");
            Check(Last().Style == LineStyle.Error && Last().Text.StartsWith("Zu viele Prozesse") && OS.Processes.Start(new CounterProcess()) == null
                && OS.Processes.All.Count == ProcessManager.MaxProcesses, "Mehr als 64 Prozesse gibt es nicht zugleich");
            Process last = OS.Processes.All[OS.Processes.All.Count - 1];
            Until(() => last.Steps == 2);
            while (OS.Processes.All.Count > before) OS.Processes.Kill(OS.Processes.All[OS.Processes.All.Count - 1].Id);
            OS.Tick();
            Check(last.Context == null && Context.Count <= ProcessManager.MaxProcesses, "Für 64 Prozesse reichen 64 Threads, auch wenn viele kommen und gehen");

            // Ein Fenster ohne eigenes Programm zählt der Scheduler mit, die CPU übergibt er ihm nicht.
            foreach (Process p in OS.Processes.All) p.Stopped = true;
            bool idle = !OS.Tick();
            Process app = OS.Processes.Start(new AppProcess("Fenster"));
            threads = Context.Count;
            bool skipped = !OS.Tick() && !OS.Tick();
            Check(skipped && app.Steps == 2 && app.State == ProcessState.Ready && app.Context == null && Context.Count == threads,
                "Ein Fenster ohne eigenes Programm bekommt seinen Anteil, aber keinen Kontext");
            OS.Shell.Execute("context " + app.Id);
            Check(Last().Text.StartsWith("Ein Fenster ohne eigenes Programm"), "context sagt, dass ein Fenster keinen Stack braucht");
            app.Stopped = true;
            Process worker = OS.Processes.Start(new CounterProcess());
            Check(idle && OS.Tick() && worker.Steps == 1, "Tick meldet, ob ein Prozess an der CPU war");
            OS.Processes.Kill(worker.Id);
            OS.Processes.Kill(app.Id);
            OS.Tick();
            foreach (Process p in OS.Processes.All) p.Stopped = false;
        }

        // Ein Prozess, dessen Programm die Prüfung mitbringt.
        private class Script : Process
        {
            private readonly Action<Script> body;
            public Script(Action<Script> body) { Name = "test"; this.body = body; }
            public override void Run() { body(this); }
            public void Pause() { Yield(); }
        }

        private static Script Spawn(Action<Script> body) { return (Script)OS.Processes.Start(new Script(body)); }

        // Summiert 1 bis n rekursiv und gibt auf jeder Ebene die CPU ab: Die halbfertige Aufrufkette muss jeden Wechsel überstehen.
        private static int Sum(Script p, int n)
        {
            if (n == 0) return 0;
            p.Pause();
            return n + Sum(p, n - 1);
        }

        // Lässt den Scheduler laufen, bis die Bedingung gilt.
        private static void Until(Func<bool> done)
        {
            for (int i = 0; i < 10000 && !done(); i++) OS.Tick();
            if (!done()) throw new Exception("Prüfung fehlgeschlagen: Darauf hat der Scheduler vergeblich gewartet");
        }

        // Lässt einen Befehl, der in Schritten arbeitet (rm -r, cp -r), weiterlaufen, bis er fertig ist oder nachfragt.
        private static void Finish() { Until(() => !OS.Shell.Busy || OS.Shell.Asking); }

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

        /// <summary>
        /// Steht im Ordner ein Eintrag mit genau diesem Namen? Anders als File.Exists und Directory.Exists gilt die Antwort
        /// auch auf einem Wirt, der Groß- und Kleinschreibung nicht unterscheidet (Mac, Windows): Dort fände "NOTIZEN" den Ordner "notizen".
        /// </summary>
        private static bool Listed(string folder, string name)
        {
            foreach (string entry in Directory.GetFileSystemEntries(folder))
                if (Path.GetFileName(entry) == name) return true;
            return false;
        }

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
            Draw(); // neu zeichnen, damit die Klickflächen zum neuen Stand passen
        }

        // Was Kernel.cs tut, wenn der Canvas eine neue Größe hat.
        private static void ScreenChanged()
        {
            if (screen.Width == Platform.Hardware.ScreenWidth && screen.Height == Platform.Hardware.ScreenHeight) return;
            screen = new Surface(Platform.Hardware.ScreenWidth, Platform.Hardware.ScreenHeight);
            desktop.Resize(screen.Width, screen.Height);
            Draw();
        }

        // Der erste Eintrag, den die Liste von Dateien gerade zeigt, oder -1.
        private static int FirstRow(FilesWindow files)
        {
            for (int i = 0; i < files.Count; i++)
                if (files.CenterOf(FilesWindow.Item + i) != null) return i;
            return -1;
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

        // Zählt die Bildpunkte in der Farbe der Einfügemarke, x ist der am weitesten links (in Punkten), sonst -1.
        // Auf dem Anmeldebildschirm hat außerhalb der Nutzerwahl nur sie genau diese Farbe.
        private static int Caret(out int x)
        {
            int count = 0, left = int.MaxValue;
            for (int i = 0; i < screen.Pixels.Length; i++)
            {
                if (((uint)screen.Pixels[i] & 0xFFFFFF) != (T.Color.Accent & 0xFFFFFF)) continue;
                count++;
                if (i % screen.PixelWidth < left) left = i % screen.PixelWidth;
            }
            x = count > 0 ? left / screen.Scale : -1;
            return count;
        }

        // Stellt die Uhr weiter, bis jede Animation fertig ist, und zeichnet neu.
        private static void Settle()
        {
            animTime += 1000;
            Draw();
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

        // Öffnet eine Datei des gezeigten Ordners in der Vorschau, auch aus einer offenen Vorschau heraus.
        private static void ShowFile(FilesWindow files, string name)
        {
            if (files.OpenFile != null) files.OnKey(Key.Backspace, '\0');
            Picture(files);
            ClickAt(files.CenterOf(FilesWindow.Item + files.IndexOf(name)));
            files.OnKey(Key.Enter, '\0');
            Picture(files);
        }

        // Zeichnet neu und fasst einen Ausschnitt des Bildschirms (links, oben, rechts, unten) zu einer Zahl zusammen.
        // Der Mauszeiger liegt dabei abseits, sonst unterschieden sich zwei Bilder schon durch ihn.
        private static long Region(int[] r)
        {
            desktop.OnMouse(2, 300, false);
            animTime += 1000;
            OS.Changed = true;
            Draw();
            long sum = 17;
            for (int y = r[1]; y < r[3]; y++)
                for (int x = r[0]; x < r[2]; x++) sum = sum * 31 + screen.Pixels[y * screen.Width + x];
            return sum;
        }

        // Zeichnet neu und fasst den Inhalt eines Fensters zu einer Zahl zusammen, um zwei Bilder zu vergleichen.
        /// <summary>Wie viel im Ausschnitt des zuletzt gezeichneten Bildes steht: Bildpunkte, die nicht die Farbe seiner linken oberen Ecke haben.</summary>
        private static int Ink(int[] r)
        {
            int ink = 0;
            for (int y = r[1]; y < r[3]; y++)
                for (int x = r[0]; x < r[2]; x++)
                    if (screen.Pixels[y * screen.Width + x] != screen.Pixels[r[1] * screen.Width + r[0]]) ink++;
            return ink;
        }
        private static long Picture(Window w)
        {
            animTime += 1000;
            OS.Changed = true;
            Draw();
            long sum = 17;
            for (int y = w.Y + T.Size.TitleBar; y < w.Y + w.Height; y++)
                for (int x = w.X; x < w.X + w.Width; x++) sum = sum * 31 + screen.Pixels[y * screen.Width + x];
            return sum;
        }

        private static void Shot(string dir, string name)
        {
            animTime += 1000;
            OS.Changed = true;
            Draw();
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
                byte[] line = new byte[1 + s.PixelWidth * 3]; // erstes Byte: Filter 0
                for (int y = 0; y < s.PixelHeight; y++)
                {
                    for (int x = 0; x < s.PixelWidth; x++)
                    {
                        int p = s.Pixels[y * s.PixelWidth + x];
                        line[1 + x * 3] = (byte)(p >> 16);
                        line[2 + x * 3] = (byte)(p >> 8);
                        line[3 + x * 3] = (byte)p;
                    }
                    z.Write(line, 0, line.Length);
                }
            }
            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), s.PixelWidth);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), s.PixelHeight);
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
