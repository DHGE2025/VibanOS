namespace VibanOS.Graphics
{
    /// <summary>Geglättete Bitmap-Schrift aus einem Atlas in FontData.cs (siehe tools/make-fonts.py).</summary>
    public class Font
    {
        public readonly int LineHeight;
        public readonly int Ascent;
        internal readonly byte[] Data;
        internal readonly int First;
        internal readonly int[] Offset; // Position des Glyphen-Kopfs in Data

        public Font(byte[] data)
        {
            Data = data;
            LineHeight = data[0];
            Ascent = data[1];
            First = data[2];
            Offset = new int[data[3]];
            int p = 4;
            for (int i = 0; i < Offset.Length; i++)
            {
                Offset[i] = p;
                p += 5 + data[p + 1] * data[p + 2];
            }
        }

        /// <summary>Kopf-Position eines Zeichens, unbekannte Zeichen werden zu '?'.</summary>
        internal int Glyph(char c)
        {
            int i = c - First;
            if (i < 0 || i >= Offset.Length) i = '?' - First;
            return Offset[i];
        }

        /// <summary>Breite eines Textes in Pixeln.</summary>
        public int Measure(string text)
        {
            int quarter = 0;
            for (int i = 0; i < text.Length; i++) quarter += Data[Glyph(text[i])];
            return (quarter + 3) / 4;
        }
    }

    /// <summary>Die Schriftrollen aus dem Designsheet. Werden beim Boot einmal geladen.</summary>
    public static class Fonts
    {
        public static Font Body;     // Inter Regular 13
        public static Font Strong;   // Inter SemiBold 13
        public static Font Caption;  // Inter Medium 11
        public static Font Display;  // Inter Display SemiBold 26
        public static Font Mono;     // JetBrains Mono Regular 13

        public static void Load()
        {
            Body = new Font(FontData.Body);
            Strong = new Font(FontData.Strong);
            Caption = new Font(FontData.Caption);
            Display = new Font(FontData.Display);
            Mono = new Font(FontData.Mono);
        }
    }
}
