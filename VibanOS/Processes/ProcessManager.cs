using System;
using System.Collections.Generic;
using VibanOS.Core;

namespace VibanOS.Processes
{
    /// <summary>Prozessliste und Scheduler-Schleife (VIB-3). Den Wechsel zwischen Kernel und Prozess erledigt Context.</summary>
    public class ProcessManager
    {
        public readonly List<Process> All = new List<Process>();
        public IScheduler Scheduler = new RoundRobinScheduler();

        /// <summary>
        /// So viele Prozesse gibt es höchstens zugleich, beendete in der Liste zählen mit. Jeder laufende hat einen
        /// Thread mit eigenem Stack, und COSMOS verwaltet höchstens 256 Threads: Darüber kennt der Garbage Collector
        /// die Stacks nicht mehr.
        /// </summary>
        public const int MaxProcesses = 64;

        /// <summary>Der Prozess, der gerade die CPU hat. null bedeutet: Kernel bzw. Shell.</summary>
        public Process Current;

        private readonly List<Process> ready = new List<Process>();
        // Mit kill entfernte Prozesse, deren Programm noch mitten im Lauf wartet. Tick baut ihren Stack ab.
        private readonly List<Process> dying = new List<Process>();
        private int nextId = 1;

        /// <summary>
        /// Nimmt einen Prozess auf und gibt ihm eine erste Speicherseite. null: Der Speicher ist voll, oder die
        /// Liste hat schon MaxProcesses Einträge.
        /// </summary>
        public Process Start(Process process)
        {
            if (All.Count >= MaxProcesses) return null;
            process.Id = nextId++;
            if (OS.Memory.AllocatePage(process.Id) < 0) return null;
            process.StartTime = DateTime.Now;
            // Besitzer ist, wer den Prozess startet: der gerade laufende Prozess, sonst der angemeldete Nutzer.
            if (Current != null) process.Owner = Current.Owner;
            else if (OS.Users.Current != null) process.Owner = OS.Users.Current.Name;
            All.Add(process);
            OS.Changed = true;
            return process;
        }

        public Process Get(int id)
        {
            int i = IndexOf(id);
            return i < 0 ? null : All[i];
        }

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
            if (p.Context != null) dying.Add(p);
            OS.Changed = true;
            return true;
        }

        /// <summary>
        /// Der laufende Prozess gibt die CPU ab: Sein Stack und seine Register bleiben stehen, der Kernel läuft
        /// weiter. Yield kehrt zurück, sobald der Scheduler den Prozess wieder wählt. Ist er dann beendet (kill,
        /// Signal Term, exit), wirft Yield ProcessKilled und baut so seinen Stack ab. Aus dem Kernel oder der
        /// Shell aufgerufen, tut Yield nichts.
        /// </summary>
        public void Yield()
        {
            Process p = Current;
            if (p == null) return;
            if (p.State != ProcessState.Terminated) p.Context.SwitchToKernel();
            if (p.State == ProcessState.Terminated) p.Context.Unwind();
        }

        /// <summary>
        /// Blockiert einen Prozess: Er bekommt die CPU nicht mehr, bis ihn Unblock weckt. Blockiert sich der
        /// laufende Prozess selbst, gibt er sie sofort ab, und Block kehrt erst nach dem Wecken zurück. Wer so
        /// auf etwas wartet (Sperre, Nachricht), prüft danach erneut, ob es da ist. false: Er war beendet oder schon
        /// blockiert. Der laufende Prozess gibt die CPU auch dann ab, sonst drehte sich seine Warteschleife für immer.
        /// </summary>
        public bool Block(Process p)
        {
            bool changed = p.State != ProcessState.Terminated && p.State != ProcessState.Blocked;
            if (changed)
            {
                p.State = ProcessState.Blocked;
                OS.Changed = true;
            }
            if (p == Current) Yield();
            return changed;
        }

        /// <summary>Weckt einen blockierten Prozess, er ist danach wieder bereit. false: Er war nicht blockiert.</summary>
        public bool Unblock(Process p)
        {
            if (p.State != ProcessState.Blocked) return false;
            p.State = ProcessState.Ready;
            OS.Changed = true;
            return true;
        }

        /// <summary>
        /// Gibt genau einem bereiten Prozess die CPU, bis er sie abgibt. Das ist ein echter Kontextwechsel: Der
        /// Kernel bleibt hier stehen, der Prozess läuft auf seinem eigenen Stack weiter. false: Es gab keinen Wechsel,
        /// weil keiner bereit war oder der gewählte kein Programm hat (Process.Passive).
        /// </summary>
        public bool Tick()
        {
            Reap();
            ready.Clear();
            for (int i = 0; i < All.Count; i++)
                if (All[i].State == ProcessState.Ready && !All[i].Stopped) ready.Add(All[i]);
            if (ready.Count == 0) return false;

            Process next = Scheduler.PickNext(ready);
            for (int i = 0; i < ready.Count; i++)
                if (ready[i] != next) ready[i].WaitSteps++;

            Current = next;
            next.State = ProcessState.Running;
            if (!next.Passive) Context.SwitchTo(next);
            Current = null;
            next.Steps++;
            if (next.State == ProcessState.Running) next.State = ProcessState.Ready;
            if (next.State == ProcessState.Terminated) OS.Memory.FreeAll(next.Id);
            return !next.Passive;
        }

        // Ein Prozess, der beendet wurde, während sein Programm mitten im Lauf wartete, hat noch einen Stack mit
        // seiner Aufrufkette. Er bekommt die CPU ein letztes Mal: Yield wirft dort ProcessKilled, seine finally-Blöcke
        // laufen, und sein Kontext wird für den nächsten Prozess frei. Das geschieht hier und nicht in Kill, weil nur
        // der Kernel die CPU vergibt und Kill auch aus einem Prozess heraus aufgerufen werden kann.
        private void Reap()
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].State == ProcessState.Terminated) Reap(All[i]);
            for (int i = 0; i < dying.Count; i++) Reap(dying[i]);
            dying.Clear();
        }

        private void Reap(Process p)
        {
            if (p.Context == null) return;
            Current = p; // Solange er abbaut, hat er die CPU. Wartet sein Aufräumcode noch einmal, bleibt er dort stehen.
            Context.SwitchTo(p);
            Current = null;
            OS.Memory.FreeAll(p.Id); // Was sein Aufräumcode noch angefordert hat, gäbe sonst niemand mehr frei.
        }
    }
}
