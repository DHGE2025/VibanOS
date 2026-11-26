using System;
using VibanOS.Core;
using VibanOS.Interrupts;
using VibanOS.Ipc;

namespace VibanOS.Processes
{
    public enum ProcessState { Ready, Running, Blocked, Terminated }

    /// <summary>Simulierter Privilegmodus (VIB-12). COSMOS selbst führt alles im Kernelmode aus.</summary>
    public enum ProcessMode { Kernel, User }

    /// <summary>
    /// Prozesskontrollblock (VIB-3). Ein Prozess ist ein Objekt, dessen Step() jeweils einen kleinen
    /// Arbeitsschritt erledigt. Der Scheduler ruft Step() reihum auf (kooperatives Multitasking).
    /// </summary>
    public abstract class Process
    {
        public int Id;
        public string Name = "";
        public int Priority = 5;
        public string Owner = "root";
        public ProcessState State = ProcessState.Ready;
        public ProcessMode Mode = ProcessMode.User;

        /// <summary>
        /// Mit dem Signal Stop angehalten, bis Cont kommt. Bewusst kein eigener Zustand: Ein Prozess kann
        /// angehalten sein und zugleich auf eine Sperre warten (Blocked), beides muss erhalten bleiben.
        /// </summary>
        public bool Stopped;

        // Statistik, Grundlage für die Kennzahlen in VIB-15
        public DateTime StartTime;
        public int Steps;
        public int WaitSteps;
        public int EstimatedSteps;

        /// <summary>Letzte Ausgabe des Prozesses, sichtbar in ps und in der Aktivitätsanzeige.</summary>
        public string Output = "";

        public readonly Mailbox Mailbox = new Mailbox();

        public abstract void Step();

        /// <summary>Standardverhalten für Signale (VIB-14). Prozesse können das überschreiben.</summary>
        public virtual void OnSignal(Signal signal)
        {
            if (State == ProcessState.Terminated) return; // Ein beendeter Prozess lebt nicht wieder auf.
            if (signal == Signal.Stop) Stopped = true;
            else if (signal == Signal.Cont) Stopped = false;
            else if (signal == Signal.Term)
            {
                State = ProcessState.Terminated;
                OS.Memory.FreeAll(Id);
            }
        }

        /// <summary>Zustand als Text für ps und die Aktivitätsanzeige.</summary>
        public string StateText
        {
            get
            {
                if (State == ProcessState.Terminated) return "beendet";
                if (Stopped) return "angehalten";
                if (State == ProcessState.Blocked) return "blockiert";
                return State == ProcessState.Running ? "läuft" : "bereit";
            }
        }
    }
}
