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
    ///
    /// Bewegung wie bei macOS, kurz und ruhig: Die Nutzerwahl fächert sich aus dem großen Bild auf und fällt
    /// beim Schließen in das gewählte zurück, das Feld schüttelt sich bei einem falschen Passwort, und nach dem
    /// Abmelden blendet die Anmeldung von unten ein. Die Zeiten kommen aus der Uhr Clock, nicht aus der Bildrate.
    /// Im Feld blinkt eine Einfügemarke und zeigt, wo getippt wird. Beim Tippen steht sie still.
    /// </summary>
    public class LoginScreen
    {
        private const int FieldWidth = 240;
        private const int MaxTries = 3;
        private const int LockSeconds = 5;

        private enum Mode { Password, Picker, Create }

        // Schritte beim Anlegen eines neuen Nutzers, in dieser Reihenfolge.
        private enum Step { Name, Password, Repeat }

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

        // Neuer Nutzer: Schritt, Name und das festgelegte Passwort, bis es wiederholt wurde.
        private Step step;
        private string newName = "";
        private string newPassword;

        // Größe der Zeichenfläche beim letzten Zeichnen. Danach richten sich die Klickflächen.
        private int width = T.Size.ScreenWidth, height = T.Size.ScreenHeight;

        /// <summary>Uhr der Animationen in Millisekunden. Der Schreibtisch setzt hier seine eigene ein.</summary>
        public Func<long> Clock = Transition.Milliseconds;

        // Beginn der laufenden Animationen auf der Uhr Clock, -1: keine.
        private long appearAt = -1;  // Anmeldung blendet ein (nach dem Abmelden, nach Tab)
        private long pickerAt = -1;  // Nutzerwahl geht auf oder zu
        private long shakeAt = -1;   // Feld schüttelt sich
        private long fieldAt = -1;   // neuer Platzhalter im Feld blendet ein
        private long hoverAt = -1;   // Maus kam auf ein Nutzerbild
        private bool pickerOpening;
        // Das Feld der Nutzerwahl, das zum großen Bild wird oder aus ihm kommt: der gewählte Nutzer oder das Plus.
        private int anchor;
        // Feld der Nutzerwahl unter der Maus (-1: keines) und ob sie auf dem großen Bild steht.
        private int hover = -1;
        private bool hoverAvatar;

        // Einfügemarke im Feld: blinkt im Takt Motion.Blink, gezählt ab der letzten Taste. Beim Tippen bleibt sie so stehen.
        // caretOn ist ihr Stand im laufenden Bild. NeedsFrame legt ihn einmal je Bild fest, damit das Vermessen und das
        // stückweise Zeichnen desselben Bildes dasselbe sehen, auch wenn die Uhr dazwischen über die Grenze läuft.
        private long typedAt;
        private bool caretOn = true;

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
            Solid(); // Das Feld nimmt wieder an: Die Einfügemarke kommt sofort zurück.
            return true;
        }

        /// <summary>true, solange eine Animation läuft. Dann braucht jedes Frame ein neues Bild.</summary>
        public bool Animating(long now)
        {
            return Running(appearAt, T.Motion.Appear, now) || Running(pickerAt, T.Motion.Picker, now)
                || Running(shakeAt, T.Motion.Shake, now) || Running(fieldAt, T.Motion.Fade, now) || Running(hoverAt, T.Motion.Hover, now);
        }

        /// <summary>
        /// Für den Schreibtisch: true, solange eine Animation läuft, und noch einmal danach. Erst dieses letzte Bild
        /// zeigt den Endstand, sonst bliebe ein Zwischenbild stehen.
        /// </summary>
        public bool NeedsFrame(long now)
        {
            // Springt die Einfügemarke um, braucht es ein Bild. Neu gezeichnet wird dabei nur ihr kleiner Ausschnitt.
            bool caret = CaretPhase(now);
            bool blink = caret != caretOn && FieldActive;
            caretOn = caret;
            if (Animating(now)) { moving = true; return true; }
            bool last = moving;
            moving = false;
            return last || blink;
        }
        private bool moving;

        /// <summary>true, wenn die Einfügemarke im laufenden Bild zu sehen ist. Für den Selbsttest.</summary>
        public bool CaretShown { get { return caretOn && FieldActive && Openness(Clock()) == 0; } }

        // Das Feld nimmt Tasten an: keine Nutzerwahl, keine Sperre nach Fehlversuchen, und die Nutzerdatei ist da.
        // Die Sperre gilt bis LockExpired, nicht bis zur Uhrzeit: So wechselt sie nie mitten in einem Bild.
        private bool FieldActive { get { return mode != Mode.Picker && lockedUntil == DateTime.MinValue && OS.Users.LoadError == null; } }

        // An oder aus zur Zeit now: Die erste Hälfte jedes Takts an, ab der letzten Taste gezählt.
        private bool CaretPhase(long now)
        {
            long t = now - typedAt;
            return t < 0 || t / T.Motion.Blink % 2 == 0;
        }

        // Die Einfügemarke steht sichtbar still und blinkt erst nach Motion.Blink weiter.
        private void Solid()
        {
            typedAt = Clock();
            caretOn = true;
        }

        /// <summary>Der Bildschirm ist gerade gesperrt worden: Die Anmeldung blendet von unten ein.</summary>
        public void Appear()
        {
            appearAt = Clock();
            hover = -1;
            hoverAvatar = false;
            Solid();
        }

        /// <summary>
        /// Neue Nutzer legt jeder am Anmeldebildschirm selbst an, ohne Bestätigung, aber nur in der Gruppe users.
        /// Erst wenn die Nutzerdatei gelesen wurde (sonst würde sie nicht gespeichert) und root ein Passwort hat:
        /// Der erste Start richtet zuerst den Administrator ein.
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
            Solid();
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
                int i = TileUnder(x, y);
                if (i >= 0) Choose(i);
                else Show(userIndex); // Klick daneben: zurück, ohne etwas zu ändern
                return;
            }
            if (OverAvatar(x, y)) OpenPicker();
        }

        /// <summary>Die Maus bewegt sich: Das Bild darunter wächst ein wenig und wird markiert.</summary>
        public void OnMove(int x, int y)
        {
            int tile = mode == Mode.Picker ? TileUnder(x, y) : -1;
            bool avatar = OverAvatar(x, y);
            if (tile == hover && avatar == hoverAvatar) return;
            if (tile >= 0 || avatar) hoverAt = Clock();
            if (tile >= 0) pickIndex = tile;
            hover = tile;
            hoverAvatar = avatar;
            OS.Changed = true;
        }

        // Das große Bild nimmt nur Klicks an, wenn es eine Nutzerwahl gibt (nicht beim Anlegen, nicht ohne Nutzerdatei).
        private bool OverAvatar(int x, int y)
        {
            if (mode != Mode.Password || OS.Users.LoadError != null) return false;
            int ax = width / 2 - T.Size.Avatar / 2, ay = AvatarY;
            return x >= ax && x < ax + T.Size.Avatar && y >= ay && y < ay + T.Size.Avatar;
        }

        private int TileUnder(int x, int y)
        {
            for (int i = 0; i < PickCount; i++)
            {
                int tx, ty;
                TileAt(i, out tx, out ty);
                if (x >= tx && x < tx + TileWidth && y >= ty && y < ty + TileHeight) return i;
            }
            return -1;
        }

        /// <summary>Öffnet die Nutzerwahl. Der gerade gezeigte Nutzer ist markiert, alle Bilder kommen aus seinem.</summary>
        public void OpenPicker()
        {
            mode = Mode.Picker;
            pickIndex = userIndex;
            anchor = userIndex;
            pickerAt = Clock();
            pickerOpening = true;
            hover = -1;
            hoverAvatar = false;
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
                Appear();
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
                        fieldAt = Clock();
                        return;
                    }
                    bool same = firstEntry == password;
                    firstEntry = null;
                    if (!same)
                    {
                        password = "";
                        Wrong("Die Passwörter stimmen nicht überein. Bitte noch einmal.");
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
            else if (key == Key.Enter) { Choose(pickIndex); return; }
            else if (key == Key.Escape) { Show(userIndex); return; }
            hoverAt = Clock(); // das markierte Bild wächst wie unter der Maus
            hover = pickIndex;
        }

        // Feld i der Nutzerwahl gewählt: ein Nutzer oder das Plus.
        private void Choose(int i)
        {
            if (i < OS.Users.All.Count) { Show(i); return; }
            ClosePicker(i);
            mode = Mode.Create;
            step = Step.Name;
            newName = "";
            newPassword = null;
            password = "";
            error = "";
        }

        // Zurück zum Passwortfeld, mit Nutzer i. Aus der Nutzerwahl fällt sein Bild an die große Stelle zurück.
        private void Show(int i)
        {
            if (mode == Mode.Picker) ClosePicker(i);
            else if (mode == Mode.Create) Appear();
            mode = Mode.Password;
            userIndex = i;
            password = "";
            error = "";
            firstEntry = null;
        }

        private void ClosePicker(int chosen)
        {
            anchor = chosen;
            pickerAt = Clock();
            pickerOpening = false;
            hover = -1;
            Solid();
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
                if (!UserManager.ValidName(newName)) Wrong("Erlaubt sind a-z, 0-9, - und _.");
                else if (users.Find(newName) != null) Wrong("Den Nutzer " + newName + " gibt es schon.");
                else Next(Step.Password);
                return;
            }
            if (password == "") return;
            if (step == Step.Password)
            {
                newPassword = password;
                Next(Step.Repeat);
            }
            else if (password != newPassword)
            {
                // Wie bei der Ersteinrichtung: Bei einem Tippfehler käme der neue Nutzer nie hinein.
                newPassword = null;
                step = Step.Password;
                Wrong("Die Passwörter stimmen nicht überein. Bitte noch einmal.");
            }
            else
            {
                // Neue Nutzer kommen immer in die Gruppe users. Administratoren macht nur ein Administrator.
                User created = users.Add(newName, newPassword, UserManager.UserGroup);
                if (created == null) Wrong("Der Nutzer konnte nicht angelegt werden.");
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

        // Nächster Schritt beim Anlegen: Der neue Platzhalter blendet ein.
        private void Next(Step next)
        {
            step = next;
            error = "";
            fieldAt = Clock();
        }

        // Etwas stimmt nicht: Meldung, und das Feld schüttelt sich wie bei macOS.
        private void Wrong(string message)
        {
            error = message;
            shakeAt = Clock();
        }

        // Falsches Passwort: Meldung, nach MaxTries Versuchen eine kurze Sperre.
        private void Failed(string message)
        {
            if (++tries >= MaxTries)
            {
                tries = 0;
                lockedUntil = DateTime.Now.AddSeconds(LockSeconds);
                Wrong("Zu viele Versuche. Bitte " + LockSeconds + " Sekunden warten.");
            }
            else Wrong(message);
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

        // Felder der Nutzerwahl: Nutzerbild mit Platz zum Wachsen und für den Ring, darunter der Name.
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
            long ms = Clock();

            s.FillRect(0, 0, s.Width, s.Height, T.Color.Scrim);

            // Uhr oben, wie auf einem Sperrbildschirm
            int ty = T.Size.MenuBar + T.Space.XXL;
            s.DrawTextCentered(Fonts.Strong, Text.Date(now), cx, ty, T.Color.TextSecondary);
            s.DrawTextCentered(Fonts.Display, Text.Time(now), cx, ty + Fonts.Strong.LineHeight + T.Space.XS, T.Color.TextPrimary);

            // open: 0 = nur die Anmeldung, 1 = nur die Nutzerwahl. Dazwischen wandern die Bilder zwischen großer Stelle
            // und ihrem Feld, und die Anmeldung blendet in der ersten Hälfte aus (beim Schließen in der zweiten ein).
            float open = Openness(ms);
            float appear = EaseOut(Progress(appearAt, T.Motion.Appear, ms));
            float main = Clamp(1 - 2 * open) * appear;
            if (open > 0) DrawPicker(s, open, ms);
            if (main > 0) DrawLogin(s, main, (int)(T.Space.L * (1 - appear)), open == 0, ms);
        }

        private float Openness(long ms)
        {
            if (pickerAt < 0) return mode == Mode.Picker ? 1 : 0;
            float p = EaseOut(Progress(pickerAt, T.Motion.Picker, ms));
            return pickerOpening ? p : 1 - p;
        }

        private void DrawLogin(Surface s, float alpha, int rise, bool avatar, long ms)
        {
            UserManager users = OS.Users;
            User user = Selected;
            bool creating = mode == Mode.Create;
            bool setup = !creating && users.NeedsSetup && user.Name == UserManager.Root;
            int cx = s.Width / 2;

            // Avatar mit Anfangsbuchstabe (beim Anlegen ein Plus, bis der Name dasteht), Name, Eingabefeld
            int ty = AvatarY + rise;
            string name = creating ? (newName != "" ? newName : "Neuer Nutzer") : user.Name;
            string letter = creating && newName == "" ? "+" : name.Substring(0, 1).ToUpper();
            if (avatar)
            {
                // Unter der Maus zeigt ein heller Ring, dass ein Klick die Nutzerwahl öffnet.
                float ring = hoverAvatar ? EaseOut(Progress(hoverAt, T.Motion.Hover, ms)) : 0;
                DrawAvatar(s, cx, ty + T.Size.Avatar / 2, T.Size.Avatar, letter, 0, ring, alpha);
            }
            ty += T.Size.Avatar + T.Space.M;
            s.DrawTextCentered(Fonts.Display, name, cx, ty, Fade(creating && newName == "" ? T.Color.TextSecondary : T.Color.TextPrimary, alpha));
            ty += Fonts.Display.LineHeight + T.Space.M;

            // Bei einem Fehler schüttelt sich das Feld: dreimal hin und her, immer schwächer.
            int fx = cx - FieldWidth / 2;
            if (Running(shakeAt, T.Motion.Shake, ms))
            {
                float p = Progress(shakeAt, T.Motion.Shake, ms);
                fx += (int)(T.Space.S * Math.Sin(p * 6 * Math.PI) * (1 - p));
            }
            s.FillRoundRect(fx, ty, FieldWidth, T.Size.Field, T.Size.Field / 2, Fade(T.Color.Dock, alpha));
            s.StrokeRoundRect(fx, ty, FieldWidth, T.Size.Field, T.Size.Field / 2, Fade(T.Color.Border, alpha));
            int textY = ty + (T.Size.Field - Fonts.Body.LineHeight) / 2;
            // Die Einfügemarke steht im leeren Feld vor dem Platzhalter, sonst hinter dem letzten Zeichen oder Punkt.
            int caretX = fx + T.Space.M - T.Size.Caret;
            if (creating && step == Step.Name && newName != "")
            {
                caretX = s.DrawText(Fonts.Body, newName, fx + T.Space.M, textY, Fade(T.Color.TextPrimary, alpha));
            }
            else if (password == "" || (creating && step == Step.Name))
            {
                string placeholder;
                if (creating) placeholder = step == Step.Name ? "Name des neuen Nutzers" : step == Step.Password ? "Passwort festlegen" : "Passwort wiederholen";
                else placeholder = !setup ? "Passwort eingeben" : firstEntry == null ? "Neues Passwort festlegen" : "Passwort wiederholen";
                float shown = EaseOut(Progress(fieldAt, T.Motion.Fade, ms));
                s.DrawText(Fonts.Body, placeholder, fx + T.Space.M, textY, Fade(T.Color.TextSecondary, alpha * shown));
            }
            else
            {
                // Punkte statt Zeichen. Zu lange Eingaben zeigen nur das Ende. Hinter dem letzten bleibt Platz für die Marke.
                int dots = password.Length, max = (FieldWidth - 2 * T.Space.M - T.Size.Caret) / T.Space.M;
                if (dots > max) dots = max;
                for (int i = 0; i < dots; i++)
                    s.FillCircle(fx + T.Space.M + 4 + i * T.Space.M, ty + T.Size.Field / 2, 8, Fade(T.Color.TextPrimary, alpha));
                caretX = fx + T.Space.M + dots * T.Space.M; // wo der nächste Punkt hinkäme
            }
            // avatar heißt auch: Die Nutzerwahl ist ganz zu. Solange sie auf- oder zugeht, hat das Feld keine Marke.
            if (avatar && caretOn && FieldActive)
                s.FillRect(caretX, textY, T.Size.Caret, Fonts.Body.LineHeight, Fade(T.Color.Accent, alpha));
            ty += T.Size.Field + T.Space.M;

            string hint;
            if (creating) hint = step == Step.Name ? "Kleinbuchstaben, Ziffern, - und _ · Esc bricht ab" : "Esc bricht ab";
            else if (setup) hint = "Erster Start: Das Passwort gilt für den Administrator root.";
            else hint = "Klick auf das Bild wechselt den Nutzer";
            if (users.LoadError != null)
            {
                s.DrawTextCentered(Fonts.Body, "Die Nutzerdatei ließ sich nicht lesen, deshalb kann sich niemand anmelden. Bitte neu starten.", cx, ty, Fade(T.Color.Danger, alpha));
                s.DrawTextCentered(Fonts.Body, users.LoadError, cx, ty + Fonts.Body.LineHeight + T.Space.XS, Fade(T.Color.TextSecondary, alpha));
            }
            else if (error != "") s.DrawTextCentered(Fonts.Body, error, cx, ty, Fade(T.Color.Danger, alpha));
            else s.DrawTextCentered(Fonts.Body, hint, cx, ty, Fade(T.Color.TextTertiary, alpha));
        }

        private void DrawPicker(Surface s, float open, long ms)
        {
            UserManager users = OS.Users;
            int count = PickCount;
            int cx = s.Width / 2, bigY = AvatarY + T.Size.Avatar / 2;
            float labels = Clamp(2 * open - 1); // Namen und Hinweis erst, wenn die Bilder fast angekommen sind
            float grow = EaseOut(Progress(hoverAt, T.Motion.Hover, ms));
            int x, y, bottom = 0;
            for (int i = 0; i < count; i++)
            {
                TileAt(i, out x, out y);
                bool plus = i >= users.All.Count;
                string name = plus ? "Neuer Nutzer" : users.All[i].Name;
                // Jedes Bild wandert zwischen der großen Stelle und seinem Feld und wechselt dabei die Größe. Das gewählte
                // bleibt dabei voll sichtbar, es wird ja zum großen Bild, die anderen tauchen aus ihm auf.
                int ax = Lerp(cx, x + TileWidth / 2, open), ay = Lerp(bigY, y + T.Space.S + T.Size.AvatarSmall / 2, open);
                int size = Lerp(T.Size.Avatar, T.Size.AvatarSmall, open);
                if (i == hover && mode == Mode.Picker) size += (int)(2 * T.Space.XS * grow);
                float alpha = i == anchor ? 1 : open;
                float ring = i == pickIndex && mode == Mode.Picker ? open : 0;
                DrawAvatar(s, ax, ay, size, plus ? "+" : name.Substring(0, 1).ToUpper(), ring, 0, alpha);
                if (labels > 0)
                    s.DrawTextCentered(Fonts.Strong, name, ax, y + T.Size.AvatarSmall + 2 * T.Space.S + T.Space.XS,
                        Fade(plus ? T.Color.TextSecondary : T.Color.TextPrimary, labels));
                bottom = y + TileHeight;
            }
            if (labels > 0)
                s.DrawTextCentered(Fonts.Body, "Nutzer wählen · Esc geht zurück", cx, bottom + T.Space.L, Fade(T.Color.TextTertiary, labels));
        }

        // Rundes Nutzerbild mit Buchstabe. ring: Ring in der Akzentfarbe (markiert), soft: heller Ring (Maus darüber),
        // beide 0–1 als Deckkraft, alpha für das ganze Bild.
        private static void DrawAvatar(Surface s, int cx, int cy, int size, string letter, float ring, float soft, float alpha)
        {
            s.FillCircle(cx, cy, size, Fade(T.Color.Dock, alpha));
            s.StrokeRoundRect(cx - size / 2, cy - size / 2, size, size, size / 2, Fade(T.Color.Border, alpha));
            if (ring > 0 || soft > 0)
            {
                uint color = ring > 0 ? Fade(T.Color.Accent, ring * alpha) : Fade(T.Color.TextTertiary, soft * alpha);
                int d = size + 2 * T.Space.XS;
                s.StrokeRoundRect(cx - d / 2, cy - d / 2, d, d, d / 2, color);
                d -= 2;
                s.StrokeRoundRect(cx - d / 2, cy - d / 2, d, d, d / 2, color);
            }
            s.DrawTextCentered(Fonts.Display, letter, cx, cy - Fonts.Display.LineHeight / 2, Fade(T.Color.TextPrimary, alpha));
        }

        // Fortschritt einer Animation, die bei at begann: 0..1, ohne Animation 1.
        private static float Progress(long at, int duration, long now)
        {
            if (at < 0) return 1;
            return Clamp((float)(now - at) / duration);
        }

        private static bool Running(long at, int duration, long now) { return at >= 0 && now - at < duration; }

        // Bremst sanft ab, wie die Fenster beim Öffnen.
        private static float EaseOut(float t) { return 1 - (1 - t) * (1 - t) * (1 - t); }

        private static float Clamp(float t) { return t < 0 ? 0 : t > 1 ? 1 : t; }

        private static int Lerp(int a, int b, float t) { return a + (int)((b - a) * t + (b > a ? 0.5f : -0.5f)); }

        // Dieselbe Farbe, um alpha (0–1) durchscheinender.
        private static uint Fade(uint color, float alpha)
        {
            if (alpha >= 1) return color;
            uint a = (uint)((color >> 24) * Clamp(alpha) + 0.5f);
            return (a << 24) | (color & 0xFFFFFF);
        }
    }
}
