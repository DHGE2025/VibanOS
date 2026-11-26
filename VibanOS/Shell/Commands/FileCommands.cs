using System;
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
        public override string Usage { get { return "rm <name>"; } }
        public override string Description { get { return "Datei oder leeren Ordner löschen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string path = Target(args, shell, Access.Write);
            if (path == null) return;
            // Wie bei mv: Ohne die Nutzerdatei begänne der nächste Start mit der Ersteinrichtung.
            if (Vfs.IsFixed(path)) { shell.Error("Das braucht das System an seinem Platz: " + path); return; }
            if (File.Exists(path)) File.Delete(path);
            else if (!Directory.Exists(path)) shell.Error("Nicht gefunden: " + path);
            else if (Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length > 0) shell.Error("Ordner ist nicht leer: " + path);
            // Sonst stünde die Shell danach in einem Ordner, den es nicht mehr gibt.
            else if (string.Equals(path, shell.CurrentDirectory, StringComparison.OrdinalIgnoreCase)) shell.Error("Das ist der aktuelle Ordner. Erst mit 'cd ..' hinausgehen.");
            else Directory.Delete(path);
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
        public override string Usage { get { return "cp <datei> <ziel>"; } }
        public override string Description { get { return "Datei kopieren"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string from, to;
            if (!Paths(args, shell, Access.Read, out from, out to)) return;
            if (Directory.Exists(from)) { shell.Error("Ordner kopiert cp nicht, nur Dateien."); return; }
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
}
