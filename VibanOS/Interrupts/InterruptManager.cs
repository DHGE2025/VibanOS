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
    /// ponytail: Der Timer wird bisher aus der Kernel-Schleife ausgelöst (OS.Tick), nicht vom PIT.
    /// Für echte Zeitscheiben in Kernel.BeforeRun einen PIT-Timer registrieren, der Raise(Timer) aufruft.
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

        public void Register(int number, string name, InterruptHandler handler)
        {
            InterruptEntry e = new InterruptEntry();
            e.Number = number;
            e.Name = name;
            e.Handler = handler;
            Entries.Add(e);
        }

        /// <summary>Löst einen Interrupt aus. Handler müssen sehr kurz sein: nur zählen oder in einen Puffer schreiben.</summary>
        public void Raise(int number)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (Entries[i].Number != number) continue;
                Entries[i].Count++;
                if (Entries[i].Handler != null) Entries[i].Handler();
            }
        }
    }
}
