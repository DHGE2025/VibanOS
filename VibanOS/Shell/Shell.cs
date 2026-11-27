using System;
using System.Collections.Generic;
using System.Diagnostics;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Users;

namespace VibanOS.Cli
{
    public enum LineStyle { Normal, Muted, Error, Input }

    public class ShellLine
    {
        public string Text = "";
        public LineStyle Style;
    }

    public delegate void AnswerHandler(string answer);

    /// <summary>
    /// Arbeit eines Befehls, die zu lange für einen Durchlauf der Kernel-Schleife dauern kann (rm -r, cp -r). Die Schleife
    /// darf nie blockieren, deshalb macht Shell.Tick je Durchlauf nur einen kleinen Schritt. Solange die Arbeit läuft, nimmt
    /// die Shell keine Befehle an, Escape im Terminal bricht ab (Shell.Cancel).
    /// </summary>
    public abstract class Job
    {
        /// <summary>Name des Befehls. Steht vor einer Fehlermeldung, wie bei Befehlen.</summary>
        public abstract string Name { get; }

        /// <summary>Steht statt des Prompts in der Eingabezeile, etwa "rm -r: 3 von 12 gelöscht".</summary>
        public abstract string Status { get; }

        /// <summary>Ein Schritt, höchstens eine Dateioperation. true: Die Arbeit ist fertig.</summary>
        public abstract bool Step(Shell shell);

        /// <summary>Abbrechen (Escape). true: Die Arbeit ist damit vorbei. false: Sie räumt in weiteren Schritten noch auf.</summary>
        public virtual bool Stop(Shell shell) { return true; }

        /// <summary>Gestartet unter sudo. Dann gelten die Rechte für alle Schritte, nicht nur für den Aufruf, der sie startet.</summary>
        public bool Elevated;

        /// <summary>
        /// Die Arbeit, die diese gestartet hat, oder null. Ein Skript (sh, VIB-28) startet etwa rm -r: Dann läuft erst rm -r
        /// zu Ende, danach geht das Skript weiter.
        /// </summary>
        public Job Parent { get; internal set; }
    }

    /// <summary>
    /// Die Shell (VIB-1): kennt alle Befehle, zerlegt Eingaben und ruft den passenden Befehl auf.
    /// Sie hält nur Text. Angezeigt wird er vom Terminal-Fenster (Gui/TerminalWindow).
    /// </summary>
    public class Shell
    {
        private const int MaxLines = 300;
        private const int MaxLineLength = 2000;
        private const int MaxHistory = 100;

        public readonly List<Command> Commands = new List<Command>();
        public readonly List<ShellLine> Lines = new List<ShellLine>();
        public readonly List<string> History = new List<string>();

        /// <summary>Aktuelles Verzeichnis als vollständiger Pfad.</summary>
        public string CurrentDirectory = Vfs.Root;

        // Offene Rückfrage eines Befehls (z. B. Passwort). Solange sie offen ist, geht die nächste Eingabe dorthin.
        private string question;
        private AnswerHandler answer;

        /// <summary>true: Die Eingabe wird mit Sternchen verdeckt.</summary>
        public bool Masked;

        /// <summary>true: Ein Befehl wartet auf eine Antwort. Die nächste Eingabe ist dann kein Befehl.</summary>
        public bool Asking { get { return answer != null; } }

        // Laufende Arbeit eines Befehls in Schritten (rm -r, cp -r), null ohne. Sie darf zwischendurch mit Ask nachfragen.
        private Job job;

        /// <summary>true: Ein Befehl arbeitet noch in Schritten. Bis er fertig ist, nimmt die Shell keine Befehle an.</summary>
        public bool Busy { get { return job != null; } }

        /// <summary>Die Arbeit, die gerade ihre Schritte macht, null ohne. Ihre Vorgänger hängen an Job.Parent.</summary>
        public Job Running { get { return job; } }

        /// <summary>
        /// Zählt alle Fehlermeldungen. Ein Skript (VIB-28) sieht daran, ob eine Zeile gescheitert ist, auch wenn der Fehler
        /// erst in einem späteren Schritt oder in der Antwort auf eine Rückfrage kommt.
        /// </summary>
        public int Errors;

        /// <summary>Uhr in Millisekunden für sleep. Der Selbsttest setzt eine eigene, damit er nicht wirklich warten muss.</summary>
        public static Func<long> Clock = Milliseconds;

        private static long Milliseconds()
        {
            long f = Stopwatch.Frequency, t = Stopwatch.GetTimestamp();
            if (f <= 0) return OS.Ticks * 10; // ohne Uhr: ein Durchlauf der Kernel-Schleife dauert mindestens 10 ms
            return t / f * 1000 + t % f * 1000 / f;
        }

        // Variablen der Shell (VIB-28): name=wert setzt, $name liest. Sie gehören dem Nutzer, der sie gesetzt hat.
        private const int MaxVariables = 100;
        private const int MaxValueLength = 1000;
        private readonly Dictionary<string, string> variables = new Dictionary<string, string>();
        private User variablesOf;

        public void Register(Command command) { Commands.Add(command); }

        public Command Find(string name)
        {
            for (int i = 0; i < Commands.Count; i++)
                if (Commands[i].Name == name) return Commands[i];
            return null;
        }

        public string Prompt
        {
            get
            {
                if (question != null) return question;
                if (job != null) return job.Status;
                return CommandPrompt;
            }
        }

        /// <summary>Der Prompt vor einem Befehl, auch während eine Arbeit läuft. sh -x zeigt damit jede Zeile wie getippt.</summary>
        public string CommandPrompt
        {
            get
            {
                string user = OS.Users.Current != null ? OS.Users.Current.Name : "?";
                return user + "@" + OS.Name + " " + CurrentDirectory + "> ";
            }
        }

        public void WriteLine(string text) { WriteLine(text, LineStyle.Normal); }

        public void Error(string text) { WriteLine(text, LineStyle.Error); }

        public void WriteLine(string text, LineStyle style)
        {
            // Zeilenenden aus Windows-Dateien und Tabulatoren kennt die Schrift nicht als Zeichen.
            string[] parts = text.Replace("\r", "").Replace("\t", "    ").Split('\n');
            // Mehr als MaxLines Zeilen hebt die Shell nicht auf, von sehr langen Texten bleibt also das Ende.
            for (int i = parts.Length > MaxLines ? parts.Length - MaxLines : 0; i < parts.Length; i++)
            {
                ShellLine line = new ShellLine();
                // Überlange Zeilen (etwa aus einer Binärdatei) würden jedes Neuzeichnen ausbremsen.
                line.Text = parts[i].Length > MaxLineLength ? parts[i].Substring(0, MaxLineLength) + " [gekürzt]" : parts[i];
                line.Style = style;
                Lines.Add(line);
            }
            if (style == LineStyle.Error) Errors++;
            if (Lines.Count > MaxLines) Lines.RemoveRange(0, Lines.Count - MaxLines);
            OS.Changed = true;
        }

        public void Clear()
        {
            Lines.Clear();
            OS.Changed = true;
        }

        /// <summary>
        /// Stellt dem Nutzer eine Rückfrage. Die Kernel-Schleife darf nie blockieren, deshalb kommt
        /// die Antwort später über handler statt als Rückgabewert.
        /// </summary>
        public void Ask(string prompt, bool masked, AnswerHandler handler)
        {
            question = prompt;
            Masked = masked;
            answer = handler;
            Settings.ApplyKeyboard(); // Passwörter tippt man deutsch
            OS.Changed = true;
        }

        /// <summary>
        /// Startet Arbeit in Schritten. Execute kehrt danach sofort zurück, OS.Tick macht die Schritte. Läuft der Befehl unter
        /// sudo, bekommt jeder Schritt dieselben Rechte: Sie gelten für den ganzen Befehl, nicht nur für seinen Anfang.
        /// </summary>
        public void Start(Job work)
        {
            work.Elevated = OS.Users.Elevated;
            work.Parent = job; // läuft schon eine (ein Skript), geht sie nach dieser weiter
            job = work;
            OS.Changed = true;
        }

        /// <summary>Ein Schritt der laufenden Arbeit. OS.Tick ruft das in jedem Durchlauf der Kernel-Schleife auf.</summary>
        public void Tick()
        {
            // Während einer Rückfrage ruht die Arbeit, die Antwort entscheidet, wie es weitergeht.
            if (job == null || answer != null) return;
            Job current = job;
            bool done, elevated = OS.Users.Elevated;
            OS.Users.Elevated = current.Elevated;
            try { done = current.Step(this); }
            catch (Exception e)
            {
                Error(current.Name + ": " + e.Message);
                done = true;
            }
            finally { OS.Users.Elevated = elevated; }
            if (done) Remove(current);
            OS.Changed = true;
        }

        // Nimmt eine fertige Arbeit aus der Kette. Hat sie im letzten Schritt noch eine gestartet, bleibt die vorn.
        private void Remove(Job done)
        {
            if (job == done) { job = done.Parent; return; }
            for (Job j = job; j != null; j = j.Parent)
                if (j.Parent == done) { j.Parent = done.Parent; return; }
        }

        /// <summary>
        /// Bricht die vorderste Arbeit ab, etwa nach einer Antwort, die nicht bestätigt. Ein Skript, das sie gestartet hat,
        /// läuft danach weiter.
        /// </summary>
        public void Cancel()
        {
            if (job != null && job.Stop(this)) job = job.Parent;
            OS.Changed = true;
        }

        /// <summary>
        /// Escape im Terminal: bricht alles ab, auch das Skript, das die vorderste Arbeit gestartet hat. Muss eine noch
        /// aufräumen (cp -r), macht nur sie weiter, bis sie fertig ist.
        /// </summary>
        public void CancelAll()
        {
            Job keep = null;
            for (Job j = job; j != null; j = j.Parent)
                if (!j.Stop(this) && keep == null) keep = j;
            if (keep != null) keep.Parent = null;
            job = keep;
            OS.Changed = true;
        }

        /// <summary>Nimmt eine fertige Eingabezeile aus dem Terminal entgegen.</summary>
        public void Submit(string input)
        {
            if (answer != null)
            {
                AnswerHandler handler = answer;
                WriteLine(question + (Masked ? "" : input), LineStyle.Input);
                question = null;
                answer = null;
                Masked = false;
                Settings.ApplyKeyboard();
                // Wie bei Befehlen: Ein Fehler in der Antwort-Behandlung landet als Meldung im Terminal.
                try { handler(input); }
                catch (Exception e) { Error(e.Message); }
                return;
            }
            // Ein Befehl arbeitet noch: Die Eingabe ist kein neuer Befehl. Das Terminal nimmt dann ohnehin nur Escape an.
            if (job != null) return;
            WriteLine(Prompt + input, LineStyle.Input);
            if (input.Trim() == "") return;
            if (History.Count >= MaxHistory) History.RemoveAt(0);
            History.Add(input);
            Execute(input);
        }

        /// <summary>Führt eine Befehlszeile aus. Fehler eines Befehls landen als Meldung im Terminal.</summary>
        public void Execute(string input)
        {
            string name, value;
            if (Assignment(input, out name, out value))
            {
                SetVariable(name, value);
                return;
            }
            List<string> words = Split(Expand(input));
            if (words.Count == 0) return;
            Run(words.ToArray());
        }

        /// <summary>Zerlegt eine Zeile in Wörter, nachdem $name ersetzt ist. Für sh, das so if und for liest.</summary>
        public List<string> Words(string input) { return Split(Expand(input)); }

        // Ist die Zeile eine Zuweisung "name=wert"? Der Wert wird wie Argumente gelesen: "Hallo Welt" in Anführungszeichen,
        // $andere ersetzt.
        private bool Assignment(string input, out string name, out string value)
        {
            name = value = null;
            string line = input.Trim();
            int equals = line.IndexOf('=');
            if (equals <= 0 || !ValidName(line.Substring(0, equals))) return false;
            name = line.Substring(0, equals);
            value = Join(Split(Expand(line.Substring(equals + 1))).ToArray(), 0);
            return true;
        }

        /// <summary>Gültiger Name einer Variablen: Buchstaben, Ziffern und _, vorn keine Ziffer.</summary>
        public static bool ValidName(string name)
        {
            if (name.Length == 0 || name.Length > 32 || (name[0] >= '0' && name[0] <= '9')) return false;
            for (int i = 0; i < name.Length; i++)
                if (!NameChar(name[i])) return false;
            return true;
        }

        private static bool NameChar(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_';
        }

        // Feste Variablen: Sie kommen aus dem System und lassen sich nicht setzen.
        private string Fixed(string name)
        {
            User user = OS.Users.Current;
            if (name == "USER") return user != null ? user.Name : "";
            if (name == "HOME") return user != null ? user.HomeDirectory : "";
            if (name == "PWD") return CurrentDirectory;
            return null;
        }

        // Variablen gehören dem Nutzer, der sie gesetzt hat. Nach einem Wechsel (abmelden, su) sieht der nächste sie nicht.
        private void Own()
        {
            if (variablesOf == OS.Users.Current) return;
            variables.Clear();
            variablesOf = OS.Users.Current;
        }

        /// <summary>Wert einer Variablen, "" wenn es sie nicht gibt (wie in anderen Shells).</summary>
        public string Variable(string name)
        {
            string value = Fixed(name);
            if (value != null) return value;
            Own();
            return variables.TryGetValue(name, out value) ? value : "";
        }

        /// <summary>Setzt eine Variable. Meldet im Terminal, warum es nicht geht.</summary>
        public bool SetVariable(string name, string value)
        {
            if (Fixed(name) != null) { Error("$" + name + " kommt vom System und lässt sich nicht setzen."); return false; }
            if (value.Length > MaxValueLength) { Error("Der Wert ist zu lang, höchstens " + MaxValueLength + " Zeichen."); return false; }
            Own();
            if (!variables.ContainsKey(name) && variables.Count >= MaxVariables) { Error("Zu viele Variablen, höchstens " + MaxVariables + "."); return false; }
            variables[name] = value;
            return true;
        }

        /// <summary>Alle gesetzten Variablen als "name=wert", sortiert. Für set.</summary>
        public List<string> VariableList()
        {
            Own();
            List<string> list = new List<string>();
            foreach (KeyValuePair<string, string> pair in variables) list.Add(pair.Key + "=" + pair.Value);
            list.Sort(string.CompareOrdinal);
            return list;
        }

        /// <summary>
        /// Ersetzt $name und ${name} durch den Wert der Variablen, $! durch die Nummer des zuletzt gestarteten Prozesses.
        /// \$ steht für ein echtes Dollarzeichen, ein $ ohne Namen dahinter bleibt stehen.
        /// </summary>
        public string Expand(string input)
        {
            if (input.IndexOf('$') < 0) return input;
            System.Text.StringBuilder result = new System.Text.StringBuilder();
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '\\' && i + 1 < input.Length && input[i + 1] == '$') { result.Append('$'); i++; continue; }
                if (c != '$' || i + 1 >= input.Length) { result.Append(c); continue; }
                char next = input[i + 1];
                if (next == '!') { result.Append(Variable("!")); i++; continue; }
                if (next == '{')
                {
                    int close = input.IndexOf('}', i + 2);
                    if (close > 0 && ValidName(input.Substring(i + 2, close - i - 2)))
                    {
                        result.Append(Variable(input.Substring(i + 2, close - i - 2)));
                        i = close;
                        continue;
                    }
                }
                else if (NameChar(next) && !(next >= '0' && next <= '9'))
                {
                    int end = i + 1;
                    while (end < input.Length && NameChar(input[end])) end++;
                    result.Append(Variable(input.Substring(i + 1, end - i - 1)));
                    i = end - 1;
                    continue;
                }
                result.Append(c);
            }
            return result.ToString();
        }

        /// <summary>
        /// Führt einen schon zerlegten Befehl aus: words[0] ist der Name, der Rest die Argumente.
        /// sudo ruft das direkt auf, damit Anführungszeichen nicht ein zweites Mal ausgewertet werden.
        /// </summary>
        public void Run(string[] words)
        {
            Command command = Find(words[0].ToLower());
            if (command == null)
            {
                Error("Unbekannter Befehl: " + words[0] + ". 'help' zeigt alle Befehle.");
                return;
            }
            if (!OS.Users.Allowed(OS.Users.Current, command.Required))
            {
                Error(Denied("'" + command.Name + "' ist Administratoren vorbehalten.", command.Required, command.Name));
                return;
            }
            string[] args = new string[words.Length - 1];
            for (int i = 0; i < args.Length; i++) args[i] = words[i + 1];
            try
            {
                command.Execute(args, this);
            }
            catch (Exception e)
            {
                Error(command.Name + ": " + e.Message);
            }
        }

        /// <summary>
        /// Meldung für ein fehlendes Recht. Hat der Nutzer es über eine Gruppe und nur sudo fehlt, sagt sie das,
        /// statt ihn glauben zu lassen, er dürfe es gar nicht.
        /// </summary>
        public static string Denied(string reason, Permission permission, string command)
        {
            if (OS.Users.HasPermission(OS.Users.Current, permission))
                return "Keine Berechtigung ohne sudo. Mit 'sudo " + command + "' geht es.";
            return "Keine Berechtigung: " + reason;
        }

        /// <summary>Fügt Argumente ab Index from wieder zu einem Text zusammen (für echo, write, send).</summary>
        public static string Join(string[] args, int from)
        {
            string s = "";
            for (int i = from; i < args.Length; i++) s += (i > from ? " " : "") + args[i];
            return s;
        }

        // Trennt an Leerzeichen. "Text in Anführungszeichen" bleibt ein Argument, auch der leere Text "".
        private static List<string> Split(string input)
        {
            List<string> words = new List<string>();
            string word = "";
            bool quoted = false, hadQuote = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '"') { quoted = !quoted; hadQuote = true; continue; }
                if (c == ' ' && !quoted)
                {
                    if (word != "" || hadQuote) words.Add(word);
                    word = "";
                    hadQuote = false;
                    continue;
                }
                word += c;
            }
            if (word != "" || hadQuote) words.Add(word);
            return words;
        }
    }
}
