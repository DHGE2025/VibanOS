using VibanOS.Core;
using VibanOS.Memory;

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

    public class AllocCommand : Command
    {
        public override string Name { get { return "alloc"; } }
        public override string Usage { get { return "alloc <pid> <seiten>"; } }
        public override string Description { get { return "Einem Prozess Speicherseiten geben"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int pid, count;
            if (args.Length < 2 || !int.TryParse(args[0], out pid) || !int.TryParse(args[1], out count))
            {
                shell.Error("Aufruf: " + Usage);
                return;
            }
            if (OS.Processes.Get(pid) == null) { shell.Error("Kein Prozess mit der ID " + pid + "."); return; }
            int done = 0;
            while (done < count && OS.Memory.AllocatePage(pid) >= 0) done++;
            shell.WriteLine(done + " Seite(n) für Prozess " + pid + " belegt.");
            if (done < count) shell.Error("Speicher voll: Es ist kein Rahmen mehr frei.");
        }
    }
}
