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
    /// </summary>
    // ponytail: gilt bis zum Ausschalten. Speichern erst, wenn jemand es vermisst, dann als Datei je Nutzer in home/<name>/.
    public static class Settings
    {
        /// <summary>Uhr in der Menüleiste mit Sekunden.</summary>
        public static bool ShowSeconds;

        /// <summary>Mauszeiger in doppelter Größe.</summary>
        public static bool LargeCursor;

        /// <summary>true: deutsche Tastaturbelegung, false: US.</summary>
        public static bool GermanKeyboard = true;
    }
}
