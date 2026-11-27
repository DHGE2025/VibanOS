using VibanOS.Cli.Commands;

namespace VibanOS.Cli
{
    /// <summary>
    /// Alle Befehle der Shell. Neue Befehle hier mit einer Zeile eintragen.
    /// Bewusst von Hand: Im COSMOS-Kernel gibt es keine Reflection, die Befehle selbst finden könnte.
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
            shell.Register(new PwdCommand());
            shell.Register(new MkdirCommand());
            shell.Register(new TouchCommand());
            shell.Register(new CatCommand());
            shell.Register(new WriteCommand());
            shell.Register(new RmCommand());
            shell.Register(new CpCommand());
            shell.Register(new MvCommand());
            shell.Register(new DfCommand());
            shell.Register(new FormatCommand());

            // Prozesse (VIB-3)
            shell.Register(new PsCommand());
            shell.Register(new StartCommand());
            shell.Register(new KillCommand());
            shell.Register(new BlockCommand());
            shell.Register(new UnblockCommand());
            shell.Register(new ContextCommand());

            // Speicher (VIB-5)
            shell.Register(new MemCommand());
            shell.Register(new FramesCommand());
            shell.Register(new AllocCommand());
            shell.Register(new PageTableCommand());
            shell.Register(new FreeCommand());
            shell.Register(new TranslateCommand());

            // Fragmentierung (VIB-6)
            shell.Register(new MallocCommand());
            shell.Register(new MfreeCommand());
            shell.Register(new MemmapCommand());
            shell.Register(new StrategyCommand());
            shell.Register(new FragstatCommand());
            shell.Register(new CompactCommand());
            shell.Register(new FragtestCommand());

            // Nutzer (VIB-16)
            shell.Register(new WhoamiCommand());
            shell.Register(new UsersCommand());
            shell.Register(new UseraddCommand());
            shell.Register(new UserdelCommand());
            shell.Register(new PasswdCommand());
            shell.Register(new SuCommand());
            shell.Register(new LoginCommand());
            shell.Register(new LogoutCommand());

            // Gruppen und Rechte (VIB-17)
            shell.Register(new GroupsCommand());
            shell.Register(new GroupaddCommand());
            shell.Register(new GroupdelCommand());
            shell.Register(new UsermodCommand());
            shell.Register(new PermsCommand());
            shell.Register(new SudoCommand());

            // Treiber, Systemaufrufe, Interrupts (VIB-9, VIB-10, VIB-11, VIB-14)
            shell.Register(new DriversCommand());
            shell.Register(new SendCommand());
            shell.Register(new SyscallsCommand());
            shell.Register(new InterruptsCommand());
        }
    }
}
