using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Users;

namespace VibanOS.Cli.Commands
{
    public class WhoamiCommand : Command
    {
        public override string Name { get { return "whoami"; } }
        public override string Description { get { return "Angemeldeten Nutzer anzeigen"; } }

        public override void Execute(string[] args, Shell shell) { shell.WriteLine(OS.Users.Current.Name); }
    }

    public class UsersCommand : Command
    {
        public override string Name { get { return "users"; } }
        public override string Description { get { return "Alle Nutzer auflisten"; } }

        public override void Execute(string[] args, Shell shell)
        {
            for (int i = 0; i < OS.Users.All.Count; i++)
            {
                User u = OS.Users.All[i];
                string groups = "";
                for (int g = 0; g < u.Groups.Count; g++) groups += (g > 0 ? ", " : "") + u.Groups[g];
                shell.WriteLine(Text.Pad(u.Id.ToString(), 4) + Text.Pad(u.Name, 18) + groups);
            }
        }
    }

    /// <summary>Basis der Befehle, die Nutzer ändern: meldet, wenn die Änderung nicht gespeichert werden konnte.</summary>
    public abstract class UserChangeCommand : Command
    {
        protected static void Done(Shell shell, string message)
        {
            shell.WriteLine(message);
            if (!OS.Users.Saved) shell.Error("Achtung: Die Nutzerdatei ließ sich nicht schreiben. Die Änderung gilt nur bis zum Neustart.");
        }
    }

    public class UseraddCommand : UserChangeCommand
    {
        public override string Name { get { return "useradd"; } }
        public override string Usage { get { return "useradd <name>"; } }
        public override string Description { get { return "Nutzer anlegen"; } }
        public override Permission Required { get { return Permission.UserManage; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 0) { shell.Error("Aufruf: " + Usage); return; }
            string name = args[0];
            // Erst prüfen, dann nach dem Passwort fragen: Sonst tippt man es für einen Namen ein, den es nie geben wird.
            if (!UserManager.ValidName(name)) { shell.Error("Ungültiger Name. Erlaubt sind a-z, 0-9, - und _, höchstens 16 Zeichen."); return; }
            if (OS.Users.Find(name) != null) { shell.Error("Den Nutzer " + name + " gibt es schon."); return; }
            shell.Ask("Passwort für " + name + ": ", true, password =>
            {
                if (password == "") { shell.Error("Abgebrochen: Das Passwort darf nicht leer sein."); return; }
                if (OS.Users.Add(name, password, UserManager.UserGroup) == null) shell.Error("Der Nutzer konnte nicht angelegt werden.");
                else Done(shell, "Nutzer " + name + " angelegt.");
            });
        }
    }

    public class UserdelCommand : UserChangeCommand
    {
        public override string Name { get { return "userdel"; } }
        public override string Usage { get { return "userdel <name>"; } }
        public override string Description { get { return "Nutzer löschen, sein Ordner bleibt"; } }
        public override Permission Required { get { return Permission.UserManage; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 0) { shell.Error("Aufruf: " + Usage); return; }
            User user = OS.Users.Find(args[0]);
            if (user == null) { shell.Error("Den Nutzer " + args[0] + " gibt es nicht."); return; }
            // Ohne root gäbe es keinen Administrator mehr, und beim nächsten Start begänne keine Ersteinrichtung.
            if (user.Name == UserManager.Root) { shell.Error("root lässt sich nicht löschen."); return; }
            if (user == OS.Users.Current) { shell.Error("Der angemeldete Nutzer kann sich nicht selbst löschen."); return; }
            shell.Ask("Nutzer " + user.Name + " löschen? Zum Bestätigen 'ja' eingeben: ", false, answer =>
            {
                if (answer != "ja") { shell.WriteLine("Abgebrochen."); return; }
                if (!OS.Users.Remove(user.Name)) { shell.Error("Der Nutzer konnte nicht gelöscht werden."); return; }
                // Wie userdel unter Unix: Die Dateien bleiben, ein Administrator räumt sie bei Bedarf selbst weg.
                Done(shell, "Nutzer " + user.Name + " gelöscht. Der Ordner " + user.HomeDirectory + " bleibt erhalten.");
            });
        }
    }

    public class SuCommand : Command
    {
        public override string Name { get { return "su"; } }
        public override string Usage { get { return "su [name]"; } }
        public override string Description { get { return "Nutzer wechseln, ohne Namen zu root"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string name = args.Length > 0 ? args[0] : UserManager.Root;
            User user = OS.Users.Find(name);
            if (user == null) { shell.Error("Den Nutzer " + name + " gibt es nicht."); return; }
            if (user == OS.Users.Current) { shell.WriteLine("Schon angemeldet als " + name + "."); return; }
            // Wie unter Unix braucht root kein Passwort. Ordner, Ausgaben und Verlauf bleiben, anders als bei login.
            if (OS.Users.Current.Name == UserManager.Root)
            {
                OS.Users.Current = user;
                OS.Changed = true;
                shell.WriteLine("Jetzt angemeldet als " + name + ".");
                return;
            }
            shell.Ask("Passwort für " + name + ": ", true, password =>
            {
                if (!OS.Users.Login(name, password)) { shell.Error("Falsches Passwort."); return; }
                shell.WriteLine("Jetzt angemeldet als " + name + ".");
            });
        }
    }

    public class LoginCommand : Command
    {
        public override string Name { get { return "login"; } }
        public override string Usage { get { return "login [name]"; } }
        public override string Description { get { return "Als anderer Nutzer neu anmelden"; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length > 0) { AskPassword(args[0], shell); return; }
            shell.Ask("Nutzername: ", false, name =>
            {
                if (name.Trim() == "") { shell.WriteLine("Abgebrochen."); return; }
                AskPassword(name.Trim(), shell);
            });
        }

        // Fragt auch bei einem unbekannten Namen nach dem Passwort: So verrät login nicht, welche Nutzer es gibt.
        private static void AskPassword(string name, Shell shell)
        {
            shell.Ask("Passwort: ", true, password =>
            {
                if (!OS.Users.Login(name, password)) { shell.Error("Anmeldung fehlgeschlagen."); return; }
                // Eine neue Sitzung: Der Nutzer sieht weder Ausgaben noch Verlauf des vorherigen und beginnt zu Hause.
                User user = OS.Users.Current;
                shell.Clear();
                shell.History.Clear();
                bool home = Vfs.Available && Directory.Exists(user.HomeDirectory);
                shell.CurrentDirectory = home ? user.HomeDirectory : Vfs.Root;
                shell.WriteLine("Angemeldet als " + user.Name + ".");
            });
        }
    }

    public class PasswdCommand : UserChangeCommand
    {
        public override string Name { get { return "passwd"; } }
        public override string Description { get { return "Eigenes Passwort ändern"; } }

        public override void Execute(string[] args, Shell shell)
        {
            User user = OS.Users.Current;
            shell.Ask("Altes Passwort: ", true, old =>
            {
                if (!OS.Users.CheckPassword(user, old)) { shell.Error("Falsches Passwort."); return; }
                shell.Ask("Neues Passwort: ", true, fresh =>
                {
                    if (fresh == "") { shell.Error("Abgebrochen: Das Passwort darf nicht leer sein."); return; }
                    OS.Users.SetPassword(user, fresh);
                    Done(shell, "Passwort geändert.");
                });
            });
        }
    }

    public class LogoutCommand : Command
    {
        public override string Name { get { return "logout"; } }
        public override string Description { get { return "Abmelden und zum Anmeldebildschirm"; } }

        public override void Execute(string[] args, Shell shell)
        {
            // Der nächste Nutzer soll weder Ausgaben noch Verlauf des vorherigen sehen.
            shell.Clear();
            shell.History.Clear();
            OS.Users.Logout();
        }
    }
}
