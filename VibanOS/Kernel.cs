using System;
using Sys = Cosmos.System;

namespace VibanOS
{
    public class Kernel : Sys.Kernel
    {
        protected override void BeforeRun()
        {
            Console.Clear();
            Console.WriteLine("VibanOS gestartet. 'help' zeigt alle Befehle.");
        }

        protected override void Run()
        {
            Console.Write("VibanOS> ");
            var input = Console.ReadLine()?.Trim() ?? "";
            var parts = input.Split(' ', 2);
            var arg = parts.Length > 1 ? parts[1] : "";

            switch (parts[0].ToLower())
            {
                case "":
                    break;
                case "help":
                    Console.WriteLine("help, echo <text>, clear, time, reboot, shutdown");
                    break;
                case "echo":
                    Console.WriteLine(arg);
                    break;
                case "clear":
                    Console.Clear();
                    break;
                case "time":
                    Console.WriteLine(DateTime.Now.ToString());
                    break;
                case "reboot":
                    Sys.Power.Reboot();
                    break;
                case "shutdown":
                    Sys.Power.Shutdown();
                    break;
                default:
                    Console.WriteLine($"Unbekannter Befehl: {parts[0]}");
                    break;
            }
        }
    }
}
