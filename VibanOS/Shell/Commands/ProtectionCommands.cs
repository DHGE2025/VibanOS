using VibanOS.Core;
using VibanOS.Memory;
using VibanOS.Processes;

namespace VibanOS.Cli.Commands
{
    /// <summary>Hilfen für die Befehle aus VIB-7.</summary>
    public static class Protection
    {
        /// <summary>Liest Rechte wie "r", "rw", "rwx", "rx" oder "-" (gar nichts).</summary>
        public static bool ParseRights(string s, out int access)
        {
            access = 0;
            if (s == "-") return true;
            for (int i = 0; i < s.Length; i++)
            {
                int bit = s[i] == 'r' ? PageAccess.Read : s[i] == 'w' ? PageAccess.Write : s[i] == 'x' ? PageAccess.Execute : 0;
                if (bit == 0 || (access & bit) != 0) return false;
                access |= bit;
            }
            return s.Length > 0;
        }

        /// <summary>Liest einen Bytewert 0 bis 255, dezimal oder mit 0x.</summary>
        public static bool ParseByte(string s, out byte value)
        {
            int v;
            value = 0;
            if (!TranslateCommand.ParseAddress(s, out v) || v > 255) return false;
            value = (byte)v;
            return true;
        }
    }

    public class ProtectCommand : ProcessCommand
    {
        public override string Name { get { return "protect"; } }
        public override string Usage { get { return "protect <pid> <seite> r|rw|rwx"; } }
        public override string Description { get { return "Zugriffsrechte einer Seite setzen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int page, access;
            if (args.Length < 3 || !int.TryParse(args[1], out page) || !Protection.ParseRights(args[2], out access)) { shell.Error("Aufruf: " + Usage); return; }
            Process p = Target(args, shell);
            if (p == null) return;
            PageTable table = OS.Memory.TableOf(p.Id);
            if (table == null || page < 0 || page >= table.Pages.Count || !table.Pages[page].Present) { shell.Error("Prozess " + p.Id + " hat keine Seite " + page + "."); return; }
            table.Pages[page].Access = access;
            OS.Changed = true;
            shell.WriteLine("Seite " + page + " von Prozess " + p.Id + ": " + PageTableCommand.Rights(access));
        }
    }

    public class PeekCommand : ProcessCommand
    {
        public override string Name { get { return "peek"; } }
        public override string Usage { get { return "peek <pid> <adresse>"; } }
        public override string Description { get { return "Ein Byte als Prozess lesen, mit Rechteprüfung"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int address;
            if (args.Length < 2 || !TranslateCommand.ParseAddress(args[1], out address)) { shell.Error("Aufruf: " + Usage); return; }
            Process p = Target(args, shell);
            if (p == null) return;
            try
            {
                byte value = OS.Memory.ReadByte(p.Id, address);
                shell.WriteLine("Adresse " + address + " von Prozess " + p.Id + ": " + value + " (0x" + Text.Hex(value, 2) + ")");
            }
            catch (MemoryAccessException e) { shell.Error(e.Message + ". Steht in memlog."); }
        }
    }

    public class PokeCommand : ProcessCommand
    {
        public override string Name { get { return "poke"; } }
        public override string Usage { get { return "poke <pid> <adresse> <wert>"; } }
        public override string Description { get { return "Ein Byte als Prozess schreiben, mit Rechteprüfung"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int address;
            byte value;
            if (args.Length < 3 || !TranslateCommand.ParseAddress(args[1], out address) || !Protection.ParseByte(args[2], out value))
            {
                shell.Error("Aufruf: " + Usage + " (Wert 0 bis 255)");
                return;
            }
            Process p = Target(args, shell);
            if (p == null) return;
            try
            {
                OS.Memory.WriteByte(p.Id, address, value);
                shell.WriteLine("Adresse " + address + " von Prozess " + p.Id + " ist jetzt " + value + ".");
            }
            catch (MemoryAccessException e) { shell.Error(e.Message + ". Steht in memlog."); }
        }
    }

    public class MemlogCommand : Command
    {
        public override string Name { get { return "memlog"; } }
        public override string Description { get { return "Abgelehnte Speicherzugriffe anzeigen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (OS.Memory.Log.Count == 0) { shell.WriteLine("Noch kein Zugriff abgelehnt."); return; }
            for (int i = 0; i < OS.Memory.Log.Count; i++)
                shell.WriteLine(Text.PadLeft((i + 1).ToString(), 3) + "  " + OS.Memory.Log[i]);
        }
    }

    public class ShareCommand : ProcessCommand
    {
        public override string Name { get { return "share"; } }
        public override string Usage { get { return "share <pid> <seite> <ziel> r|rw"; } }
        public override string Description { get { return "Seite als gemeinsamen Speicher einblenden"; } }

        public override void Execute(string[] args, Shell shell)
        {
            int page, to, access;
            if (args.Length < 4 || !int.TryParse(args[1], out page) || !int.TryParse(args[2], out to) || !Protection.ParseRights(args[3], out access))
            {
                shell.Error("Aufruf: " + Usage);
                return;
            }
            Process p = Target(args, shell);
            if (p == null) return;
            // Auch beim Ziel gilt: Fremde Prozesse nur mit dem Recht dazu.
            Process target = Target(new string[] { args[2] }, shell);
            if (target == null) return;
            if (target.State == ProcessState.Terminated) { shell.Error("Prozess " + target.Id + " ist beendet."); return; }
            int at = OS.Memory.SharePage(p.Id, page, target.Id, access);
            if (at < 0) { shell.Error(p.Id == target.Id ? "Quelle und Ziel sind derselbe Prozess." : "Prozess " + p.Id + " hat keine Seite " + page + "."); return; }
            shell.WriteLine("Seite " + page + " von Prozess " + p.Id + " ist bei Prozess " + target.Id + " Seite " + at + " ("
                + PageTableCommand.Rights(access) + "), Adresse " + at * PhysicalMemory.FrameSize + ".");
        }
    }

    public class MemdemoCommand : Command
    {
        public override string Name { get { return "memdemo"; } }
        public override string Description { get { return "Erlaubte und verbotene Speicherzugriffe vorführen"; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (OS.Processes.All.Count + 2 > ProcessManager.MaxProcesses) { shell.Error("Zu viele Prozesse für die Vorführung."); return; }
            Process a = OS.Processes.Start(new DemoProcess("demo-a"));
            Process b = a == null ? null : OS.Processes.Start(new DemoProcess("demo-b"));
            if (b == null)
            {
                if (a != null) OS.Processes.Kill(a.Id);
                shell.Error("Speicher voll: Für die Vorführung sind keine Rahmen frei.");
                return;
            }
            int size = PhysicalMemory.FrameSize;
            shell.WriteLine("Prozess " + a.Id + " (A) und " + b.Id + " (B) gestartet, jeder hat Seite 0.", LineStyle.Muted);

            Try(shell, "A schreibt 42 in seine Seite 0", () => OS.Memory.WriteByte(a.Id, 7, 42));
            Try(shell, "A liest es wieder", () => Check(OS.Memory.ReadByte(a.Id, 7) == 42));
            Try(shell, "B liest Adresse 7: seine eigene Seite, nicht die von A", () => Check(OS.Memory.ReadByte(b.Id, 7) == 0));
            Try(shell, "B liest Adresse " + size + ": gehört B nicht", () => OS.Memory.ReadByte(b.Id, size));
            OS.Memory.TableOf(a.Id).Pages[0].Access = PageAccess.Read;
            shell.WriteLine("A setzt seine Seite 0 auf nur lesen (r--).", LineStyle.Muted);
            Try(shell, "A schreibt in Seite 0", () => OS.Memory.WriteByte(a.Id, 7, 1));
            OS.Memory.TableOf(a.Id).Pages[0].Access = PageAccess.ReadWrite;
            int shared = OS.Memory.SharePage(a.Id, 0, b.Id, PageAccess.Read);
            shell.WriteLine("A teilt Seite 0 mit B, B darf nur lesen. Bei B ist es Seite " + shared + ".", LineStyle.Muted);
            Try(shell, "B liest den gemeinsamen Speicher", () => Check(OS.Memory.ReadByte(b.Id, shared * size + 7) == 42));
            Try(shell, "A schreibt 43, B sieht es sofort", () => { OS.Memory.WriteByte(a.Id, 7, 43); Check(OS.Memory.ReadByte(b.Id, shared * size + 7) == 43); });
            bool violated = !Try(shell, "B schreibt in den gemeinsamen Speicher", () => OS.Memory.WriteByte(b.Id, shared * size + 7, 0));
            if (violated)
            {
                // So endet ein Prozess, der das selbst tut: Die Ausnahme beendet ihn (Context.Loop), kill räumt auf.
                OS.Processes.Kill(b.Id);
                shell.WriteLine("B hat eine Schutzverletzung ausgelöst und wurde beendet.", LineStyle.Muted);
            }
            OS.Processes.Kill(a.Id);
            shell.WriteLine("Die abgelehnten Zugriffe stehen in memlog.", LineStyle.Muted);
        }

        /// <summary>Führt einen Zugriff aus und zeigt, ob er erlaubt war. Rückgabe: erlaubt.</summary>
        private static bool Try(Shell shell, string what, System.Action action)
        {
            try
            {
                action();
                shell.WriteLine("erlaubt    " + what);
                return true;
            }
            catch (MemoryAccessException e)
            {
                shell.Error("abgelehnt  " + what + ": " + e.Message);
                return false;
            }
        }

        private static void Check(bool ok)
        {
            if (!ok) throw new System.InvalidOperationException("memdemo: unerwarteter Wert");
        }
    }
}
