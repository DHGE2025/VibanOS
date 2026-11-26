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
    /// Prozesskontrollblock (VIB-3). Das Programm eines Prozesses steht in Run() und läuft in einem eigenen
    /// Kontext (Context): eigener Stack, eigene Register. Der Scheduler gibt reihum einem Prozess die CPU, der
    /// Prozess gibt sie mit Yield() zurück und macht beim nächsten Mal an derselben Stelle weiter.
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

        /// <summary>Stack und Register des Prozesses. null, bevor er zum ersten Mal läuft und nachdem er beendet ist.</summary>
        public Context Context;

        /// <summary>
        /// Ein Prozess ohne eigenes Programm, etwa das Fenster einer App. Er steht in der Liste und der Scheduler zählt
        /// ihm seinen Anteil, er bekommt aber keinen Kontext: Für nichts die CPU zu übergeben, kostete den Kernel jedes
        /// Mal das Warten auf den Wechsel.
        /// </summary>
        public virtual bool Passive { get { return false; } }

        /// <summary>
        /// Das Programm des Prozesses. Es darf Schleifen und lokale Variablen haben und mittendrin warten: Yield()
        /// gibt die CPU ab, beim nächsten Mal geht es dahinter weiter. Kehrt Run zurück, ist der Prozess beendet.
        /// Ohne eigenes Run läuft Step() in einer Schleife.
        /// </summary>
        public virtual void Run()
        {
            while (true)
            {
                Step();
                Yield();
            }
        }

        /// <summary>Die einfache Form eines Programms: ein kleiner Arbeitsschritt, nach jedem ist der nächste Prozess dran.</summary>
        public virtual void Step() { }

        /// <summary>
        /// Gibt die CPU ab. Der Prozess bleibt bereit und läuft hier weiter, sobald der Scheduler ihn wieder wählt.
        /// Wurde er inzwischen beendet, kehrt Yield nicht zurück (ProcessKilled).
        /// </summary>
        protected void Yield() { OS.Processes.Yield(); }

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
