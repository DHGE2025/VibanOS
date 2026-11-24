using VibanOS.Core;
using VibanOS.Processes;
using VibanOS.Users;

namespace VibanOS.Cli.Commands
{
    public class PsCommand : Command
    {
        public override string Name { get { return "ps"; } }
        public override string Description { get { return "Alle Prozesse anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            shell.WriteLine("PID  NAME      ZUSTAND    PRIO  NUTZER    SCHRITTE", LineStyle.Muted);
            for (int i = 0; i < OS.Processes.All.Count; i++)
            {
                Process p = OS.Processes.All[i];
                shell.WriteLine(Text.Pad(p.Id.ToString(), 5) + Text.Pad(p.Name, 10) + Text.Pad(Process.StateName(p.State), 11)
                    + Text.Pad(p.Priority.ToString(), 6) + Text.Pad(p.Owner, 10) + p.Steps);
            }
            if (OS.Processes.All.Count == 0) shell.WriteLine("Keine Prozesse. 'start counter' startet einen.", LineStyle.Muted);
        }
    }

    public class StartCommand : Command
    {
        public override string Name { get { return "start"; } }
        public override string Usage { get { return "start <name> [prio]"; } }
        public override string Description { get { return "Beispielprozess starten: counter, clock, busy"; } }

        public override void Execute(string[] args, Shell shell)
        {
            Process p = null;
            string name = args.Length > 0 ? args[0] : "";
            if (name == "counter") p = new CounterProcess();
            else if (name == "clock") p = new ClockProcess();
            else if (name == "busy") p = new BusyProcess();
            if (p == null) { shell.Error("Aufruf: start counter|clock|busy [priorität]"); return; }

            int priority;
            if (args.Length > 1 && int.TryParse(args[1], out priority)) p.Priority = priority;
            OS.Processes.Start(p);
            shell.WriteLine("Prozess " + p.Id + " (" + p.Name + ") gestartet.");
        }
    }

    public class KillCommand : Command
    {
        public override string Name { get { return "kill"; } }
        public override string Usage { get { return "kill <pid>"; } }
        public override string Description { get { return "Prozess beenden"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int id;
            if (args.Length == 0 || !int.TryParse(args[0], out id)) { shell.Error("Aufruf: " + Usage); return; }
            Process p = OS.Processes.Get(id);
            if (p == null) { shell.Error("Kein Prozess mit der ID " + id + "."); return; }
            // Fremde Prozesse darf nur beenden, wer das Systemrecht dazu hat (VIB-17).
            if (p.Owner != OS.Users.Current.Name && !OS.Users.HasPermission(OS.Users.Current, Permission.ProcessKillAny))
            {
                shell.Error("Keine Berechtigung: Prozess " + id + " gehört " + p.Owner + ".");
                return;
            }
            OS.Processes.Kill(id);
            shell.WriteLine("Prozess " + id + " beendet.");
        }
    }
}
