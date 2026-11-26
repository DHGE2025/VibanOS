using System.Text;
using VibanOS.Platform;

namespace VibanOS.Drivers
{
    /// <summary>
    /// Liest kleine Textdateien, die QEMU dem Gast mitgibt (fw_cfg, QEMU-Option -fw_cfg name=opt/...,string=...).
    /// Darüber sagt die Werkstatt VibanOS, wie groß der Bildschirm des Rechners ist, auf dem QEMU läuft.
    /// Außerhalb von QEMU fehlt die Kennung "QEMU" am Datenport, dann gibt es keine Dateien.
    /// Aufbau nach docs/specs/fw_cfg.rst im QEMU-Quelltext: Schlüssel 16 Bit an Port 0x510, Daten byteweise an 0x511.
    /// </summary>
    public static class FwCfg
    {
        /// <summary>Bildschirm des Rechners als "BreitexHöhe" in Punkten: die Fläche, die das Fenster von QEMU im Vollbild füllt.</summary>
        public const string ScreenFile = "opt/vibanos/screen";

        private const int SelectorPort = 0x510;
        private const int DataPort = 0x511;
        private const ushort SignatureKey = 0x0000;
        private const ushort DirectoryKey = 0x0019;
        private const int NameBytes = 56;

        // QEMU legt nur wenige Dateien an. Die Grenzen schützen vor Unsinn auf einem Port, an dem etwas anderes hängt.
        private const uint MaxFiles = 256;
        private const int MaxBytes = 64;

        /// <summary>Inhalt der Datei name, höchstens 64 Zeichen. null: kein QEMU oder keine solche Datei.</summary>
        public static string Read(string name)
        {
            Hardware.Out16(SelectorPort, SignatureKey);
            if (Next() != 'Q' || Next() != 'E' || Next() != 'M' || Next() != 'U') return null;

            Hardware.Out16(SelectorPort, DirectoryKey);
            uint count = BigEndian(4);
            for (uint i = 0; i < count && i < MaxFiles; i++)
            {
                // Je Eintrag: Größe (32 Bit), Schlüssel (16 Bit), 16 Bit frei, Name mit Null am Ende. Alles big-endian.
                uint size = BigEndian(4);
                ushort key = (ushort)BigEndian(2);
                BigEndian(2);
                bool match = true;
                for (int k = 0; k < NameBytes; k++)
                {
                    int c = Next();
                    if (k <= name.Length && c != (k < name.Length ? name[k] : 0)) match = false;
                }
                if (!match) continue;

                Hardware.Out16(SelectorPort, key);
                StringBuilder text = new StringBuilder();
                for (uint k = 0; k < size && k < MaxBytes; k++) text.Append((char)Next());
                return text.ToString();
            }
            return null;
        }

        private static int Next() { return Hardware.In(DataPort); }

        private static uint BigEndian(int bytes)
        {
            uint value = 0;
            for (int i = 0; i < bytes; i++) value = (value << 8) | (uint)Next();
            return value;
        }
    }
}
