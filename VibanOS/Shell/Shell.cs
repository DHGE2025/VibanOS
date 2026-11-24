using System;
using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.FileSystem;

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
    /// Die Shell (VIB-1): kennt alle Befehle, zerlegt Eingaben und ruft den passenden Befehl auf.
    /// Sie hält nur Text. Angezeigt wird er vom Terminal-Fenster (Gui/TerminalWindow).
    /// </summary>
    public class Shell
    {
        private const int MaxLines = 300;

        public readonly List<Command> Commands = new List<Command>();
        public readonly List<ShellLine> Lines = new List<ShellLine>();
        public readonly List<string> History = new List<string>();

        /// <summary>Aktuelles Verzeichnis als vollständiger Pfad.</summary>
        public string CurrentDirectory = Vfs.Root;

        // Offene Rückfrage eines Befehls (z. B. Passwort). Solange sie offen ist, geht die nächste Eingabe dorthin.
        private string question;
        private AnswerHandler answer;

        /// <summary>true: Die Eingabe wird mit Punkten verdeckt.</summary>
        public bool Masked;

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
                string user = OS.Users.Current != null ? OS.Users.Current.Name : "?";
                return user + "@" + OS.Name + " " + CurrentDirectory + "> ";
            }
        }

        public void WriteLine(string text) { WriteLine(text, LineStyle.Normal); }

        public void Error(string text) { WriteLine(text, LineStyle.Error); }

        public void WriteLine(string text, LineStyle style)
        {
            string[] parts = text.Split('\n');
            for (int i = 0; i < parts.Length; i++)
            {
                ShellLine line = new ShellLine();
                line.Text = parts[i];
                line.Style = style;
                Lines.Add(line);
            }
            while (Lines.Count > MaxLines) Lines.RemoveAt(0);
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
                handler(input);
                return;
            }
            WriteLine(Prompt + input, LineStyle.Input);
            if (input.Trim() == "") return;
            History.Add(input);
            Execute(input);
        }

        /// <summary>Führt eine Befehlszeile aus. Fehler eines Befehls landen als Meldung im Terminal.</summary>
        public void Execute(string input)
        {
            List<string> words = Split(input);
            if (words.Count == 0) return;
            Command command = Find(words[0].ToLower());
            if (command == null)
            {
                Error("Unbekannter Befehl: " + words[0] + ". 'help' zeigt alle Befehle.");
                return;
            }
            if (!OS.Users.HasPermission(OS.Users.Current, command.Required))
            {
                Error("Keine Berechtigung: '" + command.Name + "' ist Administratoren vorbehalten.");
                return;
            }
            string[] args = new string[words.Count - 1];
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

        /// <summary>Fügt Argumente ab Index from wieder zu einem Text zusammen (für echo, write, send).</summary>
        public static string Join(string[] args, int from)
        {
            string s = "";
            for (int i = from; i < args.Length; i++) s += (i > from ? " " : "") + args[i];
            return s;
        }

        // Trennt an Leerzeichen. "Text in Anführungszeichen" bleibt ein Argument.
        private static List<string> Split(string input)
        {
            List<string> words = new List<string>();
            string word = "";
            bool quoted = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (c == '"') { quoted = !quoted; continue; }
                if (c == ' ' && !quoted)
                {
                    if (word != "") words.Add(word);
                    word = "";
                    continue;
                }
                word += c;
            }
            if (word != "") words.Add(word);
            return words;
        }
    }
}
