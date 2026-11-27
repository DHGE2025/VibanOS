using System;
using System.Collections.Generic;
using VibanOS.Core;

namespace VibanOS.Memory
{
    /// <summary>
    /// Seitenverwaltung auf dem simulierten RAM (VIB-5) mit geprüften Zugriffen (VIB-7).
    /// VIB-6 (Blöcke variabler Größe, First/Best/Worst Fit) liegt in eigenen Rahmen auf demselben PhysicalMemory.
    /// </summary>
    public class MemoryManager
    {
        public readonly PhysicalMemory Physical = new PhysicalMemory();
        public readonly FrameTable Frames = new FrameTable();
        public readonly List<PageTable> Tables = new List<PageTable>();

        /// <summary>Rahmen für die Blöcke variabler Größe (VIB-6): 8 Rahmen = 32 KB.</summary>
        public const int HeapFrames = 8;

        /// <summary>Blöcke variabler Größe (VIB-6). Die Rahmen dafür werden erst beim ersten malloc belegt.</summary>
        public readonly BlockAllocator Heap = new BlockAllocator(HeapFrames * PhysicalMemory.FrameSize);

        /// <summary>Erster Rahmen des Blockbereichs, -1 solange kein Block existiert.</summary>
        public int HeapFrame = -1;

        /// <summary>Protokoll der abgelehnten Zugriffe.</summary>
        public readonly List<string> Log = new List<string>();

        public PageTable TableOf(int pid)
        {
            int i = IndexOf(pid);
            return i < 0 ? null : Tables[i];
        }

        private int IndexOf(int pid)
        {
            for (int i = 0; i < Tables.Count; i++)
                if (Tables[i].ProcessId == pid) return i;
            return -1;
        }

        /// <summary>Gibt dem Prozess eine neue Seite. Rückgabe: Seitennummer oder -1, wenn kein Rahmen frei ist.</summary>
        public int AllocatePage(int pid)
        {
            int frame = Frames.Allocate(pid);
            if (frame < 0) return -1;
            // Ein neuer Rahmen ist immer leer: Sonst läse ein Prozess, was sein Vorgänger dort hinterlassen hat.
            Array.Clear(Physical.Data, frame * PhysicalMemory.FrameSize, PhysicalMemory.FrameSize);
            PageTable table = TableOf(pid);
            if (table == null)
            {
                table = new PageTable(pid);
                Tables.Add(table);
            }
            OS.Changed = true;
            for (int i = 0; i < table.Pages.Count; i++)
            {
                if (table.Pages[i].Present) continue;
                table.Pages[i].Frame = frame;
                table.Pages[i].Present = true;
                table.Pages[i].Access = PageAccess.ReadWrite; // nicht die Rechte der früheren Seite erben
                return i;
            }
            PageEntry entry = new PageEntry();
            entry.Frame = frame;
            entry.Present = true;
            table.Pages.Add(entry);
            return table.Pages.Count - 1;
        }

        public bool FreePage(int pid, int page)
        {
            PageTable table = TableOf(pid);
            if (table == null || page < 0 || page >= table.Pages.Count || !table.Pages[page].Present) return false;
            Frames.Release(table.Pages[page].Frame);
            table.Pages[page].Present = false;
            OS.Changed = true;
            return true;
        }

        public void FreeAll(int pid)
        {
            PageTable table = TableOf(pid);
            if (table == null) return;
            for (int i = 0; i < table.Pages.Count; i++) FreePage(pid, i);
            Tables.RemoveAt(IndexOf(pid));
        }

        /// <summary>
        /// Übersetzt eine virtuelle Adresse in eine physische: (Seite, Offset) → Rahmen * Rahmengröße + Offset.
        /// Prüft dabei Zuordnung und Rechte, sonst MemoryAccessException.
        /// </summary>
        public int Translate(int pid, int address, int access)
        {
            int page = address / PhysicalMemory.FrameSize;
            int offset = address % PhysicalMemory.FrameSize;
            PageTable table = TableOf(pid);
            if (address < 0 || table == null || page >= table.Pages.Count || !table.Pages[page].Present)
                throw Fault("Page Fault: Prozess " + pid + ", Adresse " + address + " ist nicht zugeordnet");
            if ((table.Pages[page].Access & access) != access)
                throw Fault("Schutzverletzung: Prozess " + pid + ", Seite " + page + " erlaubt diesen Zugriff nicht");
            return table.Pages[page].Frame * PhysicalMemory.FrameSize + offset;
        }

        public byte ReadByte(int pid, int address)
        {
            return Physical.Data[Translate(pid, address, PageAccess.Read)];
        }

        public void WriteByte(int pid, int address, byte value)
        {
            Physical.Data[Translate(pid, address, PageAccess.Write)] = value;
        }

        /// <summary>
        /// Vergibt einen Block variabler Größe (VIB-6). null: Es gibt keine passende Lücke, oder für den
        /// Blockbereich sind nicht genug Rahmen am Stück frei (dann ist HeapFrame noch -1).
        /// </summary>
        public MemoryBlock Malloc(int size)
        {
            if (HeapFrame < 0)
            {
                HeapFrame = Frames.AllocateRange(HeapFrames, FrameTable.Kernel);
                if (HeapFrame < 0) return null;
            }
            MemoryBlock block = Heap.Allocate(size);
            if (block != null) Array.Clear(Physical.Data, HeapAddress(block), block.Length);
            else ReleaseHeapIfEmpty();
            OS.Changed = true;
            return block;
        }

        public bool MFree(int id)
        {
            if (!Heap.Release(id)) return false;
            ReleaseHeapIfEmpty();
            OS.Changed = true;
            return true;
        }

        /// <summary>Schiebt die Blöcke zusammen, die Bytes wandern im simulierten RAM mit. Rückgabe: verschobene Blöcke.</summary>
        public int Compact()
        {
            int moved = Heap.Compact((from, to, length) =>
                Array.Copy(Physical.Data, HeapFrame * PhysicalMemory.FrameSize + from, Physical.Data, HeapFrame * PhysicalMemory.FrameSize + to, length));
            OS.Changed = true;
            return moved;
        }

        /// <summary>Physische Adresse eines Blocks im simulierten RAM.</summary>
        public int HeapAddress(MemoryBlock block)
        {
            return HeapFrame * PhysicalMemory.FrameSize + block.Start;
        }

        // Ohne Blöcke braucht der Bereich seine Rahmen nicht, dann bekommen sie wieder die Prozesse.
        private void ReleaseHeapIfEmpty()
        {
            if (HeapFrame < 0 || Heap.Used.Count > 0) return;
            for (int i = 0; i < HeapFrames; i++) Frames.Release(HeapFrame + i);
            HeapFrame = -1;
        }

        private MemoryAccessException Fault(string message)
        {
            Log.Add(message);
            return new MemoryAccessException(message);
        }
    }
}
