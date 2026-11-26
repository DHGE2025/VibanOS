using System;
using VibanOS.Graphics;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>Hintergrundbild: weicher Farbverlauf aus den Wall-Tokens, einmal beim Start berechnet.</summary>
    public static class Wallpaper
    {
        private const int Cell = 8; // Der Verlauf wird in 1/8 Auflösung berechnet und dann weich vergrößert.

        // Streumuster (Bayer 4x4). Verteilt den Rundungsfehler, damit im Verlauf keine Streifen entstehen.
        private static readonly int[] Dither = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        public static void Render(Surface target)
        {
            // ponytail: läuft einmal beim Boot über alle Pixel. Wird der Start zu langsam, Cell erhöhen.
            // Gerechnet wird in echten Pixeln, bei Retina (Scale 2) also mit doppelt so großen Zellen.
            int width = target.PixelWidth, height = target.PixelHeight, cell = Cell * target.Scale;
            int lw = width / cell + 2, lh = height / cell + 2;
            float aspect = width / (float)height;
            int[] low = new int[lw * lh * 3]; // je Zelle R, G, B mit 8 Nachkommabits
            float[] c = new float[3];
            for (int j = 0; j < lh; j++)
            {
                for (int i = 0; i < lw; i++)
                {
                    float u = i * cell / (float)width, v = j * cell / (float)height;
                    c[0] = c[1] = c[2] = 0;
                    Blend(c, T.Color.WallBase1, 1);
                    Blend(c, T.Color.WallBase2, v > 1 ? 1 : v);
                    Blend(c, T.Color.WallBlob1, Blob(u, v, aspect, 0.18f, 0.20f, 0.75f) * 0.85f);
                    Blend(c, T.Color.WallBlob4, Blob(u, v, aspect, 0.78f, 0.05f, 0.60f) * 0.70f);
                    Blend(c, T.Color.WallBlob2, Blob(u, v, aspect, 0.72f, 0.80f, 0.70f) * 0.90f);
                    Blend(c, T.Color.WallBlob3, Blob(u, v, aspect, 1.00f, 1.05f, 0.50f) * 0.95f);
                    for (int k = 0; k < 3; k++) low[(j * lw + i) * 3 + k] = (int)(c[k] * 256);
                }
            }

            for (int y = 0; y < height; y++)
            {
                int j = y / cell, ty = y % cell;
                for (int x = 0; x < width; x++)
                {
                    int i = x / cell, tx = x % cell;
                    int a = (j * lw + i) * 3, b = a + lw * 3;
                    int noise = Dither[(y & 3) * 4 + (x & 3)] * 16;
                    int pixel = 255;
                    for (int k = 0; k < 3; k++)
                    {
                        int top = low[a + k] * (cell - tx) + low[a + 3 + k] * tx;
                        int bottom = low[b + k] * (cell - tx) + low[b + 3 + k] * tx;
                        int value = ((top * (cell - ty) + bottom * ty) / (cell * cell) + noise) >> 8;
                        pixel = (pixel << 8) | (value > 255 ? 255 : value);
                    }
                    target.Pixels[y * width + x] = pixel;
                }
            }
        }

        // Mischt eine Farbe mit Anteil t (0–1) in c (R, G, B).
        private static void Blend(float[] c, uint color, float t)
        {
            c[0] += (((color >> 16) & 255) - c[0]) * t;
            c[1] += (((color >> 8) & 255) - c[1]) * t;
            c[2] += ((color & 255) - c[2]) * t;
        }

        // Stärke (0–1) eines weichen Farbflecks an der Stelle (u, v). Mitte und Radius in Bildhöhen.
        private static float Blob(float u, float v, float aspect, float cx, float cy, float radius)
        {
            float du = (u - cx) * aspect, dv = v - cy;
            float d = (float)Math.Sqrt(du * du + dv * dv) / radius;
            if (d >= 1) return 0;
            float t = 1 - d;
            return t * t * (3 - 2 * t);
        }
    }
}
