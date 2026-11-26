using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.Users;

namespace VibanOS.Cli.Commands
{
    // Gruppen, Rechte und sudo (VIB-17).

    public class GroupsCommand : Command
    {
        public override string Name { get { return "groups"; } }
        public override string Usage { get { return "groups [nutzer]"; } }
        public override string Description { get { return "Gruppen eines Nutzers anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            User user = args.Length > 0 ? OS.Users.Find(args[0]) : OS.Users.Current;
            if (user == null) { shell.Error("Den Nutzer " + args[0] + " gibt es nicht."); return; }
            if (user.Groups.Count == 0) { shell.WriteLine(user.Name + " ist in keiner Gruppe."); return; }
            string groups = "";
            for (int i = 0; i < user.Groups.Count; i++) groups += (i > 0 ? " " : "") + user.Groups[i];
            shell.WriteLine(groups);
        }
    }

    public class GroupaddCommand : UserChangeCommand
    {
        public override string Name { get { return "groupadd"; } }
        public override string Usage { get { return "groupadd <name>"; } }
        public override string Description { get { return "Gruppe anlegen"; } }
        public override Permission Required { get { return Permission.ChangePermissions; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 0) { shell.Error("Aufruf: " + Usage); return; }
            string name = args[0];
            if (!UserManager.ValidName(name)) { shell.Error("Ungültiger Name. Erlaubt sind a-z, 0-9, - und _, höchstens 16 Zeichen."); return; }
            if (OS.Users.FindGroup(name) != null) { shell.Error("Die Gruppe " + name + " gibt es schon."); return; }
            OS.Users.AddGroup(name);
            Done(shell, "Gruppe " + name + " angelegt.");
        }
    }

    public class GroupdelCommand : UserChangeCommand
    {
        public override string Name { get { return "groupdel"; } }
        public override string Usage { get { return "groupdel <name>"; } }
        public override string Description { get { return "Gruppe löschen"; } }
        public override Permission Required { get { return Permission.ChangePermissions; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 0) { shell.Error("Aufruf: " + Usage); return; }
            string name = args[0];
            if (OS.Users.FindGroup(name) == null) { shell.Error("Die Gruppe " + name + " gibt es nicht."); return; }
            // Ohne admin hätte niemand außer root mehr Rechte, ohne users fehlte neuen Nutzern ihre Gruppe.
            if (UserManager.IsBuiltIn(name)) { shell.Error("Die Gruppe " + name + " gehört zum System und bleibt."); return; }
            int members = OS.Users.Members(name).Count;
            OS.Users.RemoveGroup(name);
            Done(shell, "Gruppe " + name + " gelöscht" + (members > 0 ? ", " + members + " Mitglied" + (members == 1 ? "" : "er") + " ausgetragen." : "."));
        }
    }

    public class UsermodCommand : UserChangeCommand
    {
        public override string Name { get { return "usermod"; } }
        public override string Usage { get { return "usermod -aG|-rG <g> <n>"; } }
        public override string Description { get { return "Nutzer n in Gruppe g aufnehmen (-aG) oder entfernen (-rG)"; } }
        public override Permission Required { get { return Permission.ChangePermissions; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length != 3 || (args[0] != "-aG" && args[0] != "-rG"))
            {
                shell.Error("Aufruf: usermod -aG <gruppe> <nutzer> oder usermod -rG <gruppe> <nutzer>");
                return;
            }
            bool add = args[0] == "-aG";
            string group = args[1];
            User user = OS.Users.Find(args[2]);
            if (OS.Users.FindGroup(group) == null) { shell.Error("Die Gruppe " + group + " gibt es nicht."); return; }
            if (user == null) { shell.Error("Den Nutzer " + args[2] + " gibt es nicht."); return; }
            if (add)
            {
                if (!OS.Users.AddToGroup(user, group)) { shell.WriteLine(user.Name + " ist schon in " + group + "."); return; }
                Done(shell, user.Name + " ist jetzt in " + group + ".");
                return;
            }
            if (!UserManager.InGroup(user, group)) { shell.WriteLine(user.Name + " ist nicht in " + group + "."); return; }
            if (!OS.Users.RemoveFromGroup(user, group)) { shell.Error("root bleibt immer in " + UserManager.AdminGroup + "."); return; }
            Done(shell, user.Name + " ist nicht mehr in " + group + ".");
        }
    }

    public class PermsCommand : Command
    {
        public override string Name { get { return "perms"; } }
        public override string Usage { get { return "perms [nutzer]"; } }
        public override string Description { get { return "Systemrechte eines Nutzers anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            User user = args.Length > 0 ? OS.Users.Find(args[0]) : OS.Users.Current;
            if (user == null) { shell.Error("Den Nutzer " + args[0] + " gibt es nicht."); return; }
            List<string> rows = new List<string>();
            for (int i = 0; i < UserManager.AllPermissions.Length; i++)
            {
                Permission p = UserManager.AllPermissions[i];
                string from = OS.Users.GrantedBy(user, p);
                if (from != null) rows.Add(Text.Pad(UserManager.PermissionName(p), 20) + "über " + from);
            }
            if (rows.Count == 0) { shell.WriteLine(user.Name + " hat keine Systemrechte."); return; }
            shell.WriteLine("RECHT               HERKUNFT", LineStyle.Muted);
            for (int i = 0; i < rows.Count; i++) shell.WriteLine(rows[i]);
            if (user.Name != UserManager.Root) shell.WriteLine("Diese Rechte gelten nur mit sudo.", LineStyle.Muted);
        }
    }

    public class SudoCommand : Command
    {
        public override string Name { get { return "sudo"; } }
        public override string Usage { get { return "sudo <befehl>"; } }
        public override string Description { get { return "Befehl mit Admin-Rechten ausführen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 0) { shell.Error("Aufruf: " + Usage); return; }
            User user = OS.Users.Current;
            // Erst prüfen, dann nach dem Passwort fragen, wie bei useradd.
            if (shell.Find(args[0].ToLower()) == null) { shell.Error("Unbekannter Befehl: " + args[0] + ". 'help' zeigt alle Befehle."); return; }
            if (user.Name == UserManager.Root) { shell.Run(args); return; }
            if (!UserManager.InGroup(user, UserManager.AdminGroup))
            {
                shell.Error(user.Name + " ist nicht in der Gruppe " + UserManager.AdminGroup + " und darf sudo nicht benutzen.");
                return;
            }
            // Jedes Mal neu nach dem Passwort fragen: Ein offenes Terminal allein soll keine Admin-Rechte geben.
            shell.Ask("[sudo] Passwort für " + user.Name + ": ", true, password =>
            {
                // Wer während der Frage abgemeldet oder gewechselt hat, bekommt keine Rechte des anderen.
                if (OS.Users.Current != user || !OS.Users.CheckPassword(user, password)) { shell.Error("Falsches Passwort."); return; }
                OS.Users.Elevated = true;
                try { shell.Run(args); }
                finally { OS.Users.Elevated = false; }
            });
        }
    }
}
