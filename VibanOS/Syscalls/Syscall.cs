using System;
using System.Collections.Generic;
using VibanOS.Core;
using VibanOS.Platform;
using VibanOS.Processes;

namespace VibanOS.Syscalls
{
    /// <summary>Nummern der Systemaufrufe (VIB-11). Read und OpenFile sind reserviert, aber noch nicht umgesetzt.</summary>
    public enum SyscallNumber
    {
        Write = 1, Read = 2, OpenFile = 3, Alloc = 4, Free = 5, GetTime = 6, Exit = 7,
        Shutdown = 8, Reboot = 9, PortIn = 10, PortOut = 11
    }

    public delegate object SyscallHandler(object[] args);

    public class SyscallEntry
    {
        public SyscallNumber Number;
        public string Name = "";
        public string Description = "";
        /// <summary>true: nur im Kernelmode erlaubt (VIB-12).</summary>
        public bool KernelOnly;
        public SyscallHandler Handler;
    }

    public class SyscallResult
    {
        public const int Ok = 0;
        public const int Unknown = 1;
        public const int Denied = 2;
        public const int Failed = 3;

        public int Error;
        public object Value;

        public string Message
        {
            get
            {
                if (Error == Unknown) return "Unbekannter Systemaufruf";
                if (Error == Denied) return "Nicht erlaubt: Dieser Systemaufruf braucht den Kernelmode";
                if (Error == Failed) return "Systemaufruf fehlgeschlagen: " + Value;
                return "";
            }
        }
    }

    /// <summary>Ordnet jeder Nummer einen Handler zu. Neue Systemaufrufe werden in RegisterDefaults ergänzt.</summary>
    public static class SyscallTable
    {
        public static readonly List<SyscallEntry> Entries = new List<SyscallEntry>();

        public static void Register(SyscallNumber number, string name, string description, bool kernelOnly, SyscallHandler handler)
        {
            SyscallEntry e = new SyscallEntry();
            e.Number = number;
            e.Name = name;
            e.Description = description;
            e.KernelOnly = kernelOnly;
            e.Handler = handler;
            Entries.Add(e);
        }

        public static SyscallEntry Find(SyscallNumber number)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Number == number) return Entries[i];
            return null;
        }

        public static void RegisterDefaults()
        {
            Entries.Clear();
            Register(SyscallNumber.Write, "write", "Text im Terminal ausgeben", false,
                a => { OS.Shell.WriteLine((string)a[0]); return null; });
            Register(SyscallNumber.Alloc, "alloc", "Eine Speicherseite anfordern", false,
                a => OS.Memory.AllocatePage(Syscall.CallerId));
            Register(SyscallNumber.Free, "free", "Eine Speicherseite freigeben", false,
                a => OS.Memory.FreePage(Syscall.CallerId, (int)a[0]));
            Register(SyscallNumber.GetTime, "gettime", "Datum und Uhrzeit lesen", false,
                a => DateTime.Now);
            Register(SyscallNumber.Exit, "exit", "Den aufrufenden Prozess beenden", false,
                a => { if (OS.Processes.Current != null) OS.Processes.Current.State = ProcessState.Terminated; return null; });
            Register(SyscallNumber.Shutdown, "shutdown", "System ausschalten", true,
                a => { Hardware.Shutdown(); return null; });
            Register(SyscallNumber.Reboot, "reboot", "System neu starten", true,
                a => { Hardware.Reboot(); return null; });
            Register(SyscallNumber.PortIn, "inb", "Ein Byte von einem I/O-Port lesen", true,
                a => Hardware.In((int)a[0]));
            Register(SyscallNumber.PortOut, "outb", "Ein Byte auf einen I/O-Port schreiben", true,
                a => { Hardware.Out((int)a[0], (byte)(int)a[1]); return null; });
        }
    }

    /// <summary>
    /// Die Systemaufruf-Schnittstelle. Shell-Befehle und Prozesse rufen Kernel-Funktionen
    /// über Invoke auf, nicht direkt. Hier sitzen Modusprüfung (VIB-12) und Protokoll.
    /// </summary>
    public static class Syscall
    {
        /// <summary>Protokolliert jeden Aufruf (strace).</summary>
        public static bool Trace;
        public static readonly List<string> Log = new List<string>();

        /// <summary>Modus der Shell. Prozesse bringen ihren eigenen Modus mit.</summary>
        public static ProcessMode ShellMode = ProcessMode.Kernel;

        /// <summary>Prozess-ID des Aufrufers, 0 steht für Kernel und Shell.</summary>
        public static int CallerId { get { return OS.Processes.Current != null ? OS.Processes.Current.Id : 0; } }

        public static ProcessMode CallerMode { get { return OS.Processes.Current != null ? OS.Processes.Current.Mode : ShellMode; } }

        public static SyscallResult Invoke(SyscallNumber number, params object[] args)
        {
            SyscallResult result = new SyscallResult();
            SyscallEntry entry = SyscallTable.Find(number);
            if (entry == null)
            {
                result.Error = SyscallResult.Unknown;
            }
            else if (entry.KernelOnly && CallerMode != ProcessMode.Kernel)
            {
                result.Error = SyscallResult.Denied;
                Record("ABGELEHNT pid " + CallerId + ": " + entry.Name);
            }
            else
            {
                try
                {
                    result.Value = entry.Handler(args);
                }
                catch (Exception e)
                {
                    result.Error = SyscallResult.Failed;
                    result.Value = e.Message;
                }
                if (Trace) Record("pid " + CallerId + ": " + entry.Name + " = " + result.Error);
            }
            return result;
        }

        private static void Record(string line)
        {
            if (Log.Count >= 200) Log.RemoveAt(0);
            Log.Add(line);
        }
    }
}
