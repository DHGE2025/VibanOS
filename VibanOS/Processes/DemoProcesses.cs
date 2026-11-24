using System;
using VibanOS.Core;

namespace VibanOS.Processes
{
    /// <summary>Zählt endlos hoch.</summary>
    public class CounterProcess : Process
    {
        private int count;

        public CounterProcess() { Name = "counter"; }

        public override void Step()
        {
            count++;
            Output = "Zähler " + count;
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
        private int candidate = 2;
        private int found;

        public BusyProcess() { Name = "busy"; EstimatedSteps = 2000; }

        public override void Step()
        {
            bool prime = true;
            for (int d = 2; d * d <= candidate; d++)
                if (candidate % d == 0) { prime = false; break; }
            if (prime) found++;
            candidate++;
            Output = found + " Primzahlen bis " + candidate;
            if (Steps + 1 >= EstimatedSteps) State = ProcessState.Terminated;
        }
    }
}
