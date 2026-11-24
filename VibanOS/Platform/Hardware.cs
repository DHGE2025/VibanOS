using Cosmos.Core;

namespace VibanOS.Platform
{
    /// <summary>
    /// Die einzige Stelle neben Kernel.cs, die COSMOS direkt anspricht. Alles andere geht über
    /// diese Klasse (bzw. über Syscalls), damit der Kern unabhängig von COSMOS bleibt.
    /// tools/Preview ersetzt diese Datei durch eine Attrappe.
    /// </summary>
    public static class Hardware
    {
        public static void Shutdown() { Cosmos.System.Power.Shutdown(); }

        public static void Reboot() { Cosmos.System.Power.Reboot(); }

        public static byte In(int port) { return IOPort.Read8(port); }

        public static void Out(int port, byte value) { IOPort.Write8(port, value); }
    }
}
