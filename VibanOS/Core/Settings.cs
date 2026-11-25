using System;
using System.Collections.Generic;
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

        /// <summary>Die Auflösung, die der Nutzer in den Einstellungen gewählt hat. 0: keine Wahl, der Bildschirm bleibt, wie er ist.</summary>
        public static int Width, Height;

        /// <summary>Die Akzentfarben zur Wahl. Nur diese nimmt Load aus der Datei an.</summary>
        public static readonly uint[] Accents =
        {
            T.Color.AccentBlue, T.Color.AccentPurple, T.Color.AccentPink, T.Color.AccentOrange, T.Color.AccentGreen, T.Color.AccentGraphite
        };

        public const string FileName = "settings.txt";

        // Auswahl der Auflösungen: kleiner passen die Fenster nicht, größer wird das Zeichnen ohne Grafikbeschleunigung zäh.
        private const int MinWidth = 800, MinHeight = 600, MaxPixels = 1920 * 1200;

        // Mehr liest Load nicht. Die Datei ist winzig, und im Home-Ordner könnte jemand eine riesige ablegen.
        private const int MaxFileBytes = 1024;

        /// <summary>Eine Auflösung, mit der die Oberfläche etwas anfangen kann. Nur solche stehen zur Wahl und werden geladen.</summary>
        public static bool Usable(Resolution r)
        {
            return r.Width >= MinWidth && r.Height >= MinHeight && r.Width * r.Height <= MaxPixels;
        }

        private static string PathOf(User user) { return Vfs.Combine(user.HomeDirectory, FileName); }

        /// <summary>
        /// Nach der Anmeldung: die Einstellungen des Nutzers übernehmen. Was in der Datei fehlt oder nicht stimmt,
        /// steht auf dem Grundwert. Die Auflösung bleibt ohne Eintrag, wie sie ist. Nach der Abmeldung (user null)
        /// gelten die Grundwerte.
        /// Wirft nie: Im Home-Ordner darf bisher jeder schreiben, die Datei kann also beliebigen Inhalt haben, und
        /// eine Ausnahme hier landete bei der Anmeldung in der Kernel-Schleife.
        /// </summary>
        public static void Load(User user)
        {
            ShowSeconds = false;
            LargeCursor = false;
            GermanKeyboard = true;
            T.Color.Accent = T.Color.AccentBlue;
            Width = Height = 0;
            int width = 0, height = 0;
            try
            {
                bool more;
                string text = user != null && Vfs.Available && File.Exists(PathOf(user)) ? Vfs.ReadStart(PathOf(user), MaxFileBytes, out more) : "";
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
                    else if (key == "akzent") SetAccent(value);
                    else if (key == "breite") int.TryParse(value, out width);
                    else if (key == "hoehe") int.TryParse(value, out height);
                }
            }
            catch (Exception) { }
            try
            {
                // Auch nach einem Lesefehler: Sonst bliebe die Belegung des Vorgängers an der Tastatur.
                Hardware.SetKeyboardLayout(GermanKeyboard);

                // Nur eine Auflösung, die die Grafikkarte jetzt anbietet und die auch die Einstellungen zur Wahl stellen.
                // Eine andere Zahl aus der Datei erreicht die Grafikkarte nie. Ohne Eintrag wird sie gar nicht erst gefragt.
                if (width <= 0 || height <= 0) return;
                List<Resolution> modes = Hardware.ScreenModes();
                for (int i = 0; i < modes.Count; i++)
                {
                    if (modes[i].Width != width || modes[i].Height != height || !Usable(modes[i])) continue;
                    Resolution now = Hardware.ScreenSize();
                    if ((now.Width != width || now.Height != height) && !Hardware.SetScreenMode(width, height)) return;
                    Width = width;
                    Height = height;
                    return;
                }
            }
            catch (Exception) { }
        }

        /// <summary>Schreibt die Einstellungen des Nutzers. false: keine Platte oder Schreibfehler, dann gelten sie nur bis zur Abmeldung.</summary>
        public static bool Save(User user)
        {
            if (user == null || !Vfs.Available) return false;
            string text = "sekunden=" + (ShowSeconds ? "1" : "0") + "\n"
                + "grosserzeiger=" + (LargeCursor ? "1" : "0") + "\n"
                + "tastatur=" + (GermanKeyboard ? "de" : "us") + "\n"
                + "akzent=" + Text.Hex(T.Color.Accent, 8) + "\n";
            if (Width > 0) text += "breite=" + Width + "\n" + "hoehe=" + Height + "\n";
            try
            {
                if (!Directory.Exists(user.HomeDirectory)) Directory.CreateDirectory(user.HomeDirectory);
                File.WriteAllText(PathOf(user), text);
                return true;
            }
            catch (Exception) { return false; }
        }

        // Nimmt eine Akzentfarbe als acht Hex-Ziffern an, aber nur eine der Farben zur Wahl.
        private static void SetAccent(string hex)
        {
            for (int i = 0; i < Accents.Length; i++)
                if (Text.Hex(Accents[i], 8) == hex.ToLowerInvariant()) T.Color.Accent = Accents[i];
        }
    }
}
