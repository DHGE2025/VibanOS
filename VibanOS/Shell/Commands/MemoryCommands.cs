using VibanOS.Core;
using VibanOS.Memory;
using VibanOS.Processes;

namespace VibanOS.Cli.Commands
{
    public class MemCommand : Command
    {
        public override string Name { get { return "mem"; } }
        public override string Description { get { return "Speicherübersicht: Rahmen frei und belegt"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int free = OS.Memory.Frames.FreeCount;
            shell.WriteLine("Rahmen gesamt: " + PhysicalMemory.FrameCount + " zu je " + PhysicalMemory.FrameSize + " Byte");
            shell.WriteLine("Belegt: " + (PhysicalMemory.FrameCount - free) + "   Frei: " + free);
        }
    }

    public class FramesCommand : Command
    {
        public override string Name { get { return "frames"; } }
        public override string Description { get { return "Rahmentabelle anzeigen (# belegt, . frei)"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int[] owner = OS.Memory.Frames.Owner;
            for (int row = 0; row < owner.Length; row += 64)
            {
                string line = "[";
                for (int i = row; i < row + 64 && i < owner.Length; i++) line += owner[i] == FrameTable.Free ? "." : "#";
                shell.WriteLine(line + "]");
            }
        }
    }

    public class AllocCommand : ProcessCommand
    {
        public override string Name { get { return "alloc"; } }
        public override string Usage { get { return "alloc <pid> <seiten>"; } }
        public override string Description { get { return "Einem Prozess Speicherseiten geben"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int count;
            if (args.Length < 2 || !int.TryParse(args[1], out count) || count < 1) { shell.Error("Aufruf: " + Usage); return; }
            Process p = Target(args, shell);
            if (p == null) return;
            // Ein beendeter Prozess gibt nichts mehr frei, seine Seiten blieben für immer belegt.
            if (p.State == ProcessState.Terminated) { shell.Error("Prozess " + p.Id + " ist beendet."); return; }
            int done = 0;
            while (done < count && OS.Memory.AllocatePage(p.Id) >= 0) done++;
            shell.WriteLine(done + " Seite(n) für Prozess " + p.Id + " belegt.");
            if (done < count) shell.Error("Speicher voll: Es ist kein Rahmen mehr frei.");
        }
    }

    public class PageTableCommand : ProcessCommand
    {
        public override string Name { get { return "pagetable"; } }
        public override string Usage { get { return "pagetable <pid>"; } }
        public override string Description { get { return "Seitentabelle eines Prozesses anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            Process p = Target(args, shell);
            if (p == null) return;
            PageTable table = OS.Memory.TableOf(p.Id);
            if (table == null || table.Pages.Count == 0) { shell.WriteLine("Prozess " + p.Id + " hat keine Seiten."); return; }
            shell.WriteLine("SEITE  RAHMEN  VORHANDEN  RECHTE  VIRTUELLE ADRESSEN", LineStyle.Muted);
            for (int i = 0; i < table.Pages.Count; i++)
            {
                PageEntry e = table.Pages[i];
                int start = i * PhysicalMemory.FrameSize;
                // Eine freigegebene Seite behält ihren Platz in der Tabelle, sonst verschöben sich alle Seitennummern danach.
                shell.WriteLine(Text.Pad(i.ToString(), 7)
                    + Text.Pad(e.Present ? e.Frame.ToString() : "-", 8)
                    + Text.Pad(e.Present ? "ja" : "nein", 11)
                    + Text.Pad(Rights(e.Access), 8)
                    + start + " bis " + (start + PhysicalMemory.FrameSize - 1));
            }
        }

        /// <summary>Rechte wie bei Unix: "rw-", "r-x" und so weiter.</summary>
        public static string Rights(int access)
        {
            return ((access & PageAccess.Read) != 0 ? "r" : "-")
                + ((access & PageAccess.Write) != 0 ? "w" : "-")
                + ((access & PageAccess.Execute) != 0 ? "x" : "-");
        }
    }

    public class FreeCommand : ProcessCommand
    {
        public override string Name { get { return "free"; } }
        public override string Usage { get { return "free <pid> <seite>"; } }
        public override string Description { get { return "Eine Speicherseite eines Prozesses freigeben"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int page;
            if (args.Length < 2 || !int.TryParse(args[1], out page)) { shell.Error("Aufruf: " + Usage); return; }
            Process p = Target(args, shell);
            if (p == null) return;
            PageTable table = OS.Memory.TableOf(p.Id);
            int frame = table != null && page >= 0 && page < table.Pages.Count ? table.Pages[page].Frame : -1;
            if (!OS.Memory.FreePage(p.Id, page)) { shell.Error("Prozess " + p.Id + " hat keine Seite " + page + "."); return; }
            shell.WriteLine("Seite " + page + " von Prozess " + p.Id + " freigegeben, " + (OS.Memory.Frames.Owner[frame] == FrameTable.Free
                ? "Rahmen " + frame + " ist wieder frei." : "Rahmen " + frame + " nutzt noch Prozess " + OS.Memory.Frames.Owner[frame] + " (gemeinsamer Speicher)."));
        }
    }

    public class TranslateCommand : ProcessCommand
    {
        public override string Name { get { return "translate"; } }
        public override string Usage { get { return "translate <pid> <adresse>"; } }
        public override string Description { get { return "Virtuelle Adresse Schritt für Schritt übersetzen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int address;
            if (args.Length < 2 || !ParseAddress(args[1], out address)) { shell.Error("Aufruf: " + Usage + " (Adresse dezimal oder mit 0x)"); return; }
            Process p = Target(args, shell);
            if (p == null) return;

            int size = PhysicalMemory.FrameSize;
            int page = address / size;
            int offset = address % size;
            shell.WriteLine("Virtuelle Adresse " + address + " (0x" + Text.Hex((ulong)address, 8) + ")");
            shell.WriteLine("1. Seite  = " + address + " / " + size + " = " + page);
            shell.WriteLine("2. Offset = " + address + " mod " + size + " = " + offset);

            PageTable table = OS.Memory.TableOf(p.Id);
            if (table == null || page >= table.Pages.Count || !table.Pages[page].Present)
            {
                // Nur anzeigen, nicht ins Protokoll: translate schaut nach, es greift nicht auf den Speicher zu.
                shell.Error("3. Seite " + page + " ist Prozess " + p.Id + " nicht zugeordnet: Page Fault.");
                return;
            }
            int frame = table.Pages[page].Frame;
            shell.WriteLine("3. Seitentabelle: Seite " + page + " liegt in Rahmen " + frame
                + " (" + PageTableCommand.Rights(table.Pages[page].Access) + ")");
            // Das Ergebnis rechnet der MemoryManager, damit translate genau das zeigt, was ReadByte und WriteByte tun.
            int physical = OS.Memory.Translate(p.Id, address, 0);
            shell.WriteLine("4. Physisch = " + frame + " * " + size + " + " + offset + " = " + physical
                + " (0x" + Text.Hex((ulong)physical, 8) + ")");
        }

        /// <summary>Liest eine Adresse dezimal ("4103") oder hexadezimal ("0x1007"). Negative Adressen gibt es nicht.</summary>
        public static bool ParseAddress(string s, out int value)
        {
            value = 0;
            if (s.Length > 2 && s[0] == '0' && (s[1] == 'x' || s[1] == 'X'))
            {
                if (s.Length > 9) return false; // mehr als 7 Hexziffern passt nicht sicher in int
                for (int i = 2; i < s.Length; i++)
                {
                    int d = "0123456789abcdefABCDEF".IndexOf(s[i]);
                    if (d < 0) return false;
                    if (d > 15) d -= 6;
                    value = value * 16 + d;
                }
                return true;
            }
            return int.TryParse(s, out value) && value >= 0;
        }
    }
}
