using System;
using System.Collections.Generic;
using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Platform;
using VibanOS.Users;

namespace VibanOS.Cli.Commands
{
    /// <summary>Gemeinsame Basis der Dateibefehle (VIB-2): Pfad auflösen, Dateisystem und Rechte prüfen.</summary>
    public abstract class FileCommand : Command
    {
        /// <summary>
        /// Löst args[0] zu einem vollständigen Pfad auf und prüft das Recht. Gibt null zurück und
        /// meldet den Grund im Terminal, wenn etwas fehlt. args enthält nur den einen Namen: Wer mehr Argumente hat
        /// (write, cp, mv), reicht ihn einzeln herein.
        /// </summary>
        protected string Target(string[] args, Shell shell, Access access)
        {
            if (!Vfs.Available)
            {
                shell.Error("Kein Dateisystem gefunden. VibanOS mit einer Festplatte starten und einmal 'format' ausführen (siehe README).");
                return null;
            }
            if (args.Length == 0 && Usage != Name)
            {
                shell.Error("Aufruf: " + Usage);
                return null;
            }
            // Ohne Anführungszeichen zerfällt "Neuer Ordner" in zwei Namen. Den zweiten still zu übergehen hieße bei rm:
            // "Neuer" löschen und "Neuer Ordner" stehen lassen.
            if (args.Length > 1)
            {
                shell.Error("Zu viele Namen. Ein Name mit Leerzeichen gehört in Anführungszeichen.");
                return null;
            }
            string path = Vfs.Resolve(shell.CurrentDirectory, args.Length > 0 ? args[0] : "");
            if (!Vfs.CanAccess(OS.Users.Current, path, access))
            {
                shell.Error("Zugriff verweigert: " + path);
                return null;
            }
            return path;
        }

        /// <summary>Nimmt ein vorangestelltes -r aus den Argumenten (rm -r, cp -r). true: Es war da.</summary>
        protected static bool Recursive(ref string[] args)
        {
            if (args.Length == 0 || (args[0] != "-r" && args[0] != "-R")) return false;
            string[] rest = new string[args.Length - 1];
            for (int i = 0; i < rest.Length; i++) rest[i] = args[i + 1];
            args = rest;
            return true;
        }
    }

    public class LsCommand : FileCommand
    {
        public override string Name { get { return "ls"; } }
        public override string Description { get { return "Inhalt des aktuellen Ordners anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string path = Target(args, shell, Access.Read);
            if (path == null) return;
            if (!Directory.Exists(path)) { shell.Error("Ordner nicht gefunden: " + path); return; }
            string[] dirs = Directory.GetDirectories(path);
            string[] files = Directory.GetFiles(path);
            for (int i = 0; i < dirs.Length; i++) shell.WriteLine(Vfs.NameOf(dirs[i]) + Vfs.Separator);
            for (int i = 0; i < files.Length; i++) shell.WriteLine(Vfs.NameOf(files[i]));
            if (dirs.Length + files.Length == 0) shell.WriteLine("(leer)", LineStyle.Muted);
        }
    }

    public class CdCommand : FileCommand
    {
        public override string Name { get { return "cd"; } }
        public override string Usage { get { return "cd <ordner>"; } }
        public override string Description { get { return "Ordner wechseln, 'cd ..' geht eine Ebene hoch"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string path = Target(args, shell, Access.Execute);
            if (path == null) return;
            if (!Directory.Exists(path)) { shell.Error("Ordner nicht gefunden: " + path); return; }
            shell.CurrentDirectory = path;
        }
    }

    public class MkdirCommand : FileCommand
    {
        public override string Name { get { return "mkdir"; } }
        public override string Usage { get { return "mkdir <name>"; } }
        public override string Description { get { return "Ordner anlegen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string path = Target(args, shell, Access.Write);
            if (path == null) return;
            if (Directory.Exists(path) || File.Exists(path)) { shell.Error("Gibt es schon: " + path); return; }
            Directory.CreateDirectory(path);
        }
    }

    public class TouchCommand : FileCommand
    {
        public override string Name { get { return "touch"; } }
        public override string Usage { get { return "touch <name>"; } }
        public override string Description { get { return "Leere Datei anlegen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string path = Target(args, shell, Access.Write);
            if (path == null) return;
            if (Directory.Exists(path) || File.Exists(path)) { shell.Error("Gibt es schon: " + path); return; }
            File.Create(path).Close();
        }
    }

    public class CatCommand : FileCommand
    {
        public override string Name { get { return "cat"; } }
        public override string Usage { get { return "cat <datei>"; } }
        public override string Description { get { return "Datei anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string path = Target(args, shell, Access.Read);
            if (path == null) return;
            if (!File.Exists(path)) { shell.Error("Datei nicht gefunden: " + path); return; }
            shell.WriteLine(File.ReadAllText(path));
        }
    }

    public class WriteCommand : FileCommand
    {
        public override string Name { get { return "write"; } }
        public override string Usage { get { return "write <datei> <text>"; } }
        public override string Description { get { return "Text in eine Datei schreiben"; } }

        public override void Execute(string[] args, Shell shell)
        {
            // Ohne Text bliebe eine leere Datei zurück. Wer das will, schreibt ausdrücklich "".
            if (args.Length < 2) { shell.Error("Aufruf: " + Usage); return; }
            string path = Target(new[] { args[0] }, shell, Access.Write);
            if (path == null) return;
            if (Directory.Exists(path)) { shell.Error("Das ist ein Ordner: " + path); return; }
            File.WriteAllText(path, Shell.Join(args, 1));
        }
    }

    public class RmCommand : FileCommand
    {
        public override string Name { get { return "rm"; } }
        public override string Usage { get { return "rm [-r] <name>"; } }
        public override string Description { get { return "Datei oder leeren Ordner löschen, mit -r einen Ordner samt Inhalt"; } }

        public override void Execute(string[] args, Shell shell)
        {
            bool recursive = Recursive(ref args);
            string path = Target(args, shell, Access.Write);
            if (path == null) return;
            // Wie bei mv: Ohne die Nutzerdatei begänne der nächste Start mit der Ersteinrichtung.
            if (Vfs.IsFixed(path)) { shell.Error("Das braucht das System an seinem Platz: " + path); return; }
            // Unter seinem FAT-Kurznamen erkennt Vfs.IsFixed einen Ordner nicht wieder: 'rm -r /SYSTEM~1' träfe sonst den Systemordner.
            if (recursive && path.IndexOf('~') >= 0) { shell.Error("rm -r nimmt keinen Pfad mit ~. Bitte die langen Namen verwenden."); return; }
            if (File.Exists(path)) DeleteFile(path, shell);
            else if (!Directory.Exists(path)) shell.Error("Nicht gefunden: " + path);
            else if (recursive) DeleteFolder(path, shell);
            else if (Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length > 0) shell.Error("Ordner ist nicht leer: " + path + ". Mit 'rm -r' samt Inhalt löschen.");
            // Sonst stünde die Shell danach in einem Ordner, den es nicht mehr gibt.
            else if (string.Equals(path, shell.CurrentDirectory, StringComparison.OrdinalIgnoreCase)) shell.Error("Das ist der aktuelle Ordner. Erst mit 'cd ..' hinausgehen.");
            else Directory.Delete(path);
        }

        // rm -r: Die Arbeit läuft in Schritten (RemoveJob), Execute kehrt sofort zurück.
        private static void DeleteFolder(string path, Shell shell)
        {
            // Die Shell stünde danach in einem Ordner, den es nicht mehr gibt, auch wenn sie in einem Unterordner steht.
            if (Text.HasPrefix(shell.CurrentDirectory.ToLowerInvariant() + Vfs.Separator, path.ToLowerInvariant() + Vfs.Separator))
            {
                shell.Error("Darin steht gerade die Shell. Erst mit 'cd' hinausgehen.");
                return;
            }
            shell.Start(new RemoveJob(path));
        }

        // Wie in der App Dateien: Eine Datei, die das System liest (bisher settings.txt), geht erst nach einer Warnung.
        private static void DeleteFile(string path, Shell shell)
        {
            string warning = Vfs.SystemFile(path);
            if (warning == null) { File.Delete(path); return; }
            shell.WriteLine("Achtung, Systemdatei: " + warning);
            shell.Ask(path + " trotzdem löschen? Zum Bestätigen 'ja' eingeben: ", false, answer =>
            {
                if (answer != "ja") { shell.WriteLine("Abgebrochen."); return; }
                // Zwischen Frage und Antwort kann sie in der App Dateien schon gelöscht worden sein.
                if (File.Exists(path)) File.Delete(path);
            });
        }
    }

    public class PwdCommand : Command
    {
        public override string Name { get { return "pwd"; } }
        public override string Description { get { return "Aktuellen Ordner anzeigen"; } }

        public override void Execute(string[] args, Shell shell) { shell.WriteLine(shell.CurrentDirectory); }
    }

    /// <summary>Gemeinsame Basis von cp und mv: Quelle und Ziel prüfen. Ist das Ziel ein Ordner, landet die Quelle darin.</summary>
    public abstract class TransferCommand : FileCommand
    {
        protected bool Paths(string[] args, Shell shell, Access source, out string from, out string to)
        {
            from = to = null;
            // Genau zwei: Ein Name mit Leerzeichen ohne Anführungszeichen zerfiele sonst still in Ziel und Rest.
            if (args.Length != 2) { shell.Error("Aufruf: " + Usage + " (Namen mit Leerzeichen in Anführungszeichen)"); return false; }
            from = Target(new[] { args[0] }, shell, source);
            if (from == null) return false;
            to = Target(new[] { args[1] }, shell, Access.Write);
            if (to == null) return false;
            if (!File.Exists(from) && !Directory.Exists(from)) { shell.Error("Nicht gefunden: " + from); return false; }
            // FAT unterscheidet Groß- und Kleinschreibung nicht: "mv a.txt A.txt" träfe dieselbe Datei.
            bool same = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
            if (!same && Directory.Exists(to))
            {
                to = Vfs.Combine(to, Vfs.NameOf(from));
                same = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);
            }
            if (same)
            {
                shell.Error(from == to ? "Quelle und Ziel sind dasselbe: " + from
                    : "Auf der Platte ist das derselbe Name, Groß- und Kleinschreibung zählt dort nicht: " + to);
                return false;
            }
            // Der Name kommt aus der Quelle und könnte ein Kurzname mit ~ sein: noch einmal prüfen.
            if (!Vfs.CanAccess(OS.Users.Current, to, Access.Write)) { shell.Error("Zugriff verweigert: " + to); return false; }
            if (File.Exists(to) || Directory.Exists(to)) { shell.Error("Gibt es schon: " + to); return false; }
            return true;
        }
    }

    public class CpCommand : TransferCommand
    {
        public override string Name { get { return "cp"; } }
        public override string Usage { get { return "cp [-r] <quelle> <ziel>"; } }
        public override string Description { get { return "Datei kopieren, mit -r einen Ordner samt Inhalt"; } }

        public override void Execute(string[] args, Shell shell)
        {
            bool recursive = Recursive(ref args);
            string from, to;
            if (!Paths(args, shell, Access.Read, out from, out to)) return;
            if (Directory.Exists(from))
            {
                if (!recursive) { shell.Error("Ordner kopiert cp nur mit -r: cp -r <ordner> <ziel>"); return; }
                // Unter einem FAT-Kurznamen erkennen die Prüfungen denselben Ordner nicht wieder (wie bei mv), und
                // 'cp -r daten DATEN~1/innen' kopierte ihn in sich selbst.
                if (from.IndexOf('~') >= 0 || to.IndexOf('~') >= 0) { shell.Error("Ordner kopiert cp -r nicht über einen Pfad mit ~. Bitte die langen Namen verwenden."); return; }
                if (Vfs.IsFixed(from)) { shell.Error(CopyJob.Fixed + from); return; }
                if (Vfs.IsFixed(to)) { shell.Error(CopyJob.Fixed + to); return; }
                // Die Kopie würde selbst wieder kopiert, ohne Ende.
                if (Text.HasPrefix(to.ToLowerInvariant() + Vfs.Separator, from.ToLowerInvariant() + Vfs.Separator)) { shell.Error("Ein Ordner passt nicht in sich selbst."); return; }
                shell.Start(new CopyJob(from, to));
                return;
            }
            try { File.Copy(from, to, false); }
            catch (Exception)
            {
                // Kein halbes Ziel zurücklassen, etwa wenn die Platte voll ist. Das Ziel gab es vorher nicht (Paths),
                // was jetzt dort liegt, stammt also von diesem Versuch. Den Fehler meldet die Shell.
                try { File.Delete(to); } catch (Exception) { }
                throw;
            }
        }
    }

    public class MvCommand : TransferCommand
    {
        public override string Name { get { return "mv"; } }
        public override string Usage { get { return "mv <name> <ziel>"; } }
        public override string Description { get { return "Datei oder Ordner verschieben oder umbenennen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string from, to;
            if (!Paths(args, shell, Access.Write, out from, out to)) return;
            if (Vfs.IsFixed(from)) { shell.Error("Das braucht das System an seinem Platz: " + from); return; }
            if (File.Exists(from)) { File.Move(from, to); return; }
            // Unter seinem FAT-Kurznamen (daten ist auch DATEN~1) erkennen die Prüfungen hier denselben Ordner nicht wieder.
            // 'mv /daten /DATEN~1/innen' schöbe ihn in sich selbst, und der Treiber ließe ihn dann verschwinden.
            if (from.IndexOf('~') >= 0 || to.IndexOf('~') >= 0) { shell.Error("Ordner verschiebt mv nicht über einen Pfad mit ~. Bitte die langen Namen verwenden."); return; }
            string inside = from.ToLowerInvariant() + Vfs.Separator;
            if (Text.HasPrefix(shell.CurrentDirectory.ToLowerInvariant() + Vfs.Separator, inside)) { shell.Error("Darin steht gerade die Shell. Erst mit 'cd' hinausgehen."); return; }
            if (Text.HasPrefix(to.ToLowerInvariant() + Vfs.Separator, inside)) { shell.Error("Ein Ordner passt nicht in sich selbst."); return; }
            Directory.Move(from, to);
        }
    }

    public class DfCommand : Command
    {
        public override string Name { get { return "df"; } }
        public override string Description { get { return "Belegten und freien Platz der Festplatte anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            ulong total, free;
            if (!Vfs.Available) { shell.Error("Kein Dateisystem eingebunden."); return; }
            if (!Hardware.DiskSpace(out total, out free)) { shell.Error("Der freie Platz lässt sich nicht ermitteln."); return; }
            shell.WriteLine(Vfs.Root + "  " + Text.Bytes(total) + " gesamt, " + Text.Bytes(total - free) + " belegt, " + Text.Bytes(free) + " frei");
        }
    }

    public class FormatCommand : Command
    {
        public override string Name { get { return "format"; } }
        public override string Description { get { return "Leere Festplatte einrichten (FAT)"; } }
        public override Permission Required { get { return Permission.DriverManage; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (Vfs.Available) { shell.Error("Es ist schon ein Dateisystem eingebunden: " + Vfs.Root); return; }
            shell.Ask("Die leere Festplatte wird jetzt eingerichtet. Zum Bestätigen 'ja' eingeben: ", false, answer =>
            {
                if (answer != "ja") { shell.WriteLine("Abgebrochen."); return; }
                string error = Hardware.FormatDisk();
                if (error == null)
                {
                    Vfs.Init(Vfs.Root);
                    if (!Vfs.Available) error = "Die Festplatte ist eingerichtet, lässt sich aber nicht lesen. Bitte neu starten.";
                }
                if (error != null) { shell.Error(error); return; }

                // Die Nutzer gab es bisher nur im Arbeitsspeicher. Jetzt bekommen sie ihre Ordner und die Nutzerdatei.
                for (int i = 0; i < OS.Users.All.Count; i++) Directory.CreateDirectory(OS.Users.All[i].HomeDirectory);
                shell.CurrentDirectory = OS.Users.Current.HomeDirectory;
                shell.WriteLine("Festplatte eingerichtet und unter " + Vfs.Root + " eingebunden.");
                if (!OS.Users.Save()) shell.Error("Die Nutzerdatei ließ sich nicht schreiben. Nutzer gelten nur bis zum Neustart.");
            });
        }
    }

    /// <summary>
    /// Ein Ordner samt Unterordnern für rm -r und cp -r, eingelesen in Schritten: je Schritt ein Ordner. Der FAT-Treiber von
    /// COSMOS merkt sich keine Ordner, jedes Lesen sucht den Pfad neu ab. Entry zählt erst die Ordner in der Reihenfolge, in
    /// der sie gefunden wurden (der erste ist der Ordner selbst, jeder steht hinter dem, in dem er liegt), dann die Dateien.
    /// Vorwärts gelesen kommt so jeder Ordner vor seinem Inhalt, rückwärts jeder Inhalt vor seinem Ordner.
    /// </summary>
    public class FolderTree
    {
        /// <summary>Mehr Einträge nimmt sich ein Befehl nicht auf einmal vor, damit die Listen den Arbeitsspeicher nicht füllen.</summary>
        public const int MaxEntries = 2000;

        public readonly List<string> Folders = new List<string>();
        public readonly List<string> Files = new List<string>();
        private int listed; // so viele aus Folders sind schon gelesen

        public FolderTree(string root) { Folders.Add(root); }

        public string Root { get { return Folders[0]; } }

        public int Count { get { return Folders.Count + Files.Count; } }

        /// <summary>true: Alle Ordner sind gelesen.</summary>
        public bool Complete { get { return listed == Folders.Count; } }

        public string Entry(int i) { return i < Folders.Count ? Folders[i] : Files[i - Folders.Count]; }

        /// <summary>Liest den nächsten Ordner. Gibt eine Meldung zurück, wenn es zu viele Einträge werden, sonst null.</summary>
        public string Step()
        {
            string folder = Folders[listed++];
            // Die Namen hängen an folder statt an dem, was GetFiles liefert: je nach System Namen oder ganze Pfade (Vfs.NameOf).
            string[] dirs = Directory.GetDirectories(folder);
            for (int i = 0; i < dirs.Length; i++) Folders.Add(Vfs.Combine(folder, Vfs.NameOf(dirs[i])));
            string[] files = Directory.GetFiles(folder);
            for (int i = 0; i < files.Length; i++) Files.Add(Vfs.Combine(folder, Vfs.NameOf(files[i])));
            if (Count > MaxEntries) return "Mehr als " + MaxEntries + " Einträge, so viel auf einmal geht nicht. Bitte in Teilen.";
            return null;
        }

        /// <summary>Was im Ordner liegt, für Rückfrage und Meldungen: " mit 3 Dateien und 1 Unterordner", leer bei leerem Ordner.</summary>
        public string Content
        {
            get
            {
                string files = Files.Count > 0 ? Plural(Files.Count, "Datei", "Dateien") : "";
                string folders = Folders.Count > 1 ? Plural(Folders.Count - 1, "Unterordner", "Unterordnern") : "";
                if (files == "" && folders == "") return "";
                return " mit " + files + (files != "" && folders != "" ? " und " : "") + folders;
            }
        }

        public static string Plural(int n, string one, string many) { return n + " " + (n == 1 ? one : many); }
    }

    /// <summary>
    /// rm -r auf einen Ordner: einlesen und alles prüfen, nachfragen, dann je Schritt eine Datei oder einen leeren Ordner
    /// löschen, die Dateien zuerst, die Ordner von innen nach außen. Gelöscht wird nur, wenn alles erlaubt ist: nichts ohne
    /// Schreibrecht und nichts, worauf sich das System verlässt (Vfs.IsFixed), auch nicht tief im Ordner.
    /// </summary>
    public class RemoveJob : Job
    {
        private readonly FolderTree tree;
        private bool confirmed;
        private int removed;

        public RemoveJob(string folder) { tree = new FolderTree(folder); }

        public override string Name { get { return "rm"; } }

        public override string Status
        {
            get
            {
                if (!confirmed) return "rm -r: lese " + FolderTree.Plural(tree.Count, "Eintrag", "Einträge") + ", Escape bricht ab";
                return "rm -r: " + removed + " von " + tree.Count + " gelöscht, Escape bricht ab";
            }
        }

        public override bool Step(Shell shell)
        {
            if (!tree.Complete)
            {
                string error = tree.Step();
                if (error == null && tree.Complete) error = Check();
                if (error != null) { shell.Error(error + " Nichts gelöscht."); return true; }
                if (tree.Complete) Ask(shell);
                return false;
            }
            string path = tree.Entry(tree.Count - 1 - removed);
            bool folder = tree.Count - 1 - removed < tree.Folders.Count;
            // Zwischen Frage und Schritt kann in der App Dateien etwas passiert sein: Jeder Schritt prüft neu.
            if (!Vfs.CanAccess(OS.Users.Current, path, Access.Write) || Vfs.IsFixed(path))
            {
                shell.Error("Abgebrochen, das darf rm -r nicht löschen: " + path + ". " + Progress);
                return true;
            }
            if (!folder) { if (File.Exists(path)) File.Delete(path); }
            else if (Directory.Exists(path))
            {
                // Directory.Delete bekommt nur leere Ordner, wie bei rm: Was inzwischen hinzukam, ist nicht gefragt worden.
                if (Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length > 0)
                {
                    shell.Error("Abgebrochen, in " + path + " liegt inzwischen etwas Neues. " + Progress);
                    return true;
                }
                Directory.Delete(path);
            }
            removed++;
            if (removed < tree.Count) return false;
            shell.WriteLine("Gelöscht: " + tree.Root + tree.Content + ".");
            return true;
        }

        public override bool Stop(Shell shell)
        {
            shell.WriteLine(confirmed ? "Abgebrochen. " + Progress : "Abgebrochen, nichts gelöscht.");
            return true;
        }

        private string Progress { get { return removed + " von " + tree.Count + " Einträgen sind schon gelöscht."; } }

        // Nach dem Einlesen, bevor gefragt wird: Darf der Nutzer alles löschen, und ist nichts Festes dabei?
        private string Check()
        {
            for (int i = 0; i < tree.Count; i++)
            {
                string path = tree.Entry(i);
                if (!Vfs.CanAccess(OS.Users.Current, path, Access.Write)) return "Zugriff verweigert: " + path + ".";
                if (Vfs.IsFixed(path)) return "Darin liegt etwas, das das System an seinem Platz braucht: " + path + ".";
            }
            return null;
        }

        private void Ask(Shell shell)
        {
            // Wie in der App Dateien: Eine Datei, die das System liest (settings.txt), geht erst nach einer Warnung.
            for (int i = tree.Folders.Count; i < tree.Count; i++)
            {
                string warning = Vfs.SystemFile(tree.Entry(i));
                if (warning != null) shell.WriteLine("Achtung, Systemdatei " + tree.Entry(i) + ": " + warning);
            }
            string content = tree.Content;
            shell.Ask((content == "" ? "Leeren Ordner " : "Ordner ") + tree.Root + content + " löschen? Zum Bestätigen 'ja' eingeben: ", false, answer =>
            {
                if (answer == "ja") confirmed = true;
                else shell.Cancel();
            });
        }
    }

    /// <summary>
    /// cp -r auf einen Ordner: die Quelle einlesen und prüfen, dann je Schritt einen Ordner anlegen oder ein Stück einer Datei
    /// kopieren, die Ordner vor ihrem Inhalt. Wie cp überschreibt sie nichts und lässt keine halbe Kopie zurück: Scheitert ein
    /// Schritt oder bricht Escape ab, entfernt sie in weiteren Schritten wieder, was sie angelegt hat, und nur das.
    /// </summary>
    public class CopyJob : Job
    {
        public const string Fixed = "Das braucht das System, cp -r fasst es nicht an: ";

        // So viel einer Datei je Schritt. Eine große Datei in einem Stück hielte die Kernel-Schleife zu lange an.
        private const int Chunk = 64 * 1024;

        private readonly FolderTree tree;
        private readonly string target;
        private readonly List<string> created = new List<string>(); // in der Reihenfolge des Anlegens
        private int copied;                     // fertige Einträge in der Reihenfolge von FolderTree.Entry
        private FileStream input, output;       // Datei, die gerade kopiert wird
        private byte[] buffer;
        private bool cleaning;                  // räumt die angefangene Kopie weg

        public CopyJob(string from, string to)
        {
            tree = new FolderTree(from);
            target = to;
        }

        public override string Name { get { return "cp"; } }

        public override string Status
        {
            get
            {
                if (cleaning) return "cp -r: entferne die angefangene Kopie, noch " + created.Count;
                if (!tree.Complete) return "cp -r: lese " + FolderTree.Plural(tree.Count, "Eintrag", "Einträge") + ", Escape bricht ab";
                return "cp -r: " + copied + " von " + tree.Count + " kopiert, Escape bricht ab";
            }
        }

        public override bool Step(Shell shell)
        {
            if (cleaning) return Clean(shell);
            string error;
            try
            {
                if (!tree.Complete)
                {
                    error = tree.Step();
                    if (error == null && tree.Complete) error = Check();
                    if (error == null) return false;
                    shell.Error(error + " Nichts kopiert.");
                    return true;
                }
                error = Copy();
            }
            catch (Exception e) { error = e.Message; }
            if (error != null)
            {
                shell.Error("cp: " + error);
                return Abort(shell);
            }
            if (copied < tree.Count) return false;
            shell.WriteLine("Kopiert nach " + target + tree.Content + ".");
            return true;
        }

        public override bool Stop(Shell shell)
        {
            if (cleaning) return false; // Das Aufräumen läuft zu Ende, sonst bliebe die halbe Kopie doch liegen.
            shell.WriteLine("Abgebrochen.");
            return Abort(shell);
        }

        // Nach dem Einlesen: Darf der Nutzer alles lesen, und ist nichts dabei, worauf sich das System verlässt?
        private string Check()
        {
            for (int i = 0; i < tree.Count; i++)
            {
                string path = tree.Entry(i);
                if (!Vfs.CanAccess(OS.Users.Current, path, Access.Read)) return "Zugriff verweigert: " + path + ".";
                if (Vfs.IsFixed(path)) return Fixed + path + ".";
            }
            return null;
        }

        // Ein Schritt: einen Ordner anlegen, eine Datei öffnen oder ein Stück davon kopieren. Gibt eine Fehlermeldung zurück, sonst null.
        private string Copy()
        {
            if (input != null)
            {
                int n = input.Read(buffer, 0, buffer.Length);
                if (n > 0) { output.Write(buffer, 0, n); return null; }
                Close();
                copied++;
                return null;
            }
            string from = tree.Entry(copied), to = target + from.Substring(tree.Root.Length);
            // Seit dem Einlesen kann sich in der App Dateien etwas geändert haben: Jeder Schritt prüft neu.
            if (!Vfs.CanAccess(OS.Users.Current, from, Access.Read)) return "Zugriff verweigert: " + from;
            if (!Vfs.CanAccess(OS.Users.Current, to, Access.Write)) return "Zugriff verweigert: " + to;
            if (Vfs.IsFixed(from)) return Fixed + from;
            if (Vfs.IsFixed(to)) return Fixed + to;
            if (File.Exists(to) || Directory.Exists(to)) return "Gibt es schon: " + to;
            if (copied < tree.Folders.Count)
            {
                Directory.CreateDirectory(to);
                created.Add(to);
                copied++;
                return null;
            }
            if (buffer == null) buffer = new byte[Chunk];
            input = File.OpenRead(from);
            output = File.Create(to);
            created.Add(to);
            return null;
        }

        // Nach einem Fehler oder Escape: aufräumen, was angelegt wurde. true: Es gibt nichts aufzuräumen, die Arbeit ist vorbei.
        private bool Abort(Shell shell)
        {
            // Eine Datei, die sich nicht schließen lässt, räumt Clean trotzdem weg, so gut es geht.
            try { Close(); } catch (Exception) { }
            if (created.Count == 0)
            {
                shell.WriteLine("Nichts kopiert.");
                return true;
            }
            cleaning = true;
            return false;
        }

        // Je Schritt eines der angelegten Dinge entfernen, das zuletzt angelegte zuerst: Dateien vor ihren Ordnern. Nur leere
        // Ordner, wie bei rm. Was in der Zwischenzeit jemand anderes hineingelegt hat, bleibt mit seinem Ordner stehen.
        private bool Clean(Shell shell)
        {
            string path = created[created.Count - 1];
            created.RemoveAt(created.Count - 1);
            try
            {
                if (File.Exists(path)) File.Delete(path);
                else if (Directory.Exists(path))
                {
                    if (Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length > 0)
                    {
                        shell.Error("Die angefangene Kopie ließ sich nicht ganz entfernen, in " + path + " liegt etwas Neues.");
                        return true;
                    }
                    Directory.Delete(path);
                }
            }
            catch (Exception e)
            {
                shell.Error("Die angefangene Kopie ließ sich nicht ganz entfernen: " + path + " (" + e.Message + ")");
                return true;
            }
            if (created.Count > 0) return false;
            shell.WriteLine("Die angefangene Kopie ist wieder entfernt, nichts kopiert.");
            return true;
        }

        private void Close()
        {
            FileStream a = input, b = output;
            input = output = null;
            try { if (a != null) a.Dispose(); }
            finally { if (b != null) b.Dispose(); }
        }
    }
}
