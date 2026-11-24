using System;
using System.IO;
using VibanOS.Core;
using VibanOS.FileSystem;
using VibanOS.Graphics;
using VibanOS.Users;
using T = VibanOS.Design.Tokens;

namespace VibanOS.Gui
{
    /// <summary>
    /// Anmeldebildschirm (VIB-16). Beim allerersten Start vergibt man hier das Passwort für root
    /// und tippt es zur Sicherheit zweimal ein. Tab wechselt zwischen den Nutzern.
    /// </summary>
    public class LoginScreen
    {
        private const int FieldWidth = 240;
        private const int MaxTries = 3;
        private const int LockSeconds = 5;

        private int userIndex;
        private string password = "";
        private string error = "";
        private int tries;
        private DateTime lockedUntil;

        // Ersteinrichtung: die erste Eingabe des neuen Passworts, bis es wiederholt wurde.
        private string firstEntry;

        /// <summary>true genau einmal, wenn die Sperre nach zu vielen Versuchen abgelaufen ist. Dann verschwindet die Meldung.</summary>
        public bool LockExpired(DateTime now)
        {
            if (lockedUntil == DateTime.MinValue || now < lockedUntil) return false;
            lockedUntil = DateTime.MinValue;
            error = "";
            return true;
        }

        public void OnKey(Key key, char c)
        {
            if (DateTime.Now < lockedUntil) return;
            UserManager users = OS.Users;
            if (userIndex >= users.All.Count) userIndex = 0;
            User user = users.All[userIndex];

            if (key == Key.Tab)
            {
                userIndex = (userIndex + 1) % users.All.Count;
                password = "";
                error = "";
                firstEntry = null;
            }
            else if (key == Key.Backspace)
            {
                if (password.Length > 0) password = password.Substring(0, password.Length - 1);
            }
            else if (key == Key.Enter)
            {
                if (password == "") return;
                if (users.NeedsSetup && user.Name == UserManager.Root)
                {
                    // Das neue Passwort zweimal eintippen: Ein Tippfehler würde root sonst für immer aussperren.
                    if (firstEntry == null)
                    {
                        firstEntry = password;
                        password = "";
                        error = "";
                        return;
                    }
                    bool same = firstEntry == password;
                    firstEntry = null;
                    if (!same)
                    {
                        password = "";
                        error = "Die Passwörter stimmen nicht überein. Bitte noch einmal.";
                        return;
                    }
                    users.SetPassword(user, password);
                }
                if (users.Login(user.Name, password))
                {
                    tries = 0;
                    error = "";
                    bool home = Vfs.Available && Directory.Exists(user.HomeDirectory);
                    OS.Shell.CurrentDirectory = home ? user.HomeDirectory : Vfs.Root;
                }
                else if (++tries >= MaxTries)
                {
                    tries = 0;
                    lockedUntil = DateTime.Now.AddSeconds(LockSeconds);
                    error = "Zu viele Versuche. Bitte " + LockSeconds + " Sekunden warten.";
                }
                else
                {
                    error = "Falsches Passwort";
                }
                password = "";
            }
            else if (key == Key.None && c >= ' ' && c <= 255)
            {
                password += c;
            }
        }

        public void Draw(Surface s)
        {
            UserManager users = OS.Users;
            if (userIndex >= users.All.Count) userIndex = 0;
            User user = users.All[userIndex];
            bool setup = users.NeedsSetup && user.Name == UserManager.Root;
            int cx = s.Width / 2;
            DateTime now = DateTime.Now;

            s.FillRect(0, 0, s.Width, s.Height, T.Color.Scrim);

            // Uhr oben, wie auf einem Sperrbildschirm
            int ty = T.Size.MenuBar + T.Space.XXL;
            s.DrawTextCentered(Fonts.Strong, Text.Date(now), cx, ty, T.Color.TextSecondary);
            s.DrawTextCentered(Fonts.Display, Text.Time(now), cx, ty + Fonts.Strong.LineHeight + T.Space.XS, T.Color.TextPrimary);

            // Avatar mit Anfangsbuchstabe, Name, Passwortfeld
            ty = s.Height / 2 - T.Size.Avatar;
            s.FillCircle(cx, ty + T.Size.Avatar / 2, T.Size.Avatar, T.Color.Dock);
            s.StrokeRoundRect(cx - T.Size.Avatar / 2, ty, T.Size.Avatar, T.Size.Avatar, T.Size.Avatar / 2, T.Color.Border);
            s.DrawTextCentered(Fonts.Display, user.Name.Substring(0, 1).ToUpper(), cx,
                ty + (T.Size.Avatar - Fonts.Display.LineHeight) / 2, T.Color.TextPrimary);
            ty += T.Size.Avatar + T.Space.M;
            s.DrawTextCentered(Fonts.Display, user.Name, cx, ty, T.Color.TextPrimary);
            ty += Fonts.Display.LineHeight + T.Space.M;

            int fx = cx - FieldWidth / 2;
            s.FillRoundRect(fx, ty, FieldWidth, T.Size.Field, T.Size.Field / 2, T.Color.Dock);
            s.StrokeRoundRect(fx, ty, FieldWidth, T.Size.Field, T.Size.Field / 2, T.Color.Border);
            int textY = ty + (T.Size.Field - Fonts.Body.LineHeight) / 2;
            if (password == "")
            {
                string placeholder = !setup ? "Passwort eingeben" : firstEntry == null ? "Neues Passwort festlegen" : "Passwort wiederholen";
                s.DrawText(Fonts.Body, placeholder, fx + T.Space.M, textY, T.Color.TextSecondary);
            }
            else
            {
                // Punkte statt Zeichen. Zu lange Eingaben zeigen nur das Ende.
                int dots = password.Length, max = (FieldWidth - 2 * T.Space.M) / T.Space.M;
                if (dots > max) dots = max;
                for (int i = 0; i < dots; i++)
                    s.FillCircle(fx + T.Space.M + 4 + i * T.Space.M, ty + T.Size.Field / 2, 8, T.Color.TextPrimary);
            }
            ty += T.Size.Field + T.Space.M;

            string hint = setup ? "Erster Start: Das Passwort gilt für den Administrator root."
                : users.All.Count > 1 ? "Eingabetaste meldet an, Tab wechselt den Nutzer." : "Eingabetaste meldet an.";
            if (error != "") s.DrawTextCentered(Fonts.Body, error, cx, ty, T.Color.Danger);
            else s.DrawTextCentered(Fonts.Body, hint, cx, ty, T.Color.TextSecondary);
        }
    }
}
