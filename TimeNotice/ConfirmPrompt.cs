// Confirm prompt (Option 1 poll, SCOPE-clean).
// Polls Ctrl+Alt+Z/X with GetAsyncKeyState — observes keys without blocking
// them, no focus change, no hook. Modifier combo so normal typing never fires
// it. Only active while the sticky popup is visible.
// Fires automatically on a new heuristic session; also manual via tray test.
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TimeNotice;

public sealed class ConfirmPrompt : IDisposable
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_Z = 0x5A;
    private const int VK_X = 0x58;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt; either side counts, combo keeps game typing safe
    private const int TimeoutFloorSeconds = 5; // never shorter: a missed popup must stay readable
    private const int TimeoutCeilSeconds = 120;
    private int _timeoutSecs = 30;

    private readonly OverlayWindow _overlay;
    private readonly Settings _settings;
    private readonly string _exe;
    private readonly string _display;
    private readonly Action<string, string> _onDone;
    private readonly bool _persist; // false = dry run: test popup never touches Games/Ignored
    private DispatcherTimer? _poll;
    private DateTime _deadlineUtc;
    private bool _done;
    private bool _prevZ, _prevX;
    private int _lastSecs = -1;

    public bool Active => !_done;

    public ConfirmPrompt(OverlayWindow overlay, Settings settings, string exe, string display, Action<string, string> onDone, bool persist = true)
    {
        _overlay = overlay;
        _settings = settings;
        _exe = exe;
        _display = display;
        _onDone = onDone;
        _persist = persist;
    }

    public void Start()
    {
        _timeoutSecs = Math.Clamp(_settings.ConfirmTimeoutSeconds, TimeoutFloorSeconds, TimeoutCeilSeconds);
        _deadlineUtc = DateTime.UtcNow.AddSeconds(_timeoutSecs);
        // Require a fresh press: keys already held at open don't count.
        _prevZ = IsDown(VK_Z);
        _prevX = IsDown(VK_X);
        _overlay.ShowSticky(PromptMain(), ExeLine(), Footer(_timeoutSecs));
        _lastSecs = _timeoutSecs;
        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _poll.Tick += (_, _) => Tick();
        _poll.Start();
        Log($"src=confirm-test-shown exe={_exe} keys=Ctrl+Alt+Z/X timeout={_timeoutSecs}s");
    }

    private static bool IsDown(int vk)
    {
        try { return (GetAsyncKeyState(vk) & 0x8000) != 0; }
        catch { return false; }
    }

    private string PromptMain() => $"{_display} Detected";

    // Raw exe on its own smaller line so generic hosts (javaw.exe) stay identifiable
    // without crowding the header. Hidden when the display name is the exe itself.
    private string ExeLine() =>
        _display.Equals(_exe, StringComparison.OrdinalIgnoreCase) ? "" : $"Process {_exe}";

    private string Footer(int secs)
    {
        // The auto line marks whichever outcome the timeout applies (Settings → Popup → Confirm prompt).
        if (_settings.ConfirmTimeoutAction.Equals("Reject", StringComparison.OrdinalIgnoreCase))
            return $"Ctrl+Alt+Z — Accept\nCtrl+Alt+X — Reject (auto in {secs}s)";
        return $"Ctrl+Alt+Z — Accept (auto in {secs}s)\nCtrl+Alt+X — Reject";
    }

    private void Tick()
    {
        if (_done) return;
        var secs = Math.Max(0, (int)Math.Ceiling((_deadlineUtc - DateTime.UtcNow).TotalSeconds));
        if (secs != _lastSecs)
        {
            _lastSecs = secs;
            _overlay.UpdateSticky(PromptMain(), ExeLine(), Footer(secs));
        }
        // Edge-trigger: fresh Ctrl+Alt+letter press only; held keys don't repeat-fire.
        var combo = IsDown(VK_CONTROL) && IsDown(VK_MENU);
        var downZ = IsDown(VK_Z);
        var downX = IsDown(VK_X);
        var hitZ = combo && downZ && !_prevZ;
        var hitX = combo && downX && !_prevX;
        _prevZ = downZ; _prevX = downX;
        if (hitZ) Resolve("accept-key-Z");
        else if (hitX) Resolve("reject-key-X");
        else if (secs <= 0) Resolve("timeout-elapsed");
    }

    private void Resolve(string choice)
    {
        if (_done) return;
        _done = true;
        try { _poll?.Stop(); } catch { }
        try { _overlay.HideNow(); } catch { }
        try
        {
            var timeout = choice.StartsWith("timeout", StringComparison.OrdinalIgnoreCase);
            var timeoutAccepts = _settings.ConfirmTimeoutAction.Equals("Accept", StringComparison.OrdinalIgnoreCase);
            var accept = choice.StartsWith("accept-key", StringComparison.OrdinalIgnoreCase)
                || (timeout && timeoutAccepts);
            if (!_persist) { /* dry-run test: never write */ }
            else if (accept)
            {
                var raw = _exe;
                // Only store "exe = Name" when the name is richer than Title-case (javaw.exe = Minecraft).
                if (!string.IsNullOrWhiteSpace(_display) && !_display.Equals(Settings.TitleCaseExe(_exe), StringComparison.OrdinalIgnoreCase))
                    raw = $"{_exe} = {_display}"; // friendly name from window title, e.g. javaw.exe = Minecraft
                _settings.AddGameExe(raw); // sanitized + merged inside; timeout-accept honors the configured action
            }
            else if (choice.StartsWith("reject", StringComparison.OrdinalIgnoreCase) || timeout)
            {
                _settings.AddIgnoreExe(_exe);
            }
        }
        catch { }
        Log($"src=confirm-result choice={choice} exe={_exe}");
        try { _onDone(choice, _exe); } catch { }
    }

    private static void Log(string msg)
    {
        Logger.Log(msg);
    }

    public void Dispose()
    {
        try { _poll?.Stop(); } catch { }
        _done = true;
    }
}
