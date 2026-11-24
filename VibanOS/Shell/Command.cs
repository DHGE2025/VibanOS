using VibanOS.Users;

namespace VibanOS.Cli
{
    /// <summary>
    /// Basisklasse aller Shell-Befehle (VIB-1). Ein neuer Befehl ist eine neue Klasse
    /// plus eine Zeile in CommandList.RegisterAll.
    /// </summary>
    public abstract class Command
    {
        /// <summary>Name, wie er eingetippt wird (klein geschrieben).</summary>
        public abstract string Name { get; }

        /// <summary>Hilfetext für help.</summary>
        public abstract string Description { get; }

        /// <summary>Aufruf mit Argumenten für help, z. B. "cd &lt;ordner&gt;".</summary>
        public virtual string Usage { get { return Name; } }

        /// <summary>Systemrecht, das der angemeldete Nutzer braucht (VIB-17). None: jeder darf.</summary>
        public virtual Permission Required { get { return Permission.None; } }

        /// <summary>
        /// Führt den Befehl aus. args enthält nur die Argumente, nicht den Befehlsnamen.
        /// Ausgaben gehen über shell.WriteLine, nicht über Console: Das Terminal ist ein Fenster.
        /// </summary>
        public abstract void Execute(string[] args, Shell shell);
    }
}
