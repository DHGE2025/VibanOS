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
    /// und tippt es zur Sicherheit zweimal ein. Ein Klick auf das Nutzerbild öffnet die Nutzerwahl:
    /// Dort wechselt man den Nutzer oder legt mit dem Plus einen neuen an. Tab wechselt ohne Maus.
    /// </summary>
    public class LoginScreen
    {
        private const int FieldWidth = 240;
        private const int MaxTries = 3;
        private const int LockSeconds = 5;

        private enum Mode { Password, Picker, Create }

        // Schritte beim Anlegen eines neuen Nutzers, in dieser Reihenfolge.
        private enum Step { Name, Password, Repeat, Admin }

        private Mode mode = Mode.Password;
        private int userIndex;
        private string password = "";
        private string error = "";
        private int tries;
        private DateTime lockedUntil;

        // Ersteinrichtung: die erste Eingabe des neuen Passworts, bis es wiederholt wurde.
        private string firstEntry;

        // Nutzerwahl: das markierte Feld. Die Nutzer in der Reihenfolge der Nutzerdatei, danach das Plus.
        private int pickIndex;

        // Neuer Nutzer: Schritt, Name und das festgelegte Passwort, bis ein Administrator bestätigt hat.
        private Step step;
        private string newName = "";
        private string newPassword;

        // Größe der Zeichenfläche beim letzten Zeichnen. Danach richten sich die Klickflächen.
        private int width = T.Size.ScreenWidth, height = T.Size.ScreenHeight;

        /// <summary>true, solange die Nutzerwahl offen ist.</summary>
        public bool Picking { get { return mode == Mode.Picker; } }

        /// <summary>true, solange ein neuer Nutzer angelegt wird.</summary>
        public bool Creating { get { return mode == Mode.Create; } }

        /// <summary>true genau einmal, wenn die Sperre nach zu vielen Versuchen abgelaufen ist. Dann verschwindet die Meldung.</summary>
        public bool LockExpired(DateTime now)
        {
            if (lockedUntil == DateTime.MinValue || now < lockedUntil) return false;
            lockedUntil = DateTime.MinValue;
            error = "";
            return true;
        }

        /// <summary>
        /// Neue Nutzer nur, wenn die Nutzerdatei gelesen wurde und root schon ein Passwort hat. Vorher gäbe es
        /// keinen Administrator, der bestätigen könnte.
        /// </summary>
        private static bool CanCreate { get { return OS.Users.LoadError == null && !OS.Users.NeedsSetup; } }

        // Felder der Nutzerwahl: alle Nutzer und, wenn erlaubt, das Plus.
        private static int PickCount { get { return OS.Users.All.Count + (CanCreate ? 1 : 0); } }

        private User Selected
        {
            get
            {
                if (userIndex >= OS.Users.All.Count) userIndex = 0;
                return OS.Users.All[userIndex];
            }
        }

        public void OnKey(Key key, char c)
        {
            if (DateTime.Now < lockedUntil) return;
            if (mode == Mode.Picker) PickerKey(key);
            else if (mode == Mode.Create) CreateKey(key, c);
            else PasswordKey(key, c);
        }

        /// <summary>Klick mit der Maus. Das Nutzerbild öffnet die Nutzerwahl, dort wählt ein Klick einen Nutzer oder das Plus.</summary>
        public void OnClick(int x, int y)
        {
            if (DateTime.Now < lockedUntil) return;
            if (mode == Mode.Picker)
            {
                for (int i = 0; i < PickCount; i++)
                {
                    int tx, ty;
                    TileAt(i, out tx, out ty);
                    if (x >= tx && x < tx + TileWidth && y >= ty && y < ty + TileHeight) { Choose(i); return; }
                }
                Show(userIndex); // Klick daneben: zurück, ohne etwas zu ändern
                return;
            }
            if (mode == Mode.Create || OS.Users.LoadError != null) return;
            int ax = width / 2 - T.Size.Avatar / 2, ay = AvatarY;
            if (x >= ax && x < ax + T.Size.Avatar && y >= ay && y < ay + T.Size.Avatar) OpenPicker();
        }

        /// <summary>Öffnet die Nutzerwahl. Der gerade gezeigte Nutzer ist markiert.</summary>
        public void OpenPicker()
        {
            mode = Mode.Picker;
            pickIndex = userIndex;
            password = "";
            error = "";
            firstEntry = null;
            OS.Changed = true;
        }

        private void PasswordKey(Key key, char c)
        {
            UserManager users = OS.Users;
            User user = Selected;

            if (key == Key.Tab)
            {
                Show((userIndex + 1) % users.All.Count);
            }
            else if (key == Key.Escape)
            {
                password = "";
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
                if (users.Login(user.Name, password)) Enter(user);
                else Failed("Falsches Passwort");
                password = "";
            }
            else if (key == Key.None && c >= ' ' && c <= 255)
            {
                password += c;
            }
        }

        private void PickerKey(Key key)
        {
            int count = PickCount;
            if (key == Key.Right || key == Key.Tab) pickIndex = (pickIndex + 1) % count;
            else if (key == Key.Left) pickIndex = (pickIndex + count - 1) % count;
            else if (key == Key.Enter) Choose(pickIndex);
            else if (key == Key.Escape) Show(userIndex);
        }

        // Feld i der Nutzerwahl gewählt: ein Nutzer oder das Plus.
        private void Choose(int i)
        {
            if (i < OS.Users.All.Count) { Show(i); return; }
            mode = Mode.Create;
            step = Step.Name;
            newName = "";
            newPassword = null;
            password = "";
            error = "";
        }

        // Zurück zum Passwortfeld, mit Nutzer i.
        private void Show(int i)
        {
            mode = Mode.Password;
            userIndex = i;
            password = "";
            error = "";
            firstEntry = null;
        }

        private void CreateKey(Key key, char c)
        {
            if (key == Key.Escape) { Show(userIndex); return; }
            if (key == Key.Backspace)
            {
                if (step == Step.Name) { if (newName.Length > 0) newName = newName.Substring(0, newName.Length - 1); }
                else if (password.Length > 0) password = password.Substring(0, password.Length - 1);
                return;
            }
            if (key == Key.None && c >= ' ' && c <= 255)
            {
                if (step != Step.Name) { password += c; return; }
                // Namen sind klein, Großbuchstaben werden es beim Tippen. Mehr als erlaubt nimmt das Feld nicht an.
                if (c >= 'A' && c <= 'Z') c = (char)(c - 'A' + 'a');
                if (newName.Length < UserManager.MaxNameLength) newName += c;
                return;
            }
            if (key != Key.Enter) return;

            UserManager users = OS.Users;
            if (step == Step.Name)
            {
                if (newName == "") return;
                if (!UserManager.ValidName(newName)) error = "Erlaubt sind a-z, 0-9, - und _.";
                else if (users.Find(newName) != null) error = "Den Nutzer " + newName + " gibt es schon.";
                else { step = Step.Password; error = ""; }
                return;
            }
            if (password == "") return;
            if (step == Step.Password)
            {
                newPassword = password;
                step = Step.Repeat;
                error = "";
            }
            else if (step == Step.Repeat)
            {
                // Wie bei der Ersteinrichtung: Bei einem Tippfehler käme der neue Nutzer nie hinein.
                if (password == newPassword) { step = Step.Admin; error = ""; }
                else { step = Step.Password; newPassword = null; error = "Die Passwörter stimmen nicht überein. Bitte noch einmal."; }
            }
            else if (users.FindAdmin(password) == null)
            {
                // Zählt wie ein falsches Passwort beim Anmelden, sonst ließe sich das Admin-Passwort hier durchprobieren.
                Failed("Das ist nicht das Passwort eines Administrators.");
            }
            else
            {
                tries = 0;
                User created = users.Add(newName, newPassword, UserManager.UserGroup);
                if (created == null) error = "Der Nutzer konnte nicht angelegt werden.";
                else
                {
                    if (!users.Saved) OS.Shell.Error("Achtung: Die Nutzerdatei ließ sich nicht schreiben. " + newName + " gibt es nur bis zum Neustart.");
                    users.Login(created.Name, newPassword);
                    userIndex = users.All.IndexOf(created);
                    Enter(created);
                }
                newPassword = null;
            }
            password = "";
        }

        // Falsches Passwort: Meldung, nach MaxTries Versuchen eine kurze Sperre.
        private void Failed(string message)
        {
            if (++tries >= MaxTries)
            {
                tries = 0;
                lockedUntil = DateTime.Now.AddSeconds(LockSeconds);
                error = "Zu viele Versuche. Bitte " + LockSeconds + " Sekunden warten.";
            }
            else error = message;
        }

        // Angemeldet: Die Shell beginnt im eigenen Ordner. Der Anmeldebildschirm steht beim nächsten Mal wieder auf dem Passwort.
        private void Enter(User user)
        {
            mode = Mode.Password;
            tries = 0;
            error = "";
            bool home = Vfs.Available && Directory.Exists(user.HomeDirectory);
            OS.Shell.CurrentDirectory = home ? user.HomeDirectory : Vfs.Root;
        }

        /// <summary>Mitte des großen Nutzerbilds. Für den Selbsttest, der dort hinklickt.</summary>
        public int[] AvatarCenter() { return new int[] { width / 2, AvatarY + T.Size.Avatar / 2 }; }

        /// <summary>Mitte von Feld i der Nutzerwahl: erst die Nutzer, dann das Plus. Für den Selbsttest.</summary>
        public int[] TileCenter(int i)
        {
            int x, y;
            TileAt(i, out x, out y);
            return new int[] { x + TileWidth / 2, y + TileHeight / 2 };
        }

        // Oberkante des großen Nutzerbilds.
        private int AvatarY { get { return height / 2 - T.Size.Avatar; } }

        // Felder der Nutzerwahl: Nutzerbild mit Platz für den Ring, darunter der Name.
        private static int TileWidth { get { return T.Size.AvatarSmall + 2 * T.Space.XXL; } }
        private static int TileHeight { get { return T.Size.AvatarSmall + 2 * T.Space.S + T.Space.S + Fonts.Strong.LineHeight; } }

        // Linke obere Ecke von Feld i. Passen nicht alle in eine Zeile, brechen sie um.
        private void TileAt(int i, out int x, out int y)
        {
            int count = PickCount;
            int perRow = Math.Max(1, Math.Min(count, (width - 2 * T.Space.XXL) / TileWidth));
            int rows = (count + perRow - 1) / perRow;
            int row = i / perRow, col = i % perRow;
            int inRow = row == rows - 1 ? count - row * perRow : perRow;
            x = (width - inRow * TileWidth) / 2 + col * TileWidth;
            y = height / 2 - rows * TileHeight / 2 + row * TileHeight;
        }

        public void Draw(Surface s)
        {
            width = s.Width;
            height = s.Height;
            int cx = s.Width / 2;
            DateTime now = DateTime.Now;

            s.FillRect(0, 0, s.Width, s.Height, T.Color.Scrim);

            // Uhr oben, wie auf einem Sperrbildschirm
            int ty = T.Size.MenuBar + T.Space.XXL;
            s.DrawTextCentered(Fonts.Strong, Text.Date(now), cx, ty, T.Color.TextSecondary);
            s.DrawTextCentered(Fonts.Display, Text.Time(now), cx, ty + Fonts.Strong.LineHeight + T.Space.XS, T.Color.TextPrimary);

            if (mode == Mode.Picker) DrawPicker(s);
            else DrawLogin(s);
        }

        private void DrawLogin(Surface s)
        {
            UserManager users = OS.Users;
            User user = Selected;
            bool creating = mode == Mode.Create;
            bool setup = !creating && users.NeedsSetup && user.Name == UserManager.Root;
            int cx = s.Width / 2;

            // Avatar mit Anfangsbuchstabe (beim Anlegen ein Plus, bis der Name dasteht), Name, Eingabefeld
            int ty = AvatarY;
            string name = creating ? (newName != "" ? newName : "Neuer Nutzer") : user.Name;
            string letter = creating && newName == "" ? "+" : name.Substring(0, 1).ToUpper();
            DrawAvatar(s, cx, ty + T.Size.Avatar / 2, T.Size.Avatar, Fonts.Display, letter, false);
            ty += T.Size.Avatar + T.Space.M;
            s.DrawTextCentered(Fonts.Display, name, cx, ty, creating && newName == "" ? T.Color.TextSecondary : T.Color.TextPrimary);
            ty += Fonts.Display.LineHeight + T.Space.M;

            int fx = cx - FieldWidth / 2;
            s.FillRoundRect(fx, ty, FieldWidth, T.Size.Field, T.Size.Field / 2, T.Color.Dock);
            s.StrokeRoundRect(fx, ty, FieldWidth, T.Size.Field, T.Size.Field / 2, T.Color.Border);
            int textY = ty + (T.Size.Field - Fonts.Body.LineHeight) / 2;
            if (creating && step == Step.Name && newName != "")
            {
                s.DrawText(Fonts.Body, newName, fx + T.Space.M, textY, T.Color.TextPrimary);
            }
            else if (password == "" || (creating && step == Step.Name))
            {
                string placeholder;
                if (creating) placeholder = step == Step.Name ? "Name des neuen Nutzers" : step == Step.Password ? "Passwort festlegen"
                    : step == Step.Repeat ? "Passwort wiederholen" : "Passwort eines Administrators";
                else placeholder = !setup ? "Passwort eingeben" : firstEntry == null ? "Neues Passwort festlegen" : "Passwort wiederholen";
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

            string hint;
            if (creating) hint = step == Step.Admin ? "Ein Administrator bestätigt den neuen Nutzer. Esc bricht ab."
                : step == Step.Name ? "Erlaubt sind a-z, 0-9, - und _. Esc bricht ab." : "Esc bricht ab.";
            else if (setup) hint = "Erster Start: Das Passwort gilt für den Administrator root.";
            else hint = "Eingabetaste meldet an. Ein Klick auf das Bild wechselt den Nutzer.";
            if (users.LoadError != null)
            {
                s.DrawTextCentered(Fonts.Body, "Die Nutzerdatei ließ sich nicht lesen, deshalb kann sich niemand anmelden. Bitte neu starten.", cx, ty, T.Color.Danger);
                s.DrawTextCentered(Fonts.Body, users.LoadError, cx, ty + Fonts.Body.LineHeight + T.Space.XS, T.Color.TextSecondary);
            }
            else if (error != "") s.DrawTextCentered(Fonts.Body, error, cx, ty, T.Color.Danger);
            else s.DrawTextCentered(Fonts.Body, hint, cx, ty, T.Color.TextSecondary);
        }

        private void DrawPicker(Surface s)
        {
            UserManager users = OS.Users;
            int cx = s.Width / 2;
            int count = PickCount;
            int x, y;
            TileAt(0, out x, out y);
            s.DrawTextCentered(Fonts.Strong, "Nutzer wählen", cx, y - T.Space.XL - Fonts.Strong.LineHeight, T.Color.TextSecondary);

            int bottom = y;
            for (int i = 0; i < count; i++)
            {
                TileAt(i, out x, out y);
                bool plus = i >= users.All.Count;
                string name = plus ? "Neuer Nutzer" : users.All[i].Name;
                int ax = x + TileWidth / 2, ay = y + T.Space.S + T.Size.AvatarSmall / 2;
                DrawAvatar(s, ax, ay, T.Size.AvatarSmall, Fonts.Display, plus ? "+" : name.Substring(0, 1).ToUpper(), i == pickIndex);
                s.DrawTextCentered(Fonts.Strong, name, ax, y + T.Size.AvatarSmall + 2 * T.Space.S + T.Space.XS,
                    plus ? T.Color.TextSecondary : T.Color.TextPrimary);
                bottom = y + TileHeight;
            }
            s.DrawTextCentered(Fonts.Body, "Nutzer anklicken oder mit den Pfeiltasten wählen. Esc geht zurück.", cx, bottom + T.Space.L, T.Color.TextSecondary);
        }

        // Rundes Nutzerbild mit Buchstabe. Markiert: ein Ring in der Akzentfarbe, mit etwas Luft zum Bild.
        private static void DrawAvatar(Surface s, int cx, int cy, int size, Font font, string letter, bool marked)
        {
            s.FillCircle(cx, cy, size, T.Color.Dock);
            s.StrokeRoundRect(cx - size / 2, cy - size / 2, size, size, size / 2, T.Color.Border);
            if (marked)
            {
                int ring = size + 2 * T.Space.XS;
                s.StrokeRoundRect(cx - ring / 2, cy - ring / 2, ring, ring, ring / 2, T.Color.Accent);
                ring -= 2;
                s.StrokeRoundRect(cx - ring / 2, cy - ring / 2, ring, ring, ring / 2, T.Color.Accent);
            }
            s.DrawTextCentered(font, letter, cx, cy - font.LineHeight / 2, T.Color.TextPrimary);
        }
    }
}
