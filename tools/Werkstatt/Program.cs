// VibanOS-Werkstatt: holt Branches, baut sie mit 'cosmos build' und startet sie in QEMU.
// Ein Server für diesen Rechner (nur 127.0.0.1), die Oberfläche ist index.html. Start: README, Abschnitt "Werkstatt".
//
// Jeder Branch bekommt einen eigenen Ordner (git worktree) neben dem Klon: <Klon>-werkstatt/<branch>.
// Diese Ordner gehören der Werkstatt, ein Start setzt sie hart auf den Stand von GitHub zurück.
// Der eigene Klon (Ziel ".") wird nur gebaut und gestartet, git fasst ihn dabei nicht an.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

string repo = FindRepo(AppContext.BaseDirectory)
    ?? throw new InvalidOperationException("Die Werkstatt muss in einem git-Klon von VibanOS liegen (Ordner mit VibanOS.sln).");
string branchesDir = repo + "-werkstatt";
string settingsFile = Path.Combine(repo, "tools", "Werkstatt", "einstellungen.json");
int port = args.Length > 0 && int.TryParse(args[0], out int p) ? p : 5099;
string url = $"http://127.0.0.1:{port}";

// Wie im Startskript für den Mac: Homebrew und die globalen dotnet-Werkzeuge (cosmos) sollen auch gefunden werden,
// wenn die Werkstatt per Doppelklick startet. DOTNET_ROOT zeigt auf die Laufzeit, mit der die Werkstatt selbst läuft.
string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
List<string> pathDirs = [.. (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)];
string[] wanted = OperatingSystem.IsMacOS()
    ? [Path.Combine(home, ".dotnet", "tools"), "/opt/homebrew/bin", "/usr/local/bin"]
    : [Path.Combine(home, ".dotnet", "tools")];
foreach (string d in wanted)
    if (!pathDirs.Contains(d)) pathDirs.Add(d);
Environment.SetEnvironmentVariable("PATH", string.Join(Path.PathSeparator, pathDirs));
if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ROOT")))
    Environment.SetEnvironmentVariable("DOTNET_ROOT", Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..")));
Environment.SetEnvironmentVariable("GIT_TERMINAL_PROMPT", "0"); // git soll nie im Terminal auf eine Eingabe warten

// Wie 'cosmos run --disk disk.img -- -vga vmware', aber mit dem QEMU aus Homebrew:
// Das von COSMOS 3.0.99 mitgelieferte QEMU startet auf dem Mac nicht (libSDL3 fehlt im Paket).
string[] macQemu =
[
    "-M", "q35", "-cpu", "max", "-m", "512M",
    "-drive", "file=output-x64/VibanOS.iso,if=none,id=cosmoscd,format=raw,readonly=on",
    "-device", "ide-cd,drive=cosmoscd,bootindex=0", "-boot", "d",
    "-no-reboot", "-no-shutdown", "-vga", "vmware",
    "-device", "ich9-ahci,id=ahci0",
    "-drive", "file=disk.img,if=none,id=ahcidisk0,format=raw",
    "-device", "ide-hd,drive=ahcidisk0,bus=ahci0.0",
    "-display", "cocoa,zoom-to-fit=on,zoom-interpolation=on",
    "-serial", "stdio",
];

var log = new LogBuffer();
object gate = new();
Process? current = null;      // laufender Schritt, den "Stoppen" beendet
string? busy = null;          // was gerade läuft, null = frei
string? busyTarget = null;
bool cancelled = false;
int stamp = 0;                // steigt, wenn sich Branches, PRs oder Ordner ändern: Die Seite lädt dann neu
Settings settings = LoadSettings();
Dictionary<string, object> prs = [];
string? prError = null;
var origin = Regex.Match(Capture(repo, "git", "remote", "get-url", "origin").Output, @"github\.com[:/]([^/\s]+)/([^/\s]+?)(\.git)?\s*$");
string? web = origin.Success ? $"https://github.com/{origin.Groups[1]}/{origin.Groups[2]}" : null;

var builder = WebApplication.CreateBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.Hosting", LogLevel.None); // "Adresse belegt" meldet die Werkstatt selbst
builder.WebHost.UseUrls(url);
var app = builder.Build();

// Nur Aufrufe von dieser Seite: Host-Prüfung gegen DNS-Rebinding, Origin-Prüfung gegen fremde Seiten im Browser.
app.Use(async (ctx, next) =>
{
    string host = ctx.Request.Host.Host;
    string from = ctx.Request.Headers.Origin.ToString();
    if ((host != "127.0.0.1" && host != "localhost") ||
        (!HttpMethods.IsGet(ctx.Request.Method) && from != "" && from != $"http://{ctx.Request.Host}"))
    {
        ctx.Response.StatusCode = 403;
        return;
    }
    await next();
});

app.MapGet("/", () => Results.File(Path.Combine(AppContext.BaseDirectory, "index.html"), "text/html; charset=utf-8"));

app.MapGet("/api/state", (int from) =>
{
    var (next, lines, reset) = log.Since(from);
    lock (gate) return Results.Json(new { busy, target = busyTarget, stamp, next, lines, reset });
});

app.MapGet("/api/info", Info);

app.MapPost("/api/refresh", async () =>
{
    await Task.WhenAll(Task.Run(Fetch), LoadPrs());
    Interlocked.Increment(ref stamp);
    return Info();
});

app.MapPost("/api/settings", (Settings s) =>
{
    settings = s with { QemuArgs = s.QemuArgs ?? "" };
    File.WriteAllText(settingsFile, JsonSerializer.Serialize(settings, JsonSerializerOptions.Web));
    return Results.Ok();
});

app.MapPost("/api/stop", () =>
{
    lock (gate)
    {
        if (busy == null) return Results.Ok();
        cancelled = true;
        try { current?.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
    }
    log.Add("==> Gestoppt");
    return Results.Ok();
});

app.MapPost("/api/action", (ActionRequest r) =>
{
    var t = Resolve(r.Target);
    if (t == null) return Results.BadRequest(new { error = "Diesen Branch gibt es auf GitHub nicht (mehr). Aktualisieren?" });
    var (dir, branch) = t.Value;
    switch (r.Action)
    {
        case "open":
            if (!Directory.Exists(dir)) return Results.BadRequest(new { error = "Der Ordner existiert noch nicht." });
            try { Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true }); }
            catch (Exception e) { return Results.BadRequest(new { error = "Der Ordner ließ sich nicht öffnen: " + e.Message }); }
            return Results.Ok();
        case "disk":
        case "remove":
            lock (gate)
                if (busyTarget == r.Target) return Results.Conflict(new { error = "Erst stoppen: Für diesen Ordner läuft gerade etwas." });
            if (r.Action == "disk")
            {
                File.Delete(Path.Combine(dir, "VibanOS", "disk.img"));
                log.Add("==> disk.img gelöscht, der nächste Start legt eine leere an");
            }
            else if (branch != null)
            {
                var (code, output) = Capture(repo, "git", "worktree", "remove", "--force", dir);
                log.Add(code == 0 ? "==> Ordner entfernt: " + dir : "!! Entfernen fehlgeschlagen: " + output.Trim());
            }
            Interlocked.Increment(ref stamp);
            return Results.Ok();
        case "start" or "build" or "run" or "test" or "check":
            lock (gate)
            {
                if (busy != null) return Results.Conflict(new { error = $"Es läuft schon etwas ({busy}). Erst stoppen." });
                busy = "Startet";
                busyTarget = r.Target;
                cancelled = false;
                stamp++;
            }
            _ = Task.Run(() => Pipeline(dir, branch, r.Action));
            return Results.Ok();
        default:
            return Results.BadRequest(new { error = "Unbekannte Aktion" });
    }
});

app.MapGet("/api/shot", (string target, string file) =>
{
    var t = Resolve(target);
    string path = t == null ? "" : Path.Combine(t.Value.Dir, "preview", Path.GetFileName(file));
    return Path.GetFileName(file) == file && file.EndsWith(".png") && File.Exists(path)
        ? Results.File(path, "image/png")
        : Results.NotFound();
});

try
{
    await app.StartAsync();
}
catch (IOException)
{
    Console.WriteLine($"Die Werkstatt läuft schon: {url}");
    OpenBrowser(url);
    return;
}
Console.WriteLine($"VibanOS-Werkstatt läuft: {url}   (Beenden: Ctrl+C)");
_ = Task.Run(Fetch).ContinueWith(_ => Interlocked.Increment(ref stamp));
_ = LoadPrs().ContinueWith(_ => Interlocked.Increment(ref stamp));
OpenBrowser(url);
await app.WaitForShutdownAsync();
return;

// ---------------------------------------------------------------------------------------------------------------

async Task Pipeline(string dir, string? branch, string action)
{
    string kernel = Path.Combine(dir, "VibanOS");
    try
    {
        if (action == "check")
        {
            Status("Prüft die Werkzeuge");
            await Step(repo, "cosmos", "check");
            return;
        }
        if (action != "run" && branch != null)
        {
            Status("Holt den Stand");
            if (!await Update(dir, branch) || cancelled) return;
        }
        if (action == "test")
        {
            Status("Selbsttest");
            log.Add("==> Selbsttest ohne VM (tools/Preview)");
            int code = await Step(dir, "dotnet", "run", "--project", Path.Combine("tools", "Preview"));
            if (!cancelled) log.Add(code == 0 ? "==> Selbsttest fertig, die Bildschirmfotos stehen unter dem Branch" : "!! Selbsttest fehlgeschlagen");
            return;
        }
        if (action != "run")
        {
            Status("Baut");
            if (!await Build(kernel)) return;
            if (action == "build")
            {
                log.Add("==> Bau fertig");
                return;
            }
        }
        Status("VM läuft");
        await RunVm(kernel);
    }
    catch (Exception e)
    {
        log.Add("!! " + e.Message);
    }
    finally
    {
        lock (gate)
        {
            busy = null;
            busyTarget = null;
            current = null;
            stamp++;
        }
    }
}

async Task<bool> Update(string dir, string branch)
{
    log.Add($"==> Hole {branch} von GitHub");
    if (await Step(repo, "git", "fetch", "--prune", "origin") != 0 && !cancelled)
        log.Add("!! Abrufen fehlgeschlagen (offline?), nehme den zuletzt geholten Stand.");
    string commit = "refs/remotes/origin/" + branch;
    int result;
    if (Path.Exists(Path.Combine(dir, ".git")))
        result = await Step(dir, "git", "reset", "--hard", commit);
    else
    {
        Directory.CreateDirectory(branchesDir);
        await Step(repo, "git", "worktree", "prune");
        result = await Step(repo, "git", "worktree", "add", "--detach", dir, commit);
    }
    if (result != 0 && !cancelled) log.Add("!! Konnte den Branch nicht in " + dir + " auschecken.");
    return result == 0;
}

async Task<bool> Build(string kernel)
{
    if (!Directory.Exists(kernel))
    {
        log.Add("!! Kein Ordner VibanOS in diesem Branch.");
        return false;
    }
    if (kernel.Contains(' '))
    {
        log.Add("!! Der Pfad enthält ein Leerzeichen, daran scheitert cosmos build 3.0.99: " + kernel);
        log.Add("   Den Klon in einen Ordner ohne Leerzeichen legen und die Werkstatt dort starten.");
        return false;
    }
    log.Add("==> Baue den Kernel (cosmos build)");
    string[] buildArgs = settings.Verbose ? ["build", "-v"] : ["build"];
    int code = await Step(kernel, "cosmos", buildArgs);
    if (cancelled) return false;
    if (code != 0) log.Add("!! Bau fehlgeschlagen." + (settings.Verbose ? "" : " Die volle Ausgabe zeigt die Einstellung „Ausführlich bauen“."));
    return code == 0;
}

async Task RunVm(string kernel)
{
    if (!File.Exists(Path.Combine(kernel, "output-x64", "VibanOS.iso")))
    {
        log.Add("!! Noch nicht gebaut: Keine ISO in output-x64.");
        return;
    }
    string disk = Path.Combine(kernel, "disk.img");
    if (!File.Exists(disk))
    {
        using (var f = File.Create(disk)) f.SetLength(64L << 20);
        log.Add("==> disk.img angelegt (64 MB, leer). Als root anmelden und 'format' tippen, dann bleiben Dateien und Nutzer erhalten.");
    }
    string[] extra = settings.QemuArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    log.Add("==> Starte VibanOS in QEMU. Beenden: 'shutdown' tippen und das Fenster schließen, oder hier „VM stoppen“.");
    log.Add("    Ab hier steht die serielle Ausgabe des Kernels im Log.");
    int code;
    if (OperatingSystem.IsMacOS())
    {
        log.Add("    Fenster frei vergrößerbar, Vollbild: Cmd+F. Maus aus dem Fenster holen: Ctrl+Alt+G.");
        code = await Step(kernel, "qemu-system-x86_64", [.. macQemu, .. extra]);
    }
    else
        code = await Step(kernel, "cosmos", ["run", "--disk", "disk.img", "--", "-vga", "vmware", .. extra]);
    if (!cancelled) log.Add(code == 0 ? "==> VM beendet" : $"!! QEMU endete mit Code {code}");
}

void Status(string what)
{
    lock (gate)
    {
        busy = what;
        stamp++;
    }
}

// Führt ein Programm aus und schreibt seine Ausgabe Zeile für Zeile ins Log. "Stoppen" beendet es samt Kindern.
async Task<int> Step(string cwd, string file, params string[] arguments)
{
    if (cancelled) return -1;
    log.Add("$ " + file + " " + string.Join(" ", arguments));
    var psi = StartInfo(cwd, file, arguments);
    psi.RedirectStandardError = true;
    psi.StandardErrorEncoding = Encoding.UTF8;
    var proc = new Process { StartInfo = psi };
    proc.OutputDataReceived += (_, e) => { if (e.Data != null) log.Add(e.Data); };
    proc.ErrorDataReceived += (_, e) => { if (e.Data != null) log.Add(e.Data); };
    try
    {
        proc.Start();
    }
    catch (Exception e)
    {
        log.Add($"!! {file} ließ sich nicht starten ({e.Message}). Ist es installiert und im PATH?");
        return -1;
    }
    lock (gate)
    {
        current = proc;
        if (cancelled) proc.Kill(entireProcessTree: true);
    }
    proc.BeginOutputReadLine();
    proc.BeginErrorReadLine();
    // Nicht auf das Ende der Ausgabe warten: Build-Server von dotnet erben die Leitung und laufen weiter.
    while (!proc.HasExited) await Task.Delay(200);
    await Task.WhenAny(proc.WaitForExitAsync(), Task.Delay(2000)); // Rest der Ausgabe abholen
    lock (gate) current = null;
    return proc.ExitCode;
}

// Kurzer Aufruf ohne Log, für Abfragen. Bricht nach einer Minute ab.
(int Code, string Output) Capture(string cwd, string file, params string[] arguments)
{
    try
    {
        using var proc = Process.Start(StartInfo(cwd, file, arguments))!;
        var output = proc.StandardOutput.ReadToEndAsync();
        if (!proc.WaitForExit(60_000)) proc.Kill(entireProcessTree: true);
        proc.WaitForExit(5_000);
        return (proc.ExitCode, output.Wait(5_000) ? output.Result : "");
    }
    catch (Exception e)
    {
        return (-1, e.Message);
    }
}

ProcessStartInfo StartInfo(string cwd, string file, string[] arguments)
{
    var psi = new ProcessStartInfo(Which(file) ?? file)
    {
        WorkingDirectory = cwd,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardInput = true, // QEMU liest COM1 von stdin, sonst nähme es dem Terminal der Werkstatt die Tasten weg
        StandardOutputEncoding = Encoding.UTF8,
    };
    foreach (string a in arguments) psi.ArgumentList.Add(a);
    return psi;
}

void Fetch()
{
    log.Add("==> Hole die Branches von GitHub");
    var (code, _) = Capture(repo, "git", "fetch", "--prune", "origin");
    log.Add(code == 0 ? "==> Branches aktuell" : "!! Abrufen fehlgeschlagen (offline?), zeige den zuletzt geholten Stand.");
}

async Task LoadPrs()
{
    if (!origin.Success) return;
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("VibanOS-Werkstatt");
        string? token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token)) http.DefaultRequestHeaders.Authorization = new("Bearer", token);
        string json = await http.GetStringAsync($"https://api.github.com/repos/{origin.Groups[1]}/{origin.Groups[2]}/pulls?state=open&per_page=100");
        Dictionary<string, object> found = [];
        foreach (var pr in JsonDocument.Parse(json).RootElement.EnumerateArray())
            found[pr.GetProperty("head").GetProperty("ref").GetString()!] = new
            {
                number = pr.GetProperty("number").GetInt32(),
                title = pr.GetProperty("title").GetString(),
                url = pr.GetProperty("html_url").GetString(),
                draft = pr.GetProperty("draft").GetBoolean(),
                author = pr.GetProperty("user").GetProperty("login").GetString(),
                baseRef = pr.GetProperty("base").GetProperty("ref").GetString(),
            };
        prs = found;
        prError = null;
    }
    catch (Exception e)
    {
        prError = e.Message;
    }
}

IResult Info()
{
    List<object> branches = [];
    string refs = Capture(repo, "git", "for-each-ref", "--sort=-committerdate",
        "--format=%(refname:lstrip=3)%00%(objectname)%00%(committerdate:iso-strict)%00%(authorname)%00%(subject)",
        "refs/remotes/origin").Output;
    foreach (string line in refs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] f = line.Split('\0');
        if (f.Length < 5 || f[0] == "HEAD") continue;
        var issue = Regex.Match(f[0], @"VIB-\d+", RegexOptions.IgnoreCase);
        branches.Add(new
        {
            id = f[0], sha = f[1], date = f[2], author = f[3], subject = f[4],
            pr = prs.GetValueOrDefault(f[0]),
            linear = issue.Success ? "https://linear.app/cxdex/issue/" + issue.Value.ToUpperInvariant() : null,
            local = Local(Path.Combine(branchesDir, Slug(f[0]))),
        });
    }
    string changes = Capture(repo, "git", "status", "--porcelain").Output;
    return Results.Json(new
    {
        repo, branchesDir, web, prError, settings,
        mac = OperatingSystem.IsMacOS(),
        own = new
        {
            id = ".",
            branch = Capture(repo, "git", "branch", "--show-current").Output.Trim(),
            changes = changes.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length,
            local = Local(repo),
        },
        branches,
        tools = new
        {
            git = Which("git"),
            dotnet = Which("dotnet"),
            cosmos = Which("cosmos"),
            qemu = OperatingSystem.IsMacOS() ? Which("qemu-system-x86_64") ?? "" : null, // sonst bringt cosmos run QEMU mit
        },
    });
}

object? Local(string dir)
{
    if (!Path.Exists(Path.Combine(dir, ".git"))) return null;
    string iso = Path.Combine(dir, "VibanOS", "output-x64", "VibanOS.iso");
    string shots = Path.Combine(dir, "preview");
    return new
    {
        dir,
        sha = Capture(dir, "git", "rev-parse", "HEAD").Output.Trim(),
        built = File.Exists(iso) ? File.GetLastWriteTime(iso) : (DateTime?)null,
        disk = File.Exists(Path.Combine(dir, "VibanOS", "disk.img")),
        shots = Directory.Exists(shots) ? Directory.GetFiles(shots, "*.png").Select(f => Path.GetFileName(f)).Order().ToArray() : Array.Empty<string>(),
    };
}

// "." ist der eigene Klon, sonst muss es ein Branch von origin sein. So kommt kein beliebiger Pfad oder Name durch.
(string Dir, string? Branch)? Resolve(string target)
{
    if (target == ".") return (repo, null);
    string names = Capture(repo, "git", "for-each-ref", "--format=%(refname:lstrip=3)", "refs/remotes/origin").Output;
    return target != "HEAD" && names.Split('\n').Contains(target) ? (Path.Combine(branchesDir, Slug(target)), target) : null;
}

Settings LoadSettings()
{
    try
    {
        var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsFile), JsonSerializerOptions.Web);
        return s == null ? new() : s with { QemuArgs = s.QemuArgs ?? "" };
    }
    catch (Exception e) when (e is IOException or JsonException)
    {
        return new();
    }
}

static string Slug(string branch) => branch.Replace('/', '-');

static string? FindRepo(string start)
{
    for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
        if (File.Exists(Path.Combine(d.FullName, "VibanOS.sln"))) return d.FullName;
    return null;
}

// Sucht ein Programm im PATH, unter Windows auch mit .exe, .cmd und .bat.
static string? Which(string name)
{
    string[] exts = OperatingSystem.IsWindows() ? [".exe", ".cmd", ".bat"] : [""];
    foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        foreach (string ext in exts)
        {
            if (dir.Trim('"') == "") continue;
            string f = Path.Combine(dir.Trim('"'), name + ext);
            if (File.Exists(f)) return f;
        }
    return null;
}

static void OpenBrowser(string url)
{
    try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
    catch (Exception) { Console.WriteLine("Im Browser öffnen: " + url); }
}

record Settings(string QemuArgs = "", bool Verbose = false);

record ActionRequest(string Target, string Action);

// Die letzten Zeilen aller Schritte. Die Seite fragt mit der Nummer der letzten Zeile nach, die sie schon hat.
class LogBuffer
{
    private static readonly Regex Ansi = new(@"\x1B\[[0-?]*[ -/]*[@-~]|\x1B\][^\x07]*\x07|\r");
    private readonly List<string> lines = [];
    private int first; // Nummer von lines[0]

    public void Add(string line)
    {
        lock (lines)
        {
            lines.Add(Ansi.Replace(line, ""));
            if (lines.Count > 20_000)
            {
                lines.RemoveRange(0, 5_000);
                first += 5_000;
            }
        }
    }

    public (int Next, string[] Lines, bool Reset) Since(int from)
    {
        lock (lines)
        {
            int next = first + lines.Count;
            bool reset = from < first || from > next; // Seite ist zu weit hinten oder kennt einen früheren Lauf der Werkstatt
            int start = reset ? 0 : from - first;
            return (next, lines.GetRange(start, lines.Count - start).ToArray(), reset);
        }
    }
}
