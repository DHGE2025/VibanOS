using System.Collections.Generic;

namespace VibanOS.Memory
{
    /// <summary>Strategie, mit der eine Lücke für einen neuen Block gesucht wird (VIB-6).</summary>
    public enum FitStrategy { First, Best, Worst }

    /// <summary>Ein belegter oder freier Bereich: Start und Länge in Bytes, relativ zum Anfang des Bereichs.</summary>
    public class MemoryBlock
    {
        public int Id;        // 0 bei freien Bereichen
        public int Start;
        public int Length;    // tatsächlich belegt, auf Alignment aufgerundet
        public int Requested; // so viel wurde angefragt
    }

    /// <summary>
    /// Vergibt Blöcke variabler Größe in einem zusammenhängenden Bereich (VIB-6).
    /// Kennt nur Start und Länge, nicht die Bytes selbst: Die liegen im PhysicalMemory, das Kopieren beim
    /// Kompaktieren übernimmt der MemoryManager. So kann fragtest eigene Allokatoren nehmen, ohne den RAM anzufassen.
    /// </summary>
    public class BlockAllocator
    {
        /// <summary>Jeder Block wird auf ein Vielfaches davon aufgerundet, wie bei echten Allokatoren.</summary>
        public const int Alignment = 16;

        public readonly int Size;
        public FitStrategy Strategy = FitStrategy.First;

        /// <summary>Belegte Blöcke, nach Start sortiert.</summary>
        public readonly List<MemoryBlock> Used = new List<MemoryBlock>();

        /// <summary>Freie Bereiche, nach Start sortiert. Nachbarn werden immer zusammengefasst.</summary>
        public readonly List<MemoryBlock> Free = new List<MemoryBlock>();

        private int nextId = 1;

        public BlockAllocator(int size)
        {
            Size = size;
            MemoryBlock all = new MemoryBlock();
            all.Start = 0;
            all.Length = size;
            Free.Add(all);
        }

        public static int Round(int size)
        {
            return (size + Alignment - 1) / Alignment * Alignment;
        }

        /// <summary>Gibt einen neuen Block zurück, oder null, wenn keine Lücke groß genug ist.</summary>
        public MemoryBlock Allocate(int size)
        {
            if (size < 1 || size > Size) return null;
            int need = Round(size);
            int pick = -1;
            for (int i = 0; i < Free.Count; i++)
            {
                if (Free[i].Length < need) continue;
                if (pick < 0) { pick = i; if (Strategy == FitStrategy.First) break; continue; }
                if (Strategy == FitStrategy.Best && Free[i].Length < Free[pick].Length) pick = i;
                if (Strategy == FitStrategy.Worst && Free[i].Length > Free[pick].Length) pick = i;
            }
            if (pick < 0) return null;

            MemoryBlock hole = Free[pick];
            MemoryBlock block = new MemoryBlock();
            block.Id = nextId++;
            block.Start = hole.Start;
            block.Length = need;
            block.Requested = size;
            // Der Block nimmt den Anfang der Lücke, der Rest bleibt frei.
            hole.Start += need;
            hole.Length -= need;
            if (hole.Length == 0) Free.RemoveAt(pick);
            Insert(Used, block);
            return block;
        }

        public MemoryBlock Get(int id)
        {
            for (int i = 0; i < Used.Count; i++)
                if (Used[i].Id == id) return Used[i];
            return null;
        }

        public bool Release(int id)
        {
            MemoryBlock block = Get(id);
            if (block == null) return false;
            Used.Remove(block);
            MemoryBlock hole = new MemoryBlock();
            hole.Start = block.Start;
            hole.Length = block.Length;
            Insert(Free, hole);
            Merge();
            return true;
        }

        /// <summary>
        /// Schiebt alle belegten Blöcke an den Anfang, damit eine einzige große Lücke entsteht.
        /// Ruft move(alterStart, neuerStart, länge) für jeden verschobenen Block, damit die Daten mitwandern.
        /// </summary>
        public int Compact(System.Action<int, int, int> move)
        {
            int at = 0, moved = 0;
            for (int i = 0; i < Used.Count; i++)
            {
                MemoryBlock b = Used[i];
                if (b.Start != at)
                {
                    if (move != null) move(b.Start, at, b.Length);
                    b.Start = at;
                    moved++;
                }
                at += b.Length;
            }
            Free.Clear();
            if (at < Size)
            {
                MemoryBlock rest = new MemoryBlock();
                rest.Start = at;
                rest.Length = Size - at;
                Free.Add(rest);
            }
            return moved;
        }

        public int FreeBytes
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < Free.Count; i++) sum += Free[i].Length;
                return sum;
            }
        }

        public int LargestHole
        {
            get
            {
                int max = 0;
                for (int i = 0; i < Free.Count; i++) if (Free[i].Length > max) max = Free[i].Length;
                return max;
            }
        }

        /// <summary>
        /// Externe Fragmentierung in Prozent: 1 - größte Lücke / freier Speicher.
        /// 0: Der freie Speicher liegt am Stück. Nahe 100: Er ist in viele kleine Lücken zerfallen.
        /// </summary>
        public int ExternalPercent
        {
            get
            {
                int free = FreeBytes;
                return free == 0 ? 0 : 100 - LargestHole * 100 / free;
            }
        }

        /// <summary>Interne Fragmentierung: Bytes, die durch das Aufrunden der Blöcke verloren gehen.</summary>
        public int InternalBytes
        {
            get
            {
                int sum = 0;
                for (int i = 0; i < Used.Count; i++) sum += Used[i].Length - Used[i].Requested;
                return sum;
            }
        }

        /// <summary>Zum Vergleich: So viel ginge verloren, wenn jeder Block ganze 4-KB-Seiten bekäme.</summary>
        public int InternalBytesWithPages
        {
            get
            {
                int sum = 0, page = PhysicalMemory.FrameSize;
                for (int i = 0; i < Used.Count; i++)
                    sum += (Used[i].Requested + page - 1) / page * page - Used[i].Requested;
                return sum;
            }
        }

        private static void Insert(List<MemoryBlock> list, MemoryBlock block)
        {
            int i = 0;
            while (i < list.Count && list[i].Start < block.Start) i++;
            list.Insert(i, block);
        }

        /// <summary>Fasst benachbarte freie Bereiche zusammen. Ohne das zerfiele der Speicher mit jedem Freigeben weiter.</summary>
        private void Merge()
        {
            for (int i = Free.Count - 1; i > 0; i--)
            {
                if (Free[i - 1].Start + Free[i - 1].Length != Free[i].Start) continue;
                Free[i - 1].Length += Free[i].Length;
                Free.RemoveAt(i);
            }
        }
    }
}
