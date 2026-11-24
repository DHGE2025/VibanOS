using VibanOS.Cli.Commands;

namespace VibanOS.Cli
{
    /// <summary>
    /// Alle Befehle der Shell. Neue Befehle hier mit einer Zeile eintragen.
    /// Bewusst von Hand: COSMOS unterstützt Reflection nur sehr eingeschränkt.
    /// </summary>
    public static class CommandList
    {
        public static void RegisterAll(Shell shell)
        {
            // System (VIB-1)
            shell.Register(new HelpCommand());
            shell.Register(new EchoCommand());
            shell.Register(new ClearCommand());
            shell.Register(new TimeCommand());
            shell.Register(new RebootCommand());
            shell.Register(new ShutdownCommand());

            // Dateisystem (VIB-2)
            shell.Register(new LsCommand());
            shell.Register(new CdCommand());
            shell.Register(new MkdirCommand());
            shell.Register(new TouchCommand());
            shell.Register(new CatCommand());
            shell.Register(new WriteCommand());
            shell.Register(new RmCommand());

            // Prozesse (VIB-3)
            shell.Register(new PsCommand());
            shell.Register(new StartCommand());
            shell.Register(new KillCommand());

            // Speicher (VIB-5)
            shell.Register(new MemCommand());
            shell.Register(new FramesCommand());
            shell.Register(new AllocCommand());

            // Nutzer (VIB-16)
            shell.Register(new WhoamiCommand());
            shell.Register(new UsersCommand());
            shell.Register(new UseraddCommand());
            shell.Register(new PasswdCommand());
            shell.Register(new LogoutCommand());

            // Treiber, Systemaufrufe, Interrupts (VIB-9, VIB-10, VIB-11, VIB-14)
            shell.Register(new DriversCommand());
            shell.Register(new SendCommand());
            shell.Register(new SyscallsCommand());
            shell.Register(new InterruptsCommand());
        }
    }
}
