using VibanOS.Graphics;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>App-Symbole, aus Formen gezeichnet statt aus Bilddateien. So bleiben sie in jeder Größe scharf.</summary>
    public static class Icons
    {
        public const int Files = 0;
        public const int Terminal = 1;
        public const int Activity = 2;
        public const int Settings = 3;
        public const int About = 4;
        public const int Count = 5;

        // Richtungen der acht Zähne des Zahnrads, mal 100 (Kosinus und Sinus in 45°-Schritten).
        private static readonly int[] ToothX = { 100, 71, 0, -71, -100, -71, 0, 71 };
        private static readonly int[] ToothY = { 0, 71, 100, 71, 0, -71, -100, -71 };

        public static void Draw(Surface s, int icon, int x, int y, int size)
        {
            int r = size * T.Radius.Icon / T.Size.DockIcon;
            if (icon == Files)
            {
                s.FillRoundRect(x, y, size, size, r, T.Color.Raised, T.Color.Sunken);
                Folder(s, x + size / 6, y + size / 5, size * 2 / 3);
            }
            else if (icon == Terminal)
            {
                s.FillRoundRect(x, y, size, size, r, T.Color.Raised, T.Color.Sunken);
                s.DrawText(Fonts.Mono, ">_", x + size / 5, y + size / 5, T.Color.TextPrimary);
            }
            else if (icon == Activity)
            {
                s.FillRoundRect(x, y, size, size, r, T.Color.WallBlob4, T.Color.Accent);
                int bar = size / 8, baseY = y + size - size / 4;
                int[] heights = { 3, 5, 2, 4 };
                for (int i = 0; i < heights.Length; i++)
                {
                    int bh = heights[i] * size / 10;
                    s.FillRoundRect(x + size / 5 + i * (bar + bar / 2), baseY - bh, bar, bh, T.Radius.XS, T.Color.TextOnAccent);
                }
            }
            else if (icon == Settings)
            {
                // Zahnrad: acht Zähne auf einem Ring, in der Mitte ein Loch in der Grundfarbe
                s.FillRoundRect(x, y, size, size, r, T.Color.Raised, T.Color.Sunken);
                // Deckende Farben, sonst wären die Stellen dunkler, an denen sich Zahn und Ring überlappen.
                uint gear = Surface.Mix(T.Color.Raised, T.Color.TextPrimary, 190), hole = Surface.Mix(T.Color.Raised, T.Color.Sunken, 128);
                int cx = x + size / 2, cy = y + size / 2, ring = size * 3 / 10, tooth = size / 6;
                for (int i = 0; i < ToothX.Length; i++)
                    s.FillCircle(cx + ToothX[i] * ring / 100, cy + ToothY[i] * ring / 100, tooth, gear);
                s.FillCircle(cx, cy, 2 * ring, gear);
                s.FillCircle(cx, cy, ring, hole);
            }
            else
            {
                // Das Logo: Verlauf aus den Hintergrundfarben mit einem V
                s.FillRoundRect(x, y, size, size, r, T.Color.WallBlob1, T.Color.WallBlob2);
                Font font = size >= T.Size.DockIcon ? Fonts.Display : Fonts.Caption;
                s.DrawTextCentered(font, "V", x + size / 2, y + (size - font.LineHeight) / 2, T.Color.TextOnAccent);
            }
            s.StrokeRoundRect(x, y, size, size, r, T.Color.Border);
        }

        /// <summary>Ordner: Reiter hinten in dunklerem Blau, vorn die Mappe.</summary>
        public static void Folder(Surface s, int x, int y, int size)
        {
            int r = size / 8 + 1;
            s.FillRoundRect(x, y + size / 8, size * 2 / 5, size / 3, r, T.Color.FolderBack);
            s.FillRoundRect(x, y + size / 4, size, size * 5 / 8, r, T.Color.FolderBack);
            s.FillRoundRect(x, y + size / 3, size, size * 13 / 24, r, T.Color.Folder);
        }

        /// <summary>Datei: ein Blatt mit drei Textzeilen.</summary>
        public static void Document(Surface s, int x, int y, int size)
        {
            int w = size * 3 / 4, left = x + (size - w) / 2, line = size / 8;
            s.FillRoundRect(left, y, w, size, T.Radius.XS, T.Color.TextPrimary);
            for (int i = 0; i < 3; i++)
                s.FillRect(left + line, y + size / 4 + i * size / 5, w - 2 * line, 1, T.Color.Track);
        }

        /// <summary>Festplatte: flaches Gehäuse mit Leuchte.</summary>
        public static void Disk(Surface s, int x, int y, int size)
        {
            int h = size * 5 / 8, top = y + (size - h) / 2;
            s.FillRoundRect(x, top, size, h, T.Radius.XS, T.Color.TextSecondary);
            s.FillRect(x + size / 8, top + h - h / 3, size * 3 / 4, 1, T.Color.Track);
            s.FillCircle(x + size - size / 5, top + h / 3, size / 8 + 1, T.Color.Success);
        }
    }
}
