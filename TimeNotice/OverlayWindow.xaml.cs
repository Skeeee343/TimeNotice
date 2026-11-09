using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace TimeNotice;

public partial class OverlayWindow : Window
{
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;

    private readonly Settings _settings;
    private readonly DispatcherTimer _hideTimer;

    public event Action? Hidden;
    public bool IsShowing => Visibility == Visibility.Visible;

    public OverlayWindow(Settings settings)
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();
        _settings = settings;
        ShowActivated = false;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(settings.DisplaySeconds) };
        _hideTimer.Tick += (_, _) => FadeOut();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle;
        int ex = GetWindowLong(h, -20);
        SetWindowLong(h, -20, ex | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW);
    }

    public void ShowReminder(string timeText, string? subText = null)
    {
        BeginAnimation(OpacityProperty, null); // clear fade hold so re-show is visible
        TimeText.Text = timeText;
        SetExe(null); // reminders never show the exe line
        SubText.Text = subText ?? (_settings.ShowSession ? "TimeNotice test — no focus steal" : "");
        ApplyTheme();
        if (Visibility != Visibility.Visible) Show();
        UpdateLayout();
        Position();
        _hideTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, _settings.DisplaySeconds));
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    // Sticky confirm (test): shows without auto-hide so a 15s poll can own
    // the countdown. Still NOACTIVATE + TRANSPARENT: no focus steal, click-through.
    public void ShowSticky(string main, string? exe, string? sub)
    {
        BeginAnimation(OpacityProperty, null);
        TimeText.Text = main;
        SetExe(exe);
        SubText.Text = sub ?? "";
        ApplyTheme();
        if (Visibility != Visibility.Visible) Show();
        UpdateLayout();
        Position();
        _hideTimer.Stop();
    }

    public void UpdateSticky(string main, string? exe, string? sub)
    {
        if (Visibility != Visibility.Visible) return;
        TimeText.Text = main;
        SetExe(exe);
        SubText.Text = sub ?? "";
        UpdateLayout();
        Position();
    }

    private void SetExe(string? exe)
    {
        ExeText.Text = exe ?? "";
        ExeText.Visibility = string.IsNullOrWhiteSpace(exe) ? Visibility.Collapsed : Visibility.Visible;
    }

    public void HideNow()
    {
        _hideTimer.Stop();
        BeginAnimation(OpacityProperty, null);
        Hide();
        try { Hidden?.Invoke(); } catch { }
    }

    private void ApplyTheme()
    {
        var mode = (_settings.ThemeMode ?? "Dark").Trim();
        if (mode.Equals("MatchHost", StringComparison.OrdinalIgnoreCase))
            mode = IsHostLight() ? "Light" : "Dark";
        string bg = "#CC1E1E1E", fg = "#FFFFFFFF", sub = "#FFBBBBBB";
        if (mode.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            bg = "#FFFFFFFF"; fg = "#FF111116"; sub = "#FF5B5B66";
        }
        else if (mode.Equals("Custom", StringComparison.OrdinalIgnoreCase))
        {
            bg = _settings.CustomBackground; fg = _settings.CustomText; sub = _settings.CustomSubText;
        }
        PopupBorder.Background = new System.Windows.Media.SolidColorBrush(ParseColor(bg, "#CC1E1E1E"));
        // Light fill is pure white, so a hairline keeps the box edge visible over white windows.
        if (mode.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            PopupBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(ParseColor("#FFD5D5DC", "#FFD5D5DC"));
            PopupBorder.BorderThickness = new Thickness(1);
        }
        else PopupBorder.BorderThickness = new Thickness(0);
        TimeText.Foreground = new System.Windows.Media.SolidColorBrush(ParseColor(fg, "#FFFFFFFF"));
        SubText.Foreground = new System.Windows.Media.SolidColorBrush(ParseColor(sub, "#FFBBBBBB"));
        ExeText.Foreground = SubText.Foreground;
        // Box and text fade on their own: window stays opaque so the fade-out
        // animation still has full range. Box off = transparent background.
        Opacity = 1.0;
        var boxOp = Math.Clamp(_settings.Opacity, 0.2, 1.0);
        if (!_settings.ShowBox)
        {
            PopupBorder.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);
            PopupBorder.Opacity = 1.0;
        }
        else if (boxOp >= 1.0)
        {
            // 100% means fully blocked out: drop any translucency baked into the fill.
            var solid = ParseColor(bg, "#CC1E1E1E"); solid.A = 255;
            PopupBorder.Background = new System.Windows.Media.SolidColorBrush(solid);
            PopupBorder.Opacity = 1.0;
        }
        else PopupBorder.Opacity = boxOp;
        var top = Math.Clamp(_settings.TextOpacity, 0.2, 1.0);
        TimeText.Opacity = top;
        SubText.Opacity = top;
        ExeText.Opacity = top;
    }

    internal static System.Windows.Media.Color ParseColor(string? text, string fallback)
    {
        foreach (var candidate in new[] { text ?? "", fallback })
        {
            try
            {
                var c = System.Windows.Media.ColorConverter.ConvertFromString(candidate.Trim());
                if (c is System.Windows.Media.Color color) return color;
            }
            catch { }
        }
        return System.Windows.Media.Colors.Black;
    }

    internal static bool IsHostLight()
    {
        try
        {
            var v = Microsoft.Win32.Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 0);
            if (v is int i) return i != 0;
        }
        catch { }
        return false; // fail-dark when unknown
    }

    private void Position()
    {
        var area = SystemParameters.WorkArea;
        try
        {
            // Anchor to the focused app's monitor, converted to WPF units: Screen/Monitor APIs
            // report physical pixels, which misplaces the popup on mixed-DPI setups without this.
            // DXGI exclusive-fullscreen bypasses DWM, so no WPF window appears there — borderless only.
            var hwnd = NotificationPolicy.ForegroundWindowHandle();
            if (hwnd != IntPtr.Zero)
            {
                var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref mi))
                {
                    // Monitor APIs report physical pixels; WPF positions in device-independent units,
                    // so divide by OUR scale (150% text = 144 dpi, so 1920px = 1280 units).
                    // Never the focused game's DPI: a DPI-unaware game (javaw.exe = 96)
                    // would give scale 1 and push the corner off-screen.
                    double scale = 1.0;
                    try { scale = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX; } catch { }
                    if (scale <= 0) scale = 1.0;
                    var tl = new System.Windows.Point(mi.rcWork.Left / scale, mi.rcWork.Top / scale);
                    var br = new System.Windows.Point(mi.rcWork.Right / scale, mi.rcWork.Bottom / scale);
                    area = new Rect(tl, br);
                }
            }
        }
        catch { } // fail-safe: primary WorkArea above
        var w = ActualWidth > 0 ? ActualWidth : Width; // ponytail: SizeToContent makes Height auto, so anchor on rendered size
        var h = ActualHeight > 0 ? ActualHeight : 90;
        var pos = (_settings.Position ?? "BottomRight").Trim();
        if (pos.Equals("TopLeft", StringComparison.OrdinalIgnoreCase))
        {
            Left = area.Left + 24;
            Top = area.Top + 24;
        }
        else if (pos.Equals("TopRight", StringComparison.OrdinalIgnoreCase))
        {
            Left = area.Right - w - 24;
            Top = area.Top + 24;
        }
        else if (pos.Equals("BottomLeft", StringComparison.OrdinalIgnoreCase))
        {
            Left = area.Left + 24;
            Top = area.Bottom - h - 24;
        }
        else // BottomRight
        {
            Left = area.Right - w - 24;
            Top = area.Bottom - h - 24;
        }
    }

    private void FadeOut()
    {
        _hideTimer.Stop();
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(400));
        anim.Completed += (_, _) => { Hide(); try { Hidden?.Invoke(); } catch { } };
        BeginAnimation(OpacityProperty, anim);
    }

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }
}
