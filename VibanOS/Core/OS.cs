using VibanOS.Cli;
using VibanOS.Drivers;
using VibanOS.FileSystem;
using VibanOS.Interrupts;
using VibanOS.Memory;
using VibanOS.Processes;
using VibanOS.Syscalls;
using VibanOS.Users;

namespace VibanOS.Core
{
    /// <summary>
    /// Zentrale Anlaufstelle: hält alle Teilsysteme. Kennt weder COSMOS noch die Oberfläche,
    /// damit sich der Kern auch auf einem normalen PC bauen und prüfen lässt (tools/Preview).
    /// </summary>
    public static class OS
    {
        public const string Name = "VibanOS";
        public const string Version = "0.1";

        public static MemoryManager Memory;
        public static ProcessManager Processes;
        public static UserManager Users;
        public static DriverManager Drivers;
        public static InterruptManager Interrupts;
        public static Shell Shell;

        /// <summary>Zählt die Durchläufe der Kernel-Schleife (Timer-Ticks).</summary>
        public static long Ticks;

        /// <summary>Wird gesetzt, sobald sich etwas Sichtbares geändert hat. Die Oberfläche zeichnet dann neu.</summary>
        public static bool Changed = true;

        /// <summary>Startet alle Teilsysteme. Das Dateisystem (Vfs.Init) muss vorher eingerichtet sein.</summary>
        public static void Boot()
        {
            Interrupts = new InterruptManager();
            Memory = new MemoryManager();
            Processes = new ProcessManager();
            Users = new UserManager();
            Users.Load();
            SyscallTable.RegisterDefaults();
            Shell = new Shell();
            CommandList.RegisterAll(Shell);
            Shell.WriteLine(Name + " " + Version + " bereit. 'help' zeigt alle Befehle.", LineStyle.Muted);
            if (!Vfs.Available)
                Shell.WriteLine("Kein Dateisystem: Nutzer und Dateien gelten nur bis zum Ausschalten. 'format' richtet eine leere Festplatte ein.", LineStyle.Muted);
            Drivers = new DriverManager();
            Drivers.Register(new SerialDriver());
            if (!Drivers.Start("com1")) Shell.Error("Der Treiber com1 ließ sich nicht starten, 'send' geht deshalb nicht.");
        }

        /// <summary>
        /// Ein Durchlauf der Kernel-Schleife: Timer-Tick, ein Schritt eines Shell-Befehls, der in Schritten arbeitet (rm -r,
        /// cp -r), dann bekommt ein bereiter Prozess die CPU, bis er sie abgibt.
        /// false: Kein Prozess hatte die CPU, weil keiner bereit war oder der gewählte kein Programm hat (ein Fenster).
        /// </summary>
        public static bool Tick()
        {
            Ticks++;
            Interrupts.Raise(InterruptManager.Timer);
            Shell.Tick();
            return Processes.Tick();
        }
    }
}
