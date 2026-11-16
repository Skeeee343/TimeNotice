using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace TimeNotice;

public static class NotificationPolicy
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int count);

    // Window title of the foreground window, for naming generic hosts
    // (javaw.exe serves any Java app — the title tells us which one).

    public static IntPtr ForegroundWindowHandle()
    {
        try { return GetForegroundWindow(); }
        catch { return IntPtr.Zero; } // silent: per-tick call, fail-open means no foreground window
    }

    public static string? ForegroundWindowText()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            var sb = new System.Text.StringBuilder(512);
            return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : null;
        }
        catch { return null; } // silent: per-tick call, fail-open means no title
    }

    // Own binary under any filename: a browser-renamed download
    // (TimeNotice (1).exe) is still self and must never track/classify.
    // Resolved once from the running process, never hardcoded.
    private static readonly string OwnExeFile = SelfFile();
    private static readonly string OwnExeName = SelfName();

    private static string SelfFile()
    {
        try { return System.IO.Path.GetFileName(Environment.ProcessPath) ?? "TimeNotice.exe"; }
        catch (Exception ex)
        {
            Logger.Log($"src=foreground self-file-failed err={ex.GetType().Name}");
            return "TimeNotice.exe";
        }
    }

    private static string SelfName()
    {
        try { return Process.GetCurrentProcess().ProcessName + ".exe"; }
        catch (Exception ex)
        {
            Logger.Log($"src=foreground self-name-failed err={ex.GetType().Name}");
            return "TimeNotice.exe";
        }
    }

    public static bool IsSelf(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return false;
        var t = exe.Trim();
        return t.Equals("TimeNotice.exe", StringComparison.OrdinalIgnoreCase) // legacy literal fallback
            || t.Equals(OwnExeFile, StringComparison.OrdinalIgnoreCase)
            || t.Equals(OwnExeName, StringComparison.OrdinalIgnoreCase);
    }

    public static string? ForegroundExe() => ForegroundDiag().Exe;

    // Gaming-PC troubleshooting without dev tools (issue #10): same read as
    // ForegroundExe but with the how — pid, window title, and which fallback
    // produced the name. Source is "module", "name", or "unresolved".
    public static (string? Exe, uint Pid, string? Title, string Source) ForegroundDiag()
    {
        var hwnd = ForegroundWindowHandle();
        if (hwnd == IntPtr.Zero) return (null, 0, null, "unresolved");
        uint pid;
        try
        {
            GetWindowThreadProcessId(hwnd, out pid);
        }
        catch { return (null, 0, null, "unresolved"); } // silent: per-tick call, fail-open means unknown focus

        return ResolveForegroundProcess(pid, TryGetWindowTitle(hwnd));
    }

    private static string? TryGetWindowTitle(IntPtr hwnd)
    {
        try
        {
            var sb = new System.Text.StringBuilder(256);
            return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : null;
        }
        catch { return null; } // silent: per-tick title is best-effort
    }

    private static (string? Exe, uint Pid, string? Title, string Source) ResolveForegroundProcess(uint pid, string? title)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            var file = TryGetModuleFileName(process);
            if (!string.IsNullOrEmpty(file)) return (Path.GetFileName(file), pid, title, "module");
            var name = TryGetProcessName(process);
            return name is null ? (null, pid, title, "unresolved") : (name + ".exe", pid, title, "name");
        }
        catch { return (null, pid, title, "unresolved"); } // silent: per-tick call, pid vanished
    }

    private static string? TryGetModuleFileName(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; } // silent: elevated/protected process falls back to its name
    }

    private static string? TryGetProcessName(Process process)
    {
        try { return process.ProcessName; }
        catch { return null; } // silent: per-tick call, process exited mid-read
    }

    public static bool ShouldSuppress(string? exe, Settings s)
    {
        if (s.PauseAll) return true;
        if (string.IsNullOrWhiteSpace(exe)) return false; // fail-open for Phase 1
        // Loose match like everywhere else: "notepad" and "notepad.exe = Notepad" both silence notepad.exe (#25).
        return s.QuietModeExes.Any(q => string.Equals(Settings.ExeKey(q), Settings.ExeKey(exe), StringComparison.OrdinalIgnoreCase));
    }
}
