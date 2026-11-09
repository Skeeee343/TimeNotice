using System.Windows;
using System.Windows.Controls;

namespace TimeNotice;

public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private readonly Action _onSaved;
    private readonly Func<string, string> _gameDisplay; // App's display resolver (user mapping + catalog + Title-case)
    private readonly Func<string>? _detectStatus; // App's live detector readout (issue #9), null when unavailable
    private readonly Func<string?>? _lastFocus; // App's last non-self focus, shown before classifying (#11)
    private readonly Func<string, string>? _classifyFocus; // App's classify-last-focus, returns a receipt line
    private readonly Action<string>? _testPopup; // App's test-popup path: general / game / confirm
    private readonly HashSet<string> _openedGames; // entries present when the window opened (confirm adds merge around these)
    private readonly HashSet<string> _openedIgnore;
    private readonly HashSet<string> _openedQuiet; // same merge rule as games/ignore: classify-to-quiet while open must survive Save
    private bool _syncing; // programmatic box refresh (live sync / post-save) must not mark the form dirty
    private bool _dirty; // any control edited since open or last save
    private bool _explicitCancel; // Cancel button means discard: skip the unsaved-changes prompt
    private readonly System.Windows.Threading.DispatcherTimer _previewTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _previewOpenedUtc = DateTime.UtcNow;

    public SettingsWindow(Settings settings, Action onSaved, Func<string, string> gameDisplay, Func<string>? detectStatus = null, Func<string?>? lastFocus = null, Func<string, string>? classifyFocus = null, Action<string>? testPopup = null)
    {
        InitializeComponent();
        Icon = AppIcon.CreateWindowIcon();
        try { VersionText.Text = $"v{System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?"}"; } catch { }
        _settings = settings;
        _onSaved = onSaved;
        _gameDisplay = gameDisplay;
        _detectStatus = detectStatus;
        _lastFocus = lastFocus;
        _classifyFocus = classifyFocus;
        _testPopup = testPopup;
        GeneralEnabledBox.IsChecked = _settings.GeneralEnabled;
        StartupBox.IsChecked = _settings.StartWithWindows;
        IntervalBox.Text = _settings.GeneralMinutes.ToString();
        AlignBox.IsChecked = _settings.AlignToClock;
        WindowOnlyBox.IsChecked = _settings.WindowOnly;
        WindowStartBox.Text = _settings.WindowStart;
        WindowEndBox.Text = _settings.WindowEnd;
        GameEnabledBox.IsChecked = _settings.GameEnabled;
        GameIntervalBox.Text = _settings.GameMinutes.ToString();
        CountBox.IsChecked = _settings.CountWhenRunning;
        GameBox.Text = string.Join("\n", _settings.GameExes);
        IgnoreBox.Text = string.Join("\n", _settings.IgnoreExes);
        GameWindowOnlyBox.IsChecked = _settings.GameWindowOnly;
        GameWindowStartBox.Text = _settings.GameWindowStart;
        GameWindowEndBox.Text = _settings.GameWindowEnd;
        DisplayBox.Text = _settings.DisplaySeconds.ToString();
        ShowNextBox.IsChecked = _settings.ShowSession;
        MainTemplateBox.Text = _settings.MainTemplate;
        SubTemplateBox.Text = _settings.SubTemplate;
        QuietBox.Text = string.Join("\n", _settings.QuietModeExes);
        GameMainBox.Text = _settings.GameMainTemplate ?? "";
        GameSubBox.Text = _settings.GameSubTemplate ?? "";
        GameNotifyBox.IsChecked = _settings.GameNotifyOnDetect;
        GameDetectTemplateBox.Text = _settings.GameDetectTemplate;
        GameDetectSubBox.Text = _settings.GameDetectSubTemplate ?? "";
        HeurEnabledBox.IsChecked = _settings.HeuristicEnabled;
        HeurStartBox.Text = _settings.HeuristicStartSeconds.ToString();
        HeurSuspectBox.Text = _settings.HeuristicSuspectSeconds.ToString();
        HeurInactiveBox.Text = _settings.HeuristicInactiveMinutes.ToString();
        HeurGpuBox.Text = _settings.HeuristicGpuPercent.ToString();
        SelectTag(PositionBox, _settings.Position, "BottomRight");
        SelectTag(ThemeBox, _settings.ThemeMode, "Dark");
        BgBox.Text = _settings.CustomBackground;
        TextColorBox.Text = _settings.CustomText;
        SubColorBox.Text = _settings.CustomSubText;
        ConfirmTimeoutBox.Text = _settings.ConfirmTimeoutSeconds.ToString();
        SelectTag(ConfirmActionBox, _settings.ConfirmTimeoutAction, "Accept");
        _openedGames = new HashSet<string>(_settings.GameExes.Select(Settings.ExeKey), StringComparer.OrdinalIgnoreCase);
        _openedIgnore = new HashSet<string>(_settings.IgnoreExes.Select(Settings.ExeKey), StringComparer.OrdinalIgnoreCase);
        _openedQuiet = new HashSet<string>(_settings.QuietModeExes.Select(Settings.ExeKey), StringComparer.OrdinalIgnoreCase);
        OpacitySlider.Value = Math.Clamp(_settings.Opacity * 100, 20, 100);
        OpacityLabel.Text = $"{(int)OpacitySlider.Value}%";
        OpacitySlider.ValueChanged += (_, e) => { OpacityLabel.Text = $"{(int)e.NewValue}%"; };
        ShowBoxBox.IsChecked = _settings.ShowBox;
        TextOpacitySlider.Value = Math.Clamp(_settings.TextOpacity * 100, 20, 100);
        TextOpacityLabel.Text = $"{(int)TextOpacitySlider.Value}%";
        TextOpacitySlider.ValueChanged += (_, e) => { TextOpacityLabel.Text = $"{(int)e.NewValue}%"; };
        ThemeBox.SelectionChanged += (_, _) => { CustomColorPanel.Visibility = SelectedTag(ThemeBox) == "Custom" ? Visibility.Visible : Visibility.Collapsed; };
        CustomColorPanel.Visibility = SelectedTag(ThemeBox) == "Custom" ? Visibility.Visible : Visibility.Collapsed;
        IntervalBox.TextChanged += (_, _) => UpdateWarning();
        GameIntervalBox.TextChanged += (_, _) => UpdateWarning();
        DisplayBox.TextChanged += (_, _) => UpdateWarning();
        GeneralEnabledBox.Checked += (_, _) => UpdateWarning();
        GeneralEnabledBox.Unchecked += (_, _) => UpdateWarning();
        GameEnabledBox.Checked += (_, _) => UpdateWarning();
        GameEnabledBox.Unchecked += (_, _) => UpdateWarning();
        AlignBox.Checked += (_, _) => UpdateWarning();
        AlignBox.Unchecked += (_, _) => UpdateWarning();
        WindowOnlyBox.Checked += (_, _) => { GeneralTimePanel.Visibility = Visibility.Visible; };
        WindowOnlyBox.Unchecked += (_, _) => { GeneralTimePanel.Visibility = Visibility.Collapsed; };
        GameWindowOnlyBox.Checked += (_, _) => { GameTimePanel.Visibility = Visibility.Visible; };
        GameWindowOnlyBox.Unchecked += (_, _) => { GameTimePanel.Visibility = Visibility.Collapsed; };
        GeneralTimePanel.Visibility = WindowOnlyBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        GameTimePanel.Visibility = GameWindowOnlyBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateWarning();
        RefreshPreviews(); // Previews tab: live from the first frame
        RefreshDetectStatus(); // Detect tab: what the detector sees right now
        _previewTimer.Tick += (_, _) => RefreshPreviews();
        var detectTicks = 0; // Detect readout re-ranks GPU processes: every 5th second is enough
        _previewTimer.Tick += (_, _) => { if (++detectTicks % 5 == 0) RefreshDetectStatus(); };
        _previewTimer.Tick += (_, _) => SyncListBoxesFromLive();
        _previewTimer.Start();
        // Dirty tracking attaches last so initialization never marks the form edited.
        foreach (var box in new System.Windows.Controls.TextBox[] { IntervalBox, WindowStartBox, WindowEndBox, GameIntervalBox, GameBox, IgnoreBox, GameWindowStartBox, GameWindowEndBox, DisplayBox, MainTemplateBox, SubTemplateBox, GameMainBox, GameSubBox, GameDetectTemplateBox, GameDetectSubBox, QuietBox, BgBox, TextColorBox, SubColorBox, ConfirmTimeoutBox, HeurStartBox, HeurSuspectBox, HeurInactiveBox, HeurGpuBox })
            box.TextChanged += (_, _) => MarkDirty();
        foreach (var chk in new System.Windows.Controls.CheckBox[] { GeneralEnabledBox, StartupBox, WindowOnlyBox, GameEnabledBox, CountBox, GameWindowOnlyBox, ShowNextBox, GameNotifyBox, HeurEnabledBox, AlignBox, ShowBoxBox })
        {
            chk.Checked += (_, _) => MarkDirty();
            chk.Unchecked += (_, _) => MarkDirty();
        }
        PositionBox.SelectionChanged += (_, _) => MarkDirty();
        ThemeBox.SelectionChanged += (_, _) => MarkDirty();
        ConfirmActionBox.SelectionChanged += (_, _) => MarkDirty();
        OpacitySlider.ValueChanged += (_, _) => MarkDirty();
        TextOpacitySlider.ValueChanged += (_, _) => MarkDirty();
        UpdateSaveState(); // fresh open = no pending edits, Save dimmed
        Closing += OnClosing;
    }

    private void MarkDirty()
    {
        if (_syncing) return; // live sync / post-save refresh is not a user edit
        _dirty = true;
        ErrorText.Text = "";
        UpdateSaveState();
    }

    private void UpdateSaveState()
    {
        SaveButton.IsEnabled = _dirty;
        SaveButton.Opacity = _dirty ? 1.0 : 0.5;
    }

    // The old flow auto-saved, so a bare close discarding edits surprises. Prompt instead.
    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try { _previewTimer.Stop(); } catch { }
        if (_explicitCancel || !_dirty) return;
        var answer = System.Windows.MessageBox.Show("Save your changes?", "TimeNotice Settings",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) { e.Cancel = true; return; }
        if (answer == MessageBoxResult.Yes)
        {
            Save_Click(this, new RoutedEventArgs());
            if (_dirty) e.Cancel = true; // validation failed: stay open on the error
        }
    }

    private static string SelectedTag(System.Windows.Controls.ComboBox box)
    {
        return (box.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";
    }

    private static void SelectTag(System.Windows.Controls.ComboBox box, string? value, string fallback)
    {
        var want = (value ?? fallback).Trim();
        foreach (var obj in box.Items)
        {
            if (obj is ComboBoxItem item && string.Equals(item.Tag?.ToString(), want, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
        foreach (var obj in box.Items)
        {
            if (obj is ComboBoxItem item && string.Equals(item.Tag?.ToString(), fallback, StringComparison.OrdinalIgnoreCase))
            {
                box.SelectedItem = item;
                return;
            }
        }
    }

    private static bool IsColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        try { System.Windows.Media.ColorConverter.ConvertFromString(text.Trim()); return true; }
        catch { return false; }
    }

    private void UpdateWarning()
    {
        WarnText.Text = "";
        var okSecs = int.TryParse(DisplayBox.Text.Trim(), out var secs) ? secs : 10;
        var need = okSecs + 2; // box time + queue gap, seconds
        if (GeneralEnabledBox.IsChecked == true
            && int.TryParse(IntervalBox.Text.Trim(), out var mins)
            && mins * 60 <= need)
        {
            WarnText.Text += $"General: every {mins} min with a {okSecs}s box will overlap constantly. ";
        }
        if (GameEnabledBox.IsChecked == true
            && int.TryParse(GameIntervalBox.Text.Trim(), out var gmin)
            && gmin * 60 <= need)
        {
            WarnText.Text += $"Game: every {gmin} min with a {okSecs}s box will overlap constantly. ";
        }
        if (WarnText.Text.Length > 0)
        {
            WarnText.Text += "Popups will queue back-to-back (see debug.log).";
        }
        // Hourly-grid note lives under the Align box. Above 60 min the grid is
        // meaningless (every boundary collapses to :00), so Align disables itself.
        // Otherwise, while Align is on, warn when the interval doesn't split 60 evenly.
        var parsedMins = int.TryParse(IntervalBox.Text.Trim(), out var alignMins) && alignMins >= 1;
        var overHour = parsedMins && alignMins > 60;
        AlignBox.IsEnabled = !overHour;
        if (overHour && AlignBox.IsChecked == true) AlignBox.IsChecked = false;
        AlignWarnText.Visibility = Visibility.Collapsed;
        AlignWarnText.Text = "";
        if (overHour)
        {
            AlignWarnText.Text = "Align works up to 60 min — above that reminders count from launch.";
            AlignWarnText.Visibility = Visibility.Visible;
        }
        else if (AlignBox.IsChecked == true && parsedMins && 60 % alignMins != 0)
        {
            var marks = new List<string>();
            for (var m = 0; m < 60; m += alignMins) marks.Add($":{m:D2}");
            AlignWarnText.Text = $"{alignMins} min doesn't split the hour evenly — alerts at {string.Join(", ", marks)}, then a short gap to :00.";
            AlignWarnText.Visibility = Visibility.Visible;
        }
    }

    // Detect tab (issue #9): live detector readout. Read-only label, never
    // touches the scheduler: opening Settings can itself steal focus, and the
    // App text says so instead of showing a misleading exe.
    private void RefreshDetectStatus()
    {
        try { DetectStatusText.Text = _detectStatus?.Invoke() ?? "Status unavailable."; }
        catch { }
        try
        {
            var exe = _lastFocus?.Invoke();
            ClassifyTargetText.Text = string.IsNullOrWhiteSpace(exe) ? "none yet — focus an app first" : exe;
        }
        catch { }
    }

    // Live list sync: entries added elsewhere while open (confirm prompt,
    // Detect-tab classify) appear in the boxes within a second. Append-only:
    // lines the user deleted stay deleted (box wins), user edits untouched.
    private void SyncListBoxesFromLive()
    {
        try
        {
            _syncing = true;
            AppendMissing(GameBox, _settings.GameExes, _openedGames);
            AppendMissing(IgnoreBox, _settings.IgnoreExes, _openedIgnore);
            AppendMissing(QuietBox, _settings.QuietModeExes, _openedQuiet);
        }
        catch { }
        finally { _syncing = false; }
    }

    private static void AppendMissing(System.Windows.Controls.TextBox box, List<string> live, HashSet<string> opened)
    {
        var boxKeys = new HashSet<string>(
            box.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(Settings.ExeKey),
            StringComparer.OrdinalIgnoreCase);
        foreach (var e in live)
        {
            var k = Settings.ExeKey(e);
            if (!opened.Contains(k) && !boxKeys.Contains(k))
            {
                if (box.Text.Length > 0 && !box.Text.EndsWith("\n")) box.AppendText("\n");
                box.AppendText(e.Trim());
                boxKeys.Add(k);
                opened.Add(k); // seen now: a later user delete is respected by MergeBoxLines
            }
        }
    }

    // Classify buttons (#11): target exe is shown above, receipt below. Saved
    // immediately by App; a later Save merges the new line (MergeBoxLines).
    private void ClassifyGames_Click(object sender, RoutedEventArgs e) => Classify("games");
    private void ClassifyQuiet_Click(object sender, RoutedEventArgs e) => Classify("quiet");
    private void ClassifyIgnored_Click(object sender, RoutedEventArgs e) => Classify("ignored");

    private void Classify(string target)
    {
        try { ClassifyReceiptText.Text = _classifyFocus?.Invoke(target) ?? "Classify unavailable."; }
        catch { }
    }

    // Gaming-PC troubleshooting (issue #10): one click copies version +
    // detector readout for paste-back. No dev tools needed on that machine.
    private void CopyDebug_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            System.Windows.Clipboard.SetText($"TimeNotice v{ver}\n{DetectStatusText.Text}");
            CopyReceiptText.Text = "Copied — paste it back to me.";
        }
        catch { CopyReceiptText.Text = "Copy failed — screenshot the text above instead."; }
    }

    private void TestGeneral_Click(object sender, RoutedEventArgs e) => Test("general");
    private void TestGame_Click(object sender, RoutedEventArgs e) => Test("game");
    private void TestConfirm_Click(object sender, RoutedEventArgs e) => Test("confirm");

    private void Test(string kind)
    {
        try { _testPopup?.Invoke(kind); }
        catch { }
    }

    // Beta-debug log helpers (#11): moved here from the tray so Exit stays visible.
    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Settings.Dir);
            System.Diagnostics.Process.Start("explorer.exe", Settings.Dir);
        }
        catch { }
    }

    private void ClearLog_Click(object sender, RoutedEventArgs e)
    {
        try { Logger.Clear(); }
        catch { }
    }

    private static int LiveInt(string? text, int fallback)
    {
        return int.TryParse((text ?? "").Trim(), out var v) ? v : fallback;
    }

    // Previews tab: live read-only samples of every popup, expanded exactly like
    // App.ShowTest/ShowDetect/ConfirmPrompt so what you see is what will fire.
    // Reads the live boxes (unsaved edits included). Placeholders by design, kept
    // to 3: game name (first live Games line), elapsed (counts from window open),
    // Align anchor (real popup anchors on its last fire when still ahead).
    private void RefreshPreviews()
    {
        try
        {
            var nowLocal = DateTime.Now;
            var nowStr = nowLocal.ToString("h:mm tt");
            var gmins = Math.Max(1, LiveInt(IntervalBox.Text, _settings.GeneralMinutes));
            var ymins = Math.Max(1, LiveInt(GameIntervalBox.Text, _settings.GameMinutes));
            var align = AlignBox.IsChecked == true;
            var showSub = ShowNextBox.IsChecked == true;
            // Preview mirrors ShowTest: Align is general-only, and the shown
            // wait is the true gap to the coming boundary. The real popup also
            // anchors on its last fire when still ahead; Settings keeps no such
            // clock, so the coming boundary stands in.
            var gNext = align ? App.NextBoundary(nowLocal, gmins) : nowLocal.AddMinutes(gmins);
            var gShow = align ? Math.Max(1, (int)Math.Ceiling((gNext - nowLocal).TotalMinutes)) : gmins;
            var nextStr = gNext.ToString("h:mm tt");
            var gameNext = nowLocal.AddMinutes(ymins).ToString("h:mm tt");
            var elapsed = App.FormatElapsed(DateTime.UtcNow - _previewOpenedUtc); // sample session clock
            // Placeholder name from the live Games box; the real popup names the
            // focused/session game, the manual test always uses Test game.
            string exe = "newgame.exe";
            foreach (var line in (GameBox.Text ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var text = line ?? "";
                var eq = text.IndexOf('=');
                var e = (eq < 0 ? text : text[..eq]).Trim();
                if (e.Length > 0) { exe = e; break; }
            }
            if (exe == "newgame.exe")
            {
                foreach (var line in _settings.GameExes)
                {
                    var text = line ?? "";
                    var eq = text.IndexOf('=');
                    var e = (eq < 0 ? text : text[..eq]).Trim();
                    if (e.Length > 0) { exe = e; break; }
                }
            }
            var display = _gameDisplay(exe);
            var timeout = Math.Clamp(LiveInt(ConfirmTimeoutBox.Text, _settings.ConfirmTimeoutSeconds), 5, 120);
            var timeoutAction = SelectedTag(ConfirmActionBox);
            if (string.IsNullOrWhiteSpace(timeoutAction)) timeoutAction = _settings.ConfirmTimeoutAction;
            var gMain = (MainTemplateBox.Text ?? "").Replace("{time}", nowStr).Replace("{next}", nextStr).Replace("{game}", "").Replace("{elapsed}", "").Replace("{interval}", gShow.ToString());
            if (string.IsNullOrWhiteSpace(gMain)) gMain = nowStr; // real popup shows the time string, never blank
            var gSub = showSub ? (SubTemplateBox.Text ?? "").Replace("{time}", nowStr).Replace("{next}", nextStr).Replace("{game}", "").Replace("{elapsed}", "").Replace("{interval}", gShow.ToString()) : "";
            SetPreview(PvGeneralBorder, PvGeneralMain, PvGeneralSub, gMain, gSub);
            var gm = string.IsNullOrWhiteSpace(GameMainBox.Text) ? Settings.DefaultGameMainTemplate : GameMainBox.Text;
            var gs = string.IsNullOrWhiteSpace(GameSubBox.Text) ? Settings.DefaultGameSubTemplate : GameSubBox.Text;
            var gameMain = gm.Replace("{time}", nowStr).Replace("{next}", gameNext).Replace("{game}", display).Replace("{elapsed}", elapsed).Replace("{interval}", ymins.ToString());
            if (string.IsNullOrWhiteSpace(gameMain)) gameMain = nowStr; // same blank rule as the real popup
            var gameSub = showSub ? gs.Replace("{time}", nowStr).Replace("{next}", gameNext).Replace("{game}", display).Replace("{elapsed}", elapsed).Replace("{interval}", ymins.ToString()) : "";
            SetPreview(PvGameBorder, PvGameMain, PvGameSub, gameMain, gameSub);
            var dMain = (GameDetectTemplateBox.Text ?? "").Replace("{game}", display).Replace("{interval}", ymins.ToString()).Replace("{time}", nowStr);
            if (string.IsNullOrWhiteSpace(dMain)) dMain = $"{display} detected";
            SetPreview(PvDetectBorder, PvDetectMain, PvDetectSub, dMain,
                showSub ? (GameDetectSubBox.Text ?? "").Replace("{game}", display).Replace("{interval}", ymins.ToString()).Replace("{time}", nowStr) : "");
            var cMain = $"{display} Detected";
            var cExe = display.Equals(exe, StringComparison.OrdinalIgnoreCase) ? "" : $"Process {exe}";
            var secs = timeout; // static sample: no reason to burn a ticking countdown in a preview
            var cSub = timeoutAction.Equals("Reject", StringComparison.OrdinalIgnoreCase)
                ? $"Ctrl+Alt+Z — Accept\nCtrl+Alt+X — Reject (auto in {secs}s)"
                : $"Ctrl+Alt+Z — Accept (auto in {secs}s)\nCtrl+Alt+X — Reject";
            SetPreview(PvConfirmBorder, PvConfirmMain, PvConfirmSub, cMain, cSub, cExe, PvConfirmExe);
            SetPreview(PvAcceptBorder, PvAcceptMain, PvAcceptSub,
                $"{display} added to Games list.", $"Current time alert interval is {ymins} min.");
            SetPreview(PvRejectBorder, PvRejectMain, PvRejectSub,
                $"{display} is ignored.", "No time alerts while on the ignored list.");
            // Inline sample inside Popup tab: same General text but live from the boxes being edited.
            var liveMain = (MainTemplateBox.Text ?? "").Replace("{time}", nowStr).Replace("{next}", nextStr).Replace("{game}", "").Replace("{elapsed}", "").Replace("{interval}", gShow.ToString());
            var liveSub = (ShowNextBox.IsChecked == true ? (SubTemplateBox.Text ?? "") : "").Replace("{time}", nowStr).Replace("{next}", nextStr).Replace("{game}", "").Replace("{elapsed}", "").Replace("{interval}", gShow.ToString());
            SetPreview(PvInlineBorder, PvInlineMain, PvInlineSub, liveMain, liveSub);
        }
        catch { }
    }

    private void SetPreview(System.Windows.Controls.Border border, System.Windows.Controls.TextBlock main, System.Windows.Controls.TextBlock sub, string mainText, string subText, string exeText = "", System.Windows.Controls.TextBlock? exeBlock = null)
    {
        main.Text = mainText; // blank mains fall back at the call site (real popup shows the time string)
        sub.Text = subText;
        // Intentional: collapse keeps the preview card tight; the real popup sets
        // empty text instead, which renders the same (no sub line, no hole).
        sub.Visibility = subText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (exeBlock is not null)
        {
            exeBlock.Text = exeText;
            exeBlock.Visibility = exeText.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        var mode = SelectedTag(ThemeBox);
        if (string.IsNullOrWhiteSpace(mode)) mode = (_settings.ThemeMode ?? "Dark").Trim();
        if (mode.Equals("MatchHost", StringComparison.OrdinalIgnoreCase)) mode = OverlayWindow.IsHostLight() ? "Light" : "Dark"; // preview resolves like the real popup
        string tbg = "#CC1E1E1E", tfg = "#FFFFFFFF", tsg = "#FFBBBBBB";
        if (mode.Equals("Light", StringComparison.OrdinalIgnoreCase)) { tbg = "#FFFFFFFF"; tfg = "#FF111116"; tsg = "#FF5B5B66"; }
        else if (mode.Equals("Custom", StringComparison.OrdinalIgnoreCase)) { tbg = BgBox.Text; tfg = TextColorBox.Text; tsg = SubColorBox.Text; }
        border.Background = new System.Windows.Media.SolidColorBrush(OverlayWindow.ParseColor(tbg, "#CC1E1E1E"));
        // White preview fill on the near-white Settings page: hairline shows the box edge.
        if (mode.Equals("Light", StringComparison.OrdinalIgnoreCase))
        {
            border.BorderBrush = new System.Windows.Media.SolidColorBrush(OverlayWindow.ParseColor("#FFD5D5DC", "#FFD5D5DC"));
            border.BorderThickness = new Thickness(1);
        }
        else border.BorderThickness = new Thickness(0);
        main.Foreground = new System.Windows.Media.SolidColorBrush(OverlayWindow.ParseColor(tfg, "#FFFFFFFF"));
        sub.Foreground = new System.Windows.Media.SolidColorBrush(OverlayWindow.ParseColor(tsg, "#FFBBBBBB"));
        if (exeBlock is not null) exeBlock.Foreground = sub.Foreground;
        // Preview reads the live sliders so box/text fade separately, like the real popup.
        var boxOp = OpacitySlider.Value / 100.0;
        if (boxOp <= 0) boxOp = _settings.Opacity;
        boxOp = Math.Clamp(boxOp, 0.2, 1.0);
        var boxOff = ShowBoxBox.IsChecked == false;
        if (boxOff)
        {
            border.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Transparent);
            border.Opacity = 1.0;
        }
        else if (boxOp >= 1.0)
        {
            // 100% means fully blocked out, like the real popup.
            var solid = OverlayWindow.ParseColor(tbg, "#CC1E1E1E"); solid.A = 255;
            border.Background = new System.Windows.Media.SolidColorBrush(solid);
            border.Opacity = 1.0;
        }
        else border.Opacity = boxOp;
        var textOp = TextOpacitySlider.Value / 100.0;
        if (textOp <= 0) textOp = _settings.TextOpacity;
        textOp = Math.Clamp(textOp, 0.2, 1.0);
        main.Opacity = textOp;
        sub.Opacity = textOp;
        if (exeBlock is not null) exeBlock.Opacity = textOp;
    }

    private void PickBg_Click(object sender, RoutedEventArgs e) => PickIntoBox(BgBox);
    private void PickText_Click(object sender, RoutedEventArgs e) => PickIntoBox(TextColorBox);
    private void PickSub_Click(object sender, RoutedEventArgs e) => PickIntoBox(SubColorBox);

    // WinForms dialog has no alpha picker: keep the existing #AA prefix, replace only RRGGBB.
    private void PickIntoBox(System.Windows.Controls.TextBox box)
    {
        try
        {
            var current = (box.Text ?? "").Trim();
            string alpha = "FF";
            if (current.StartsWith("#") && current.Length == 9) alpha = current.Substring(1, 2); // #AARRGGBB
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true };
            try
            {
                var c = OverlayWindow.ParseColor(current, "#FFFFFFFF");
                dlg.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
            }
            catch { }
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var picked = $"{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            box.Text = current.StartsWith("#") && current.Length == 7 ? $"#{picked}" : $"#{alpha}{picked}"; // keep #RRGGBB style if they used it
        }
        catch { }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Text = "";
        ErrorText.Foreground = System.Windows.Media.Brushes.DarkRed;
        if (!int.TryParse(IntervalBox.Text.Trim(), out var mins) || mins < 1 || mins > 480)
        {
            ErrorText.Text = "General minutes must be 1 to 480.";
            return;
        }
        if (!int.TryParse(GameIntervalBox.Text.Trim(), out var gmin) || gmin < 1 || gmin > 480)
        {
            ErrorText.Text = "Game minutes must be 1 to 480.";
            return;
        }
        if (!int.TryParse(DisplayBox.Text.Trim(), out var secs) || secs < 1 || secs > 60)
        {
            ErrorText.Text = "Seconds must be 1 to 60.";
            return;
        }
        if (!int.TryParse(ConfirmTimeoutBox.Text.Trim(), out var confirmSecs) || confirmSecs < 5 || confirmSecs > 120)
        {
            ErrorText.Text = "Confirm wait must be 5 to 120 seconds.";
            return;
        }
        var confirmAction = SelectedTag(ConfirmActionBox);
        if (confirmAction is not ("Accept" or "Reject")) confirmAction = "Accept";
        if (!TimeSpan.TryParse(WindowStartBox.Text.Trim(), out _))
        {
            ErrorText.Text = "From time must look like 09:00.";
            return;
        }
        if (!TimeSpan.TryParse(WindowEndBox.Text.Trim(), out _))
        {
            ErrorText.Text = "To time must look like 23:00.";
            return;
        }
        if (GameWindowOnlyBox.IsChecked == true)
        {
            if (!TimeSpan.TryParse(GameWindowStartBox.Text.Trim(), out _))
            {
                ErrorText.Text = "Game from time must look like 09:00.";
                return;
            }
            if (!TimeSpan.TryParse(GameWindowEndBox.Text.Trim(), out _))
            {
                ErrorText.Text = "Game to time must look like 23:00.";
                return;
            }
        }
        if (string.IsNullOrWhiteSpace(MainTemplateBox.Text))
        {
            ErrorText.Text = "Main line cannot be empty. Use {time}.";
            return;
        }
        foreach (var rawLine in GameBox.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var eq = line.IndexOf('=');
            if ((eq < 0 ? line : line[..eq]).Trim().Length == 0)
            {
                ErrorText.Text = "Each game line needs an exe, e.g. spotify.exe.";
                return;
            }
        }
        if (GameNotifyBox.IsChecked == true && string.IsNullOrWhiteSpace(GameDetectTemplateBox.Text))
        {
            ErrorText.Text = "Detection line cannot be empty when detection pops are on.";
            return;
        }
        if (!int.TryParse(HeurStartBox.Text.Trim(), out var heurStart) || heurStart < 10 || heurStart > 600)
        {
            ErrorText.Text = "Heuristic start must be 10 to 600 seconds.";
            return;
        }
        if (!int.TryParse(HeurSuspectBox.Text.Trim(), out var heurSuspect) || heurSuspect < 5 || heurSuspect > 300)
        {
            ErrorText.Text = "Heuristic suspect must be 5 to 300 seconds.";
            return;
        }
        if (!int.TryParse(HeurInactiveBox.Text.Trim(), out var heurInactive) || heurInactive < 1 || heurInactive > 60)
        {
            ErrorText.Text = "Heuristic close must be 1 to 60 minutes.";
            return;
        }
        if (!int.TryParse(HeurGpuBox.Text.Trim(), out var heurGpu) || heurGpu < 1 || heurGpu > 100)
        {
            ErrorText.Text = "Heuristic GPU % must be 1 to 100.";
            return;
        }
        var position = SelectedTag(PositionBox);
        if (position is not ("TopLeft" or "TopRight" or "BottomLeft" or "BottomRight"))
        {
            ErrorText.Text = "Pick a popup corner.";
            return;
        }
        var theme = SelectedTag(ThemeBox);
        if (theme is not ("Dark" or "Light" or "MatchHost" or "Custom"))
        {
            ErrorText.Text = "Pick a theme.";
            return;
        }
        if (theme == "Custom")
        {
            if (!IsColor(BgBox.Text)) { ErrorText.Text = "Background color must look like #CC1E1E1E."; return; }
            if (!IsColor(TextColorBox.Text)) { ErrorText.Text = "Main text color must look like #FFFFFFFF."; return; }
            if (!IsColor(SubColorBox.Text)) { ErrorText.Text = "Sub text color must look like #FFBBBBBB."; return; }
        }
        _settings.GeneralEnabled = GeneralEnabledBox.IsChecked == true;
        _settings.StartWithWindows = StartupBox.IsChecked == true;
        _settings.GeneralMinutes = mins;
        _settings.AlignToClock = AlignBox.IsChecked == true;
        _settings.ReminderMinutes = mins; // keep legacy in sync
        _settings.WindowOnly = WindowOnlyBox.IsChecked == true;
        _settings.WindowStart = WindowStartBox.Text.Trim();
        _settings.WindowEnd = WindowEndBox.Text.Trim();
        _settings.GameEnabled = GameEnabledBox.IsChecked == true;
        _settings.GameMinutes = gmin;
        _settings.CountWhenRunning = CountBox.IsChecked == true;
        _settings.GameExes = MergeBoxLines(GameBox.Text, _settings.GameExes, _openedGames);
        _settings.IgnoreExes = MergeBoxLines(IgnoreBox.Text, _settings.IgnoreExes, _openedIgnore);
        _settings.GameWindowOnly = GameWindowOnlyBox.IsChecked == true;
        _settings.GameWindowStart = GameWindowStartBox.Text.Trim();
        _settings.GameWindowEnd = GameWindowEndBox.Text.Trim();
        _settings.DisplaySeconds = secs;
        _settings.ShowSession = ShowNextBox.IsChecked == true;
        _settings.MainTemplate = MainTemplateBox.Text.Trim();
        _settings.SubTemplate = SubTemplateBox.Text.Trim();
        _settings.GameMainTemplate = GameMainBox.Text.Trim();
        _settings.GameSubTemplate = GameSubBox.Text.Trim();
        _settings.GameNotifyOnDetect = GameNotifyBox.IsChecked == true;
        _settings.GameDetectTemplate = GameDetectTemplateBox.Text.Trim();
        _settings.GameDetectSubTemplate = GameDetectSubBox.Text.Trim();
        _settings.HeuristicEnabled = HeurEnabledBox.IsChecked == true;
        _settings.HeuristicStartSeconds = heurStart;
        _settings.HeuristicSuspectSeconds = heurSuspect;
        _settings.HeuristicInactiveMinutes = heurInactive;
        _settings.HeuristicGpuPercent = heurGpu;
        _settings.Position = position;
        _settings.ThemeMode = theme;
        _settings.CustomBackground = BgBox.Text.Trim();
        _settings.CustomText = TextColorBox.Text.Trim();
        _settings.CustomSubText = SubColorBox.Text.Trim();
        _settings.Opacity = Math.Clamp(OpacitySlider.Value / 100.0, 0.2, 1.0);
        _settings.ShowBox = ShowBoxBox.IsChecked == true;
        _settings.TextOpacity = Math.Clamp(TextOpacitySlider.Value / 100.0, 0.2, 1.0);
        _settings.QuietModeExes = MergeBoxLines(QuietBox.Text, _settings.QuietModeExes, _openedQuiet);
        _settings.ConfirmTimeoutSeconds = confirmSecs;
        _settings.ConfirmTimeoutAction = confirmAction;
        _settings.Normalize(); // clamp, sanitize, dedupe after every save
        _settings.Save();
        _onSaved();
        _dirty = false;
        UpdateSaveState();
        ErrorText.Foreground = System.Windows.Media.Brushes.Green;
        ErrorText.Text = $"Saved {DateTime.Now:h:mm:ss tt}.";
        // Stay open: the window is a workspace, not a dialog. Show exactly what
        // was saved (merge + Normalize may have added/cleaned lines) and
        // re-snapshot the merge baseline so a second save treats these as home.
        try
        {
            _syncing = true;
            GameBox.Text = string.Join("\n", _settings.GameExes);
            IgnoreBox.Text = string.Join("\n", _settings.IgnoreExes);
            QuietBox.Text = string.Join("\n", _settings.QuietModeExes);
        }
        finally { _syncing = false; }
        _openedGames.Clear();
        foreach (var k in _settings.GameExes.Select(Settings.ExeKey)) _openedGames.Add(k);
        _openedIgnore.Clear();
        foreach (var k in _settings.IgnoreExes.Select(Settings.ExeKey)) _openedIgnore.Add(k);
        _openedQuiet.Clear();
        foreach (var k in _settings.QuietModeExes.Select(Settings.ExeKey)) _openedQuiet.Add(k);
    }

    // Box lines win, except entries added elsewhere while the window was open
    // (e.g. a confirm prompt): those merge in. Lines the user deleted stay deleted.
    private static List<string> MergeBoxLines(string boxText, List<string> live, HashSet<string> opened)
    {
        var box = boxText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        foreach (var e in live)
        {
            if (!opened.Contains(Settings.ExeKey(e))
                && !box.Any(b => string.Equals(Settings.ExeKey(b), Settings.ExeKey(e), StringComparison.OrdinalIgnoreCase)))
                box.Add(e.Trim());
        }
        return box;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _explicitCancel = true; // intentional discard: no prompt
        Close();
    }
}
