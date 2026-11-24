using System;
using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.Processes;

namespace VibanOS.Ipc
{
    public class Message
    {
        public int From;
        public int To;
        public string Text = "";
        public DateTime Time;
    }

    /// <summary>
    /// Nachrichten-Warteschlange eines Prozesses (VIB-13). Vorhanden ist der asynchrone Weg,
    /// SendSync und blockierendes Receive bauen auf ProcessState.Blocked auf.
    /// </summary>
    public class Mailbox
    {
        public const int Capacity = 8;

        public readonly List<Message> Messages = new List<Message>();

        /// <summary>Holt die älteste Nachricht ab, ohne zu blockieren. null, wenn keine da ist.</summary>
        public Message TryReceive()
        {
            if (Messages.Count == 0) return null;
            Message m = Messages[0];
            Messages.RemoveAt(0);
            return m;
        }

        /// <summary>Asynchrones Senden. false, wenn es den Empfänger nicht gibt oder seine Mailbox voll ist.</summary>
        public static bool Send(int from, int to, string text)
        {
            Process target = OS.Processes.Get(to);
            if (target == null || target.Mailbox.Messages.Count >= Capacity) return false;
            Message m = new Message();
            m.From = from;
            m.To = to;
            m.Text = text;
            m.Time = DateTime.Now;
            target.Mailbox.Messages.Add(m);
            return true;
        }
    }
}
