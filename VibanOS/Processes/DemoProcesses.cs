using System;
using VibanOS.Core;

namespace VibanOS.Processes
{
    /// <summary>Zählt endlos hoch.</summary>
    public class CounterProcess : Process
    {
        public CounterProcess() { Name = "counter"; }

        public override void Run()
        {
            // Der Zähler ist eine lokale Variable: Er liegt auf dem Stack des Prozesses und übersteht jeden Wechsel.
            for (int count = 1; ; count++)
            {
                Output = "Zähler " + count;
                Yield();
            }
        }
    }

    /// <summary>Zeigt die Uhrzeit.</summary>
    public class ClockProcess : Process
    {
        public ClockProcess() { Name = "clock"; }

        public override void Step()
        {
            DateTime now = DateTime.Now;
            Output = Text.Time(now) + ":" + Text.Two(now.Second);
        }
    }

    /// <summary>Sucht Primzahlen und beendet sich nach einer festen Zahl von Schritten.</summary>
    public class BusyProcess : Process
    {
        public BusyProcess() { Name = "busy"; EstimatedSteps = 2000; }

        public override void Run()
        {
            int found = 0;
            for (int candidate = 2; ; candidate++)
            {
                bool prime = true;
                for (int d = 2; d * d <= candidate; d++)
                    if (candidate % d == 0) { prime = false; break; }
                if (prime) found++;
                Output = found + " Primzahlen bis " + candidate;
                if (Steps + 1 >= EstimatedSteps) return; // Mit dem Ende von Run ist der Prozess beendet.
                Yield();
            }
        }
    }

    /// <summary>Prozess ohne eigenes Programm für memdemo (VIB-7): Er hat nur Speicher, mit dem die Shell vorführt.</summary>
    public class DemoProcess : Process
    {
        public DemoProcess(string name) { Name = name; }

        public override bool Passive { get { return true; } }
    }
}
