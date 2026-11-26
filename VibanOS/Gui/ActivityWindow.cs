using VibanOS.Core;
using VibanOS.Graphics;
using VibanOS.Memory;
using VibanOS.Processes;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>Aktivitätsanzeige: Prozesse, Scheduler und die Rahmentabelle des Speichers auf einen Blick.</summary>
    public class ActivityWindow : Window
    {
        private const int Columns = 64;     // Rahmen pro Zeile in der Speicherkarte
        private const int CellSize = 8;
        private const int CellGap = 2;
        private const int TileHeight = 68;
        private const int MaxRows = 6;

        // Farben, mit denen Prozesse in der Speicherkarte unterschieden werden.
        private static readonly uint[] OwnerColors =
        {
            T.Color.Accent, T.Color.Success, T.Color.Warning, T.Color.Danger, T.Color.WallBlob1, T.Color.WallBlob3
        };

        public ActivityWindow()
        {
            Title = "Aktivitätsanzeige";
            Width = Columns * (CellSize + CellGap) - CellGap + 2 * T.Space.L;
            Height = T.Size.TitleBar + TileHeight + (MaxRows + 1) * T.Size.Row
                + (PhysicalMemory.FrameCount / Columns) * (CellSize + CellGap) + Fonts.Caption.LineHeight + 5 * T.Space.L;
        }

        public override bool Live { get { return true; } }

        protected override void DrawContent(Surface s, int x, int y, int w, int h)
        {
            int left = x + T.Space.L, inner = w - 2 * T.Space.L;
            int ty = y + T.Space.L;

            // Kennzahlen
            int used = PhysicalMemory.FrameCount - OS.Memory.Frames.FreeCount;
            int tile = (inner - 2 * T.Space.M) / 3;
            Tile(s, left, ty, tile, "PROZESSE", OS.Processes.All.Count.ToString());
            Tile(s, left + tile + T.Space.M, ty, tile, "SPEICHER BELEGT", used * (PhysicalMemory.FrameSize / 1024) + " KB");
            Tile(s, left + 2 * (tile + T.Space.M), ty, tile, "SCHEDULER", OS.Processes.Scheduler.Name);
            ty += TileHeight + T.Space.L;

            // Prozesstabelle
            int[] col = { 0, 56, 160, 270, 360, 450 };
            string[] head = { "PID", "NAME", "ZUSTAND", "NUTZER", "SCHRITTE", "AUSGABE" };
            int textY = (T.Size.Row - Fonts.Body.LineHeight) / 2;
            for (int c = 0; c < head.Length; c++)
                s.DrawText(Fonts.Caption, head[c], left + col[c], ty + (T.Size.Row - Fonts.Caption.LineHeight) / 2, T.Color.TextSecondary);
            s.FillRect(left, ty + T.Size.Row - 1, inner, 1, T.Color.Separator);
            ty += T.Size.Row;

            // Passen nicht alle Prozesse in die Tabelle, zeigt sie die zuletzt gestarteten.
            int count = OS.Processes.All.Count;
            int first = count > MaxRows ? count - MaxRows : 0;
            for (int i = 0; i < MaxRows; i++)
            {
                if (first + i < count)
                {
                    Process p = OS.Processes.All[first + i];
                    uint dot = p.State == ProcessState.Terminated ? T.Color.TextTertiary
                        : p.State == ProcessState.Blocked || p.Stopped ? T.Color.Warning : T.Color.Success;
                    s.DrawText(Fonts.Body, p.Id.ToString(), left + col[0], ty + textY, T.Color.TextSecondary);
                    s.DrawText(Fonts.Strong, p.Name, left + col[1], ty + textY, T.Color.TextPrimary);
                    s.FillCircle(left + col[2] + 4, ty + T.Size.Row / 2, 8, dot);
                    s.DrawText(Fonts.Body, p.StateText, left + col[2] + 14, ty + textY, T.Color.TextPrimary);
                    s.DrawText(Fonts.Body, Fit(Fonts.Body, p.Owner, col[4] - col[3] - T.Space.S), left + col[3], ty + textY, T.Color.TextSecondary);
                    s.DrawText(Fonts.Body, p.Steps.ToString(), left + col[4], ty + textY, T.Color.TextSecondary);
                    s.DrawText(Fonts.Body, p.Output, left + col[5], ty + textY, T.Color.TextPrimary);
                }
                else if (i == 0)
                {
                    s.DrawText(Fonts.Body, "Keine Prozesse. Im Terminal startet 'start counter' einen.", left, ty + textY, T.Color.TextTertiary);
                }
                ty += T.Size.Row;
            }
            ty += T.Space.L;

            // Speicherkarte: ein Kästchen pro Rahmen
            s.DrawText(Fonts.Caption, "SPEICHERRAHMEN  " + used + " VON " + PhysicalMemory.FrameCount + " BELEGT", left, ty, T.Color.TextSecondary);
            ty += Fonts.Caption.LineHeight + T.Space.S;
            int[] owner = OS.Memory.Frames.Owner;
            for (int i = 0; i < owner.Length; i++)
            {
                uint color = owner[i] == FrameTable.Free ? T.Color.Raised : OwnerColors[owner[i] % OwnerColors.Length];
                s.FillRoundRect(left + i % Columns * (CellSize + CellGap), ty + i / Columns * (CellSize + CellGap), CellSize, CellSize, T.Radius.XS, color);
            }
        }

        // Kürzt Text, bis er in eine Spalte passt. Nutzernamen dürfen länger sein als ihre Spalte breit ist.
        internal static string Fit(Font font, string text, int width)
        {
            if (font.Measure(text) <= width) return text;
            while (text.Length > 0 && font.Measure(text + "..") > width) text = text.Substring(0, text.Length - 1);
            return text + "..";
        }

        private static void Tile(Surface s, int x, int y, int w, string label, string value)
        {
            s.FillRoundRect(x, y, w, TileHeight, T.Radius.M, T.Color.Raised);
            s.DrawText(Fonts.Caption, label, x + T.Space.M, y + T.Space.S, T.Color.TextSecondary);
            s.DrawText(Fonts.Display, value, x + T.Space.M, y + T.Space.S + Fonts.Caption.LineHeight + 2, T.Color.TextPrimary);
        }
    }
}
