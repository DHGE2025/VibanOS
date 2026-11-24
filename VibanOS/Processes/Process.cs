using System;
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
            if (signal == Signal.Term) State = ProcessState.Terminated;
            else if (signal == Signal.Stop) State = ProcessState.Blocked;
            else if (signal == Signal.Cont && State == ProcessState.Blocked) State = ProcessState.Ready;
        }

        public static string StateName(ProcessState state)
        {
            switch (state)
            {
                case ProcessState.Ready: return "bereit";
                case ProcessState.Running: return "läuft";
                case ProcessState.Blocked: return "blockiert";
                default: return "beendet";
            }
        }
    }
}
