using System.Collections.Generic;

namespace VibanOS.Interrupts
{
    /// <summary>Software-Signale zwischen Prozessen (VIB-14), siehe Process.OnSignal.</summary>
    public enum Signal { Term, Stop, Cont, User1 }

    public delegate void InterruptHandler();

    public class InterruptEntry
    {
        public int Number;
        public string Name = "";
        public long Count;
        public InterruptHandler Handler;
    }

    /// <summary>
    /// Verzeichnis der Interrupts mit Zähler (VIB-14).
    /// ponytail: Timer und Tastatur werden bisher aus der Kernel-Schleife gemeldet (OS.Tick, Kernel.Frame),
    /// nicht von der Hardware. Für echte Zeitscheiben in Kernel.BeforeRun einen Timer von COSMOS anmelden,
    /// der Raise(Timer) aufruft.
    /// </summary>
    public class InterruptManager
    {
        public const int Timer = 0;     // IRQ0
        public const int Keyboard = 1;  // IRQ1
        public const int Serial = 4;    // IRQ4 (COM1)

        public readonly List<InterruptEntry> Entries = new List<InterruptEntry>();

        public InterruptManager()
        {
            Register(Timer, "Timer", null);
            Register(Keyboard, "Tastatur", null);
            Register(Serial, "COM1", null);
        }

        private InterruptEntry Find(int number)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Number == number) return Entries[i];
            return null;
        }

        /// <summary>Trägt einen Interrupt ein. Gibt es die Nummer schon, werden Name und Handler ersetzt, der Zähler bleibt.</summary>
        public void Register(int number, string name, InterruptHandler handler)
        {
            InterruptEntry e = Find(number);
            if (e == null)
            {
                e = new InterruptEntry();
                e.Number = number;
                Entries.Add(e);
            }
            e.Name = name;
            e.Handler = handler;
        }

        /// <summary>Löst einen Interrupt aus. Handler müssen sehr kurz sein: nur zählen oder in einen Puffer schreiben.</summary>
        public void Raise(int number)
        {
            InterruptEntry e = Find(number);
            if (e == null) return;
            e.Count++;
            if (e.Handler != null) e.Handler();
        }
    }
}
