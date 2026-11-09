// Phase 3 built-in catalog (SCOPE.md §5). Small verified list, 12 max.
// Priority: user GameExes mapping > this catalog > heuristic (Phase 4).
// Ignored entries here are Policy=Ignore and never count as gameplay, even
// if a user lists the same exe under Games (ignore wins).
// ponytail: static data table now, JSON-downloadable catalog later if asked.

namespace TimeNotice;

public sealed record CatalogEntry(string Exe, string Display, string Policy);

public static class GameCatalog
{
    // Keep under 12 entries per SCOPE §5. Exe spellings are the Valve/Epic/GOG/
    // Ubisoft/EA client process names plus a few widely-known game binaries.
    // If a name turns out wrong on the test PC, override locally via Settings
    // (Games / Ignored boxes) — no code change needed.
    public static readonly List<CatalogEntry> Entries = new()
    {
        new("steam.exe", "Steam", "Ignore"),
        new("epicgameslauncher.exe", "Epic Games Launcher", "Ignore"),
        new("GalaxyClient.exe", "GOG Galaxy", "Ignore"),
        new("UbisoftConnect.exe", "Ubisoft Connect", "Ignore"),
        new("eaapp.exe", "EA App", "Ignore"),
        // Multi-exe franchise listed explicitly (same title, two binaries).
        new("cs2.exe", "Counter-Strike 2", "Normal"),
        new("csgo.exe", "Counter-Strike (Legacy)", "Normal"),
        new("eldenring.exe", "Elden Ring", "Normal"),
        new("Balatro.exe", "Balatro", "Normal"),
    };

    public static List<string> DefaultIgnored() =>
        Entries.Where(e => e.Policy.Equals("Ignore", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Exe).ToList();

    // GPU-spiky non-game apps (video calls, screenshare, recording): sustained GPU + focus
    // would otherwise promote them to heuristic sessions. Ignored, never gameplay.
    // Exe spellings are the vendor client process names; matching is case-insensitive.
    private static readonly List<string> ExtraIgnored = new()
    {
        "discord.exe",
        "Zoom.exe",
        "ms-teams.exe",
        "Teams.exe",
        "slack.exe",
        "obs64.exe",
    };

    private static readonly HashSet<string> ExtraIgnoreKeys =
        new(ExtraIgnored.Select(StripExe), StringComparer.OrdinalIgnoreCase);

    public static List<string> DefaultExtraIgnored() => ExtraIgnored.ToList(); // copy: the shared table must never escape mutable

    private static string StripExe(string? exe)
    {
        var key = (exe ?? "").Trim();
        if (key.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) key = key[..^4];
        return key;
    }

    public static CatalogEntry? Find(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return null;
        var key = StripExe(exe);
        return Entries.FirstOrDefault(e => string.Equals(StripExe(e.Exe), key, StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsIgnore(string? exe)
    {
        if (Find(exe)?.Policy.Equals("Ignore", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        // GPU-spiky non-game apps are ignore-never-track even on fresh configs
        // where settings.json was never seeded (user clear still respected via
        // Settings.IgnoreExes display; this is the runtime safety net).
        if (string.IsNullOrWhiteSpace(exe)) return false;
        return ExtraIgnoreKeys.Contains(StripExe(exe));
    }
}
