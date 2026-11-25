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
