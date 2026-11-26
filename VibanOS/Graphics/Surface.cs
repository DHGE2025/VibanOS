using System;

namespace VibanOS.Graphics
{
    /// <summary>
    /// Zeichenfläche auf einem int[]-Bildpuffer (0xAARRGGBB). Kennt COSMOS nicht:
    /// Der Kernel schiebt den Puffer nur noch auf den Bildschirm. Alle Formen sind
    /// kantengeglättet und können durchscheinend sein.
    /// Koordinaten und Maße sind in Punkten. Auf einem Retina-Bildschirm (Scale 2) hat ein Punkt 2x2 Pixel:
    /// Formen und Schrift werden dann in voller Auflösung gezeichnet und sind scharf, die Oberfläche bleibt gleich groß.
    /// </summary>
    public class Surface
    {
        /// <summary>Größe in Punkten. Danach richtet sich die Oberfläche.</summary>
        public readonly int Width, Height;

        /// <summary>Pixel je Punkt (1 oder 2) und die Größe in Pixeln, also die des Puffers.</summary>
        public readonly int Scale, PixelWidth, PixelHeight;

        public readonly int[] Pixels;

        /// <summary>Pixel je Punkt für neue Flächen. Der Kernel setzt es beim Start passend zum Bildschirm.</summary>
        public static int DefaultScale = 1;

        public Surface(int width, int height) : this(width, height, DefaultScale) { }

        public Surface(int width, int height, int scale) : this(new int[width * scale * height * scale], width * scale, height * scale, scale) { }

        /// <summary>Fläche über einem fertigen Puffer, etwa dem des Bildschirms. Ein Rest unter einem Punkt bleibt ungenutzt.</summary>
        public Surface(int[] pixels, int pixelWidth, int pixelHeight, int scale)
        {
            Scale = scale;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            Width = pixelWidth / scale;
            Height = pixelHeight / scale;
            Pixels = pixels;
            Unlimit();
        }

        // Zeichenbereich. Formen und Text außerhalb werden abgeschnitten (Fensterinhalt). Er reicht nie über die
        // Grenze hinaus: Das ist der Ausschnitt, der gerade neu entsteht (Limit), sonst die ganze Fläche.
        private int clipX0, clipY0, clipX1, clipY1;
        private int limitX0, limitY0, limitX1, limitY1;

        public void SetClip(int x, int y, int w, int h)
        {
            x *= Scale; y *= Scale; w *= Scale; h *= Scale;
            clipX0 = x < limitX0 ? limitX0 : x;
            clipY0 = y < limitY0 ? limitY0 : y;
            clipX1 = x + w > limitX1 ? limitX1 : x + w;
            clipY1 = y + h > limitY1 ? limitY1 : y + h;
        }

        public void ResetClip()
        {
            clipX0 = limitX0;
            clipY0 = limitY0;
            clipX1 = limitX1;
            clipY1 = limitY1;
        }

        /// <summary>Ab jetzt landet nur noch im Rechteck (x, y, w, h) etwas, alles andere bleibt stehen. Unlimit hebt das auf.</summary>
        public void Limit(int x, int y, int w, int h)
        {
            x *= Scale; y *= Scale; w *= Scale; h *= Scale;
            limitX0 = x < 0 ? 0 : x;
            limitY0 = y < 0 ? 0 : y;
            limitX1 = x + w > PixelWidth ? PixelWidth : x + w;
            limitY1 = y + h > PixelHeight ? PixelHeight : y + h;
            ResetClip();
        }

        public void Unlimit()
        {
            limitX0 = limitY0 = 0;
            limitX1 = PixelWidth;
            limitY1 = PixelHeight;
            ResetClip();
        }

        // ---- Was hat sich geändert? ----
        // Der Schreibtisch zeichnet jedes Bild zweimal: erst zum Vermessen, dann wirklich. Beim Vermessen entsteht kein
        // Pixel. Jede Form merkt sich nur in den Kacheln, die sie berührt, eine Prüfsumme aus allem, was ihr Aussehen
        // bestimmt (Lage, Farbe, Text, Zuschnitt, Reihenfolge). Kacheln mit derselben Summe wie im letzten Bild sehen
        // gleich aus und bleiben stehen. Neu gezeichnet wird nur der Rest (Damage).

        /// <summary>Kantenlänge einer Kachel in Punkten.</summary>
        public const int Tile = 32;

        /// <summary>So viele Rechtecke liefert Damage höchstens.</summary>
        public const int MaxDamage = 6;

        private const ulong Prime = 1099511628211;
        private ulong[] marks, shown;
        private int tilesX, tilesY;
        private bool measuring;

        // Laufende Nummer: Daran erkennt die Prüfsumme, welches Bild DrawScaled zeichnet.
        private static int count;
        private readonly int serial = ++count;

        /// <summary>Ab jetzt wird nur vermessen, bis Damage. Auf einer Fläche ohne Grenze aufrufen.</summary>
        public void Measure()
        {
            if (marks == null)
            {
                tilesX = (PixelWidth + Tile * Scale - 1) / (Tile * Scale);
                tilesY = (PixelHeight + Tile * Scale - 1) / (Tile * Scale);
                marks = new ulong[tilesX * tilesY];
                shown = new ulong[tilesX * tilesY];
                Invalidate();
            }
            Array.Clear(marks, 0, marks.Length);
            measuring = true;
        }

        /// <summary>Das nächste Damage meldet die ganze Fläche, etwa nach einem neuen Hintergrund.</summary>
        public void Invalidate()
        {
            if (shown != null) Array.Fill(shown, 1UL); // eine leere Kachel hat die Summe 0
        }

        /// <summary>
        /// Beendet das Vermessen. Schreibt die geänderten Bereiche als x, y, Breite, Höhe (in Punkten) nach rects und gibt
        /// ihre Zahl zurück. Je Kachelzeile ein Streifen von der ersten bis zur letzten geänderten Kachel, gleiche Streifen
        /// untereinander werden ein Rechteck. Sind es mehr als MaxDamage, bleibt eines, das alle umfasst.
        /// </summary>
        public int Damage(int[] rects)
        {
            measuring = false;
            int n = 0, left = tilesX, top = tilesY, right = -1, bottom = -1;
            for (int ty = 0; ty < tilesY; ty++)
            {
                int a = -1, b = -1;
                for (int tx = 0; tx < tilesX; tx++)
                {
                    int t = ty * tilesX + tx;
                    if (marks[t] == shown[t]) continue;
                    shown[t] = marks[t];
                    if (a < 0) a = tx;
                    b = tx;
                }
                if (a < 0) continue;
                if (a < left) left = a;
                if (b > right) right = b;
                if (ty < top) top = ty;
                bottom = ty;
                int last = 4 * (n - 1);
                if (n > 0 && n <= MaxDamage && rects[last] == a * Tile && rects[last + 2] == (b - a + 1) * Tile && rects[last + 1] + rects[last + 3] == ty * Tile)
                {
                    rects[last + 3] += Tile;
                    continue;
                }
                if (n < MaxDamage)
                {
                    rects[4 * n] = a * Tile;
                    rects[4 * n + 1] = ty * Tile;
                    rects[4 * n + 2] = (b - a + 1) * Tile;
                    rects[4 * n + 3] = Tile;
                }
                n++;
            }
            if (n <= MaxDamage) return n;
            rects[0] = left * Tile;
            rects[1] = top * Tile;
            rects[2] = (right - left + 1) * Tile;
            rects[3] = (bottom - top + 1) * Tile;
            return 1;
        }

        private static ulong Mixed(ulong sum, long value) { return (sum ^ (ulong)value) * Prime; }

        // Für eine Form im Pixel-Rechteck (x, y, w, h): true heißt, es gibt nichts zu zeichnen. Entweder liegt sie ganz
        // außerhalb des Zeichenbereichs, oder es wird gerade vermessen. Dann geht what (alles, was ihr Aussehen bestimmt,
        // außer der Lage) in die Kacheln unter ihr ein.
        private bool Skip(int x, int y, int w, int h, ulong what)
        {
            int x0 = x < clipX0 ? clipX0 : x, y0 = y < clipY0 ? clipY0 : y;
            int x1 = x + w > clipX1 ? clipX1 : x + w, y1 = y + h > clipY1 ? clipY1 : y + h;
            if (x0 >= x1 || y0 >= y1) return true;
            if (!measuring) return false;
            // Lage und Zuschnitt: Dieselbe Form sieht an anderer Stelle oder anders beschnitten anders aus.
            what = Mixed(Mixed(Mixed(Mixed(what, x), y), w), h);
            what = Mixed(Mixed(Mixed(Mixed(what, x0), y0), x1), y1);
            int size = Tile * Scale;
            for (int ty = y0 / size; ty <= (y1 - 1) / size; ty++)
                for (int tx = x0 / size; tx <= (x1 - 1) / size; tx++)
                    marks[ty * tilesX + tx] = (marks[ty * tilesX + tx] ^ what) * Prime; // die Reihenfolge zählt mit
            return true;
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
            Blend(y * PixelWidth + x, color, coverage);
        }

        // Waagerechte Linie von x0 (einschließlich) bis x1 (ausschließlich).
        private void Span(int x0, int x1, int y, uint color)
        {
            if (y < clipY0 || y >= clipY1) return;
            if (x0 < clipX0) x0 = clipX0;
            if (x1 > clipX1) x1 = clipX1;
            if (x1 <= x0) return;
            int i = y * PixelWidth + x0;
            int end = y * PixelWidth + x1;
            if (color >> 24 == 255) Array.Fill(Pixels, (int)color, i, end - i);
            else
            {
                for (; i < end; i++) Blend(i, color, 255);
            }
        }

        public void FillRect(int x, int y, int w, int h, uint color)
        {
            x *= Scale; y *= Scale; w *= Scale; h *= Scale;
            if (Skip(x, y, w, h, Mixed(1, color))) return;
            int bottom = y + h > clipY1 ? clipY1 : y + h;
            for (int row = y < clipY0 ? clipY0 : y; row < bottom; row++) Span(x, x + w, row, color);
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
            x *= Scale; y *= Scale; w *= Scale; h *= Scale; r *= Scale;
            if (w <= 0 || h <= 0) return;
            if (r * 2 > w) r = w / 2;
            if (r * 2 > h) r = h / 2;
            if (Skip(x, y, w, h, Mixed(Mixed(Mixed(2, r), top), bottom))) return;
            for (int j = 0; j < h; j++)
            {
                if (y + j < clipY0 || y + j >= clipY1) continue;
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

        /// <summary>Ein Punkt breiter Rahmen innen entlang eines abgerundeten Rechtecks.</summary>
        public void StrokeRoundRect(int x, int y, int w, int h, int r, uint color)
        {
            if (Skip(x * Scale, y * Scale, w * Scale, h * Scale, Mixed(Mixed(3, r), color))) return;
            // Je Pixel Breite ein Ring, jeder eins weiter innen.
            for (int i = 0; i < Scale; i++) StrokePixels(x * Scale + i, y * Scale + i, w * Scale - 2 * i, h * Scale - 2 * i, r * Scale - i, color);
        }

        private void StrokePixels(int x, int y, int w, int h, int r, uint color)
        {
            if (w <= 1 || h <= 1) return;
            if (r < 0) r = 0;
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

        // Deckung des Schattens je Abstand von der Form, einmal ausgerechnet statt für jedes Pixel.
        // Kommazahlen rechnet QEMU ohne Hilfe des Prozessors nach, das kostete beim Fensterziehen am meisten.
        private sealed class Falloff
        {
            public int R, Blur, Alpha;
            public byte[] One; // neben einer geraden Kante
            public byte[] Two; // um eine Ecke
        }

        private static readonly Falloff[] falloffs = new Falloff[8];
        private static int nextFalloff;

        private static Falloff FalloffFor(int r, int blur, int alpha)
        {
            for (int i = 0; i < falloffs.Length; i++)
            {
                Falloff known = falloffs[i];
                if (known != null && known.R == r && known.Blur == blur && known.Alpha == alpha) return known;
            }
            int n = blur + r;
            Falloff made = new Falloff { R = r, Blur = blur, Alpha = alpha, One = new byte[n], Two = new byte[n * n] };
            for (int ky = 0; ky < n; ky++)
            {
                made.One[ky] = Cover(ky + 0.5f - r, blur, alpha);
                for (int kx = 0; kx < n; kx++)
                {
                    float qx = kx + 0.5f, qy = ky + 0.5f;
                    made.Two[ky * n + kx] = Cover((float)Math.Sqrt(qx * qx + qy * qy) - r, blur, alpha);
                }
            }
            falloffs[nextFalloff] = made;
            nextFalloff = (nextFalloff + 1) % falloffs.Length;
            return made;
        }

        private static byte Cover(float d, int blur, int alpha)
        {
            if (d >= blur) return 0;
            float t = d <= 0 ? 1 : 1 - d / blur;
            return (byte)(int)(alpha * t * t);
        }

        /// <summary>
        /// Weicher Schatten um ein abgerundetes Rechteck. Gezeichnet wird nur außerhalb der Form,
        /// die Form selbst kommt danach darüber. So scheint der Schatten auch nicht durch
        /// durchscheinende Flächen wie das Dock.
        /// </summary>
        public void Shadow(int x, int y, int w, int h, int r, int blur, int offsetY, int alpha, uint color)
        {
            x *= Scale; y *= Scale; w *= Scale; h *= Scale; r *= Scale; blur *= Scale; offsetY *= Scale;
            int sy = y + offsetY;
            if (Skip(x - blur, sy - blur, w + 2 * blur, h + 2 * blur, Mixed(Mixed(Mixed(Mixed(Mixed(4, r), blur), offsetY), alpha), color))) return;
            Falloff fall = FalloffFor(r, blur, alpha);
            int n = blur + r;
            int from = x - blur < clipX0 ? clipX0 : x - blur, to = x + w + blur > clipX1 ? clipX1 : x + w + blur;
            for (int py = sy - blur; py < sy + h + blur; py++)
            {
                if (py < clipY0 || py >= clipY1) continue;
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
                // kx und ky zählen die Pixel vom geraden Teil der Form nach außen, unter 0 liegt man daneben.
                int ky = Math.Max(sy + r - 1 - py, py - (sy + h - r));
                for (int px = from; px < to; px++)
                {
                    if (px >= coverFrom && px < coverTo) { px = coverTo - 1; continue; }
                    int kx = Math.Max(x + r - 1 - px, px - (x + w - r));
                    int c = kx < 0 ? (ky < 0 ? alpha : fall.One[ky]) : ky < 0 ? fall.One[kx] : fall.Two[ky * n + kx];
                    if (c != 0) Blend(py * PixelWidth + px, color, c);
                }
            }
        }

        /// <summary>
        /// Schreibt Text, y ist die Oberkante der Zeile. Gibt die x-Position nach dem Text zurück.
        /// Mit Scale 2 kommen die Zeichen aus dem doppelt so großen Atlas, die Abstände aber aus dem normalen.
        /// So läuft der Text genauso weit wie Font.Measure sagt, und die Oberfläche bleibt gleich angeordnet.
        /// </summary>
        public int DrawText(Font font, string text, int x, int y, uint color)
        {
            Font shapes = Scale == 2 && font.Double != null ? font.Double : font;
            byte[] data = shapes.Data;
            y = y * Scale + font.Ascent * Scale - shapes.Ascent; // gleiche Grundlinie wie bei Scale 1
            int pen = x * 4; // in Viertelpunkten
            // Nur zum Vermessen: was der Text überdeckt, und woran man ihn wiedererkennt.
            int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
            ulong what = Mixed(Mixed(Mixed(5, data.Length), color), pen);
            for (int n = 0; n < text.Length; n++)
            {
                int g = shapes.Glyph(text[n]);
                int gw = data[g + 1], gh = data[g + 2];
                int gx = (pen * Scale + 2) / 4 + (sbyte)data[g + 3];
                int gy = y + (sbyte)data[g + 4];
                pen += font.Data[font.Glyph(text[n])];
                if (measuring)
                {
                    what = Mixed(what, text[n]);
                    if (gw == 0 || gh == 0) continue;
                    if (gx < x0) x0 = gx;
                    if (gy < y0) y0 = gy;
                    if (gx + gw > x1) x1 = gx + gw;
                    if (gy + gh > y1) y1 = gy + gh;
                    continue;
                }
                if (gx >= clipX1 || gy >= clipY1 || gx + gw <= clipX0 || gy + gh <= clipY0) continue;
                int p = g + 5;
                for (int j = 0; j < gh; j++)
                {
                    for (int i = 0; i < gw; i++)
                    {
                        int c = data[p++];
                        if (c != 0) Plot(gx + i, gy + j, color, c);
                    }
                }
            }
            if (measuring && x1 > x0) Skip(x0, y0, x1 - x0, y1 - y0, what);
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
            x *= Scale; y *= Scale; w *= Scale; h *= Scale; r *= Scale;
            if (w <= 0 || h <= 0 || alpha <= 0) return;
            if (2 * r > w) r = w / 2;
            if (2 * r > h) r = h / 2;
            if (Skip(x, y, w, h, Mixed(Mixed(Mixed(6, source.serial), r), alpha))) return;
            int stepX = (source.PixelWidth << 16) / w, stepY = (source.PixelHeight << 16) / h;
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
                int row = ((j * stepY) >> 16) * source.PixelWidth;
                int sx = (x0 - x) * stepX;
                for (int px = x0; px < x1; px++, sx += stepX)
                    Blend(py * PixelWidth + px, (uint)source.Pixels[row + (sx >> 16)], alpha);
            }
        }

        public void CopyFrom(Surface source)
        {
            Array.Copy(source.Pixels, Pixels, Pixels.Length);
        }

        /// <summary>Kopiert einen Ausschnitt an dieselbe Stelle (für das Wiederherstellen unter dem Mauszeiger).</summary>
        public void CopyRect(Surface source, int x, int y, int w, int h)
        {
            x *= Scale; y *= Scale; w *= Scale; h *= Scale;
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > PixelWidth) w = PixelWidth - x;
            if (y + h > PixelHeight) h = PixelHeight - y;
            if (w <= 0) return;
            for (int j = 0; j < h; j++)
                Array.Copy(source.Pixels, (y + j) * PixelWidth + x, Pixels, (y + j) * PixelWidth + x, w);
        }
    }
}
