using VibanOS.Core;
using VibanOS.Drivers;
using VibanOS.Interrupts;
using VibanOS.Syscalls;

namespace VibanOS.Cli.Commands
{
    public class DriversCommand : Command
    {
        public override string Name { get { return "drivers"; } }
        public override string Description { get { return "Alle Treiber mit Status anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            for (int i = 0; i < OS.Drivers.All.Count; i++)
            {
                Driver d = OS.Drivers.All[i];
                string status = d.Status == DriverStatus.Running ? "läuft" : "gestoppt";
                shell.WriteLine(Text.Pad(d.Name, 8) + Text.Pad(status, 10) + d.Description);
            }
        }
    }

    public class SendCommand : Command
    {
        public override string Name { get { return "send"; } }
        public override string Usage { get { return "send <text>"; } }
        public override string Description { get { return "Nachricht über COM1 senden"; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 0) { shell.Error("Aufruf: " + Usage); return; }
            SerialDriver serial = OS.Drivers.Find("com1") as SerialDriver;
            if (serial == null || !serial.SendMessage(Shell.Join(args, 0)))
                shell.Error("Senden fehlgeschlagen: Der Treiber com1 läuft nicht oder die Leitung ist belegt.");
        }
    }

    public class SyscallsCommand : Command
    {
        public override string Name { get { return "syscalls"; } }
        public override string Description { get { return "Tabelle aller Systemaufrufe"; } }

        public override void Execute(string[] args, Shell shell)
        {
            shell.WriteLine("NR  NAME      MODUS   BESCHREIBUNG", LineStyle.Muted);
            for (int i = 0; i < SyscallTable.Entries.Count; i++)
            {
                SyscallEntry e = SyscallTable.Entries[i];
                shell.WriteLine(Text.Pad(((int)e.Number).ToString(), 4) + Text.Pad(e.Name, 10)
                    + Text.Pad(e.KernelOnly ? "Kernel" : "User", 8) + e.Description);
            }
        }
    }

    public class InterruptsCommand : Command
    {
        public override string Name { get { return "interrupts"; } }
        public override string Description { get { return "Registrierte Interrupts mit Zähler"; } }

        public override void Execute(string[] args, Shell shell)
        {
            for (int i = 0; i < OS.Interrupts.Entries.Count; i++)
            {
                InterruptEntry e = OS.Interrupts.Entries[i];
                shell.WriteLine("IRQ" + Text.Pad(e.Number.ToString(), 3) + Text.Pad(e.Name, 10) + e.Count);
            }
        }
    }
}
