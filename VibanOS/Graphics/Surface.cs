using System;

namespace VibanOS.Graphics
{
    /// <summary>
    /// Zeichenfläche auf einem int[]-Bildpuffer (0xAARRGGBB). Kennt COSMOS nicht:
    /// Der Kernel schiebt den Puffer nur noch auf den Bildschirm. Alle Formen sind
    /// kantengeglättet und können durchscheinend sein.
    /// </summary>
    public class Surface
    {
        public readonly int Width;
        public readonly int Height;
        public readonly int[] Pixels;

        public Surface(int width, int height) : this(width, height, new int[width * height]) { }

        public Surface(int width, int height, int[] pixels)
        {
            Width = width;
            Height = height;
            Pixels = pixels;
            ResetClip();
        }

        // Zeichenbereich. Formen und Text außerhalb werden abgeschnitten (Fensterinhalt).
        private int clipX0, clipY0, clipX1, clipY1;

        public void SetClip(int x, int y, int w, int h)
        {
            clipX0 = x < 0 ? 0 : x;
            clipY0 = y < 0 ? 0 : y;
            clipX1 = x + w > Width ? Width : x + w;
            clipY1 = y + h > Height ? Height : y + h;
        }

        public void ResetClip()
        {
            clipX0 = 0;
            clipY0 = 0;
            clipX1 = Width;
            clipY1 = Height;
        }

        /// <summary>Mischt zwei Farben, t = 0 ergibt a, t = 255 ergibt b.</summary>
        public static uint Mix(uint a, uint b, int t)
        {
            int u = 255 - t;
            uint al = (uint)((int)(a >> 24) * u + (int)(b >> 24) * t) / 255;
            uint r = (uint)((int)((a >> 16) & 255) * u + (int)((b >> 16) & 255) * t) / 255;
            uint g = (uint)((int)((a >> 8) & 255) * u + (int)((b >> 8) & 255) * t) / 255;
            uint bl = (uint)((int)(a & 255) * u + (int)(b & 255) * t) / 255;
            return (al << 24) | (r << 16) | (g << 8) | bl;
        }

        /// <summary>Gleiche Farbe mit anderer Deckkraft (0–255).</summary>
        public static uint WithAlpha(uint color, int alpha)
        {
            return (color & 0x00FFFFFF) | ((uint)alpha << 24);
        }

        // Mischt die Farbe mit zusätzlicher Deckung (0–255) auf den Pixel i. Erwartet einen gültigen Index.
        private void Blend(int i, uint color, int coverage)
        {
            int a = (int)(color >> 24) * coverage / 255;
            if (a <= 0) return;
            if (a >= 255) { Pixels[i] = (int)(color | 0xFF000000); return; }
            int dst = Pixels[i];
            int u = 255 - a;
            int r = (((int)(color >> 16) & 255) * a + ((dst >> 16) & 255) * u) / 255;
            int g = (((int)(color >> 8) & 255) * a + ((dst >> 8) & 255) * u) / 255;
            int b = (((int)color & 255) * a + (dst & 255) * u) / 255;
            Pixels[i] = (255 << 24) | (r << 16) | (g << 8) | b;
        }

        private void Plot(int x, int y, uint color, int coverage)
        {
            if (x < clipX0 || y < clipY0 || x >= clipX1 || y >= clipY1) return;
            Blend(y * Width + x, color, coverage);
        }

        // Waagerechte Linie von x0 (einschließlich) bis x1 (ausschließlich).
        private void Span(int x0, int x1, int y, uint color)
        {
            if (y < clipY0 || y >= clipY1) return;
            if (x0 < clipX0) x0 = clipX0;
            if (x1 > clipX1) x1 = clipX1;
            int i = y * Width + x0;
            int end = y * Width + x1;
            if (color >> 24 == 255)
            {
                int c = (int)color;
                for (; i < end; i++) Pixels[i] = c;
            }
            else
            {
                for (; i < end; i++) Blend(i, color, 255);
            }
        }

        public void FillRect(int x, int y, int w, int h, uint color)
        {
            for (int j = 0; j < h; j++) Span(x, x + w, y + j, color);
        }

        // Deckung (0–255) eines Pixels in einer Ecke mit Radius r. dx/dy = Abstand zum Kreismittelpunkt.
        private static int Corner(float dx, float dy, float r)
        {
            float c = r - (float)Math.Sqrt(dx * dx + dy * dy) + 0.5f;
            if (c <= 0) return 0;
            if (c >= 1) return 255;
            return (int)(c * 255);
        }

        public void FillRoundRect(int x, int y, int w, int h, int r, uint color)
        {
            FillRoundRect(x, y, w, h, r, color, color);
        }

        /// <summary>Abgerundetes Rechteck mit senkrechtem Verlauf von top nach bottom.</summary>
        public void FillRoundRect(int x, int y, int w, int h, int r, uint top, uint bottom)
        {
            if (w <= 0 || h <= 0) return;
            if (r * 2 > w) r = w / 2;
            if (r * 2 > h) r = h / 2;
            for (int j = 0; j < h; j++)
            {
                uint color = top == bottom || h == 1 ? top : Mix(top, bottom, j * 255 / (h - 1));
                int inset = 0;
                if (j < r || j >= h - r)
                {
                    float dy = j < r ? r - j - 0.5f : j - (h - r) + 0.5f;
                    for (int i = 0; i < r; i++)
                    {
                        int c = Corner(r - i - 0.5f, dy, r);
                        Plot(x + i, y + j, color, c);
                        Plot(x + w - 1 - i, y + j, color, c);
                    }
                    inset = r;
                }
                Span(x + inset, x + w - inset, y + j, color);
            }
        }

        /// <summary>1 px breiter Rahmen innen entlang eines abgerundeten Rechtecks.</summary>
        public void StrokeRoundRect(int x, int y, int w, int h, int r, uint color)
        {
            if (w <= 1 || h <= 1) return;
            if (r * 2 > w) r = w / 2;
            if (r * 2 > h) r = h / 2;
            Span(x + r, x + w - r, y, color);
            Span(x + r, x + w - r, y + h - 1, color);
            for (int j = r; j < h - r; j++)
            {
                Plot(x, y + j, color, 255);
                Plot(x + w - 1, y + j, color, 255);
            }
            for (int j = 0; j < r; j++)
            {
                for (int i = 0; i < r; i++)
                {
                    float dx = r - i - 0.5f, dy = r - j - 0.5f;
                    int c = Corner(dx, dy, r) - Corner(dx, dy, r - 1);
                    if (c <= 0) continue;
                    Plot(x + i, y + j, color, c);
                    Plot(x + w - 1 - i, y + j, color, c);
                    Plot(x + i, y + h - 1 - j, color, c);
                    Plot(x + w - 1 - i, y + h - 1 - j, color, c);
                }
            }
        }

        public void FillCircle(int cx, int cy, int diameter, uint color)
        {
            FillRoundRect(cx - diameter / 2, cy - diameter / 2, diameter, diameter, diameter / 2, color);
        }

        /// <summary>
        /// Weicher Schatten um ein abgerundetes Rechteck. Gezeichnet wird nur außerhalb der Form,
        /// die Form selbst kommt danach darüber. So scheint der Schatten auch nicht durch
        /// durchscheinende Flächen wie das Dock.
        /// </summary>
        public void Shadow(int x, int y, int w, int h, int r, int blur, int offsetY, int alpha, uint color)
        {
            int sy = y + offsetY;
            float halfW = w / 2f - r, halfH = h / 2f - r;
            float cx = x + w / 2f, cy = sy + h / 2f;
            for (int py = sy - blur; py < sy + h + blur; py++)
            {
                if (py < 0 || py >= Height) continue;
                // Von coverFrom bis coverTo (ausschließlich) deckt die Form diese Zeile voll. In den Eckzeilen
                // beginnt das dort, wo FillRoundRect die erste voll gedeckte Stelle zeichnet.
                int coverFrom = 0, coverTo = 0, j = py - y;
                if (j >= 0 && j < h)
                {
                    int inset = 0;
                    if (j < r || j >= h - r)
                    {
                        float dy = j < r ? r - j - 0.5f : j - (h - r) + 0.5f;
                        float reach = (r - 0.5f) * (r - 0.5f) - dy * dy;
                        inset = reach <= 0 ? r : r - (int)((float)Math.Sqrt(reach) + 0.5f);
                    }
                    coverFrom = x + inset;
                    coverTo = x + w - inset;
                }
                for (int px = x - blur; px < x + w + blur; px++)
                {
                    if (px == coverFrom && coverTo > coverFrom) px = coverTo;
                    if (px < 0 || px >= Width) continue;
                    float qx = px + 0.5f - cx, qy = py + 0.5f - cy;
                    qx = (qx < 0 ? -qx : qx) - halfW;
                    qy = (qy < 0 ? -qy : qy) - halfH;
                    float d;
                    if (qx > 0 && qy > 0) d = (float)Math.Sqrt(qx * qx + qy * qy) - r;
                    else d = (qx > qy ? qx : qy) - r;
                    if (d >= blur) continue;
                    float t = d <= 0 ? 1 : 1 - d / blur;
                    Blend(py * Width + px, color, (int)(alpha * t * t));
                }
            }
        }

        /// <summary>Schreibt Text, y ist die Oberkante der Zeile. Gibt die x-Position nach dem Text zurück.</summary>
        public int DrawText(Font font, string text, int x, int y, uint color)
        {
            byte[] data = font.Data;
            int pen = x * 4;
            for (int n = 0; n < text.Length; n++)
            {
                int g = font.Glyph(text[n]);
                int gw = data[g + 1], gh = data[g + 2];
                int gx = (pen + 2) / 4 + (sbyte)data[g + 3];
                int gy = y + (sbyte)data[g + 4];
                int p = g + 5;
                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        int c = data[p++];
                        if (c != 0) Plot(gx + i, gy + j, color, c);
                    }
                }
                pen += data[g];
            }
            return (pen + 3) / 4;
        }

        /// <summary>Text waagerecht zentriert um centerX.</summary>
        public void DrawTextCentered(Font font, string text, int centerX, int y, uint color)
        {
            DrawText(font, text, centerX - font.Measure(text) / 2, y, color);
        }

        /// <summary>
        /// Zeichnet source gestreckt auf das Rechteck (x, y, w, h), mit Deckkraft alpha (0–255) und Eckenradius r.
        /// Nimmt das nächstgelegene Pixel, das ist schnell genug für Animationen ohne Grafikbeschleunigung.
        /// </summary>
        public void DrawScaled(Surface source, int x, int y, int w, int h, int r, int alpha)
        {
            if (w <= 0 || h <= 0 || alpha <= 0) return;
            if (2 * r > w) r = w / 2;
            if (2 * r > h) r = h / 2;
            int stepX = (source.Width << 16) / w, stepY = (source.Height << 16) / h;
            for (int j = 0; j < h; j++)
            {
                int py = y + j;
                if (py < clipY0 || py >= clipY1) continue;
                int inset = 0;
                if (j < r || j >= h - r)
                {
                    float dy = (j < r ? r - j : j - (h - r) + 1) - 0.5f;
                    float reach = r * r - dy * dy;
                    inset = reach <= 0 ? r : r - (int)Math.Sqrt(reach);
                }
                int x0 = x + inset, x1 = x + w - inset;
                if (x0 < clipX0) x0 = clipX0;
                if (x1 > clipX1) x1 = clipX1;
                int row = ((j * stepY) >> 16) * source.Width;
                int sx = (x0 - x) * stepX;
                for (int px = x0; px < x1; px++, sx += stepX)
                    Blend(py * Width + px, (uint)source.Pixels[row + (sx >> 16)], alpha);
            }
        }

        public void CopyFrom(Surface source)
        {
            Array.Copy(source.Pixels, Pixels, Pixels.Length);
        }

        /// <summary>Kopiert einen Ausschnitt an dieselbe Stelle (für das Wiederherstellen unter dem Mauszeiger).</summary>
        public void CopyRect(Surface source, int x, int y, int w, int h)
        {
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > Width) w = Width - x;
            if (y + h > Height) h = Height - y;
            if (w <= 0) return;
            for (int j = 0; j < h; j++)
                Array.Copy(source.Pixels, (y + j) * Width + x, Pixels, (y + j) * Width + x, w);
        }
    }
}
