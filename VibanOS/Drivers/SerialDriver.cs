using VibanOS.Platform;

namespace VibanOS.Drivers
{
    /// <summary>
    /// Serielle Schnittstelle COM1 über I/O-Ports (VIB-9) mit zeilenweisen Nachrichten (VIB-10).
    /// Testen: 'cosmos run' zeigt die serielle Ausgabe im Terminal des Hosts, zwischen den Meldungen von COSMOS.
    /// COSMOS schreibt sein eigenes Protokoll auf denselben Anschluss. Deshalb stellt Init genau die
    /// Werte ein, die COSMOS selbst benutzt (115200 Baud 8N1), und verstellt so nichts.
    /// </summary>
    public class SerialDriver : Driver
    {
        private const int Base = 0x3F8;
        private const int LineStatus = Base + 5;

        public override string Name { get { return "com1"; } }
        public override string Description { get { return "Serielle Schnittstelle COM1, 115200 Baud 8N1"; } }

        protected override void Init()
        {
            Hardware.Out(Base + 1, 0x00); // Interrupts aus
            Hardware.Out(Base + 3, 0x80); // DLAB an: Teiler einstellen
            Hardware.Out(Base + 0, 0x01); // Teiler 1 = 115200 Baud
            Hardware.Out(Base + 1, 0x00);
            Hardware.Out(Base + 3, 0x03); // 8 Datenbits, keine Parität, 1 Stoppbit
            Hardware.Out(Base + 2, 0xC7); // FIFO an und leeren
            Hardware.Out(Base + 4, 0x0B); // DTR, RTS, OUT2
        }

        public bool WriteByte(byte value)
        {
            if (Status != DriverStatus.Running) return false;
            // Begrenzt warten, bis das Senderegister frei ist. Ohne Gegenstelle soll nichts hängen bleiben.
            for (int i = 0; i < 100000; i++)
            {
                if ((Hardware.In(LineStatus) & 0x20) == 0) continue;
                Hardware.Out(Base, value);
                return true;
            }
            return false;
        }

        /// <summary>Liest ein Byte, falls eines angekommen ist. Rückgabe -1: nichts da.</summary>
        public int ReadByte()
        {
            if (Status != DriverStatus.Running || (Hardware.In(LineStatus) & 0x01) == 0) return -1;
            return Hardware.In(Base);
        }

        /// <summary>Sendet eine Nachricht, abgeschlossen mit einem Zeilenumbruch.</summary>
        public bool SendMessage(string text)
        {
            for (int i = 0; i < text.Length; i++)
                if (!WriteByte((byte)text[i])) return false;
            return WriteByte((byte)'\n');
        }
    }
}
