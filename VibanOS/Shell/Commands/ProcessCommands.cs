using VibanOS.Core;
using VibanOS.Processes;
using VibanOS.Users;

namespace VibanOS.Cli.Commands
{
    /// <summary>Basis der Befehle, die in einen Prozess eingreifen (kill, alloc).</summary>
    public abstract class ProcessCommand : Command
    {
        /// <summary>
        /// Sucht den Prozess zu args[0] und prüft das Recht: Fremde Prozesse darf nur anfassen, wer das
        /// Systemrecht dazu hat (VIB-17). Gibt null zurück und meldet den Grund, wenn etwas fehlt.
        /// </summary>
        protected Process Target(string[] args, Shell shell)
        {
            int id;
            if (args.Length == 0 || !int.TryParse(args[0], out id)) { shell.Error("Aufruf: " + Usage); return null; }
            Process p = OS.Processes.Get(id);
            if (p == null) { shell.Error("Kein Prozess mit der ID " + id + "."); return null; }
            if (p.Owner != OS.Users.Current.Name && !OS.Users.HasPermission(OS.Users.Current, Permission.ProcessKillAny))
            {
                shell.Error("Keine Berechtigung: Prozess " + id + " gehört " + p.Owner + ".");
                return null;
            }
            return p;
        }
    }

    public class PsCommand : Command
    {
        public override string Name { get { return "ps"; } }
        public override string Description { get { return "Alle Prozesse anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            shell.WriteLine("PID  NAME      ZUSTAND     PRIO  NUTZER            SCHRITTE  AUSGABE", LineStyle.Muted);
            for (int i = 0; i < OS.Processes.All.Count; i++)
            {
                Process p = OS.Processes.All[i];
                shell.WriteLine(Text.Pad(p.Id.ToString(), 5) + Text.Pad(p.Name, 10) + Text.Pad(p.StateText, 12)
                    + Text.Pad(p.Priority.ToString(), 6) + Text.Pad(p.Owner, 18) + Text.Pad(p.Steps.ToString(), 10) + p.Output);
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

            int priority = p.Priority;
            if (args.Length > 1 && !int.TryParse(args[1], out priority)) { shell.Error("Die Priorität muss eine ganze Zahl sein."); return; }
            p.Priority = priority;
            if (OS.Processes.Start(p) == null) { shell.Error("Speicher voll: Für den Prozess ist kein Rahmen frei."); return; }
            shell.WriteLine("Prozess " + p.Id + " (" + p.Name + ") gestartet.");
        }
    }

    public class KillCommand : ProcessCommand
    {
        public override string Name { get { return "kill"; } }
        public override string Usage { get { return "kill <pid>"; } }
        public override string Description { get { return "Prozess beenden"; } }

        public override void Execute(string[] args, Shell shell)
        {
            Process p = Target(args, shell);
            if (p == null) return;
            OS.Processes.Kill(p.Id);
            shell.WriteLine("Prozess " + p.Id + " beendet.");
        }
    }
}
