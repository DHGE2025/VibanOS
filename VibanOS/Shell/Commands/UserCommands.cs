using VibanOS.Core;
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
