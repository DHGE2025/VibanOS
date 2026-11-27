using System;
using System.Collections.Generic;
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
            if (done) job = null;
            OS.Changed = true;
        }

        /// <summary>Bricht die laufende Arbeit ab: Escape im Terminal, oder eine Antwort, die nicht bestätigt.</summary>
        public void Cancel()
        {
            if (job != null && job.Stop(this)) job = null;
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
            List<string> words = Split(input);
            if (words.Count == 0) return;
            Run(words.ToArray());
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
