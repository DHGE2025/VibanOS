using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.Processes;

namespace VibanOS.Sync
{
    /// <summary>
    /// Sperre für eine gemeinsam genutzte Ressource (VIB-8). Ist sie belegt, wird der Prozess
    /// blockiert und wartet in der Warteschlange. Ein Prozess ruft Lock in jedem Schritt auf, bis er
    /// true bekommt. Deadlock-Erkennung und Banker-Algorithmus werten später Owner und Waiting aus.
    /// ponytail: Es gibt noch kein Verzeichnis aller Sperren. Stirbt der Besitzer, wird die Sperre erst
    /// beim nächsten Lock-Aufruf frei. VIB-8 führt das Verzeichnis ein und gibt Sperren beim Beenden frei.
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
            if (Owner != Nobody && !Alive(Owner)) Unlock(Owner); // Der Besitzer wurde beendet, ohne freizugeben.
            if (Owner == Nobody || Owner == pid) { Owner = pid; return true; }
            if (!Waiting.Contains(pid)) Waiting.Add(pid);
            SetState(pid, ProcessState.Blocked);
            return false;
        }

        /// <summary>Gibt die Sperre frei und reicht sie an den ersten Wartenden weiter, den es noch gibt.</summary>
        public void Unlock(int pid)
        {
            if (Owner != pid) return;
            Owner = WakeFirst(Waiting);
        }

        /// <summary>Weckt den ersten noch lebenden Prozess der Warteschlange. Rückgabe: seine ID oder Nobody.</summary>
        internal static int WakeFirst(List<int> waiting)
        {
            while (waiting.Count > 0)
            {
                int pid = waiting[0];
                waiting.RemoveAt(0);
                if (!Alive(pid)) continue; // Beendete Prozesse überspringen, sonst bekäme ein toter Prozess die Sperre.
                SetState(pid, ProcessState.Ready);
                return pid;
            }
            return Nobody;
        }

        internal static bool Alive(int pid)
        {
            Process p = OS.Processes.Get(pid);
            return p != null && p.State != ProcessState.Terminated;
        }

        internal static void SetState(int pid, ProcessState state)
        {
            if (Alive(pid)) OS.Processes.Get(pid).State = state;
        }
    }

    /// <summary>Zählende Semaphore (VIB-8). Ein Prozess ruft Wait in jedem Schritt auf, bis er true bekommt.</summary>
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
            if (!Waiting.Contains(pid)) Waiting.Add(pid);
            Mutex.SetState(pid, ProcessState.Blocked);
            return false;
        }

        public void Signal()
        {
            // Immer zählen: Der geweckte Prozess holt sich die Freigabe mit seinem nächsten Wait selbst ab.
            Count++;
            Mutex.WakeFirst(Waiting);
        }
    }
}
