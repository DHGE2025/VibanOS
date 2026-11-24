using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.Processes;

namespace VibanOS.Sync
{
    /// <summary>
    /// Sperre für eine gemeinsam genutzte Ressource (VIB-8). Ist sie belegt, wird der Prozess
    /// blockiert und wartet in der Warteschlange. Deadlock-Erkennung und Banker-Algorithmus
    /// werten später Owner und Waiting aller Mutexe aus.
    /// </summary>
    public class Mutex
    {
        public const int Nobody = -1;

        public readonly string Name;
        public int Owner = Nobody;
        public readonly List<int> Waiting = new List<int>();

        public Mutex(string name) { Name = name; }

        /// <summary>true: Sperre erhalten. false: Prozess wurde blockiert und wartet.</summary>
        public bool Lock(int pid)
        {
            if (Owner == Nobody || Owner == pid) { Owner = pid; return true; }
            Waiting.Add(pid);
            SetState(pid, ProcessState.Blocked);
            return false;
        }

        public void Unlock(int pid)
        {
            if (Owner != pid) return;
            Owner = Nobody;
            if (Waiting.Count == 0) return;
            Owner = Waiting[0];
            Waiting.RemoveAt(0);
            SetState(Owner, ProcessState.Ready);
        }

        internal static void SetState(int pid, ProcessState state)
        {
            Process p = OS.Processes.Get(pid);
            if (p != null && p.State != ProcessState.Terminated) p.State = state;
        }
    }

    /// <summary>Zählende Semaphore (VIB-8).</summary>
    public class Semaphore
    {
        public readonly string Name;
        public int Count;
        public readonly List<int> Waiting = new List<int>();

        public Semaphore(string name, int count) { Name = name; Count = count; }

        /// <summary>true: durchgelassen. false: Prozess wurde blockiert und wartet.</summary>
        public bool Wait(int pid)
        {
            if (Count > 0) { Count--; return true; }
            Waiting.Add(pid);
            Mutex.SetState(pid, ProcessState.Blocked);
            return false;
        }

        public void Signal()
        {
            if (Waiting.Count == 0) { Count++; return; }
            int pid = Waiting[0];
            Waiting.RemoveAt(0);
            Mutex.SetState(pid, ProcessState.Ready);
        }
    }
}
