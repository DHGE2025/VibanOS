using System;

namespace VibanOS.Core
{
    /// <summary>
    /// Kleine Texthelfer für feste Spaltenbreiten, Hex-Zahlen und deutsche Datumsangaben.
    /// Bewusst ohne Formatstrings und Kultur-Einstellungen: Der COSMOS-Kernel läuft ohne Sprachdaten.
    /// </summary>
    public static class Text
    {
        public static string Pad(string s, int width)
        {
            while (s.Length < width) s += " ";
            return s;
        }

        public static string PadLeft(string s, int width)
        {
            while (s.Length < width) s = " " + s;
            return s;
        }

        public static string Two(int n)
        {
            return n < 10 ? "0" + n : n.ToString();
        }

        public static string Hex(ulong value, int digits)
        {
            const string map = "0123456789abcdef";
            string s = "";
            for (int i = 0; i < digits; i++)
            {
                s = map[(int)(value & 15)] + s;
                value >>= 4;
            }
            return s;
        }

        public static bool HasPrefix(string s, string prefix)
        {
            if (s.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
                if (s[i] != prefix[i]) return false;
            return true;
        }

        /// <summary>Größe in Bytes für Menschen: "512 Byte", "12 KB", "3,4 MB". Komma als Dezimalzeichen.</summary>
        public static string Bytes(ulong n)
        {
            if (n < 1024) return n + " Byte";
            string[] units = { "KB", "MB", "GB", "TB" };
            ulong tenths = n / 1024 * 10 + n % 1024 * 10 / 1024; // Zehntel der Einheit, abgeschnitten, ohne Überlauf bei riesigen Werten
            int unit = 0;
            // Ab 1023,5 rundet die Ausgabe unten auf 1024: Dann ist es 1 der nächsten Einheit, nicht "1024 KB".
            // Sonst teilen und abschneiden. Gerundet wird nur einmal, bei der Ausgabe: Zweimal gerundet wären 10,46 MB "11 MB".
            while (tenths >= 10235 && unit < units.Length - 1) { tenths = tenths < 10240 ? 10 : tenths / 1024; unit++; }
            // Unter 10 mit einer Nachkommastelle, darüber ganze Zahlen.
            return (tenths < 100 && tenths % 10 != 0 ? tenths / 10 + "," + tenths % 10 : ((tenths + 5) / 10).ToString()) + " " + units[unit];
        }

        public static string Time(DateTime t)
        {
            return Two(t.Hour) + ":" + Two(t.Minute);
        }

        public static string Date(DateTime t)
        {
            string[] days = { "So.", "Mo.", "Di.", "Mi.", "Do.", "Fr.", "Sa." };
            string[] months = { "Jan.", "Feb.", "März", "Apr.", "Mai", "Juni", "Juli", "Aug.", "Sept.", "Okt.", "Nov.", "Dez." };
            return days[(int)t.DayOfWeek] + " " + t.Day + ". " + months[t.Month - 1];
        }
    }
}
