using System;
using VibanOS.Core;
using VibanOS.Syscalls;
using VibanOS.Users;

namespace VibanOS.Cli.Commands
{
    public class HelpCommand : Command
    {
        public override string Name { get { return "help"; } }
        public override string Description { get { return "Alle Befehle anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            for (int i = 0; i < shell.Commands.Count; i++)
            {
                Command c = shell.Commands[i];
                shell.WriteLine(Text.Pad(c.Usage, 26) + c.Description);
            }
            shell.WriteLine("Bild auf und Bild ab blättern im Terminal, Pfeil auf holt frühere Eingaben zurück.", LineStyle.Muted);
        }
    }

    public class EchoCommand : Command
    {
        public override string Name { get { return "echo"; } }
        public override string Usage { get { return "echo <text>"; } }
        public override string Description { get { return "Text ausgeben"; } }

        public override void Execute(string[] args, Shell shell)
        {
            Syscall.Invoke(SyscallNumber.Write, Shell.Join(args, 0));
        }
    }

    public class ClearCommand : Command
    {
        public override string Name { get { return "clear"; } }
        public override string Description { get { return "Terminal leeren"; } }

        public override void Execute(string[] args, Shell shell) { shell.Clear(); }
    }

    public class TimeCommand : Command
    {
        public override string Name { get { return "time"; } }
        public override string Description { get { return "Datum und Uhrzeit anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            DateTime now = (DateTime)Syscall.Invoke(SyscallNumber.GetTime).Value;
            shell.WriteLine(Text.Date(now) + " " + now.Year + ", " + Text.Time(now) + ":" + Text.Two(now.Second));
        }
    }

    public class RebootCommand : Command
    {
        public override string Name { get { return "reboot"; } }
        public override string Description { get { return "System neu starten"; } }
        public override Permission Required { get { return Permission.Shutdown; } }

        public override void Execute(string[] args, Shell shell)
        {
            SyscallResult r = Syscall.Invoke(SyscallNumber.Reboot);
            if (r.Error != SyscallResult.Ok) shell.Error(r.Message);
        }
    }

    public class ShutdownCommand : Command
    {
        public override string Name { get { return "shutdown"; } }
        public override string Description { get { return "System ausschalten"; } }
        public override Permission Required { get { return Permission.Shutdown; } }

        public override void Execute(string[] args, Shell shell)
        {
            SyscallResult r = Syscall.Invoke(SyscallNumber.Shutdown);
            if (r.Error != SyscallResult.Ok) shell.Error(r.Message);
        }
    }
}
