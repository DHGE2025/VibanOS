using System;
using System.Collections.Generic;
using VibanOS.Core;

namespace VibanOS.Processes
{
    /// <summary>Prozessliste und Scheduler-Schleife (VIB-3).</summary>
    public class ProcessManager
    {
        public readonly List<Process> All = new List<Process>();
        public IScheduler Scheduler = new RoundRobinScheduler();

        /// <summary>Der Prozess, dessen Step() gerade läuft. null bedeutet: Kernel bzw. Shell.</summary>
        public Process Current;

        private readonly List<Process> ready = new List<Process>();
        private int nextId = 1;

        public Process Start(Process process)
        {
            process.Id = nextId++;
            process.StartTime = DateTime.Now;
            if (OS.Users.Current != null) process.Owner = OS.Users.Current.Name;
            OS.Memory.AllocatePage(process.Id);
            All.Add(process);
            OS.Changed = true;
            return process;
        }

        public Process Get(int id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : All[i];
        }

        // Von Hand statt List.Remove/Contains: deren Vergleichslogik läuft unter COSMOS nicht zuverlässig.
        private int IndexOf(int id)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Id == id) return i;
            return -1;
        }

        /// <summary>Beendet einen Prozess und entfernt ihn aus der Liste.</summary>
        public bool Kill(int id)
        {
            Process p = Get(id);
            if (p == null) return false;
            p.State = ProcessState.Terminated;
            OS.Memory.FreeAll(id);
            All.RemoveAt(IndexOf(id));
            OS.Changed = true;
            return true;
        }

        /// <summary>Lässt genau einen bereiten Prozess einen Schritt arbeiten.</summary>
        public void Tick()
        {
            ready.Clear();
            for (int i = 0; i < All.Count; i++)
                if (All[i].State == ProcessState.Ready) ready.Add(All[i]);
            if (ready.Count == 0) return;

            Process next = Scheduler.PickNext(ready);
            for (int i = 0; i < ready.Count; i++)
                if (ready[i] != next) ready[i].WaitSteps++;

            Current = next;
            next.State = ProcessState.Running;
            try
            {
                next.Step();
            }
            catch (Exception e)
            {
                // Ein fehlerhafter Prozess (z. B. Schutzverletzung, VIB-7) darf das System nicht mitreißen.
                next.Output = "Abbruch: " + e.Message;
                next.State = ProcessState.Terminated;
            }
            Current = null;
            next.Steps++;
            if (next.State == ProcessState.Running) next.State = ProcessState.Ready;
            if (next.State == ProcessState.Terminated) OS.Memory.FreeAll(next.Id);
        }
    }
}
