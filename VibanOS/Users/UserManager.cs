using System;
using System.Collections.Generic;
using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;

namespace VibanOS.Users
{
    /// <summary>
    /// Nutzer, Anmeldung (VIB-16) und Gruppenrechte (VIB-17). Nutzer liegen in system\users.txt,
    /// eine Zeile pro Nutzer: id;name;salt;hash;home;gruppe1,gruppe2
    /// </summary>
    public class UserManager
    {
        public const string Root = "root";
        public const string AdminGroup = "admin";
        public const string UserGroup = "users";

        public readonly List<User> All = new List<User>();
        public readonly List<Group> Groups = new List<Group>();

        /// <summary>Angemeldeter Nutzer, null solange niemand angemeldet ist.</summary>
        public User Current;

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
        public bool NeedsSetup { get { return Find(Root).PasswordHash == ""; } }

        private static string FilePath { get { return Vfs.SystemDirectory + Vfs.Separator + "users.txt"; } }

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
            if (password != "") SetPassword(u, password);
            try
            {
                if (Vfs.Available && !Directory.Exists(u.HomeDirectory)) Directory.CreateDirectory(u.HomeDirectory);
            }
            catch { }
            Save();
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

        /// <summary>root darf alles, sonst entscheidet die Summe der Gruppenrechte.</summary>
        public bool HasPermission(User user, Permission permission)
        {
            if (permission == Permission.None) return true;
            if (user == null) return false;
            if (user.Name == Root) return true;
            for (int g = 0; g < Groups.Count; g++)
            {
                if (!InGroup(user, Groups[g].Name)) continue;
                for (int p = 0; p < Groups[g].Permissions.Count; p++)
                    if (Groups[g].Permissions[p] == permission) return true;
            }
            return false;
        }

        public static bool InGroup(User user, string group)
        {
            for (int i = 0; i < user.Groups.Count; i++)
                if (user.Groups[i] == group) return true;
            return false;
        }

        private static bool ValidName(string name)
        {
            if (name.Length == 0 || name.Length > 16) return false;
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
        /// ponytail: eigener FNV-1a-Hash mit 4096 Runden, weil System.Security.Cryptography unter COSMOS
        /// nicht verlässlich läuft. Nicht kryptografisch sicher. Auf SHA-256 umstellen, sobald das geht.
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
            }
            return Text.Hex(h, 16);
        }

        /// <summary>Liest die Nutzerdatei. Gibt es keine, entsteht root ohne Passwort (Ersteinrichtung).</summary>
        public void Load()
        {
            try
            {
                if (Vfs.Available && File.Exists(FilePath))
                {
                    string[] lines = File.ReadAllText(FilePath).Split('\n');
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] f = lines[i].Trim().Split(';');
                        if (f.Length < 6) continue;
                        User u = new User();
                        u.Id = int.Parse(f[0]);
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
            catch { }
            if (Find(Root) == null) Add(Root, "", AdminGroup);
        }

        public void Save()
        {
            if (!Vfs.Available) return;
            string text = "";
            for (int i = 0; i < All.Count; i++)
            {
                User u = All[i];
                string groups = "";
                for (int g = 0; g < u.Groups.Count; g++) groups += (g > 0 ? "," : "") + u.Groups[g];
                text += u.Id + ";" + u.Name + ";" + u.Salt + ";" + u.PasswordHash + ";" + u.HomeDirectory + ";" + groups + "\n";
            }
            try { File.WriteAllText(FilePath, text); } catch { }
        }
    }
}
