using System.Collections.Generic;

namespace VibanOS.Processes
{
    /// <summary>Austauschbare Scheduling-Strategie (VIB-15: FCFS, SJF, Prioritäten kommen als weitere Klassen dazu).</summary>
    public interface IScheduler
    {
        string Name { get; }

        /// <summary>Wählt aus den bereiten Prozessen (nie leer) den nächsten aus.</summary>
        Process PickNext(List<Process> ready);
    }

    /// <summary>Round Robin: Jeder Prozess bekommt Quantum Schritte, dann ist der nächste dran.</summary>
    public class RoundRobinScheduler : IScheduler
    {
        public int Quantum = 1;
        private int lastId = -1;
        private int used;

        public string Name { get { return "Round Robin"; } }

        public Process PickNext(List<Process> ready)
        {
            Process next = null;
            for (int i = 0; i < ready.Count; i++)
            {
                if (ready[i].Id == lastId && used < Quantum) { used++; return ready[i]; }
                if (next == null && ready[i].Id > lastId) next = ready[i];
            }
            if (next == null) next = ready[0];
            lastId = next.Id;
            used = 1;
            return next;
        }
    }
}
