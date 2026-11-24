using System.Collections.Generic;
using VibanOS.Core;

namespace VibanOS.Memory
{
    /// <summary>
    /// Seitenverwaltung auf dem simulierten RAM (VIB-5) mit geprüften Zugriffen (VIB-7).
    /// VIB-6 (Blöcke variabler Größe, First/Best/Worst Fit) setzt auf demselben PhysicalMemory auf.
    /// </summary>
    public class MemoryManager
    {
        public readonly PhysicalMemory Physical = new PhysicalMemory();
        public readonly FrameTable Frames = new FrameTable();
        public readonly List<PageTable> Tables = new List<PageTable>();

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

        private MemoryAccessException Fault(string message)
        {
            Log.Add(message);
            return new MemoryAccessException(message);
        }
    }
}
