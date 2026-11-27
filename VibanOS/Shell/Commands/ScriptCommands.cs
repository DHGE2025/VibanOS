using System.Collections.Generic;
using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Users;

namespace VibanOS.Cli.Commands
{
    /// <summary>
    /// Ein Skript (VIB-28): eine Textdatei mit Befehlen, die sh Zeile für Zeile ausführt. Wie rm -r arbeitet es in Schritten,
    /// je Durchlauf der Kernel-Schleife eine Zeile (Shell.Tick), damit nichts blockiert. Startet eine Zeile selbst Arbeit in
    /// Schritten (rm -r, sleep, ein weiteres sh) oder fragt nach (shell.Ask), geht es erst danach weiter.
    /// Neben Befehlen kennt es Kommentare (#), Variablen (name=wert, $name), set -x, if/else/fi und for/done.
    /// Meldet eine Zeile einen Fehler, bricht das Skript mit ihrer Nummer ab.
    /// </summary>
    public class ScriptJob : Job
    {
        public const int MaxDepth = 8;        // sh in sh in sh ...: so tief, dann ist Schluss (ein Skript, das sich selbst startet)
        public const int MaxSize = 64 * 1024; // größere Skripte liest sh nicht
        public const int MaxItems = 1000;     // Werte einer for-Schleife

        private class Loop
        {
            public int At;              // Zeile mit for
            public string Variable;
            public List<string> Items;
            public int Index;
        }

        private readonly string file;                            // Name, wie er aufgerufen wurde
        private readonly List<string> lines = new List<string>(); // nur Zeilen mit Inhalt, ohne Kommentare
        private readonly List<int> numbers = new List<int>();     // ihre Nummer in der Datei, ab 1
        private readonly int total;                               // Zeilen der Datei
        private int[] jump;    // if: zugehöriges else oder fi, else: fi, for: done, done: for
        private readonly List<Loop> loops = new List<Loop>();
        private readonly User user;
        private bool trace;    // set -x: jede Zeile wie getippt zeigen
        private int pc;        // nächste Zeile
        private int current = -1;
        private bool ran;      // im letzten Schritt lief eine Zeile, ihre Fehler zählen noch
        private int errorsBefore;

        private ScriptJob(string file, string text, bool trace)
        {
            this.file = file;
            this.trace = trace;
            user = OS.Users.Current;
            string[] all = text.Replace("\r", "").Split('\n');
            total = all.Length > 1 && all[all.Length - 1] == "" ? all.Length - 1 : all.Length; // nach dem letzten Zeilenende kommt keine Zeile mehr
            for (int i = 0; i < all.Length; i++)
            {
                string line = all[i].Replace('\t', ' ').Trim();
                if (line == "" || line[0] == '#') continue;
                lines.Add(line);
                numbers.Add(i + 1);
            }
        }

        /// <summary>Liest ein Skript ein und prüft, ob if/fi und for/done zusammenpassen. null: Der Grund steht im Terminal.</summary>
        public static ScriptJob Load(string file, string text, bool trace, Shell shell)
        {
            ScriptJob job = new ScriptJob(file, text, trace);
            return job.Match(shell) ? job : null;
        }

        public override string Name { get { return "sh"; } }

        public override string Status
        {
            get
            {
                int line = current >= 0 ? numbers[current] : 1;
                return "sh " + file + ": Zeile " + line + " von " + total + ", Escape bricht ab";
            }
        }

        // Erstes Wort einer Zeile, für if, else, fi, for, done und set.
        private static string Keyword(string line)
        {
            int space = line.IndexOf(' ');
            return space < 0 ? line : line.Substring(0, space);
        }

        private bool Match(Shell shell)
        {
            jump = new int[lines.Count];
            List<int> open = new List<int>(); // offene if und for, das innerste hinten
            for (int i = 0; i < lines.Count; i++)
            {
                string word = Keyword(lines[i]);
                int top = open.Count > 0 ? open[open.Count - 1] : -1;
                string opened = top >= 0 ? Keyword(lines[top]) : "";
                if (word == "if" || word == "for")
                {
                    if (lines[i] == word) return Fail(shell, i, word == "if" ? "if braucht eine Bedingung." : "Aufruf: for name in wert1 wert2 ...");
                    open.Add(i);
                    jump[i] = -1;
                }
                else if (word == "else" || word == "fi" || word == "done")
                {
                    if (lines[i] != word) return Fail(shell, i, word + " steht allein in seiner Zeile.");
                    string needs = word == "done" ? "for" : "if";
                    if (opened != needs) return Fail(shell, i, word + " ohne passendes " + needs + ".");
                    if (word == "else")
                    {
                        if (jump[top] >= 0) return Fail(shell, i, "Zweites else zum if in Zeile " + numbers[top] + ".");
                        jump[top] = i;
                        continue;
                    }
                    // fi: Ein if ohne else springt bei falscher Bedingung hierher, ein else springt am Ende seines Teils hierher.
                    if (word == "fi" && jump[top] >= 0) jump[jump[top]] = i;
                    else jump[top] = i;
                    if (word == "done") jump[i] = top;
                    open.RemoveAt(open.Count - 1);
                }
            }
            if (open.Count > 0)
            {
                int top = open[open.Count - 1];
                return Fail(shell, top, Keyword(lines[top]) == "if" ? "Zu diesem if fehlt fi." : "Zu diesem for fehlt done.");
            }
            return true;
        }

        private bool Fail(Shell shell, int i, string message)
        {
            shell.Error("sh " + file + ", Zeile " + numbers[i] + ": " + message);
            return false;
        }

        public override bool Step(Shell shell)
        {
            // Abgemeldet oder mit su gewechselt: Was der eine gestartet hat, läuft nicht als der andere weiter.
            if (OS.Users.Current != user)
            {
                shell.Error("sh " + file + " beendet, weil der Nutzer gewechselt hat.");
                return true;
            }
            if (ran)
            {
                ran = false;
                if (shell.Errors != errorsBefore)
                {
                    shell.Error("sh " + file + ": abgebrochen nach dem Fehler in Zeile " + numbers[current] + ".");
                    return true;
                }
            }
            if (pc >= lines.Count) return true;
            current = pc;
            errorsBefore = shell.Errors;
            ran = true;
            Run(shell, pc);
            return false;
        }

        public override bool Stop(Shell shell)
        {
            shell.WriteLine("sh " + file + ": abgebrochen" + (current >= 0 ? " in Zeile " + numbers[current] : "") + ".");
            return true;
        }

        // Führt Zeile i aus und setzt pc auf die nächste.
        private void Run(Shell shell, int i)
        {
            string line = lines[i];
            string word = Keyword(line);
            pc = i + 1;
            if (word == "if")
            {
                bool yes;
                if (Condition(shell.Words(line.Substring(2)), shell, out yes) && !yes) pc = jump[i] + 1;
                return;
            }
            if (word == "else") { pc = jump[i] + 1; return; } // Ende des Teils für eine wahre Bedingung
            if (word == "fi") return;
            if (word == "for") { For(shell, i); return; }
            if (word == "done")
            {
                Loop loop = loops[loops.Count - 1];
                loop.Index++;
                if (loop.Index < loop.Items.Count)
                {
                    shell.SetVariable(loop.Variable, loop.Items[loop.Index]);
                    pc = loop.At + 1;
                }
                else loops.RemoveAt(loops.Count - 1);
                return;
            }
            if (line == "set -x" || line == "set +x") { trace = line == "set -x"; return; }
            if (trace) shell.WriteLine(shell.CommandPrompt + shell.Expand(line), LineStyle.Input);
            shell.Execute(line);
        }

        private void For(Shell shell, int i)
        {
            List<string> words = shell.Words(lines[i]);
            if (words.Count < 3 || words[2] != "in" || !Shell.ValidName(words[1]))
            {
                shell.Error("Aufruf: for name in wert1 wert2 ...");
                return;
            }
            if (words.Count - 3 > MaxItems) { shell.Error("Zu viele Werte für for, höchstens " + MaxItems + "."); return; }
            Loop loop = new Loop();
            loop.At = i;
            loop.Variable = words[1];
            loop.Items = words.GetRange(3, words.Count - 3);
            if (loop.Items.Count == 0) { pc = jump[i] + 1; return; }
            if (!shell.SetVariable(loop.Variable, loop.Items[0])) return;
            loops.Add(loop);
        }

        /// <summary>
        /// Bedingung von if: "a == b", "a != b" oder "exists name" (Datei oder Ordner), mit ! davor das Gegenteil.
        /// false: Die Bedingung ist falsch geschrieben, der Grund steht im Terminal.
        /// </summary>
        public static bool Condition(List<string> words, Shell shell, out bool yes)
        {
            yes = false;
            bool not = words.Count > 0 && words[0] == "!";
            if (not) words.RemoveAt(0);
            if (words.Count == 2 && words[0] == "exists") yes = Exists(words[1], shell);
            else if (words.Count == 3 && words[1] == "==") yes = words[0] == words[2];
            else if (words.Count == 3 && words[1] == "!=") yes = words[0] != words[2];
            else
            {
                shell.Error("if versteht: a == b, a != b und exists <name>, mit ! davor das Gegenteil. Leere Werte in Anführungszeichen: \"$name\".");
                return false;
            }
            if (not) yes = !yes;
            return true;
        }

        private static bool Exists(string name, Shell shell)
        {
            if (!Vfs.Available || name == "") return false;
            string path = Vfs.Resolve(shell.CurrentDirectory, name);
            return Vfs.CanAccess(OS.Users.Current, path, Access.Read) && (File.Exists(path) || Directory.Exists(path));
        }
    }

    public class ShCommand : Command
    {
        public const string Autostart = "autostart.sh";

        public override string Name { get { return "sh"; } }
        public override string Usage { get { return "sh [-x|-p] <skript>"; } }
        public override string Description { get { return "Skript Zeile für Zeile ausführen, -x zeigt jede Zeile, -p zeigt das Skript"; } }

        public override void Execute(string[] args, Shell shell)
        {
            string option = args.Length > 0 && (args[0] == "-x" || args[0] == "-p") ? args[0] : null;
            if (args.Length != (option != null ? 2 : 1))
            {
                shell.Error("Aufruf: " + Usage + ". Eingebaut: " + Shell.Join(BuiltInScripts.Names, 0));
                return;
            }
            string name = args[args.Length - 1];
            string text = Read(name, shell);
            if (text == null) return;
            if (option == "-p") { shell.WriteLine(text); return; }

            int depth = 0;
            for (Job j = shell.Running; j != null; j = j.Parent)
                if (j is ScriptJob) depth++;
            if (depth >= ScriptJob.MaxDepth) { shell.Error("Zu viele Skripte ineinander, höchstens " + ScriptJob.MaxDepth + "."); return; }

            ScriptJob job = ScriptJob.Load(name, text, option == "-x", shell);
            if (job != null) shell.Start(job);
        }

        // Inhalt des Skripts: eine Datei, sonst ein eingebautes Skript wie demo.sh. Eine eigene Datei gleichen Namens geht vor.
        private static string Read(string name, Shell shell)
        {
            bool plain = name.IndexOf('/') < 0 && name.IndexOf('\\') < 0;
            if (Vfs.Available)
            {
                string path = Vfs.Resolve(shell.CurrentDirectory, name);
                if (Vfs.CanAccess(OS.Users.Current, path, Access.Read) && File.Exists(path))
                {
                    bool more;
                    string text = Vfs.ReadStart(path, ScriptJob.MaxSize, out more);
                    if (more) { shell.Error("Das Skript ist zu groß, höchstens " + ScriptJob.MaxSize / 1024 + " KB."); return null; }
                    return text;
                }
                string builtIn = plain ? BuiltInScripts.Find(name) : null;
                if (builtIn != null) return builtIn;
                if (!Vfs.CanAccess(OS.Users.Current, path, Access.Read)) shell.Error("Zugriff verweigert: " + path);
                else if (Directory.Exists(path)) shell.Error("Das ist ein Ordner: " + path);
                else shell.Error("Skript nicht gefunden: " + path);
                return null;
            }
            string fallback = plain ? BuiltInScripts.Find(name) : null;
            if (fallback == null) shell.Error("Kein Dateisystem gefunden. Ohne geht nur ein eingebautes Skript: " + Shell.Join(BuiltInScripts.Names, 0));
            return fallback;
        }

        /// <summary>
        /// Nach der Anmeldung: home/name/autostart.sh ausführen, wenn es die Datei gibt. Nicht für Nutzer mit Systemrechten:
        /// In jeden Home-Ordner darf bisher jeder schreiben (Dateirechte kommen mit VIB-18), ein anderer Nutzer könnte root
        /// sonst Befehle unterschieben.
        /// </summary>
        public static void RunAutostart(Shell shell, User user)
        {
            if (!Vfs.Available || user == null || shell.Busy || shell.Asking) return;
            string path = Vfs.Combine(user.HomeDirectory, Autostart);
            if (!File.Exists(path)) return;
            if (Privileged(user))
            {
                shell.WriteLine(Autostart + " übersprungen: Für Nutzer mit Systemrechten läuft es erst, wenn VIB-18 fremde Home-Ordner schützt.", LineStyle.Muted);
                return;
            }
            shell.WriteLine(Autostart + " läuft.", LineStyle.Muted);
            shell.Execute("sh \"" + path + "\"");
        }

        private static bool Privileged(User user)
        {
            if (user.Name == UserManager.Root || UserManager.InGroup(user, UserManager.AdminGroup)) return true;
            for (int i = 0; i < UserManager.AllPermissions.Length; i++)
                if (OS.Users.HasPermission(user, UserManager.AllPermissions[i])) return true;
            return false;
        }
    }

    public class SleepCommand : Command
    {
        public const int MaxSeconds = 600;

        public override string Name { get { return "sleep"; } }
        public override string Usage { get { return "sleep <sekunden>"; } }
        public override string Description { get { return "Warten, etwa in einem Skript, auch 0.5 geht. Escape bricht ab"; } }

        public override void Execute(string[] args, Shell shell)
        {
            long ms;
            if (args.Length != 1 || !Parse(args[0], out ms)) { shell.Error("Aufruf: " + Usage + ", höchstens " + MaxSeconds + " Sekunden."); return; }
            if (ms > 0) shell.Start(new SleepJob(Shell.Clock() + ms));
        }

        // Sekunden mit bis zu drei Nachkommastellen ("2", "0.5") in Millisekunden. Ohne Ländereinstellung: immer mit Punkt.
        public static bool Parse(string text, out long ms)
        {
            ms = 0;
            int dot = text.IndexOf('.');
            string whole = dot < 0 ? text : text.Substring(0, dot), part = dot < 0 ? "" : text.Substring(dot + 1);
            if ((whole == "" && part == "") || whole.Length > 3 || part.Length > 3) return false;
            for (int i = 0; i < whole.Length; i++) { if (whole[i] < '0' || whole[i] > '9') return false; ms = ms * 10 + (whole[i] - '0'); }
            ms *= 1000;
            int scale = 100;
            for (int i = 0; i < part.Length; i++, scale /= 10) { if (part[i] < '0' || part[i] > '9') return false; ms += (part[i] - '0') * scale; }
            return ms <= MaxSeconds * 1000L;
        }
    }

    /// <summary>sleep: wartet, ohne die Kernel-Schleife anzuhalten. Jeder Durchlauf schaut nur auf die Uhr.</summary>
    public class SleepJob : Job
    {
        private readonly long until;

        public SleepJob(long until) { this.until = until; }

        public override string Name { get { return "sleep"; } }

        public override string Status
        {
            get
            {
                long left = until - Shell.Clock();
                return "sleep: noch " + (left <= 0 ? 0 : (left + 999) / 1000) + " s, Escape bricht ab";
            }
        }

        public override bool Step(Shell shell) { return Shell.Clock() >= until; }
    }

    public class SetCommand : Command
    {
        public override string Name { get { return "set"; } }
        public override string Description { get { return "Variablen anzeigen. Setzen mit name=wert, lesen mit $name"; } }

        public override void Execute(string[] args, Shell shell)
        {
            if (args.Length == 1 && (args[0] == "-x" || args[0] == "+x")) { shell.Error("set " + args[0] + " gilt nur in Skripten (sh)."); return; }
            if (args.Length > 0) { shell.Error("Aufruf: set. Eine Variable setzt name=wert."); return; }
            List<string> list = shell.VariableList();
            shell.WriteLine("USER=" + shell.Variable("USER") + "  HOME=" + shell.Variable("HOME") + "  PWD=" + shell.Variable("PWD") + "  (vom System)", LineStyle.Muted);
            for (int i = 0; i < list.Count; i++) shell.WriteLine(list[i]);
        }
    }

    /// <summary>
    /// Skripte, die VibanOS mitbringt. sh findet sie unter ihrem Namen, wenn im aktuellen Ordner keine Datei so heißt.
    /// So gibt es demo.sh für die Vorführung (VIB-4) auf jeder Platte, auch ohne Dateisystem.
    /// </summary>
    public static class BuiltInScripts
    {
        public static readonly string[] Names = { "demo.sh" };

        public static string Find(string name)
        {
            return name.ToLowerInvariant() == "demo.sh" ? Demo : null;
        }

        public const string Demo =
@"# demo.sh: Vorführung von VibanOS (VIB-28). Zeigt die wichtigsten Teilsysteme nacheinander.
# Start mit 'sh demo.sh', Escape bricht ab. 'sh -p demo.sh' zeigt dieses Skript.
# Zeilen mit # sind Kommentare. Nach set -x erscheint jeder Befehl, als würde er getippt.
# Das Skript wechselt in den eigenen Ordner und legt dort kurz den Ordner vorfuehrung an.
pause=2

echo ""VibanOS-Vorführung für $USER. Es geht los in""
for n in 3 2 1
echo ""  $n ...""
sleep 1
done

echo """"
echo ""== 1. Nutzer und Rechte (VIB-16, VIB-17) ==""
set -x
whoami
users
groups
perms
set +x
sleep $pause

echo """"
echo ""== 2. Dateisystem (VIB-2) ==""
if ! exists ""$HOME""
echo ""Ohne Festplatte oder eigenen Ordner fällt dieser Teil aus.""
else
cd ""$HOME""
if exists vorfuehrung
echo ""Den Ordner vorfuehrung gibt es schon, er bleibt, wie er ist.""
else
set -x
mkdir vorfuehrung
write vorfuehrung/notiz.txt ""Geschrieben von demo.sh""
cp vorfuehrung/notiz.txt vorfuehrung/kopie.txt
ls vorfuehrung
cat vorfuehrung/kopie.txt
rm vorfuehrung/kopie.txt
rm vorfuehrung/notiz.txt
rm vorfuehrung
df
set +x
fi
fi
sleep $pause

echo """"
echo ""== 3. Prozesse und Scheduler (VIB-3, VIB-15) ==""
set -x
start counter
zaehler=$!
start clock
uhr=$!
ps
sleep $pause
ps
context $zaehler
set +x

echo """"
echo ""== 4. Speicher (VIB-5) ==""
set -x
mem
alloc $zaehler 2
pagetable $zaehler
translate $zaehler 0x1010
kill $zaehler
kill $uhr
mem
set +x
sleep $pause

echo """"
echo ""== 5. Treiber, Systemaufrufe, Interrupts (VIB-9, VIB-11, VIB-14) ==""
set -x
drivers
syscalls
interrupts
set +x

echo """"
echo ""Fertig. 'help' zeigt alle Befehle, 'sh -p demo.sh' dieses Skript.""
";
    }
}
