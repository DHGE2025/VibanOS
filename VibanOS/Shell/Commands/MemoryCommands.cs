using VibanOS.Core;
using VibanOS.Memory;
using VibanOS.Processes;

namespace VibanOS.Cli.Commands
{
    public class MemCommand : Command
    {
        public override string Name { get { return "mem"; } }
        public override string Description { get { return "Speicherübersicht: Rahmen frei und belegt"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int free = OS.Memory.Frames.FreeCount;
            shell.WriteLine("Rahmen gesamt: " + PhysicalMemory.FrameCount + " zu je " + PhysicalMemory.FrameSize + " Byte");
            shell.WriteLine("Belegt: " + (PhysicalMemory.FrameCount - free) + "   Frei: " + free);
        }
    }

    public class FramesCommand : Command
    {
        public override string Name { get { return "frames"; } }
        public override string Description { get { return "Rahmentabelle anzeigen (# belegt, . frei)"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int[] owner = OS.Memory.Frames.Owner;
            for (int row = 0; row < owner.Length; row += 64)
            {
                string line = "[";
                for (int i = row; i < row + 64 && i < owner.Length; i++) line += owner[i] == FrameTable.Free ? "." : "#";
                shell.WriteLine(line + "]");
            }
        }
    }

    public class AllocCommand : ProcessCommand
    {
        public override string Name { get { return "alloc"; } }
        public override string Usage { get { return "alloc <pid> <seiten>"; } }
        public override string Description { get { return "Einem Prozess Speicherseiten geben"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int count;
            if (args.Length < 2 || !int.TryParse(args[1], out count) || count < 1) { shell.Error("Aufruf: " + Usage); return; }
            Process p = Target(args, shell);
            if (p == null) return;
            // Ein beendeter Prozess gibt nichts mehr frei, seine Seiten blieben für immer belegt.
            if (p.State == ProcessState.Terminated) { shell.Error("Prozess " + p.Id + " ist beendet."); return; }
            int done = 0;
            while (done < count && OS.Memory.AllocatePage(p.Id) >= 0) done++;
            shell.WriteLine(done + " Seite(n) für Prozess " + p.Id + " belegt.");
            if (done < count) shell.Error("Speicher voll: Es ist kein Rahmen mehr frei.");
        }
    }
}
