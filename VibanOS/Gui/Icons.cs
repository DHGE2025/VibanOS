using VibanOS.Graphics;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>App-Symbole, aus Formen gezeichnet statt aus Bilddateien. So bleiben sie in jeder Größe scharf.</summary>
    public static class Icons
    {
        public const int Terminal = 0;
        public const int Activity = 1;
        public const int About = 2;
        public const int Count = 3;

        public static void Draw(Surface s, int icon, int x, int y, int size)
        {
            int r = size * T.Radius.Icon / T.Size.DockIcon;
            if (icon == Terminal)
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
            else
            {
                // Das Logo: Verlauf aus den Hintergrundfarben mit einem V
                s.FillRoundRect(x, y, size, size, r, T.Color.WallBlob1, T.Color.WallBlob2);
                Font font = size >= T.Size.DockIcon ? Fonts.Display : Fonts.Caption;
                s.DrawTextCentered(font, "V", x + size / 2, y + (size - font.LineHeight) / 2, T.Color.TextOnAccent);
            }
            s.StrokeRoundRect(x, y, size, size, r, T.Color.Border);
        }
    }
}
