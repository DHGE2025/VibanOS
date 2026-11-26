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

        /// <summary>
        /// Der Administrator mit diesem Passwort oder null. Damit bestätigt ein Administrator am Anmeldebildschirm
        /// einen neuen Nutzer, ohne dass jemand angemeldet ist.
        /// </summary>
        public User FindAdmin(string password)
        {
            for (int i = 0; i < All.Count; i++)
                if (HasPermission(All[i], Permission.UserManage) && CheckPassword(All[i], password)) return All[i];
            return null;
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
            if (Find(Root) == null) Add(Root, "", AdminGroup);
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
            try
            {
                // Erst vollständig daneben schreiben, dann umbenennen. Scheitert das Schreiben (etwa bei voller
                // Platte), bleibt die bisherige Datei unversehrt. Eine leere Nutzerdatei hieße beim nächsten
                // Start Ersteinrichtung, also root ohne Passwort.
                File.WriteAllText(FilePath + ".new", text);
                File.Move(FilePath + ".new", FilePath, true);
                Saved = true;
            }
            catch (Exception) { }
            return Saved;
        }
    }
}
