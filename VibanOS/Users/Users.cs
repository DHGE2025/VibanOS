using System.Collections.Generic;

namespace VibanOS.Users
{
    /// <summary>Systemrechte, die an Gruppen vergeben werden (VIB-17).</summary>
    public enum Permission { None, UserManage, Shutdown, DriverManage, ProcessKillAny, ChangePermissions }

    /// <summary>
    /// Eine Gruppe mit ihren Systemrechten. Wer Mitglied ist, steht beim Nutzer (User.Groups) und nicht
    /// hier, damit es nur eine Stelle gibt, die stimmen muss. UserManager.Members liefert die Liste.
    /// </summary>
    public class Group
    {
        public int Id;
        public string Name = "";
        public readonly List<Permission> Permissions = new List<Permission>();
    }

    public class User
    {
        public int Id;
        public string Name = "";
        public string PasswordHash = "";
        public string Salt = "";
        public string HomeDirectory = "";
        public readonly List<string> Groups = new List<string>();
    }
}
