using System;
using System.IO;
using VibanOS.FileSystem;
using VibanOS.Platform;
using VibanOS.Users;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Core
{
    /// <summary>Eine Bildschirmauflösung in Pixeln.</summary>
    public struct Resolution
    {
        public int Width, Height;

        public Resolution(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Einstellungen, die die Oberfläche zur Laufzeit liest. Die Einstellungen-App setzt sie.
    /// Die Akzentfarbe steht direkt in Tokens.Color.Accent, die Auflösung kennt nur der Bildschirm selbst.
    /// Jeder Nutzer hat seine eigenen, gespeichert in home/name/settings.txt als Zeilen "schlüssel=wert".
    /// </summary>
    public static class Settings
    {
        /// <summary>Uhr in der Menüleiste mit Sekunden.</summary>
        public static bool ShowSeconds;

        /// <summary>Mauszeiger in doppelter Größe.</summary>
        public static bool LargeCursor;

        /// <summary>true: deutsche Tastaturbelegung, false: US.</summary>
        public static bool GermanKeyboard = true;

        public const string FileName = "settings.txt";

        private static string PathOf(User user) { return user.HomeDirectory + Vfs.Separator + FileName; }

        /// <summary>
        /// Nach der Anmeldung: die Einstellungen des Nutzers übernehmen. Was in der Datei fehlt oder nicht lesbar ist,
        /// steht auf dem Grundwert. Die Auflösung bleibt ohne Eintrag, wie sie ist.
        /// </summary>
        public static void Load(User user)
        {
            ShowSeconds = false;
            LargeCursor = false;
            GermanKeyboard = true;
            T.Color.Accent = T.Color.AccentBlue;
            int width = 0, height = 0;
            string text = "";
            try { if (Vfs.Available && File.Exists(PathOf(user))) text = File.ReadAllText(PathOf(user)); }
            catch (Exception) { }
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                int eq = line.IndexOf('=');
                if (eq < 0) continue;
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                if (key == "sekunden") ShowSeconds = value == "1";
                else if (key == "grosserzeiger") LargeCursor = value == "1";
                else if (key == "tastatur") GermanKeyboard = value != "us";
                else if (key == "akzent") { uint c; if (ParseHex(value, out c)) T.Color.Accent = c; }
                else if (key == "breite") int.TryParse(value, out width);
                else if (key == "hoehe") int.TryParse(value, out height);
            }
            Hardware.SetKeyboardLayout(GermanKeyboard);
            Resolution now = Hardware.ScreenSize();
            if (width > 0 && height > 0 && (width != now.Width || height != now.Height)) Hardware.SetScreenMode(width, height);
        }

        /// <summary>Schreibt die Einstellungen des angemeldeten Nutzers. false: keine Platte oder Schreibfehler, dann gelten sie bis zum Ausschalten.</summary>
        public static bool Save(User user)
        {
            if (user == null || !Vfs.Available) return false;
            Resolution screen = Hardware.ScreenSize();
            string text = "sekunden=" + (ShowSeconds ? "1" : "0") + "\n"
                + "grosserzeiger=" + (LargeCursor ? "1" : "0") + "\n"
                + "tastatur=" + (GermanKeyboard ? "de" : "us") + "\n"
                + "akzent=" + Text.Hex(T.Color.Accent, 8) + "\n"
                + "breite=" + screen.Width + "\n"
                + "hoehe=" + screen.Height + "\n";
            try
            {
                if (!Directory.Exists(user.HomeDirectory)) Directory.CreateDirectory(user.HomeDirectory);
                File.WriteAllText(PathOf(user), text);
                return true;
            }
            catch (Exception) { return false; }
        }

        private static bool ParseHex(string s, out uint value)
        {
            value = 0;
            if (s.Length == 0 || s.Length > 8) return false;
            for (int i = 0; i < s.Length; i++)
            {
                int d = "0123456789abcdef".IndexOf(char.ToLowerInvariant(s[i]));
                if (d < 0) return false;
                value = value * 16 + (uint)d;
            }
            return true;
        }
    }
}
