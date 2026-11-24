using System.Collections.Generic;
using System.IO;
using VibanOS.Core;
using VibanOS.Users;

namespace VibanOS.FileSystem
{
    public enum Access { Read, Write, Execute }

    /// <summary>
    /// Zugang zum Dateisystem (VIB-2). Gelesen und geschrieben wird mit den normalen .NET-Klassen
    /// File und Directory, diese Klasse kümmert sich um Pfade und um die Rechteprüfung.
    /// </summary>
    public static class Vfs
    {
        /// <summary>false, wenn keine formatierte Festplatte gefunden wurde. Dateibefehle melden das dann.</summary>
        public static bool Available;

        /// <summary>Wurzelpfad mit abschließendem Trenner, unter COSMOS "0:\".</summary>
        public static string Root = "";

        public static char Separator = '\\';

        public static string SystemDirectory { get { return Root + "system"; } }

        /// <summary>Nach dem Registrieren des COSMOS-VFS aufrufen (Kernel.BeforeRun).</summary>
        public static void Init(string root)
        {
            Root = root;
            Separator = root[root.Length - 1];
            try
            {
                Available = Directory.Exists(root);
                if (!Available) return;
                if (!Directory.Exists(SystemDirectory)) Directory.CreateDirectory(SystemDirectory);
                if (!Directory.Exists(Root + "home")) Directory.CreateDirectory(Root + "home");
            }
            catch
            {
                Available = false;
            }
        }

        /// <summary>
        /// Macht aus dem aktuellen Verzeichnis und einer Eingabe einen vollständigen Pfad.
        /// Versteht "..", ".", beide Trenner und Pfade ab der Wurzel. Über die Wurzel hinaus geht es nie.
        /// </summary>
        public static string Resolve(string current, string path)
        {
            List<string> parts = new List<string>();
            bool absolute = path.Length > 0 && (path[0] == '/' || path[0] == '\\');
            if (Text.HasPrefix(path, Root))
            {
                absolute = true;
                path = path.Substring(Root.Length);
            }
            if (!absolute && current.Length > Root.Length) AddParts(parts, current.Substring(Root.Length));
            AddParts(parts, path);

            string result = Root;
            for (int i = 0; i < parts.Count; i++)
                result += (i > 0 ? Separator.ToString() : "") + parts[i];
            return result;
        }

        private static void AddParts(List<string> parts, string path)
        {
            string part = "";
            for (int i = 0; i <= path.Length; i++)
            {
                if (i < path.Length && path[i] != '/' && path[i] != '\\')
                {
                    part += path[i];
                    continue;
                }
                if (part == "..") { if (parts.Count > 0) parts.RemoveAt(parts.Count - 1); }
                else if (part != "" && part != ".") parts.Add(part);
                part = "";
            }
        }

        /// <summary>Letzter Teil eines Pfads. Nötig, weil Directory.GetFiles je nach System Namen oder ganze Pfade liefert.</summary>
        public static string NameOf(string path)
        {
            int cut = path.LastIndexOf('/');
            int other = path.LastIndexOf('\\');
            if (other > cut) cut = other;
            return path.Substring(cut + 1);
        }

        /// <summary>
        /// Zentrale Rechteprüfung für Dateien. Alle Dateibefehle fragen hier nach.
        /// ponytail: erlaubt bisher alles. VIB-18 ergänzt Besitzer, Gruppe und rwx-Rechte
        /// aus system\permissions.txt an genau dieser Stelle.
        /// </summary>
        public static bool CanAccess(User user, string path, Access access)
        {
            return true;
        }
    }
}
