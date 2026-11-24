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

        public override void OnKey(Key key, char c)
        {
            Shell shell = OS.Shell;
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
                int count = shell.History.Count;
                if (count == 0) return;
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

            // Nur so viele Zeilen umbrechen, wie höchstens sichtbar sind.
            rowText.Clear();
            rowStyle.Clear();
            int first = shell.Lines.Count - rows;
            for (int i = first < 0 ? 0 : first; i < shell.Lines.Count; i++)
                Wrap(shell.Lines[i].Text, shell.Lines[i].Style, columns);
            string shown = input;
            if (shell.Masked)
            {
                shown = "";
                for (int i = 0; i < input.Length; i++) shown += "*";
            }
            int promptRow = rowText.Count;
            Wrap(shell.Prompt + shown, LineStyle.Input, columns);

            int start = rowText.Count > rows ? rowText.Count - rows : 0;
            int ty = y + pad;
            int endX = x + pad;
            for (int i = start; i < rowText.Count; i++)
            {
                uint color = T.Color.TextPrimary;
                if (rowStyle[i] == LineStyle.Muted) color = T.Color.TextSecondary;
                else if (rowStyle[i] == LineStyle.Error) color = T.Color.Danger;
                else if (rowStyle[i] == LineStyle.Input && i < promptRow) color = T.Color.TextSecondary;
                endX = s.DrawText(font, rowText[i], x + pad, ty, color);
                ty += font.LineHeight;
            }
            // Einfügemarke hinter dem letzten Zeichen der Eingabe
            s.FillRect(endX + 1, ty - font.LineHeight + 2, charWidth, font.LineHeight - 4, T.Color.Accent);
        }

        private void Wrap(string text, LineStyle style, int columns)
        {
            if (columns < 1) columns = 1;
            do
            {
                int n = text.Length > columns ? columns : text.Length;
                rowText.Add(text.Substring(0, n));
                rowStyle.Add(style);
                text = text.Substring(n);
            } while (text.Length > 0);
        }
    }
}
