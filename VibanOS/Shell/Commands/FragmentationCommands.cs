using VibanOS.Core;
using VibanOS.Memory;

namespace VibanOS.Cli.Commands
{
    /// <summary>Hilfen für die Befehle aus VIB-6: Größen lesen und den Blockbereich als Zeichenkette malen.</summary>
    public static class Fragmentation
    {
        /// <summary>Liest "3000" (Byte) oder "4K" (Kilobyte).</summary>
        public static bool ParseSize(string s, out int size)
        {
            size = 0;
            int factor = 1;
            if (s.Length > 1 && (s[s.Length - 1] == 'k' || s[s.Length - 1] == 'K'))
            {
                factor = 1024;
                s = s.Substring(0, s.Length - 1);
            }
            if (!int.TryParse(s, out size) || size < 1 || size > 1024 * 1024) return false;
            size *= factor;
            return true;
        }

        public static char Letter(int id)
        {
            return (char)('A' + (id - 1) % 26);
        }

        /// <summary>
        /// Ein Zeichen pro Abschnitt: der Buchstabe des Blocks, der den größten Teil davon belegt, sonst ein Punkt.
        /// Sehr kleine Blöcke können so unsichtbar sein, die Liste darunter zeigt sie trotzdem.
        /// </summary>
        public static string Map(BlockAllocator heap, int cells)
        {
            int cell = heap.Size / cells;
            string line = "[";
            for (int c = 0; c < cells; c++)
            {
                int from = c * cell, to = from + cell, best = 0;
                char ch = '.';
                for (int i = 0; i < heap.Used.Count; i++)
                {
                    MemoryBlock b = heap.Used[i];
                    int overlap = (b.Start + b.Length < to ? b.Start + b.Length : to) - (b.Start > from ? b.Start : from);
                    if (overlap * 2 >= cell && overlap > best) { best = overlap; ch = Letter(b.Id); }
                }
                line += ch;
            }
            return line + "]";
        }

        public static string Name(FitStrategy s)
        {
            return s == FitStrategy.First ? "First Fit" : s == FitStrategy.Best ? "Best Fit" : "Worst Fit";
        }
    }

    public class MallocCommand : Command
    {
        public override string Name { get { return "malloc"; } }
        public override string Usage { get { return "malloc <größe>"; } }
        public override string Description { get { return "Block variabler Größe belegen (Byte, oder z. B. 4K)"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int size;
            if (args.Length < 1 || !Fragmentation.ParseSize(args[0], out size)) { shell.Error("Aufruf: " + Usage); return; }
            MemoryBlock b = OS.Memory.Malloc(size);
            if (b == null && OS.Memory.HeapFrame < 0)
            {
                shell.Error("Für den Blockbereich sind keine " + MemoryManager.HeapFrames + " Rahmen am Stück frei.");
                return;
            }
            if (b == null)
            {
                BlockAllocator heap = OS.Memory.Heap;
                // Genau das ist externe Fragmentierung: genug frei, aber nicht am Stück.
                string why = heap.FreeBytes >= BlockAllocator.Round(size)
                    ? " Frei sind " + Text.Bytes((ulong)heap.FreeBytes) + ", aber die größte Lücke hat nur " + Text.Bytes((ulong)heap.LargestHole) + "."
                    : " Frei sind nur " + Text.Bytes((ulong)heap.FreeBytes) + ".";
                shell.Error("Keine passende Lücke für " + Text.Bytes((ulong)size) + "." + why);
                return;
            }
            shell.WriteLine("Block " + b.Id + " (" + Fragmentation.Letter(b.Id) + "): " + b.Length + " Byte ab Adresse " + OS.Memory.HeapAddress(b)
                + (b.Length != size ? ", angefragt " + size : "") + " [" + Fragmentation.Name(OS.Memory.Heap.Strategy) + "]");
        }
    }

    public class MfreeCommand : Command
    {
        public override string Name { get { return "mfree"; } }
        public override string Usage { get { return "mfree <id>"; } }
        public override string Description { get { return "Block wieder freigeben"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int id;
            if (args.Length < 1 || !int.TryParse(args[0], out id)) { shell.Error("Aufruf: " + Usage); return; }
            MemoryBlock b = OS.Memory.Heap.Get(id);
            int length = b == null ? 0 : b.Length;
            if (!OS.Memory.MFree(id)) { shell.Error("Es gibt keinen Block " + id + "."); return; }
            shell.WriteLine("Block " + id + " freigegeben (" + length + " Byte).");
        }
    }

    public class MemmapCommand : Command
    {
        public override string Name { get { return "memmap"; } }
        public override string Description { get { return "Blockbereich grafisch anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            BlockAllocator heap = OS.Memory.Heap;
            shell.WriteLine(Fragmentation.Map(heap, 64));
            shell.WriteLine("Ein Zeichen = " + heap.Size / 64 + " Byte, " + Fragmentation.Name(heap.Strategy)
                + (OS.Memory.HeapFrame < 0 ? ", noch keine Rahmen belegt" : ", Rahmen " + OS.Memory.HeapFrame + " bis " + (OS.Memory.HeapFrame + MemoryManager.HeapFrames - 1)), LineStyle.Muted);
            for (int i = 0; i < heap.Used.Count; i++)
            {
                MemoryBlock b = heap.Used[i];
                shell.WriteLine(Fragmentation.Letter(b.Id) + "  Block " + Text.Pad(b.Id.ToString(), 4) + "Start " + Text.Pad(b.Start.ToString(), 7)
                    + b.Length + " Byte" + (b.Length != b.Requested ? " (angefragt " + b.Requested + ")" : ""));
            }
        }
    }

    public class StrategyCommand : Command
    {
        public override string Name { get { return "strategy"; } }
        public override string Usage { get { return "strategy first|best|worst"; } }
        public override string Description { get { return "Vergabestrategie für malloc zeigen oder umschalten"; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 0) { shell.WriteLine("Strategie: " + Fragmentation.Name(OS.Memory.Heap.Strategy)); return; }
            if (args[0] == "first") OS.Memory.Heap.Strategy = FitStrategy.First;
            else if (args[0] == "best") OS.Memory.Heap.Strategy = FitStrategy.Best;
            else if (args[0] == "worst") OS.Memory.Heap.Strategy = FitStrategy.Worst;
            else { shell.Error("Aufruf: " + Usage); return; }
            shell.WriteLine("Strategie: " + Fragmentation.Name(OS.Memory.Heap.Strategy));
        }
    }

    public class FragstatCommand : Command
    {
        public override string Name { get { return "fragstat"; } }
        public override string Description { get { return "Kennzahlen zur Fragmentierung"; } }

        public override void Execute(string[] args, Shell shell)
        {
            BlockAllocator h = OS.Memory.Heap;
            shell.WriteLine("Blöcke: " + h.Used.Count + "   Lücken: " + h.Free.Count + "   Frei: " + Text.Bytes((ulong)h.FreeBytes)
                + "   Größte Lücke: " + Text.Bytes((ulong)h.LargestHole));
            shell.WriteLine("Externe Fragmentierung: " + h.ExternalPercent + " %  (1 - größte Lücke / freier Speicher)");
            shell.WriteLine("Interne Fragmentierung: " + h.InternalBytes + " Byte durch Aufrunden auf " + BlockAllocator.Alignment
                + " Byte, mit ganzen 4-KB-Seiten wären es " + h.InternalBytesWithPages + " Byte");
        }
    }

    public class CompactCommand : Command
    {
        public override string Name { get { return "compact"; } }
        public override string Description { get { return "Blöcke zusammenschieben, damit eine große Lücke entsteht"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int moved = OS.Memory.Compact();
            shell.WriteLine(moved + " Block/Blöcke verschoben. Größte Lücke jetzt " + Text.Bytes((ulong)OS.Memory.Heap.LargestHole) + ".");
            if (moved > 0) shell.WriteLine("Die Adressen der verschobenen Blöcke haben sich geändert.", LineStyle.Muted);
        }
    }

    public class FragtestCommand : Command
    {
        public override string Name { get { return "fragtest"; } }
        public override string Description { get { return "Gleiche Anfragen mit First, Best und Worst Fit vergleichen"; } }

        // Erst füllen, dann Lücken reißen, dann neue Anfragen, die in die Lücken passen müssen. In KB.
        private static readonly int[] Fill = { 4, 2, 6, 1, 3, 5, 2, 4 };
        private static readonly int[] FreeAgain = { 2, 4, 6 }; // Blöcke 2, 4 und 6 (2 KB, 1 KB, 5 KB)
        private static readonly int[] Requests = { 1, 2, 4, 5, 3 };

        public override void Execute(string[] args, Shell shell)
        {
            shell.WriteLine("32 KB füllen mit 4, 2, 6, 1, 3, 5, 2, 4 KB, dann die Blöcke mit 2, 1 und 5 KB freigeben.", LineStyle.Muted);
            shell.WriteLine("Danach anfragen: 1, 2, 4, 5, 3 KB. Ein Zeichen = 512 Byte.", LineStyle.Muted);
            shell.WriteLine("");
            FitStrategy[] all = { FitStrategy.First, FitStrategy.Best, FitStrategy.Worst };
            for (int s = 0; s < all.Length; s++)
            {
                int failed;
                BlockAllocator h = Run(all[s], out failed);
                shell.WriteLine(Text.Pad(Fragmentation.Name(all[s]), 11) + Fragmentation.Map(h, 64));
                shell.WriteLine(Text.Pad("", 11) + "abgelehnt: " + failed + "   frei: " + Text.Bytes((ulong)h.FreeBytes)
                    + "   größte Lücke: " + Text.Bytes((ulong)h.LargestHole) + "   extern: " + h.ExternalPercent + " %");
            }
        }

        /// <summary>Spielt die Folge mit einer Strategie auf einem eigenen Bereich durch. Der echte Speicher bleibt unberührt.</summary>
        public static BlockAllocator Run(FitStrategy strategy, out int failed)
        {
            failed = 0;
            BlockAllocator h = new BlockAllocator(MemoryManager.HeapFrames * PhysicalMemory.FrameSize);
            h.Strategy = strategy;
            for (int i = 0; i < Fill.Length; i++) h.Allocate(Fill[i] * 1024);
            for (int i = 0; i < FreeAgain.Length; i++) h.Release(FreeAgain[i]);
            for (int i = 0; i < Requests.Length; i++) if (h.Allocate(Requests[i] * 1024) == null) failed++;
            return h;
        }
    }
}
