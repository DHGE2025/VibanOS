using System.Collections.Generic;
using VibanOS.Cli;
using VibanOS.Core;
using VibanOS.Graphics;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>Terminal-Fenster: zeigt den Text der Shell und nimmt die Eingabezeile entgegen.</summary>
    public class TerminalWindow : Window
    {
        private string input = "";
        private int historyIndex = -1;

        // So viele Zeilen ist der Text nach oben geblättert. 0 zeigt das Ende mit der Eingabezeile.
        private int scroll;
        private int visibleRows = 1;

        // Zeilen nach dem Umbruch, bei jedem Zeichnen neu gefüllt.
        private readonly List<string> rowText = new List<string>();
        private readonly List<LineStyle> rowStyle = new List<LineStyle>();

        public TerminalWindow()
        {
            Title = "Terminal";
            Width = 720;
            Height = 440;
        }

        protected override uint Background { get { return T.Color.Sunken; } }

        public override bool CanZoom { get { return true; } }

        public override void OnKey(Key key, char c)
        {
            Shell shell = OS.Shell;
            if (key == Key.PageUp || key == Key.PageDown)
            {
                // Eine Seite weiter, eine Zeile bleibt als Anschluss stehen. Das obere Ende begrenzt DrawContent.
                int page = visibleRows > 1 ? visibleRows - 1 : 1;
                scroll += key == Key.PageUp ? page : -page;
                if (scroll < 0) scroll = 0;
                return;
            }
            scroll = 0; // Jede andere Taste springt zurück zur Eingabezeile.

            // Ein Befehl arbeitet noch in Schritten (rm -r, cp -r): Getippt wird erst danach wieder, Escape bricht ab.
            // Fragt er zwischendurch nach, geht die Antwort ganz normal hinein.
            if (shell.Busy && !shell.Asking)
            {
                if (key == Key.Escape) shell.Cancel();
                return;
            }

            if (key == Key.Enter)
            {
                string line = input;
                input = "";
                historyIndex = -1;
                shell.Submit(line);
            }
            else if (key == Key.Backspace)
            {
                if (input.Length > 0) input = input.Substring(0, input.Length - 1);
            }
            else if (key == Key.Up || key == Key.Down)
            {
                // In einer Rückfrage (etwa nach dem Passwort) gibt es keinen Verlauf.
                // Pfeil ab ohne vorheriges Blättern lässt die getippte Eingabe stehen.
                int count = shell.History.Count;
                if (shell.Asking || count == 0 || (key == Key.Down && historyIndex < 0)) return;
                if (historyIndex < 0) historyIndex = count;
                historyIndex += key == Key.Up ? -1 : 1;
                if (historyIndex < 0) historyIndex = 0;
                if (historyIndex >= count) { historyIndex = -1; input = ""; }
                else input = shell.History[historyIndex];
            }
            else if (key == Key.None && c >= ' ' && c <= 255)
            {
                input += c;
            }
        }

        protected override void DrawContent(Surface s, int x, int y, int w, int h)
        {
            Shell shell = OS.Shell;
            Font font = Fonts.Mono;
            int pad = T.Space.M;
            int charWidth = font.Measure("M");
            int columns = (w - 2 * pad) / charWidth;
            int rows = (h - 2 * pad) / font.LineHeight;
            if (rows < 1) rows = 1;
            visibleRows = rows;

            // Alles umbrechen, was die Shell aufgehoben hat (höchstens 300 Zeilen), damit man zurückblättern kann.
            rowText.Clear();
            rowStyle.Clear();
            for (int i = 0; i < shell.Lines.Count; i++)
                Wrap(shell.Lines[i].Text, shell.Lines[i].Style, columns);
            string shown = input;
            if (shell.Masked)
            {
                shown = "";
                for (int i = 0; i < input.Length; i++) shown += "*";
            }
            int promptRow = rowText.Count;
            Wrap(shell.Prompt + shown, LineStyle.Input, columns);

            int last = rowText.Count > rows ? rowText.Count - rows : 0; // erste sichtbare Zeile ohne Blättern
            if (scroll > last) scroll = last;
            int start = last - scroll;
            int end = start + rows < rowText.Count ? start + rows : rowText.Count;
            int ty = y + pad;
            int endX = x + pad;
            for (int i = start; i < end; i++)
            {
                uint color = T.Color.TextPrimary;
                if (rowStyle[i] == LineStyle.Muted) color = T.Color.TextSecondary;
                else if (rowStyle[i] == LineStyle.Error) color = T.Color.Danger;
                else if (rowStyle[i] == LineStyle.Input && i < promptRow) color = T.Color.TextSecondary;
                endX = s.DrawText(font, rowText[i], x + pad, ty, color);
                ty += font.LineHeight;
            }
            // Einfügemarke hinter dem letzten Zeichen der Eingabe. Beim Zurückblättern ist die Eingabezeile nicht zu sehen,
            // und solange ein Befehl noch arbeitet, gibt es nichts zu tippen.
            if (scroll == 0 && (!shell.Busy || shell.Asking)) s.FillRect(endX + 1, ty - font.LineHeight + 2, charWidth, font.LineHeight - 4, T.Color.Accent);
        }

        private void Wrap(string text, LineStyle style, int columns)
        {
            if (columns < 1) columns = 1;
            int at = 0;
            do
            {
                int n = text.Length - at > columns ? columns : text.Length - at;
                rowText.Add(text.Substring(at, n));
                rowStyle.Add(style);
                at += n;
            } while (at < text.Length);
        }
    }
}
