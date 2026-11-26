using System;
using System.Collections.Generic;
using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;

namespace VibanOS.Users
{
    /// <summary>
    /// Nutzer, Anmeldung (VIB-16) und Gruppenrechte (VIB-17). Nutzer liegen in system/users.txt,
    /// eine Zeile pro Nutzer: id;name;salt;hash;home;gruppe1,gruppe2
    /// Gruppen liegen in system/groups.txt, eine Zeile pro Gruppe: id;name;recht1,recht2
    /// </summary>
    public class UserManager
    {
        public const string Root = "root";
        public const string AdminGroup = "admin";
        public const string UserGroup = "users";
        public const int MaxNameLength = 16;

        public readonly List<User> All = new List<User>();
        public readonly List<Group> Groups = new List<Group>();

        /// <summary>Angemeldeter Nutzer, null solange niemand angemeldet ist.</summary>
        public User Current;

        /// <summary>false: Die letzte Änderung steht nicht in der Nutzerdatei und gilt nur bis zum Neustart.</summary>
        public bool Saved = true;

        /// <summary>
        /// true nur, solange sudo einen Befehl ausführt. Dann gelten für den angemeldeten Nutzer die Rechte
        /// seiner Gruppen. Sonst hat nur root Systemrechte, wie unter Unix und macOS.
        /// </summary>
        public bool Elevated;

        // Die Gruppendatei war da, ließ sich aber nicht lesen. Dann wird sie nicht überschrieben.
        private bool groupsUnreadable;

        /// <summary>Alle Systemrechte außer None, in der Reihenfolge für perms.</summary>
        public static readonly Permission[] AllPermissions =
        {
            Permission.UserManage, Permission.Shutdown, Permission.DriverManage, Permission.ProcessKillAny, Permission.ChangePermissions
        };

        /// <summary>
        /// Nicht null: Die Nutzerdatei war da, ließ sich aber nicht lesen (hier steht die Fehlermeldung).
        /// Dann wird sie nicht überschrieben, sonst wären mit einem Lesefehler alle Nutzer verloren.
        /// Anmelden kann sich in diesem Zustand niemand: Ein Lesefehler darf nicht zur Ersteinrichtung
        /// führen, bei der jeder das Passwort von root festlegen könnte.
        /// </summary>
        public string LoadError;

        public UserManager()
        {
            Group admin = new Group();
            admin.Id = 0;
            admin.Name = AdminGroup;
            admin.Permissions.Add(Permission.UserManage);
            admin.Permissions.Add(Permission.Shutdown);
            admin.Permissions.Add(Permission.DriverManage);
            admin.Permissions.Add(Permission.ProcessKillAny);
            admin.Permissions.Add(Permission.ChangePermissions);
            Groups.Add(admin);

            Group users = new Group();
            users.Id = 1;
            users.Name = UserGroup;
            Groups.Add(users);
        }

        /// <summary>true beim allerersten Start: root hat noch kein Passwort und muss eines vergeben.</summary>
        public bool NeedsSetup { get { return LoadError == null && Find(Root).PasswordHash == ""; } }

        private static string FilePath { get { return Vfs.SystemDirectory + Vfs.Separator + "users.txt"; } }

        private static string GroupPath { get { return Vfs.SystemDirectory + Vfs.Separator + "groups.txt"; } }

        public User Find(string name)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Name == name) return All[i];
            return null;
        }

        /// <summary>Legt einen Nutzer mit Home-Verzeichnis an. null, wenn der Name ungültig oder vergeben ist.</summary>
        public User Add(string name, string password, string group)
        {
            if (!ValidName(name) || Find(name) != null) return null;
            User u = new User();
            u.Id = All.Count == 0 ? 0 : All[All.Count - 1].Id + 1;
            u.Name = name;
            u.HomeDirectory = Vfs.Root + "home" + Vfs.Separator + name;
            u.Groups.Add(group);
            All.Add(u);
            try
            {
                if (Vfs.Available && !Directory.Exists(u.HomeDirectory)) Directory.CreateDirectory(u.HomeDirectory);
            }
            catch (Exception) { }
            if (password != "") SetPassword(u, password);
            else Save();
            return u;
        }

        public bool Remove(string name)
        {
            User u = Find(name);
            if (u == null || name == Root) return false;
            for (int i = 0; i < All.Count; i++)
                if (All[i] == u) { All.RemoveAt(i); break; }
            Save();
            return true;
        }

        public void SetPassword(User user, string password)
        {
            user.Salt = Text.Hex((ulong)new Random().Next() ^ (ulong)DateTime.Now.Ticks, 8);
            user.PasswordHash = Hash(password, user.Salt);
            Save();
        }

        public bool CheckPassword(User user, string password)
        {
            return user.PasswordHash != "" && user.PasswordHash == Hash(password, user.Salt);
        }

        public bool Login(string name, string password)
        {
            User u = Find(name);
            if (u == null || !CheckPassword(u, password)) return false;
            Current = u;
            OS.Changed = true;
            return true;
        }

        public void Logout()
        {
            Current = null;
            OS.Changed = true;
        }

        /// <summary>
        /// Rechte, die der Nutzer über seine Gruppen hat. root hat alle. Ob sie gerade gelten, sagt Allowed:
        /// Außer root braucht jeder dafür sudo.
        /// </summary>
        public bool HasPermission(User user, Permission permission)
        {
            return GrantedBy(user, permission) != null;
        }

        /// <summary>Name der Gruppe, die dem Nutzer das Recht gibt, "root" für root, null ohne das Recht.</summary>
        public string GrantedBy(User user, Permission permission)
        {
            if (permission == Permission.None) return "";
            if (user == null) return null;
            if (user.Name == Root) return Root;
            for (int g = 0; g < Groups.Count; g++)
            {
                if (!InGroup(user, Groups[g].Name)) continue;
                for (int p = 0; p < Groups[g].Permissions.Count; p++)
                    if (Groups[g].Permissions[p] == permission) return Groups[g].Name;
            }
            return null;
        }

        /// <summary>
        /// Darf der Nutzer das gerade? root immer, alle anderen nur mit dem Recht aus einer Gruppe und
        /// nur während sudo einen Befehl für sie ausführt (Elevated).
        /// </summary>
        public bool Allowed(User user, Permission permission)
        {
            if (!HasPermission(user, permission)) return false;
            return permission == Permission.None || user.Name == Root || (Elevated && user == Current);
        }

        public Group FindGroup(string name)
        {
            for (int i = 0; i < Groups.Count; i++)
                if (Groups[i].Name == name) return Groups[i];
            return null;
        }

        /// <summary>admin und users gibt es immer. Sie lassen sich nicht löschen.</summary>
        public static bool IsBuiltIn(string group) { return group == AdminGroup || group == UserGroup; }

        /// <summary>Legt eine Gruppe ohne Systemrechte an. null, wenn der Name ungültig oder vergeben ist.</summary>
        public Group AddGroup(string name)
        {
            if (!ValidName(name) || FindGroup(name) != null) return null;
            Group group = new Group();
            for (int i = 0; i < Groups.Count; i++)
                if (Groups[i].Id >= group.Id) group.Id = Groups[i].Id + 1;
            group.Name = name;
            Groups.Add(group);
            Save();
            return group;
        }

        /// <summary>Löscht eine Gruppe und nimmt alle Nutzer aus ihr heraus. Die Standardgruppen bleiben.</summary>
        public bool RemoveGroup(string name)
        {
            Group group = FindGroup(name);
            if (group == null || IsBuiltIn(name)) return false;
            Groups.Remove(group);
            for (int i = 0; i < All.Count; i++) All[i].Groups.Remove(name);
            Save();
            return true;
        }

        /// <summary>Nimmt den Nutzer in die Gruppe auf. false, wenn es die Gruppe nicht gibt oder er schon drin ist.</summary>
        public bool AddToGroup(User user, string group)
        {
            if (FindGroup(group) == null || InGroup(user, group)) return false;
            user.Groups.Add(group);
            Save();
            return true;
        }

        /// <summary>Nimmt den Nutzer aus der Gruppe. root bleibt immer in admin.</summary>
        public bool RemoveFromGroup(User user, string group)
        {
            if (!InGroup(user, group) || (user.Name == Root && group == AdminGroup)) return false;
            user.Groups.Remove(group);
            Save();
            return true;
        }

        /// <summary>Mitgliederliste einer Gruppe.</summary>
        public List<User> Members(string group)
        {
            List<User> members = new List<User>();
            for (int i = 0; i < All.Count; i++)
                if (InGroup(All[i], group)) members.Add(All[i]);
            return members;
        }

        /// <summary>Name eines Rechts für Anzeige und Gruppendatei. Von Hand statt ToString, ohne Reflection.</summary>
        public static string PermissionName(Permission permission)
        {
            switch (permission)
            {
                case Permission.UserManage: return "UserManage";
                case Permission.Shutdown: return "Shutdown";
                case Permission.DriverManage: return "DriverManage";
                case Permission.ProcessKillAny: return "ProcessKillAny";
                case Permission.ChangePermissions: return "ChangePermissions";
                default: return "None";
            }
        }

        public static bool InGroup(User user, string group)
        {
            for (int i = 0; i < user.Groups.Count; i++)
                if (user.Groups[i] == group) return true;
            return false;
        }

        /// <summary>Erlaubt sind a-z, 0-9, - und _, höchstens 16 Zeichen. Der Name wird auch Ordnername.</summary>
        public static bool ValidName(string name)
        {
            if (name.Length == 0 || name.Length > MaxNameLength) return false;
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Passwort-Hash mit Salt, im Klartext wird nie etwas gespeichert.
        /// ponytail: eigener FNV-1a-Hash mit 4096 Runden. System.Security.Cryptography stützt sich auf die
        /// Kryptobibliothek des Wirtssystems, die es im COSMOS-Kernel nicht gibt. Nicht kryptografisch
        /// sicher. Auf SHA-256 mit vielen Runden umstellen, sobald eine Implementierung im Kernel läuft.
        /// </summary>
        public static string Hash(string password, string salt)
        {
            string input = salt + ":" + password;
            ulong h = 14695981039346656037UL;
            for (int round = 0; round < 4096; round++)
            {
                for (int i = 0; i < input.Length; i++)
                {
                    h ^= input[i];
                    h *= 1099511628211UL;
                }
                h ^= (ulong)round;
                // Die Multiplikation trägt Unterschiede nur nach oben. Ohne diesen Schritt hingen die
                // unteren Bits nie von den oberen ab und wären am Ende für jedes Passwort gleich.
                h ^= h >> 29;
            }
            return Text.Hex(h, 16);
        }

        /// <summary>
        /// Liest die Nutzerdatei. Gibt es keine, entsteht root ohne Passwort (Ersteinrichtung).
        /// Kaputte Zeilen werden übersprungen, damit eine beschädigte Zeile nicht alle Nutzer aussperrt.
        /// </summary>
        public void Load()
        {
            try
            {
                // Wurde das Umbenennen in Save() unterbrochen, liegt der vollständige neue Stand noch unter .new.
                string path = FilePath;
                if (Vfs.Available && !File.Exists(path)) path += ".new";
                if (Vfs.Available && File.Exists(path))
                {
                    string[] lines = File.ReadAllText(path).Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] f = lines[i].Trim().Split(';');
                        int id;
                        if (f.Length < 6 || !int.TryParse(f[0], out id) || !ValidName(f[1]) || Find(f[1]) != null) continue;
                        User u = new User();
                        u.Id = id;
                        u.Name = f[1];
                        u.Salt = f[2];
                        u.PasswordHash = f[3];
                        u.HomeDirectory = f[4];
                        string[] groups = f[5].Split(',');
                        for (int g = 0; g < groups.Length; g++)
                            if (groups[g] != "") u.Groups.Add(groups[g]);
                        All.Add(u);
                    }
                }
            }
            catch (Exception e)
            {
                LoadError = e.Message ?? "";
            }
            LoadGroups();
            if (Find(Root) == null) Add(Root, "", AdminGroup);
        }

        /// <summary>
        /// Liest die Gruppendatei. Gibt es keine (etwa auf einer Platte von vor VIB-17), bleiben admin und users,
        /// und das nächste Speichern legt sie an. admin hat immer alle Rechte, egal was in der Datei steht:
        /// Sonst könnte eine kaputte Zeile alle Administratoren entmachten.
        /// </summary>
        private void LoadGroups()
        {
            try
            {
                string path = GroupPath;
                if (Vfs.Available && !File.Exists(path)) path += ".new";
                if (!Vfs.Available || !File.Exists(path)) return;
                string[] lines = File.ReadAllText(path).Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string[] f = lines[i].Trim().Split(';');
                    int id;
                    if (f.Length < 3 || !int.TryParse(f[0], out id) || !ValidName(f[1]) || f[1] == AdminGroup) continue;
                    Group group = FindGroup(f[1]);
                    if (group == null)
                    {
                        group = new Group();
                        group.Id = id;
                        group.Name = f[1];
                        Groups.Add(group);
                    }
                    group.Permissions.Clear();
                    string[] names = f[2].Split(',');
                    for (int n = 0; n < names.Length; n++)
                        for (int p = 0; p < AllPermissions.Length; p++)
                            if (names[n] == PermissionName(AllPermissions[p]) && !group.Permissions.Contains(AllPermissions[p]))
                                group.Permissions.Add(AllPermissions[p]);
                }
            }
            catch (Exception)
            {
                groupsUnreadable = true;
            }
        }

        /// <summary>Schreibt die Nutzerdatei. false: Es gibt kein Dateisystem oder das Schreiben ist gescheitert.</summary>
        public bool Save()
        {
            Saved = false;
            if (!Vfs.Available || LoadError != null) return false;
            string text = "";
            for (int i = 0; i < All.Count; i++)
            {
                User u = All[i];
                string groups = "";
                for (int g = 0; g < u.Groups.Count; g++) groups += (g > 0 ? "," : "") + u.Groups[g];
                text += u.Id + ";" + u.Name + ";" + u.Salt + ";" + u.PasswordHash + ";" + u.HomeDirectory + ";" + groups + "\n";
            }
            // Erst die Nutzer: Ihre Datei entscheidet, wer sich anmelden kann.
            if (!WriteSafely(FilePath, text)) return false;
            if (groupsUnreadable) return false;
            string groupText = "";
            for (int i = 0; i < Groups.Count; i++)
            {
                Group g = Groups[i];
                string perms = "";
                for (int p = 0; p < g.Permissions.Count; p++) perms += (p > 0 ? "," : "") + PermissionName(g.Permissions[p]);
                groupText += g.Id + ";" + g.Name + ";" + perms + "\n";
            }
            Saved = WriteSafely(GroupPath, groupText);
            return Saved;
        }

        /// <summary>
        /// Erst vollständig daneben schreiben, dann umbenennen. Scheitert das Schreiben (etwa bei voller
        /// Platte), bleibt die bisherige Datei unversehrt. Eine leere Nutzerdatei hieße beim nächsten
        /// Start Ersteinrichtung, also root ohne Passwort.
        /// </summary>
        private static bool WriteSafely(string path, string text)
        {
            try
            {
                File.WriteAllText(path + ".new", text);
                File.Move(path + ".new", path, true);
                return true;
            }
            catch (Exception) { return false; }
        }
    }
}
