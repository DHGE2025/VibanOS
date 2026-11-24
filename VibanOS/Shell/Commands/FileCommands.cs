using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;

namespace VibanOS.Cli.Commands
{
    /// <summary>Gemeinsame Basis der Dateibefehle (VIB-2): Pfad auflösen, Dateisystem und Rechte prüfen.</summary>
    public abstract class FileCommand : Command
    {
        /// <summary>
        /// Löst args[0] zu einem vollständigen Pfad auf und prüft das Recht. Gibt null zurück und
        /// meldet den Grund im Terminal, wenn etwas fehlt.
        /// </summary>
        protected string Target(string[] args, Shell shell, Access access)
        {
            if (!Vfs.Available)
            {
                shell.Error("Kein Dateisystem gefunden. Eine FAT32-Festplatte an die VM hängen (siehe README).");
                return null;
            }
            if (args.Length == 0 && Usage != Name)
            {
                shell.Error("Aufruf: " + Usage);
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
            string path = Target(args, shell, Access.Write);
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
            if (File.Exists(path)) File.Delete(path);
            else if (!Directory.Exists(path)) shell.Error("Nicht gefunden: " + path);
            else if (Directory.GetFiles(path).Length + Directory.GetDirectories(path).Length > 0) shell.Error("Ordner ist nicht leer: " + path);
            else Directory.Delete(path);
        }
    }
}
