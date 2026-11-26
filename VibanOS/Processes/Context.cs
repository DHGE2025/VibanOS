using System;
using System.Collections.Generic;
using System.Threading;
using VibanOS.Platform;

namespace VibanOS.Processes
{
    /// <summary>
    /// Ausführungskontext eines Prozesses (VIB-3): ein eigener Thread, also ein eigener Stack und eigene Register.
    /// Gibt ein Prozess die CPU ab, bleibt er mitten in seinem Programm stehen. Seine lokalen Variablen und die
    /// Aufrufkette liegen auf seinem Stack, seine Register sichert der Kern von COSMOS dort beim Wechsel und lädt
    /// sie wieder, wenn der Prozess weiterläuft. Unter COSMOS 3 ist jeder Thread ein Kernel-Thread mit 256 KiB Stack,
    /// den Wechsel selbst erledigt die Interrupt-Routine von COSMOS (alle Register und der Stackzeiger).
    ///
    /// Einen eigenen Wechsel in Assembler daneben gibt es bewusst nicht: Der Garbage Collector durchsucht nur
    /// Stacks, die bei COSMOS angemeldet sind. Objekte, die nur von einem fremden Stack aus erreichbar wären,
    /// würde er wegräumen.
    ///
    /// Es läuft immer genau einer: der Kernel (Shell, Oberfläche, Scheduler) oder ein Prozess. Der Scheduler von
    /// VibanOS bestimmt, wer drankommt, und übergibt die CPU wie einen Staffelstab. Deshalb braucht der übrige Code
    /// keine Sperren gegen gleichzeitige Zugriffe.
    /// ponytail: Ein Prozess behält die CPU, bis er sie abgibt (Yield, Block, Ende). Zeitscheiben erzwingen hieße,
    /// einen Thread mitten in einem Kernaufruf anzuhalten. Dafür müsste jeder Zugriff auf gemeinsame Daten geschützt
    /// sein (VIB-11, VIB-14).
    /// </summary>
    public sealed class Context
    {
        /// <summary>Stack je Prozess in Bytes. Unter 128 KiB lehnt .NET tiefe Aufrufe ab, 256 KiB nimmt COSMOS selbst.</summary>
        public const int StackSize = 256 * 1024;

        /// <summary>So viele Threads hat VibanOS bisher angelegt.</summary>
        public static int Count;

        // Weckt den Kernel, wenn ein Prozess die CPU abgibt.
        private static readonly AutoResetEvent kernel = new AutoResetEvent(false);

        // Kontexte beendeter Prozesse. Der nächste neue Prozess übernimmt einen davon: COSMOS 3.0.99 gibt den Stack
        // eines beendeten Threads nicht frei, jeder neue Thread kostete sonst 256 KiB für immer.
        private static readonly List<Context> free = new List<Context>();

        private readonly AutoResetEvent turn = new AutoResetEvent(false);
        private readonly Thread thread;
        private Process process;
        private bool unwound;

        /// <summary>Nummer des Threads in .NET.</summary>
        public int ThreadId { get { return thread.ManagedThreadId; } }

        /// <summary>Der Thread, wie ihn COSMOS führt, für Hardware.InspectThread. null in tools/Preview.</summary>
        public object Handle;

        private Context()
        {
            thread = new Thread(Loop, StackSize);
            thread.IsBackground = true; // In tools/Preview hält ein wartender Kontext das Programmende nicht auf.
            thread.Start();
            Count++;
        }

        /// <summary>
        /// Kernel-Seite des Wechsels: gibt dem Prozess die CPU und kehrt zurück, sobald er sie abgibt oder endet.
        /// Beim ersten Mal bekommt der Prozess seinen Kontext. Nur ProcessManager ruft das auf.
        /// </summary>
        internal static void SwitchTo(Process p)
        {
            if (p.Context == null)
            {
                Context c;
                if (free.Count > 0)
                {
                    c = free[free.Count - 1];
                    free.RemoveAt(free.Count - 1);
                }
                else
                {
                    try { c = new Context(); }
                    catch (Exception e)
                    {
                        // Kein Speicher für den Stack, oder der Scheduler von COSMOS läuft nicht.
                        p.Output = "Abbruch: kein Thread (" + e.Message + ")";
                        p.State = ProcessState.Terminated;
                        return;
                    }
                }
                c.process = p;
                p.Context = c;
            }
            p.Context.turn.Set();
            kernel.WaitOne();
        }

        /// <summary>Prozess-Seite des Wechsels: gibt die CPU an den Kernel und wartet, bis der Scheduler sie zurückgibt.</summary>
        internal void SwitchToKernel()
        {
            kernel.Set();
            turn.WaitOne();
        }

        /// <summary>
        /// Prozess-Seite nach dem Ende des Prozesses (kill, Signal Term, exit): baut den Stack ab, indem ProcessKilled
        /// durch sein Programm fällt. Fängt das Programm sie ab und wartet weiter, bleibt sein Thread für immer stehen.
        /// Das kostet seinen Stack, hält aber das System nicht an.
        /// </summary>
        internal void Unwind()
        {
            if (!unwound) { unwound = true; throw new ProcessKilled(); }
            process.Context = null;
            process = null;
            kernel.Set();
            while (true) turn.WaitOne(); // Niemand kennt diesen Kontext mehr, er bekommt die CPU nie wieder.
        }

        // Der Thread des Kontexts: führt nacheinander die Programme der Prozesse aus, die ihn bekommen.
        private void Loop()
        {
            while (true)
            {
                turn.WaitOne();
                if (Handle == null) Handle = Hardware.CurrentThread();
                unwound = false;
                Process p = process;
                try
                {
                    p.Run();
                }
                catch (ProcessKilled)
                {
                    // kill, Signal Term oder exit: Der Stack ist abgebaut, mehr ist nicht zu tun.
                }
                catch (Exception e)
                {
                    // Ein fehlerhafter Prozess (z. B. Schutzverletzung, VIB-7) darf das System nicht mitreißen.
                    p.Output = "Abbruch: " + e.Message;
                }
                p.State = ProcessState.Terminated;
                p.Context = null;
                process = null;
                free.Add(this);
                kernel.Set();
            }
        }
    }

    /// <summary>
    /// Baut den Stack eines beendeten Prozesses ab: Yield wirft sie, sobald der Prozess beendet wurde, damit sein
    /// Programm nicht weiterläuft und seine finally-Blöcke noch aufräumen. Prozesse fangen sie nicht ab (Context.Unwind).
    /// </summary>
    public sealed class ProcessKilled : Exception
    {
        public ProcessKilled() : base("Prozess beendet") { }
    }

    /// <summary>Was COSMOS über den Thread eines Kontexts weiß (Hardware.InspectThread), für den Befehl context.</summary>
    public class ThreadInfo
    {
        public uint Id;
        public ulong StackBase, StackSize;
        public ulong CpuTimeNs;

        /// <summary>Die gesicherten Register in der Reihenfolge von RegisterNames. null, solange der Thread läuft.</summary>
        public ulong[] Registers;

        public static readonly string[] RegisterNames =
        {
            "RIP", "RSP", "RBP", "RFLAGS", "RAX", "RBX", "RCX", "RDX", "RSI", "RDI",
            "R8", "R9", "R10", "R11", "R12", "R13", "R14", "R15"
        };
    }
}
