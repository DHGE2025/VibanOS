using System;
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

        /// <summary>Wurzelpfad mit abschließendem Trenner, unter COSMOS "/mnt/".</summary>
        public static string Root = "";

        public static char Separator = '/';

        public static string SystemDirectory { get { return Root + "system"; } }

        /// <summary>Nach dem Einhängen der Festplatte aufrufen (Kernel.BeforeRun) und noch einmal nach 'format'.</summary>
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
            catch (Exception)
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
            // Die Wurzel gilt mit und ohne abschließenden Trenner: "/mnt" ist dasselbe wie "/mnt/".
            string root = Root.Substring(0, Root.Length - 1);
            if (Text.HasPrefix(path, root) && (path.Length == root.Length || path[root.Length] == '/' || path[root.Length] == '\\'))
            {
                absolute = true;
                path = path.Substring(root.Length);
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
                else
                {
                    // FAT kennt keine Namen, die auf Punkt oder Leerzeichen enden. Ohne das Abschneiden
                    // wäre "system." ein zweiter Name für den geschützten Ordner "system".
                    part = part.TrimEnd('.', ' ');
                    if (part != "") parts.Add(part);
                }
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

        /// <summary>Hängt einen Namen an einen Ordnerpfad. Nur die Wurzel endet schon mit dem Trenner.</summary>
        public static string Combine(string directory, string name)
        {
            return directory.Length > 0 && directory[directory.Length - 1] == Separator ? directory + name : directory + Separator + name;
        }

        /// <summary>
        /// Pfade, auf die sich das System verlässt: die Wurzel, der Ordner system samt Inhalt (die Nutzerdatei), home und
        /// die Home-Ordner der Nutzer. Verschoben fände sie niemand wieder. Ohne die Nutzerdatei begänne der nächste
        /// Start mit der Ersteinrichtung, und außerhalb von system könnte sie jeder lesen.
        /// </summary>
        public static bool IsFixed(string path)
        {
            string p = path.ToLowerInvariant(), system = SystemDirectory.ToLowerInvariant();
            if (p == Root.ToLowerInvariant() || p == (Root + "home").ToLowerInvariant() || p == system || Text.HasPrefix(p, system + Separator)) return true;
            // Nur die Ordner der Nutzer, nicht jeder Ordner, den jemand in home anlegt: Den würde sonst niemand wieder los.
            for (int i = 0; i < OS.Users.All.Count; i++)
                if (p == OS.Users.All[i].HomeDirectory.ToLowerInvariant()) return true;
            return false;
        }

        /// <summary>
        /// Liest höchstens max Bytes vom Anfang einer Datei als Text. Für Stellen, an denen nur der Anfang zählt und eine
        /// riesige Datei nicht den Arbeitsspeicher füllen darf. more: Die Datei geht danach noch weiter.
        /// </summary>
        public static string ReadStart(string path, int max, out bool more)
        {
            byte[] data = new byte[max];
            int count = 0;
            using (FileStream file = File.OpenRead(path))
            {
                // Ein einzelner Aufruf darf weniger liefern, als da ist. Erst 0 heißt Dateiende.
                int n;
                while (count < max && (n = file.Read(data, count, max - count)) > 0) count += n;
                more = count == max && file.ReadByte() >= 0;
            }
            // Wie File.ReadAllText: Die drei Bytes, mit denen manche Editoren UTF-8 kennzeichnen, gehören nicht zum Text.
            int start = count >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF ? 3 : 0;
            return System.Text.Encoding.UTF8.GetString(data, start, count - start);
        }

        /// <summary>
        /// Zentrale Rechteprüfung für Dateien. Alle Dateibefehle fragen hier nach.
        /// Der Systemordner mit der Nutzerdatei ist Administratoren vorbehalten, sonst könnte sich
        /// jeder Nutzer dort selbst zum Administrator machen.
        /// ponytail: Alles andere steht bisher jedem offen. VIB-18 ergänzt Besitzer, Gruppe und
        /// rwx-Rechte aus system/permissions.txt an genau dieser Stelle.
        /// </summary>
        public static bool CanAccess(User user, string path, Access access)
        {
            if (OS.Users.HasPermission(user, Permission.UserManage)) return true;
            // FAT gibt jedem langen Namen einen zweiten, kurzen (aus "system" wird SYSTEM~1) und findet
            // Dateien unter beiden. Die Prüfung unten kennt nur den langen Namen. Damit sie sich über den
            // kurzen nicht umgehen lässt, sind Namen mit ~ Administratoren vorbehalten.
            if (path.IndexOf('~') >= 0) return false;
            // FAT unterscheidet Groß- und Kleinschreibung nicht, deshalb hier auch nicht.
            string wanted = path.ToLowerInvariant(), system = SystemDirectory.ToLowerInvariant();
            return wanted != system && !Text.HasPrefix(wanted, system + Separator);
        }
    }
}
