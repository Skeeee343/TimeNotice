// Phase 4 heuristics (SCOPE §4). Stdlib + Win32 only: GetLastInputInfo for
// idle, GetWindowRect + monitor compare for fullscreen/borderless, raw PDH
// "GPU Engine" counters per pid (no vendor SDK, no new deps).
// ponytail: one 5s poll drives idle + GPU + fullscreen together; per-signal
// 1s/5s split later if timing proves too coarse.

using System.Runtime.InteropServices;

namespace TimeNotice;

public enum HeurState { Idle, Active, Suspect, Inactive }

public static class Heuristics
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hmon, ref MONITORINFO info);
    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        public MONITORINFO() { cbSize = Marshal.SizeOf<MONITORINFO>(); rcMonitor = new(); rcWork = new(); dwFlags = 0; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    public static uint ForegroundPid()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return 0;
            GetWindowThreadProcessId(hwnd, out var pid);
            return pid;
        }
        catch { return 0; }
    }

    public static int IdleSeconds()
    {
        try
        {
            var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (!GetLastInputInfo(ref info)) return 0; // fail-open: assume active
            return (int)(unchecked((uint)Environment.TickCount - info.dwTime) / 1000);
        }
        catch { return 0; }
    }

    // True when the foreground window covers its whole monitor (exclusive
    // fullscreen or true borderless). Maximized desktop windows cover only
    // the work area (taskbar excluded), so they do NOT count — conservative.
    public static bool IsForegroundFullscreen()
    {
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;
            if (!GetWindowRect(hwnd, out var r)) return false;
            var hmon = MonitorFromWindow(hwnd, 2);
            if (hmon == IntPtr.Zero) return false;
            var mi = new MONITORINFO();
            if (!GetMonitorInfo(hmon, ref mi)) return false;
            const int tol = 2;
            return Math.Abs(r.Left - mi.rcMonitor.Left) <= tol
                && Math.Abs(r.Top - mi.rcMonitor.Top) <= tol
                && Math.Abs(r.Right - mi.rcMonitor.Right) <= tol
                && Math.Abs(r.Bottom - mi.rcMonitor.Bottom) <= tol;
        }
        catch { return false; }
    }

    // GPU Engine sampler via Microsoft's PerformanceCounter package (no raw
    // PDH marshaling — the hand-rolled struct layout crashed the app once).
    // ponytail: per-instance counters cached, instance list refreshed slowly.
    public sealed class GpuSampler : IDisposable
    {
        private readonly Dictionary<string, System.Diagnostics.PerformanceCounter> _counters = new(StringComparer.OrdinalIgnoreCase);
        private DateTime _lastRefresh = DateTime.MinValue;
        private bool _primed;
        public bool Available { get; private set; }
        public bool Probed { get; private set; }

        public GpuSampler()
        {
            try
            {
                var _ = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
                Available = true; // category exists; per-pid instances resolved per sample
            }
            catch { Available = false; } // no GPU counters (VM): fullscreen path still works
            finally { Probed = true; }
        }

        // Returns summed % for pid, or null when unknown (priming, no
        // per-process instances yet, or error). No match reads as unknown,
        // never as 0%: a fresh launch has no instances until the next refresh.
        public double? SampleSum(uint pid)
        {
            if (!Available || pid == 0) return null;
            try
            {
                var tag = $"pid_{pid}_";
                var haveAny = _counters.Keys.Any(k => k.Contains(tag, StringComparison.OrdinalIgnoreCase));
                if ((DateTime.UtcNow - _lastRefresh).TotalSeconds > 15 || !haveAny)
                    Refresh(tag); // relaunch/new pid: instances appear after the last refresh
                if (!_primed) { _primed = true; return null; } // first NextValue is always 0
                double sum = 0;
                var matched = 0;
                foreach (var (name, c) in _counters)
                {
                    if (!name.Contains(tag, StringComparison.OrdinalIgnoreCase)) continue;
                    matched++;
                    try { sum += c.NextValue(); } catch { }
                }
                if (matched == 0) return null;
                return sum;
            }
            catch { return null; }
        }

        private void Refresh(string tag)
        {
            _lastRefresh = DateTime.UtcNow;
            string[] names;
            try
            {
                var cat = new System.Diagnostics.PerformanceCounterCategory("GPU Engine");
                names = cat.GetInstanceNames();
            }
            catch { Available = false; return; }
            var wanted = new HashSet<string>(names.Where(n => n.Contains(tag, StringComparison.OrdinalIgnoreCase)), StringComparer.OrdinalIgnoreCase);
            foreach (var dead in _counters.Keys.Where(k => !wanted.Contains(k)).ToList())
            {
                try { _counters[dead].Dispose(); } catch { }
                _counters.Remove(dead);
            }
            foreach (var name in wanted)
            {
                if (_counters.ContainsKey(name)) continue;
                try
                {
                    var c = new System.Diagnostics.PerformanceCounter("GPU Engine", "Utilization Percentage", name, readOnly: true);
                    _ = c.NextValue(); // prime at creation; readings valid from the next tick
                    _counters[name] = c;
                }
                catch { }
            }
            // no _primed reset: existing counters keep valid readings; new ones were primed above
        }

        // Top GPU consumers across ALL processes, for the Detect-tab readout
        // (issue #9): single-monitor users can't keep the game focused while
        // Settings is open, so rank by GPU instead of by focus. Resolves exe
        // via ProcessName only — no MainModule read, so anti-cheat-protected
        // processes still resolve. Caller throttles; a full enumeration every
        // tick would spam NextValue calls.
        public List<(uint Pid, string Exe, double Gpu)> SnapshotTop(int count)
        {
            var top = new List<(uint Pid, string Exe, double Gpu)>();
            if (!Available) return top;
            try
            {
                Refresh(""); // empty tag matches every instance
                var sums = new Dictionary<uint, double>();
                foreach (var (name, c) in _counters)
                {
                    var pid = ParsePid(name);
                    if (pid is null) continue;
                    double v;
                    try { v = c.NextValue(); } catch { continue; }
                    sums[pid.Value] = sums.GetValueOrDefault(pid.Value) + v;
                }
                foreach (var (pid, sum) in sums.OrderByDescending(kv => kv.Value).Take(Math.Max(1, count)))
                {
                    if (sum <= 0) continue;
                    string exe;
                    try { using var p = System.Diagnostics.Process.GetProcessById((int)pid); exe = p.ProcessName + ".exe"; }
                    catch { continue; } // exited between sample and resolve
                    top.Add((pid, exe, sum));
                }
            }
            catch { }
            return top;
        }

        private static uint? ParsePid(string instance)
        {
            const string tag = "pid_";
            var i = instance.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i += tag.Length;
            var j = i;
            while (j < instance.Length && char.IsDigit(instance[j])) j++;
            return j > i && uint.TryParse(instance.AsSpan(i, j - i), out var pid) ? pid : null;
        }

        public void Dispose()
        {
            foreach (var c in _counters.Values)
                try { c.Dispose(); } catch { }
            _counters.Clear();
            try { GC.SuppressFinalize(this); } catch { }
        }
    }
}
