namespace VibanOS.Design
{
    /// <summary>
    /// Design-Tokens von VibanOS. Einzige Quelle für Farben, Abstände, Radien und Größen im Code.
    /// Die Werte spiegeln docs/design/designsheet.md. Wer hier etwas ändert, ändert es auch dort.
    /// Regel: In Gui/ stehen keine rohen Farb- oder Pixelwerte, nur Tokens.
    /// </summary>
    public static class Tokens
    {
        /// <summary>Farben als 0xAARRGGBB.</summary>
        public static class Color
        {
            // Akzent und Status. Accent ist die einzige veränderliche Farbe: Die Einstellungen setzen eine der Akzentfarben darunter.
            public static uint Accent = AccentBlue;
            public const uint AccentBlue = 0xFF0A84FF;
            public const uint AccentPurple = 0xFFBF5AF2;
            public const uint AccentPink = 0xFFFF375F;
            public const uint AccentOrange = 0xFFFF9F0A;
            public const uint AccentGreen = 0xFF32D74B;
            public const uint AccentGraphite = 0xFF8E8E93;
            public const uint Success = 0xFF30D158;
            public const uint Warning = 0xFFFFD60A;
            public const uint Danger = 0xFFFF453A;

            // Flächen (dunkles Erscheinungsbild)
            public const uint Window = 0xFF1E1E20;
            public const uint TitleBar = 0xFF2C2C2F;
            public const uint Sunken = 0xFF161618;       // Terminal, Eingabefelder
            public const uint Raised = 0xFF3A3A3D;       // Kacheln, Tabellenkopf
            public const uint Grouped = 0xFF2A2A2D;      // Gruppen in den Einstellungen
            public const uint Track = 0xFF48484C;        // ausgeschalteter Schalter
            public const uint MenuBar = 0x8C161618;      // durchscheinend
            public const uint Dock = 0x73303034;         // durchscheinend
            public const uint Scrim = 0x66000000;        // Abdunklung hinter dem Login
            public const uint Border = 0x24FFFFFF;
            public const uint Separator = 0x14FFFFFF;

            // Text
            public const uint TextPrimary = 0xFFF5F5F7;
            public const uint TextSecondary = 0xA6EBEBF5;
            public const uint TextTertiary = 0x59EBEBF5;
            public const uint TextOnAccent = 0xFFFFFFFF;

            // Fensterknöpfe
            public const uint Close = 0xFFFF5F57;
            public const uint Minimize = 0xFFFEBC2E;
            public const uint Zoom = 0xFF28C840;
            public const uint ControlInactive = 0xFF4E4E52;

            // Hintergrundbild (Verlauf)
            public const uint WallBase1 = 0xFF140F3D;
            public const uint WallBase2 = 0xFF2A1B6B;
            public const uint WallBlob1 = 0xFF7B3FE4;
            public const uint WallBlob2 = 0xFFE4477E;
            public const uint WallBlob3 = 0xFFFF9552;
            public const uint WallBlob4 = 0xFF2A7BEA;

            // Symbole der App Dateien
            public const uint Folder = 0xFF5AB4F5;       // Ordner, vorn
            public const uint FolderBack = 0xFF3A8FDB;   // Ordner, Reiter und Rückseite

            public const uint Shadow = 0xFF000000;
            public const uint CursorFill = 0xFF111113;
            public const uint CursorEdge = 0xFFFFFFFF;
        }

        /// <summary>Abstände im 4-px-Raster.</summary>
        public static class Space
        {
            public const int XS = 4;
            public const int S = 8;
            public const int M = 12;
            public const int L = 16;
            public const int XL = 24;
            public const int XXL = 32;
        }

        /// <summary>Eckenradien.</summary>
        public static class Radius
        {
            public const int XS = 2;    // Kleinteile: Balken, Kästchen der Speicherkarte
            public const int S = 6;     // Knöpfe, Farbfelder
            public const int M = 10;    // Fenster, Kacheln
            public const int L = 18;    // Dock
            public const int Icon = 11; // App-Symbole (48 px)
        }

        /// <summary>Feste Maße der Oberfläche.</summary>
        public static class Size
        {
            // Entwurfsgröße: Wunschauflösung in Bootloader/limine.conf und Größe der Vorschaubilder.
            // Zur Laufzeit zählt die echte Bildschirmgröße, Kernel.cs fragt sie ab.
            public const int ScreenWidth = 1280;
            public const int ScreenHeight = 720;
            public const int MenuBar = 28;
            public const int TitleBar = 34;
            public const int Control = 12;      // Durchmesser der Fensterknöpfe
            public const int ControlGap = 8;
            public const int DockIcon = 48;
            public const int DockIndicator = 4;
            public const int Field = 30;        // Höhe von Eingabefeldern und Knöpfen
            public const int Caret = 2;         // Breite der Einfügemarke im Eingabefeld
            public const int Row = 24;          // Zeilenhöhe in Listen
            public const int Avatar = 88;
            public const int AvatarSmall = 64; // Nutzerbild in der Nutzerwahl
            public const int Sidebar = 180;     // Seitenleiste der Einstellungen und der App Dateien
            public const int ToggleWidth = 38;  // Schalter an/aus
            public const int ToggleHeight = 22;
            public const int Swatch = 20;       // Farbpunkt der Akzentfarbe
            public const int CursorScale = 2;   // Vergrößerung des großen Mauszeigers
        }

        /// <summary>Schatten: Unschärfe, Versatz nach unten, Deckkraft (0–255).</summary>
        public static class Shadow
        {
            public const int WindowBlur = 30;
            public const int WindowOffset = 12;
            public const int WindowAlpha = 130;
            public const int WindowAlphaInactive = 70;
            public const int DockBlur = 20;
            public const int DockOffset = 6;
            public const int DockAlpha = 80;
        }

        /// <summary>Bewegung: Dauer der Fenster-Animationen in Millisekunden und ihre Eckwerte.</summary>
        public static class Motion
        {
            public const int Open = 200;        // Fenster erscheint
            public const int Close = 150;       // Fenster verschwindet
            public const int Minimize = 300;    // ins Dock und zurück
            public const int Zoom = 250;        // grüner Knopf
            public const int StartScale = 90;   // Größe in Prozent am Anfang des Öffnens und am Ende des Schließens
            public const int DockAlpha = 90;    // Deckkraft (0–255) als Symbol im Dock

            // Anmeldebildschirm
            public const int Appear = 300;      // Anmeldung blendet nach dem Abmelden von unten ein
            public const int Picker = 280;      // Nutzerwahl fächert sich aus dem Nutzerbild auf und fällt zurück
            public const int Shake = 400;       // Feld schüttelt sich bei falschem Passwort
            public const int Hover = 120;       // Nutzerbild unter der Maus wächst, Ring blendet ein
            public const int Fade = 150;        // neuer Platzhalter im Feld blendet ein
            public const int Blink = 500;       // Einfügemarke im Feld: so lange an, dann so lange aus
        }
    }
}
