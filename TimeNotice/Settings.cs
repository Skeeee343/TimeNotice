using System.Text.Json;
using System.IO;

namespace TimeNotice;

public sealed class Settings
{
    public int ReminderMinutes { get; set; } = 30; // legacy Phase 2 field, migrates to GeneralMinutes on load
    public bool GeneralEnabled { get; set; } = true;
    public int GeneralMinutes { get; set; } = 30;
    public bool AlignToClock { get; set; } = false; // hourly grid (:00/:07/.. for 7 min) instead of counting from launch
    public bool WindowOnly { get; set; } = false;
    public string WindowStart { get; set; } = "09:00"; // ponytail: HH:mm text, TimePicker upgrade if asked
    public string WindowEnd { get; set; } = "23:00";
    public bool GameEnabled { get; set; } = true; // game reminders on out of the box; heuristic discovers unlisted games
    public int GameMinutes { get; set; } = 30;
    public bool CountWhenRunning { get; set; } = false; // optional: session follows a running game, focus not needed
    public List<string> GameExes { get; set; } = new();
    public bool GameWindowOnly { get; set; } = false;
    public string GameWindowStart { get; set; } = "09:00"; // ponytail: HH:mm text, TimePicker upgrade if asked
    public string GameWindowEnd { get; set; } = "23:00";
    public int DisplaySeconds { get; set; } = 10;
    public bool ShowSession { get; set; } = true;
    public string MainTemplate { get; set; } = "It is now {time}"; // ponytail: {time} {next} {interval} tokens, preview in Settings window
    public string SubTemplate { get; set; } = "Next time alert in {interval} min";
    public string Position { get; set; } = "BottomRight"; // ponytail: 4 corners only, free-form drag later if asked
    public double Opacity { get; set; } = 0.92; // box background only; text uses TextOpacity
    public double TextOpacity { get; set; } = 1.0; // main/sub/exe text, 0.2-1.0
    public bool ShowBox { get; set; } = true; // false = transparent background, text floats alone
    public string ThemeMode { get; set; } = "Dark"; // Dark | Light | MatchHost | Custom
    public string CustomBackground { get; set; } = "#CC1E1E1E"; // ponytail: hex #RRGGBB or #AARRGGBB, color picker later if asked
    public string CustomText { get; set; } = "#FFFFFFFF";
    public string CustomSubText { get; set; } = "#FFBBBBBB";
    public const string DefaultGameMainTemplate = "You have been playing {game} for {elapsed}"; // ponytail: {game} {elapsed} {interval} {time} {next} work here; game never reuses MainTemplate
    public const string DefaultGameSubTemplate = "Next game alert in {interval} min";
    public string GameMainTemplate { get; set; } = DefaultGameMainTemplate;
    public string GameSubTemplate { get; set; } = DefaultGameSubTemplate;
    public bool GameNotifyOnDetect { get; set; } = true; // starting to track a listed game says so
    public string GameDetectTemplate { get; set; } = "{game} matched Games list"; // ponytail: {game} {interval} {time} tokens
    public string GameDetectSubTemplate { get; set; } = "Alerts set to {interval} min"; // blank = no sub line
    public bool PauseAll { get; set; } = false;
    public bool StartWithWindows { get; set; } = false;
    public List<string> QuietModeExes { get; set; } = new();
    public List<string> IgnoreExes { get; set; } = new();
    // ponytail: pre-rename key — Load merges it into IgnoreExes once, then drops it; delete this property next cleanup
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? LauncherExes { get; set; }
    public bool HeuristicEnabled { get; set; } = true;
    public int HeuristicStartSeconds { get; set; } = 60; // sustained active before a session is found
    public int HeuristicSuspectSeconds { get; set; } = 30; // fg loss/idle before SUSPECT
    public int HeuristicInactiveMinutes { get; set; } = 5; // SUSPECT age before INACTIVE/close
    public int HeuristicGpuPercent { get; set; } = 10; // per-process GPU Engine % counts as active
    public int ConfirmTimeoutSeconds { get; set; } = 30; // sticky confirm countdown; timeout applies ConfirmTimeoutAction
    public string ConfirmTimeoutAction { get; set; } = "Accept"; // Accept | Reject — what a missed confirm does
    [System.Text.Json.Serialization.JsonIgnore]
    public bool WasResetFromCorrupt { get; private set; } // set only by Load's catch path, never persisted
    [System.Text.Json.Serialization.JsonIgnore]
    public string? CorruptBackupPath { get; private set; } // where the unreadable file was backed up, if at all

    private readonly object _saveLock = new(); // confirm vs Settings-window saves serialize here (same UI thread, logical race)

    public static string Dir => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TimeNotice");

    public static string Path => System.IO.Path.Combine(Dir, "settings.json");

    private static Settings SeedDefaults()
    {
        var fresh = new Settings();
        fresh.IgnoreExes = GameCatalog.DefaultIgnored()
            .Concat(GameCatalog.DefaultExtraIgnored()).ToList();
        return fresh;
    }

    private static Settings SeedWithReset(string? backup)
    {
        var reset = SeedDefaults();
        reset.WasResetFromCorrupt = true;
        reset.CorruptBackupPath = backup;
        return reset;
    }

    public static Settings Load()
    {
        Settings s;
        string raw = "";
        try
        {
            if (File.Exists(Path))
            {
                raw = File.ReadAllText(Path);
                s = JsonSerializer.Deserialize<Settings>(raw) ?? new Settings();
            }
            else
            {
                return SeedDefaults();
            }
        }
        catch
        {
            // ponytail: corrupt config keeps a backup for diagnosis, then falls back to defaults
            string? backup = null;
            try
            {
                Directory.CreateDirectory(Dir);
                backup = System.IO.Path.Combine(Dir, $"settings.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                File.Copy(Path, backup, overwrite: false);
            }
            catch { backup = null; }
            return SeedWithReset(backup);
        }
        if (s.GeneralMinutes <= 0) s.GeneralMinutes = s.ReminderMinutes > 0 ? s.ReminderMinutes : 30; // migrate Phase 2 file
        s.ReminderMinutes = s.GeneralMinutes; // keep legacy in sync
        if (s.GameMinutes <= 0) s.GameMinutes = 30;
        if (string.IsNullOrWhiteSpace(s.GameWindowStart)) s.GameWindowStart = "09:00";
        if (string.IsNullOrWhiteSpace(s.GameWindowEnd)) s.GameWindowEnd = "23:00";
        if (string.IsNullOrWhiteSpace(s.MainTemplate)) s.MainTemplate = "It is now {time}";
        if (string.IsNullOrWhiteSpace(s.SubTemplate)) s.SubTemplate = "Next time alert in {interval} min";
        if (string.IsNullOrWhiteSpace(s.GameMainTemplate)) s.GameMainTemplate = DefaultGameMainTemplate;
        if (string.IsNullOrWhiteSpace(s.GameSubTemplate)) s.GameSubTemplate = DefaultGameSubTemplate;
        if (string.IsNullOrWhiteSpace(s.GameDetectTemplate)) s.GameDetectTemplate = "{game} matched Games list";
        if (s.GameDetectSubTemplate is null) s.GameDetectSubTemplate = "Alerts set to {interval} min";
        var validPositions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "TopLeft", "TopRight", "BottomLeft", "BottomRight" };
        if (!validPositions.Contains(s.Position ?? "")) s.Position = "BottomRight";
        var validThemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dark", "Light", "MatchHost", "Custom" };
        if (!validThemes.Contains(s.ThemeMode ?? "")) s.ThemeMode = "Dark";
        if (s.Opacity < 0.2) s.Opacity = 0.2;
        if (s.Opacity > 1.0) s.Opacity = 1.0;
        if (s.TextOpacity < 0.2) s.TextOpacity = 0.2;
        if (s.TextOpacity > 1.0) s.TextOpacity = 1.0;
        if (string.IsNullOrWhiteSpace(s.CustomBackground)) s.CustomBackground = "#CC1E1E1E";
        if (string.IsNullOrWhiteSpace(s.CustomText)) s.CustomText = "#FFFFFFFF";
        if (string.IsNullOrWhiteSpace(s.CustomSubText)) s.CustomSubText = "#FFBBBBBB";
        if (s.IgnoreExes is null) s.IgnoreExes = new();
        if (s.LauncherExes is not null && s.LauncherExes.Count > 0)
            s.IgnoreExes.AddRange(s.LauncherExes); // one-version migration from the LauncherExes key
        s.LauncherExes = null; // stop writing the old key
        s.IgnoreExes = s.IgnoreExes
            .Select(x => (x ?? "").Trim()).Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // Seed defaults only when the file never had the key (old config).
        // A present-but-empty list means the user cleared it on purpose.
        if (s.IgnoreExes.Count == 0 && !raw.Contains("\"IgnoreExes\""))
            s.IgnoreExes = GameCatalog.DefaultIgnored()
                .Concat(GameCatalog.DefaultExtraIgnored()).ToList(); // seed the ignore list on old configs
        if (s.HeuristicStartSeconds < 10 || s.HeuristicStartSeconds > 600) s.HeuristicStartSeconds = 60;
        if (s.HeuristicSuspectSeconds < 5 || s.HeuristicSuspectSeconds > 300) s.HeuristicSuspectSeconds = 30;
        if (s.HeuristicInactiveMinutes < 1 || s.HeuristicInactiveMinutes > 60) s.HeuristicInactiveMinutes = 5;
        if (s.HeuristicGpuPercent < 1 || s.HeuristicGpuPercent > 100) s.HeuristicGpuPercent = 10;
        s.Normalize(); // repair hand-edited or migrated values (windows, colors, lists, confirm)
        return s;
    }

    // Central repair for hand-edited or migrated configs. Called from Load and after every Settings-window save,
    // so Save_Click validation (which blocks bad input) and Load (which must survive it) share one rule.
    public void Normalize()
    {
        if (ConfirmTimeoutSeconds < 5 || ConfirmTimeoutSeconds > 120) ConfirmTimeoutSeconds = 30;
        if (!ConfirmTimeoutAction.Equals("Accept", StringComparison.OrdinalIgnoreCase)
            && !ConfirmTimeoutAction.Equals("Reject", StringComparison.OrdinalIgnoreCase))
            ConfirmTimeoutAction = "Accept";
        if (!TimeSpan.TryParse(WindowStart, out _)) WindowStart = "09:00";
        if (!TimeSpan.TryParse(WindowEnd, out _)) WindowEnd = "23:00";
        if (!TimeSpan.TryParse(GameWindowStart, out _)) GameWindowStart = "09:00";
        if (!TimeSpan.TryParse(GameWindowEnd, out _)) GameWindowEnd = "23:00";
        if (!IsColorLike(CustomBackground)) CustomBackground = "#CC1E1E1E";
        if (!IsColorLike(CustomText)) CustomText = "#FFFFFFFF";
        if (!IsColorLike(CustomSubText)) CustomSubText = "#FFBBBBBB";
        if (TextOpacity < 0.2) TextOpacity = 0.2;
        if (TextOpacity > 1.0) TextOpacity = 1.0;
        if (string.IsNullOrWhiteSpace(GameMainTemplate)) GameMainTemplate = DefaultGameMainTemplate; // game never falls back to general wording
        if (string.IsNullOrWhiteSpace(GameSubTemplate)) GameSubTemplate = DefaultGameSubTemplate;
        GameExes = NormalizeExeList(GameExes);
        IgnoreExes = NormalizeExeList(IgnoreExes);
        QuietModeExes = NormalizeExeList(QuietModeExes);
        QuietModeExes.RemoveAll(q => ExeKey(q).Equals("example-game", StringComparison.OrdinalIgnoreCase)); // drop the shipped placeholder
    }

    private static bool IsColorLike(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Trim();
        if (!t.StartsWith('#') || (t.Length != 7 && t.Length != 9)) return false;
        return t.Skip(1).All(Uri.IsHexDigit);
    }

    // Strip line-breaks and '=' (window titles can contain both), trim, cap display names at 64 chars.
    public static string SanitizeListLine(string? line)
    {
        var t = (line ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
        var eq = t.IndexOf('=');
        if (eq < 0) return t;
        var exe = t[..eq].Trim();
        var name = t[(eq + 1)..].Trim();
        if (name.Length > 64) name = name[..64].Trim();
        return name.Length == 0 ? exe : $"{exe} = {name}";
    }

    // Single Title-case fallback shared by popups, confirms, and previews.
    internal static string TitleCaseExe(string? exe)
    {
        var t = ExeKey(exe);
        if (t.Length == 0) return exe ?? "";
        return char.ToUpperInvariant(t[0]) + t[1..];
    }

    // Same key App.SameExe matches on (mapping suffix and .exe stripped). Keep in sync.
    internal static string ExeKey(string? s)
    {
        var t = (s ?? "").Trim();
        var eq = t.IndexOf('=');
        if (eq >= 0) t = t[..eq].Trim();
        if (t.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) t = t[..^4];
        return t;
    }

    private static List<string> NormalizeExeList(List<string>? lines)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var line in lines ?? Enumerable.Empty<string>())
        {
            var clean = SanitizeListLine(line);
            if (clean.Length == 0 || !seen.Add(ExeKey(clean))) continue;
            result.Add(clean);
        }
        return result;
    }

    // Single-writer entry points: merge into the live lists and persist at once,
    // so a confirm-timeout write and a Settings-window save cannot silently drop each other.
    public void AddGameExe(string entry)
    {
        lock (_saveLock)
        {
            var clean = SanitizeListLine(entry);
            if (clean.Length > 0 && !GameExes.Any(l => string.Equals(ExeKey(l), ExeKey(clean), StringComparison.OrdinalIgnoreCase)))
            {
                GameExes.Add(clean);
                Save();
            }
        }
    }

    public void AddIgnoreExe(string exe)
    {
        lock (_saveLock)
        {
            var clean = SanitizeListLine(exe);
            if (clean.Length > 0 && !IgnoreExes.Any(l => string.Equals(ExeKey(l), ExeKey(clean), StringComparison.OrdinalIgnoreCase)))
            {
                IgnoreExes.Add(clean);
                Save();
            }
        }
    }

    public void AddQuietExe(string exe)
    {
        lock (_saveLock)
        {
            var clean = SanitizeListLine(exe);
            if (clean.Length > 0 && !QuietModeExes.Any(l => string.Equals(ExeKey(l), ExeKey(clean), StringComparison.OrdinalIgnoreCase)))
            {
                QuietModeExes.Add(clean);
                Save();
            }
        }
    }

    public void Save()
    {
        lock (_saveLock) // every save path serializes here: confirm-timeout vs Settings-window can no longer interleave
        {
            Directory.CreateDirectory(Dir);
            // Atomic write: temp + move so a crash/power loss never leaves a torn settings.json.
            var tmp = Path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, Path, overwrite: true);
        }
    }
}
