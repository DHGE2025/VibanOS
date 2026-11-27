using System;
using System.Collections.Generic;

namespace VibanOS.Memory
{
    /// <summary>Simulierter RAM (VIB-5): ein byte[], aufgeteilt in gleich große Rahmen.</summary>
    public class PhysicalMemory
    {
        public const int FrameSize = 4096;
        public const int FrameCount = 256; // 1 MB

        public readonly byte[] Data = new byte[FrameSize * FrameCount];
    }

    /// <summary>Rahmentabelle: pro Rahmen der Besitzer (Prozess-ID) oder Free.</summary>
    public class FrameTable
    {
        public const int Free = -1;

        /// <summary>Besitzer der Rahmen, in denen die Blöcke aus VIB-6 liegen. Prozess-IDs beginnen bei 1.</summary>
        public const int Kernel = 0;

        public readonly int[] Owner = new int[PhysicalMemory.FrameCount];
        public int FreeCount = PhysicalMemory.FrameCount;

        public FrameTable()
        {
            for (int i = 0; i < Owner.Length; i++) Owner[i] = Free;
        }

        /// <summary>Belegt den ersten freien Rahmen. Gibt -1 zurück, wenn der Speicher voll ist.</summary>
        public int Allocate(int pid)
        {
            for (int i = 0; i < Owner.Length; i++)
            {
                if (Owner[i] != Free) continue;
                Owner[i] = pid;
                FreeCount--;
                return i;
            }
            return -1;
        }

        /// <summary>
        /// Belegt count Rahmen am Stück, für Blöcke, die über Rahmengrenzen reichen. Gibt den ersten zurück,
        /// oder -1, wenn kein so langer freier Abschnitt da ist.
        /// </summary>
        public int AllocateRange(int count, int owner)
        {
            int run = 0;
            for (int i = 0; i < Owner.Length; i++)
            {
                run = Owner[i] == Free ? run + 1 : 0;
                if (run < count) continue;
                int first = i - count + 1;
                for (int j = first; j <= i; j++) Owner[j] = owner;
                FreeCount -= count;
                return first;
            }
            return -1;
        }

        public void Release(int frame)
        {
            if (Owner[frame] == Free) return;
            Owner[frame] = Free;
            FreeCount++;
        }
    }

    /// <summary>Zugriffsrechte einer Seite (VIB-7), als Bits kombinierbar.</summary>
    public static class PageAccess
    {
        public const int Read = 1;
        public const int Write = 2;
        public const int Execute = 4;
        public const int ReadWrite = Read | Write;
    }

    public class PageEntry
    {
        public int Frame;
        public bool Present;
        public int Access = PageAccess.ReadWrite;
    }

    /// <summary>Seitentabelle eines Prozesses: Seitennummer (Index) → Rahmen.</summary>
    public class PageTable
    {
        public readonly int ProcessId;
        public readonly List<PageEntry> Pages = new List<PageEntry>();

        public PageTable(int processId) { ProcessId = processId; }
    }

    /// <summary>Page Fault oder Schutzverletzung.</summary>
    public class MemoryAccessException : Exception
    {
        public MemoryAccessException(string message) : base(message) { }
    }
}
