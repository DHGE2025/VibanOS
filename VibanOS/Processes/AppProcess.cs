namespace VibanOS.Processes
{
    /// <summary>
    /// Prozess eines offenen Fensters. Der Schreibtisch startet ihn beim Öffnen und beendet ihn beim Schließen,
    /// damit ps und die Aktivitätsanzeige auch die Apps zeigen. Wird er beendet (kill, Signal), schließt sich das Fenster.
    /// </summary>
    public class AppProcess : Process
    {
        public AppProcess(string name) { Name = name; Output = "Fenster offen"; }

        public override void Step() { }
    }
}
