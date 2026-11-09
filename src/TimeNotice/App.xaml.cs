using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace TimeNotice;

public partial class App : System.Windows.Application
{
    private static Mutex? _singleInstance;
    private WinForms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private System.Drawing.Icon? _trayIconDim; // cached once: dimmed face while suppressed, no per-tick handles
    private OverlayWindow? _overlay;
    private Settings _settings = new();
    private DispatcherTimer? _scheduler;
    private SettingsWindow? _settingsWindow;
    private ConfirmPrompt? _confirm;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Single instance: second launch exits silently (tray already running).
        try { _singleInstance = new Mutex(true, @"Local\TimeNotice-single", out var owned); 
        if (!owned) { Shutdown(); return; } 
        } catch { /* fail-open: single user, second copy just runs */ }
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.Log($"src=crash ui ex={args.Exception.GetType().Name} msg={args.Exception.Message}");
            args.Handled = true; // stay alive: tray app must not vanish on one bad tick
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Logger.Log($"src=crash bg ex={args.ExceptionObject?.GetType().Name}");
        };
        _settings = Settings.Load();
        try
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
            Logger.Log($"src=startup ver=v{ver} os={Environment.OSVersion.VersionString} dir={Settings.Dir}");
        }
        catch { Logger.Log($"Unknown error has occured");}
        ApplyStartup();
        _overlay = new OverlayWindow(_settings);
        _overlay.Hidden += OnOverlayHidden;

        // ponytail: shared AppIcon helper; a .ico file replaces this if the Explorer exe icon needs branding too
        _trayIcon = AppIcon.CreateTrayIcon(dimmed: false);
        _trayIconDim = AppIcon.CreateTrayIcon(dimmed: true);
        _tray = new WinForms.NotifyIcon
        {
            Icon = _trayIcon,
            Visible = true,
            Text = "TimeNotice"
        };
        _tray.ContextMenuStrip = new WinForms.ContextMenuStrip();
        // Status first, then act, then exit. Everything else lives in Settings (#11).
        var statusItem = new WinForms.ToolStripMenuItem("TimeNotice") { Enabled = false };
        _tray.ContextMenuStrip.Items.Add(statusItem);
        var pauseItem = new WinForms.ToolStripMenuItem("Pause") { Checked = _settings.PauseAll, CheckOnClick = true };
        pauseItem.CheckedChanged += (_, _) =>
        {
            _settings.PauseAll = pauseItem.Checked;
            _settings.Save();
        };
        _tray.ContextMenuStrip.Items.Add(pauseItem);
        _tray.ContextMenuStrip.Items.Add("Settings…", null, (_, _) => OpenSettings());
        _tray.ContextMenuStrip.Items.Add(new WinForms.ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => Shutdown());
        _tray.ContextMenuStrip.Opening += (_, _) => RefreshTrayMenu(statusItem, pauseItem);
        _tray.MouseClick += (_, args) =>
        {
            if (args.Button == WinForms.MouseButtons.Left) OpenSettings();
        };
        Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerMode; // SCOPE §4: sleep/resume closes sessions
        if (_settings.WasResetFromCorrupt)
        {
            var where = _settings.CorruptBackupPath;
            Logger.Log($"src=config-reset backup={where ?? "<backup-failed>"}");
            try
            {
                _tray.ShowBalloonTip(8000, "TimeNotice settings were reset",
                    string.IsNullOrWhiteSpace(where)
                        ? "The settings file was unreadable and could not be backed up."
                        : $"The settings file was unreadable. A backup was saved at {where}.",
                    WinForms.ToolTipIcon.Warning);
            }
            catch { }
        }
        StartScheduler();
    }

    // Phase 6: Registry Run entry (HKCU, no elevation). Called on startup
    // and after every Settings save (SettingsWindow passes ApplyStartup).
    public void ApplyStartup()
    {
        try
        {
            using var run = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (run is null) return;
            if (_settings.StartWithWindows)
            {
                // Prefer the runtime path (MainModule can be null); the single
                // Run value is overwritten each launch, so a new beta exe in a
                // new folder repoints autostart at itself — no stale duplicate.
                var exe = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exe))
                {
                    exe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }
                if (!string.IsNullOrWhiteSpace(exe))
                {
                    run.SetValue("TimeNotice", $"\"{exe}\"");
                    Logger.Log($"src=startup run={exe}");
                }
                else Logger.Log("src=startup run=<unresolved>");
            }
            else
            {
                try { run.DeleteValue("TimeNotice", throwOnMissingValue: false); } catch { }
                Logger.Log("src=startup run=removed");
            }
        }
        catch { Logger.Log("startup Unsuccessful"); }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_settings, () => { ApplyStartup(); StartScheduler(); }, GameDisplayName, DetectionStatus, LastFocusExe, ClassifyLastFocus, TestPopup);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        if (!_settingsWindow.IsVisible) _settingsWindow.Show();
        // Tray/taskbar reopen must land in front: Activate alone only flashes
        // behind a borderless game, so force the Z-order with a Topmost pulse.
        _settingsWindow.Activate();
        _settingsWindow.Topmost = true;
        _settingsWindow.Topmost = false;
        _settingsWindow.Focus();
    }

    // Status header + tooltip + icon, refreshed on every menu open so Quiet/Pause
    // never looks identical to broken. Icons are cached once: no per-tick GDI handles.
    private void RefreshTrayMenu(WinForms.ToolStripMenuItem status, WinForms.ToolStripMenuItem pause)
    {
        try
        {
            var fg = NotificationPolicy.ForegroundExe();
            var suppressed = _settings.PauseAll || NotificationPolicy.ShouldSuppress(fg, _settings);
            string text;
            if (_settings.PauseAll) text = "Paused — no popups";
            else if (!string.IsNullOrWhiteSpace(fg) && NotificationPolicy.ShouldSuppress(fg, _settings))
                text = $"Quiet: {fg} focused — silent";
            else text = "Running — popups on schedule";
            status.Text = text;
            pause.Checked = _settings.PauseAll;
            if (_tray is not null)
            {
                var tip = $"TimeNotice - {text}";
                _tray.Text = tip.Length <= 63 ? tip : tip[..63];
                _tray.Icon = (suppressed && _trayIconDim is not null) ? _trayIconDim : _trayIcon;
            }
        }
        catch { }
    }

    private void StartScheduler()
    {
        _scheduler?.Stop();
        if (_lastGeneral == default) _lastGeneral = DateTime.UtcNow; // ponytail: UTC math so DST shifts never move intervals; display stays local
        _scheduler = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) }; // ponytail: 5s poll so game-detect is prompt; per-mode intervals compared inside
        _scheduler.Tick += (_, _) => CheckAuto();
        _scheduler.Start();
    }

    private DateTime _lastGeneral;
    private DateTime _lastGame; // shared single clock for all game reminders (known + heuristic) by design: one popup stream, no per-game stacking
    private DateTime _knownGoneSinceUtc; // alt-tab grace start for known-game sessions (same Suspect hold as heuristic)
    private string? _gameSessionExe;
    private string? _lastActiveGameExe; // latest tick's session exe, feeds wording when focus is elsewhere
    private DateTime _gameSessionStart;
    private readonly HashSet<string> _detectSeen = new(StringComparer.OrdinalIgnoreCase); // ponytail: cleared when process exits, relaunch re-notifies
    private readonly HashSet<string> _unknownSeen = new(StringComparer.OrdinalIgnoreCase); // ponytail: one silent log line per exe per run
    private string? _lastNonSelfFg; // last fg that wasn't self/null: tray click steals focus, so classify uses this

    // Phase 4 heuristic session (SCOPE §3-§4): IDLE -> ACTIVE -> SUSPECT -> INACTIVE.
    private HeurState _heurState = HeurState.Idle;
    private string? _heurExe;
    private DateTime _heurStartUtc;
    private DateTime _heurSuspectSinceUtc;
    private DateTime _heurCandSinceUtc;
    private string? _heurCandExe;
    private DateTime _heurCandLastGoodUtc; // last tick the candidate showed a real signal; dips inside grace hold the clock
    private Heuristics.GpuSampler? _gpu;
    private bool _heurInitLogged;

    // Phase 3: self + ignore list never count as gameplay. Ignore wins
    // over a Games entry for the same exe (SCOPE §5: built-in Ignored wins).
    private bool IsIgnored(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return false;
        if (NotificationPolicy.IsSelf(exe)) return true; // never score self, whatever the download was named
        foreach (var line in _settings.IgnoreExes)
            if (SameExe(ParseGameEntry(line).Exe, exe)) return true;
        return GameCatalog.IsIgnore(exe);
    }

    // Effective game list: user mapping first, then catalog Normal entries not
    // overridden into Ignored. Unknown exes are NOT added — silent log only.
    // Ignore wins: a user GameExes entry that is also ignored is dropped here,
    // so IsKnownGame + gameActive + detect all read time-only for dual-listed exes.
    private List<(string Exe, string? Name)> EffectiveGames()
    {
        var list = _settings.GameExes
            .Select(ParseGameEntry)
            .Where(g => g.Exe.Length > 0 && !IsIgnored(g.Exe))
            .ToList();
        var have = new HashSet<string>(list.Select(g => ExeKey(g.Exe)), StringComparer.OrdinalIgnoreCase);
        foreach (var c in GameCatalog.Entries)
        {
            if (!c.Policy.Equals("Normal", StringComparison.OrdinalIgnoreCase)) continue;
            if (have.Contains(ExeKey(c.Exe)) || IsIgnored(c.Exe)) continue;
            list.Add((c.Exe, c.Display));
            have.Add(ExeKey(c.Exe));
        }
        return list;
    }

    private void NoteUnknown(string? fg)
    {
        if (string.IsNullOrWhiteSpace(fg)) return;
        if (IsIgnored(fg)) return;
        if (_settings.QuietModeExes.Any(q => SameExe(q, fg))) return;
        foreach (var line in _settings.GameExes)
            if (SameExe(ParseGameEntry(line).Exe, fg)) return;
        if (GameCatalog.Find(fg) is not null) return;
        if (!_unknownSeen.Add(fg)) return; // silent after first sighting per run
        Logger.Log($"src=unknown-exe fg={fg} hint=tray");
        if (_tray is not null)
        {
            var tip = $"TimeNotice - unknown: {fg} (right-click > Settings to classify)";
            _tray.Text = tip.Length <= 63 ? tip : tip[..63]; // WinForms cap is 64 chars
        }
    }

    // Last non-self focus seen by the poll. Tray/Settings clicks steal focus,
    // so a live read at click time just says TimeNotice.exe — Settings shows
    // this value before classifying, so the target is never a surprise (#11).
    public string? LastFocusExe() => _lastNonSelfFg;

    // Classify the last-focused app. Called from Settings, where the target
    // exe is displayed first. Saves immediately; Settings Save merges it.
    public string ClassifyLastFocus(string target)
    {
        var fg = _lastNonSelfFg;
        if (string.IsNullOrWhiteSpace(fg)) return "Nothing focused yet — focus an app first.";
        if (NotificationPolicy.IsSelf(fg)) return "That is this app — never listed.";
        // Move semantics: one list owns the exe, the others drop it (no dual entries).
        if (target == "games") { _settings.QuietModeExes.RemoveAll(q => SameExe(q, fg)); _settings.IgnoreExes.RemoveAll(l => SameExe(ParseGameEntry(l).Exe, fg)); _settings.AddGameExe(fg); _settings.Save(); }
        else if (target == "quiet") { _settings.GameExes.RemoveAll(l => SameExe(ParseGameEntry(l).Exe, fg)); _settings.IgnoreExes.RemoveAll(l => SameExe(ParseGameEntry(l).Exe, fg)); _settings.AddQuietExe(fg); _settings.Save(); }
        else { _settings.GameExes.RemoveAll(l => SameExe(ParseGameEntry(l).Exe, fg)); _settings.QuietModeExes.RemoveAll(q => SameExe(q, fg)); _settings.AddIgnoreExe(fg); _settings.Save(); }
        Logger.Log($"src=classify fg={fg} target={target}");
        return $"{fg} saved to {target}.";
    }

    // Test popups, called from the Settings Detect tab (#11). Same paths as the old tray items.
    public void TestPopup(string kind)
    {
        if (_overlay is null) return;
        if (kind.Equals("game", StringComparison.OrdinalIgnoreCase)) ShowTest("manual-game", "game");
        else if (kind.Equals("confirm", StringComparison.OrdinalIgnoreCase)) TestConfirm();
        else ShowTest("manual-general", "general");
    }

    private void CheckAuto()
    {
        var nowUtc = DateTime.UtcNow; // ponytail: interval math in UTC, DST-proof
        var nowLocal = DateTime.Now; // ponytail: time windows are local wall-clock by user intent
        var fg = NotificationPolicy.ForegroundExe();
        if (!string.IsNullOrWhiteSpace(fg) && !NotificationPolicy.IsSelf(fg))
            _lastNonSelfFg = fg; // remember before menu clicks steal focus
        if (_settings.PauseAll) { _lastGeneral = nowUtc; _lastGame = nowUtc; return; } // freeze clocks while paused: unpause resumes the full interval, no overdue burst
        if (NotificationPolicy.ShouldSuppress(fg, _settings)) return; // Quiet wins over both modes, retry next tick
        NoteUnknown(fg); // Phase 3: silent log + tray hint, never a popup
        if (IsIgnored(fg)) fg = null; // ignored + self never hold focus for sessions
        var games = EffectiveGames();
        games.RemoveAll(g => IsIgnored(g.Exe)); // ignore wins over a Games entry for the same exe
        // detection: any listed game process running (focused or not)
        if (_settings.GameEnabled && games.Count > 0)
        {
            var running = GetRunningGames(games);
            _detectSeen.RemoveWhere(seen => !running.ContainsKey(seen));
            foreach (var (exe, _) in running.OrderBy(kv => kv.Value))
            {
                if (_detectSeen.Add(exe) && _settings.GameNotifyOnDetect)
                    ShowDetect(exe);
            }
            // session clock follows the focused game's process start, not app launch
            string? focused = null;
            if (!string.IsNullOrWhiteSpace(fg))
            {
                foreach (var g in games)
                {
                    if (SameExe(g.Exe, fg)) { focused = g.Exe; break; }
                }
            }
            string? active = focused;
            DateTime activeStart = default;
            if (active is not null && !running.TryGetValue(active, out activeStart)) active = null;
            if (active is null && _settings.CountWhenRunning)
            {
                foreach (var (exe, start) in running.OrderBy(kv => kv.Value))
                { active = exe; activeStart = start; break; } // earliest-running listed game, focus not needed
            }
            _lastActiveGameExe = active; // feeds ShowTest wording when focus is elsewhere
            if (active is not null)
            {
                _knownGoneSinceUtc = default; // focus back: grace cancelled
                if (!SameExe(_gameSessionExe, active) || _gameSessionStart != activeStart)
                {
                    _gameSessionExe = active; // new session: overdue fires on next due tick
                    _gameSessionStart = activeStart;
                    _lastGame = activeStart;
                }
                if (GameInWindow(nowLocal) && IsGameDue(nowUtc, nowLocal, _gameSessionStart, _lastGame, Math.Max(1, _settings.GameMinutes)))
                {
                    _lastGame = nowUtc;
                    ShowTest("auto-game", "game");
                }
            }
            else
            {
                // Known-game alt-tab grace (same hold as heuristic Suspect): brief focus
                // loss keeps the session clock, only a sustained absence drops it.
                if (_gameSessionExe is not null && IsKnownGame(_gameSessionExe))
                {
                    if (_knownGoneSinceUtc == default) _knownGoneSinceUtc = nowUtc;
                    if ((nowUtc - _knownGoneSinceUtc).TotalSeconds > Math.Max(5, _settings.HeuristicSuspectSeconds))
                    {
                        _knownGoneSinceUtc = default;
                        _gameSessionExe = null; // grace expired: drop session, keep detect memory
                    }
                    // inside grace: keep the session clock, fall through to general + heuristic
                }
                else
                {
                    _knownGoneSinceUtc = default;
                    _gameSessionExe = null; // no focused game: keep detect memory, drop session
                }
            }
        }
        if (_settings.GeneralEnabled && InWindow(nowLocal) && IsGeneralDue(nowUtc, nowLocal, _lastGeneral, Math.Max(1, _settings.GeneralMinutes)))
        {
            _lastGeneral = nowUtc;
            ShowTest("auto-general", "general");
        }
        UpdateHeuristic(fg, nowUtc); // Phase 4: unknown sustained sessions; never overrides a known-game session
    }

    private bool IsKnownGame(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return false;
        if (IsIgnored(exe)) return false; // ignore wins: dual-listed reads as time-only
        foreach (var g in EffectiveGames())
            if (SameExe(g.Exe, exe)) return true;
        return false;
    }

    private void HeurReset(string why)
    {
        if (_heurState != HeurState.Idle || _heurExe is not null || _heurCandExe is not null)
            Logger.Log($"src=heur-state to=idle why={why} exe={_heurExe ?? _heurCandExe ?? "<none>"}");
        _heurState = HeurState.Idle;
        _heurExe = null;
        _heurCandExe = null;
        _heurCandLastGoodUtc = default;
        if (_gameSessionExe is not null && !IsKnownGame(_gameSessionExe))
            _gameSessionExe = null; // drop heuristic bridge, keep known-game sessions
    }

    private void UpdateHeuristic(string? fg, DateTime nowUtc)
    {
        if (!_settings.HeuristicEnabled || !_settings.GameEnabled)
        {
            if (_heurState != HeurState.Idle || _heurExe is not null) HeurReset("disabled");
            return;
        }
        _gpu ??= new Heuristics.GpuSampler();
        var candidate = !string.IsNullOrWhiteSpace(fg) && !IsIgnored(fg) && !IsKnownGame(fg)
            && !_settings.QuietModeExes.Any(q => SameExe(q, fg))
            && GameCatalog.Find(fg) is null;
        var idleSec = Heuristics.IdleSeconds();
        var fs = candidate && Heuristics.IsForegroundFullscreen();
        var gpu = candidate ? _gpu.SampleSum(Heuristics.ForegroundPid()) : null;
        if (!_heurInitLogged)
        {
            _heurInitLogged = true;
            Logger.Log($"src=heur-init gpuAvail={_gpu.Available} idle={idleSec}s fs={fs}");
        }
        var idleLost = idleSec > Math.Max(5, _settings.HeuristicSuspectSeconds); // AFK past threshold
        var signal = candidate && !idleLost && (fs || (gpu ?? 0) >= Math.Max(1, _settings.HeuristicGpuPercent));

        switch (_heurState)
        {
            case HeurState.Idle:
                if (signal && SameExe(_heurCandExe, fg))
                {
                    _heurCandLastGoodUtc = nowUtc;
                    if ((nowUtc - _heurCandSinceUtc).TotalSeconds >= _settings.HeuristicStartSeconds)
                    {
                        _heurState = HeurState.Active; // session found: sustained unknown activity
                        _heurExe = fg;
                        _heurStartUtc = _heurCandSinceUtc;

                        _heurCandExe = null;
                        Logger.Log($"src=heur-state to=active exe={fg} idle={idleSec}s fs={fs} gpu={(gpu is null ? "n/a" : $"{gpu:F0}%")}");
                        if (_tray is not null)
                        {
                            var tip = $"TimeNotice - session: {GameDisplayName(fg!)}";
                            _tray.Text = tip.Length <= 63 ? tip : tip[..63];
                        }
                        MaybeAutoConfirm(fg); // new heuristic session: ask once, Z=Games X=Ignored
                    }
                }
                else if (signal)
                {
                    if (!SameExe(_heurCandExe, fg))
                        Logger.Log($"src=heur-cand exe={fg} gpu={(gpu is null ? "n/a" : $"{gpu:F0}%")} idle={idleSec}s fs={fs}");
                    _heurCandExe = fg; // first sustained tick; brief dips hold the clock (grace below)
                    _heurCandSinceUtc = nowUtc;
                    _heurCandLastGoodUtc = nowUtc;
                }
                else if (SameExe(_heurCandExe, fg) && fg is not null
                    && (nowUtc - _heurCandLastGoodUtc).TotalSeconds <= Math.Max(5, _settings.HeuristicSuspectSeconds))
                {
                    // same exe, brief dip (loading screen, overlay, one slow tick): hold the clock
                }
                else
                {
                    if (_heurCandExe is not null && SameExe(_heurCandExe, fg))
                        Logger.Log($"src=heur-cand-drop exe={fg} gpu={(gpu is null ? "n/a" : $"{gpu:F0}%")} idle={idleSec}s fs={fs}");
                    _heurCandExe = null;
                }
                break;
            case HeurState.Active:
                if (signal && SameExe(_heurExe, fg))
                {

                    if (_gameSessionExe is null || SameExe(_gameSessionExe, _heurExe))
                    {
                        _gameSessionExe = _heurExe; // bridge: game wording + elapsed reuse the heuristic clock
                        _gameSessionStart = _heurStartUtc;
                    }
                    if (GameInWindow(DateTime.Now) && IsGameDue(nowUtc, DateTime.Now, _heurStartUtc, _lastGame, Math.Max(1, _settings.GameMinutes)))
                    {
                        _lastGame = nowUtc;
                        ShowTest("auto-game", "game");
                    }
                }
                else
                {
                    _heurState = HeurState.Suspect; // alt-tab / menu dip / short AFK: grace, no flap
                    _heurSuspectSinceUtc = nowUtc;
                    Logger.Log($"src=heur-state to=suspect exe={_heurExe} idle={idleSec}s");
                }
                break;
            case HeurState.Suspect:
                if (signal && SameExe(_heurExe, fg))
                {
                    _heurState = HeurState.Active; // recovered inside grace: session continues, clock kept

                    Logger.Log($"src=heur-state to=active exe={_heurExe} why=recovered");
                }
                else if ((nowUtc - _heurSuspectSinceUtc).TotalMinutes >= Math.Max(1, _settings.HeuristicInactiveMinutes))
                {
                    var done = _heurExe;
                    _heurState = HeurState.Inactive;
                    Logger.Log($"src=heur-state to=inactive exe={done} why=timeout");
                    HeurReset("closed");
                }
                break;
            case HeurState.Inactive:
                HeurReset("closed");
                break;
        }
    }

    private static (string Exe, string? Name) ParseGameEntry(string line)
    {
        var text = (line ?? "").Trim();
        var eq = text.IndexOf('=');
        if (eq < 0) return (text, null);
        return (text[..eq].Trim(), text[(eq + 1)..].Trim());
    }

    // "spotify" == "spotify.exe": strip suffix once so boxes can mix forms.
    private static bool SameExe(string? a, string? b)
    {
        return string.Equals(ExeKey(a), ExeKey(b), StringComparison.OrdinalIgnoreCase);
    }

    private static string ExeKey(string? s)
    {
        return Settings.ExeKey(s); // single definition lives on Settings (Load/Normalize share it)
    }

    private string GameDisplayName(string exe)
    {
        foreach (var line in _settings.GameExes)
        {
            var (e, name) = ParseGameEntry(line);
            if (SameExe(e, exe) && !string.IsNullOrWhiteSpace(name))
                return name!;
        }
        var hit = GameCatalog.Find(exe); // catalog display when user gave a bare exe
        if (hit is not null) return hit.Display;
        return Settings.TitleCaseExe(exe); // ponytail: auto Title-case, override with "exe = Name"
    }

    // Detect-tab live readout (issue #9): plain-words snapshot of what the
    // detector sees right now. Read-only: sampling GPU here just consumes a
    // sample, it never starts sessions or writes settings.
    public string DetectionStatus()
    {
        try
        {
            // Last focus, not live focus (issue #9): on a single monitor opening
            // Settings always steals focus, so the live read would just say
            // itself. The cached last non-self fg is the useful line.
            var lastFg = _lastNonSelfFg;
            _gpu ??= new Heuristics.GpuSampler();
            string lastGpu;
            try
            {
                double? sample = null;
                if (!string.IsNullOrWhiteSpace(lastFg))
                {
                    var procName = lastFg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? lastFg[..^4] : lastFg;
                    using var proc = System.Diagnostics.Process.GetProcessesByName(procName).FirstOrDefault();
                    if (proc is not null) sample = _gpu.SampleSum((uint)proc.Id);
                }
                lastGpu = sample is null ? "n/a" : $"{sample:F0}%";
            }
            catch { lastGpu = "n/a"; }
            var lastLine = string.IsNullOrWhiteSpace(lastFg) ? "none yet" : $"{lastFg} (GPU {lastGpu})";
        // Live read (issue #10): pid + title + resolve source so a game that
        // never appears in last-focus is provable (protected handle vs alias).
        // Meaningless while Settings is open (live=self) — Last focus stays canonical.
        string liveLine;
        try
        {
            var (liveExe, livePid, liveTitle, liveSrc) = NotificationPolicy.ForegroundDiag();
            var shortTitle = liveTitle is null ? "" : liveTitle.Length > 40 ? liveTitle[..40] + "…" : liveTitle;
            liveLine = string.IsNullOrWhiteSpace(liveExe)
                ? $"unresolved (pid {livePid}, via {liveSrc}) title=\"{shortTitle}\""
                : $"{liveExe} (pid {livePid}, via {liveSrc}) title=\"{shortTitle}\"";
        }
        catch { liveLine = "unavailable"; }
            string session = "none";
            if (!string.IsNullOrWhiteSpace(_gameSessionExe))
                session = $"{GameDisplayName(_gameSessionExe)} — {FormatElapsed(DateTime.UtcNow - _gameSessionStart)}";
            string watch = _heurState switch
            {
                HeurState.Active => $"tracking {_heurExe}",
                HeurState.Suspect => $"grace after losing {_heurExe}",
                HeurState.Inactive => "closing",
                _ => _heurCandExe is null ? "idle"
                    : $"watching {_heurCandExe} ({(int)(DateTime.UtcNow - _heurCandSinceUtc).TotalSeconds}s of {_settings.HeuristicStartSeconds}s)",
            };
            var idle = Heuristics.IdleSeconds();
            // Fullscreen is only measurable on the live foreground window, so
            // it only means anything when the game still holds focus.
            var liveFg = NotificationPolicy.ForegroundExe();
            var fs = SameExe(liveFg, lastFg) && !string.IsNullOrWhiteSpace(lastFg)
                ? (Heuristics.IsForegroundFullscreen() ? "yes" : "no")
                : "n/a (not focused)";
            var status = $"Last focus: {lastLine}\nLive: {liveLine}\nSession: {session}\nWatch: {watch}\nIdle {idle}s | Fullscreen {fs}";
            var top = TopGpuSnapshot();
            if (top.Count > 0)
                status += $"\n\nTop {TopGpuCount} GPU processes (why each does/doesn't track):\n" + string.Join("\n", top.Select(t => $"• {t.Exe} (pid {t.Pid}) — {t.Gpu:F0}% — {GpuVerdict(t.Exe, t.Gpu)}"));
            else if (_gpu is null || !_gpu.Available)
                status += "\n\nTop GPU: engine counters unavailable on this machine.";
            return status;
        }
        catch { return "Status unavailable."; }
    }

    private List<(uint Pid, string Exe, double Gpu)> _topSnap = new();
    private DateTime _topSnapUtc;
    private const int TopGpuCount = 5; // readout cap, named in the header so the limit is visible

    // Throttled full-GPU ranking for the readout (issue #9): enumeration +
    // NextValue over every engine instance is fine every few seconds, not
    // every Settings-timer tick.
    private List<(uint Pid, string Exe, double Gpu)> TopGpuSnapshot()
    {
        if ((DateTime.UtcNow - _topSnapUtc).TotalSeconds < 5 && _topSnap.Count > 0) return _topSnap;
        try
        {
            _gpu ??= new Heuristics.GpuSampler();
            _topSnap = _gpu.SnapshotTop(TopGpuCount);
            _topSnapUtc = DateTime.UtcNow;
        }
        catch { }
        return _topSnap;
    }

    // One-line reason this GPU-hot exe does or doesn't start a session.
    private string GpuVerdict(string exe, double gpu)
    {
        if (NotificationPolicy.IsSelf(exe)) return "skipped: this app, never tracked";
        if (GameCatalog.IsIgnore(exe)) return "skipped: built-in Ignored list";
        foreach (var line in _settings.IgnoreExes)
            if (SameExe(ParseGameEntry(line).Exe, exe)) return "skipped: your Ignored list";
        if (_settings.QuietModeExes.Any(q => SameExe(q, exe))) return "skipped: Quiet Mode";
        if (IsKnownGame(exe))
            return SameExe(_gameSessionExe, exe)
                ? $"TRACKED: listed game, session {FormatElapsed(DateTime.UtcNow - _gameSessionStart)}"
                : "TRACKED: listed game, running";
        return gpu >= Math.Max(1, _settings.HeuristicGpuPercent)
            ? $"unlisted: meets the {_settings.HeuristicGpuPercent}% bar — focus it {_settings.HeuristicStartSeconds}s to confirm"
            : $"unlisted: below the {_settings.HeuristicGpuPercent}% bar";
    }

    private readonly Dictionary<string, DateTime> _startCache = new(StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, DateTime> GetRunningGames(List<(string Exe, string? Name)> games)
    {
        var found = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        foreach (var (exe, _) in games)
        {
            var procName = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;
            if (procName.Length == 0) continue;
            try
            {
                var any = false;
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(procName))
                {
                    any = true;
                        DateTime start;
                        try { start = p.StartTime.ToUniversalTime(); } // ponytail: UTC so DST shifts never move the session clock
                        catch { start = _startCache.TryGetValue(exe, out var old) ? old : DateTime.UtcNow; } // elevated: reuse first-seen start, never reset to now each tick
                        _startCache[exe] = start;
                        if (!found.TryGetValue(exe, out var cur) || start < cur)
                            found[exe] = start;
                }
                if (!any) _startCache.Remove(exe); // exited: next launch is a new session
            }
            catch { } // ponytail: silent unknown-exe, tray hint only
        }
        return found;
    }

    internal static string FormatElapsed(TimeSpan span)
    {
        var mins = Math.Max(0, (int)Math.Floor(span.TotalMinutes));
        if (mins < 60) return $"{mins} min";
        return $"{mins / 60} h {mins % 60} min";
    }

    private void ShowDetect(string exe)
    {
        if (_overlay is null) return;
        var fg = NotificationPolicy.ForegroundExe();
        var suppress = NotificationPolicy.ShouldSuppress(fg, _settings);
        var display = GameDisplayName(exe);
        Logger.Log($"src=auto-detect mode=game game={display} suppress={suppress} pause={_settings.PauseAll}");
        if (suppress) return; // Quiet Mode / paused: silent
        var mins = Math.Max(1, _settings.GameMinutes).ToString();
        var main = (_settings.GameDetectTemplate ?? "")
            .Replace("{game}", display)
            .Replace("{interval}", mins)
            .Replace("{time}", DateTime.Now.ToString("h:mm tt"));
        if (string.IsNullOrWhiteSpace(main)) main = $"{display} detected";
        var sub = "";
        if (_settings.ShowSession)
        {
            sub = (_settings.GameDetectSubTemplate ?? "")
                .Replace("{game}", display)
                .Replace("{interval}", mins)
                .Replace("{time}", DateTime.Now.ToString("h:mm tt"));
        }
        ShowOrQueue(main, sub, "auto-detect");
    }

    // Single-slot queue, latest wins: a popup shown while the overlay is busy
    // replaces any waiting one, so a triple-fire shows at most the last queued
    // popup after the gap. Middle popups are dropped by design (logged) — the
    // queue never grows, one slot is the whole backlog.
    private const double QueueGapSeconds = 1.5; // ponytail: single-slot queue, latest replaces waiting
    private string? _queuedMain;
    private string? _queuedSub;
    private string _queuedSource = "";
    private DispatcherTimer? _drainTimer;

    private void ShowOrQueue(string main, string sub, string source)
    {
        if (_overlay is null) return;
        // Central gate (SCOPE §5+§8): Quiet/Pause re-checked at show time, so
        // receipts, drains, and any future caller stay silent when the fg changed mid-flight.
        if (NotificationPolicy.ShouldSuppress(NotificationPolicy.ForegroundExe(), _settings)) { Logger.Log($"src=suppressed-at-show orig={source} pause={_settings.PauseAll}"); return; }
        if (_confirm is { Active: true })
        {
            // A confirm owns the overlay: scheduler ticks must not queue behind the sticky prompt
            // and overwrite its result (a 30s window doubles the exposure). The poll retries next tick.
            Logger.Log($"src=suppressed-while-confirm orig={source}");
            return;
        }
        if (_overlay.IsShowing)
        {
            var replaced = _queuedMain is not null;
            _queuedMain = main;
            _queuedSub = sub;
            _queuedSource = source;
            Logger.Log($"src={(replaced ? "queued-replaced" : "queued")} orig={source}");
            return;
        }
        _overlay.ShowReminder(main, sub);
    }

    private void OnOverlayHidden()
    {
        if (_queuedMain is null) return;
        _drainTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(QueueGapSeconds) };
        _drainTimer.Tick -= DrainQueueTick; // one reused timer: never stack handlers
        _drainTimer.Tick += DrainQueueTick;
        _drainTimer.Stop();
        _drainTimer.Start();
    }

    private void DrainQueueTick(object? sender, EventArgs e)
    {
        _drainTimer?.Stop();
        if (_queuedMain is null) return;
        var main = _queuedMain;
        var sub = _queuedSub ?? "";
        var source = _queuedSource;
        _queuedMain = null;
        _queuedSub = null;
        if (NotificationPolicy.ShouldSuppress(NotificationPolicy.ForegroundExe(), _settings)) { Logger.Log($"src=queued-dropped orig={source} pause={_settings.PauseAll}"); return; } // Quiet/Pause entered during the gap: drop, don't show
        Logger.Log($"src=queued-shown orig={source}");
        _overlay?.ShowReminder(main, sub);
    }

    private bool InWindow(DateTime now)
    {
        return InWindow(now, _settings.WindowOnly, _settings.WindowStart, _settings.WindowEnd);
    }

    private bool GameInWindow(DateTime now)
    {
        return InWindow(now, _settings.GameWindowOnly, _settings.GameWindowStart, _settings.GameWindowEnd);
    }

    private static bool InWindow(DateTime now, bool only, string startText, string endText)
    {
        if (!only) return true;
        if (!TimeSpan.TryParse(startText, out var start)) return true; // fail-open on bad input
        if (!TimeSpan.TryParse(endText, out var end)) return true;
        var t = now.TimeOfDay;
        if (start <= end) return t >= start && t < end;
        return t >= start || t < end; // overnight, e.g. 22:00-07:00
    }

    // Align-to-each-hour: grid resets every hour (30 min -> :00/:30, 7 min -> :00/:07/.../:56).
    // Elapsed mode stays DST-proof UTC; aligned mode is wall-clock by intent.
    private static DateTime FloorBoundary(DateTime nowLocal, int minutes)
    {
        minutes = Math.Max(1, minutes);
        var hourStart = nowLocal.Date.AddHours(nowLocal.Hour);
        var elapsed = (nowLocal - hourStart).TotalMinutes;
        return hourStart.AddMinutes(Math.Floor(elapsed / minutes) * minutes);
    }

    internal static DateTime NextBoundary(DateTime nowLocal, int minutes)
    {
        // First grid boundary strictly after now. The grid restarts at :00
        // every hour, so near the hour a short gap replaces the overshoot
        // (7m at 1:57 -> 2:00, not 2:03). On-the-boundary ticks show the
        // coming boundary, never now (which rendered "Next: 5:15" at 5:15).
        minutes = Math.Max(1, minutes);
        var hourStart = nowLocal.Date.AddHours(nowLocal.Hour);
        var elapsed = (nowLocal - hourStart).TotalMinutes;
        var k = (int)Math.Floor(elapsed / minutes) + 1;
        if (k * minutes < 60) return hourStart.AddMinutes(k * minutes);
        return hourStart.AddHours(1);
    }

    private bool IsGeneralDue(DateTime nowUtc, DateTime nowLocal, DateTime lastUtc, int minutes)
    {
        if (!_settings.AlignToClock)
            return (nowUtc - lastUtc) >= TimeSpan.FromMinutes(minutes);
        return FloorBoundary(nowLocal, minutes) > lastUtc.ToLocalTime();
    }

    private bool IsGameDue(DateTime nowUtc, DateTime nowLocal, DateTime sessionStartUtc, DateTime lastUtc, int minutes)
    {
        // Game alerts never align to the clock: sessions start mid-hour, so elapsed time is the honest measure.
        // AlignToClock applies to general reminders only (hour-anchored grid).
        return (nowUtc - sessionStartUtc) >= TimeSpan.FromMinutes(minutes)
            && (nowUtc - lastUtc) >= TimeSpan.FromMinutes(minutes);
    }

    // Auto confirm: first tick of a new heuristic session asks once.
    // Z writes Games, X writes Ignored, timeout (15s) accepts like Z.
    // Generic hosts (javaw.exe) are named from the window title when known.
    private static readonly HashSet<string> GenericHostExes = new(StringComparer.OrdinalIgnoreCase) { "javaw.exe" };

    private string AutoDisplayName(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe)) return "";
        foreach (var line in _settings.GameExes)
        {
            var (e, name) = ParseGameEntry(line);
            if (SameExe(e, exe) && !string.IsNullOrWhiteSpace(name))
                return GameDisplayName(exe); // user mapping wins, never overridden
        }
        if (GameCatalog.Find(exe) is not null) return GameDisplayName(exe);
        if (GenericHostExes.Contains(exe))
        {
            var title = NotificationPolicy.ForegroundWindowText();
            if (title is not null && title.Contains("minecraft", StringComparison.OrdinalIgnoreCase))
                return "Minecraft";
        }
        return GameDisplayName(exe);
    }
    private void MaybeAutoConfirm(string? exe)
    {
        if (_overlay is null) return;
        if (_confirm is { Active: true }) return; // one prompt at a time
        if (string.IsNullOrWhiteSpace(exe)) return;
        if (IsIgnored(exe)) return;
        if (IsKnownGame(exe)) return;
        if (_settings.QuietModeExes.Any(q => SameExe(q, exe))) return;
        if (GameCatalog.Find(exe) is not null) return;
        foreach (var line in _settings.IgnoreExes)
            if (SameExe(ParseGameEntry(line).Exe, exe)) return;
        ClearQueue(); // confirm owns the overlay; stale queue must not overwrite its result
        _confirm = new ConfirmPrompt(_overlay, _settings, exe, AutoDisplayName(exe), OnConfirmDone);
        _confirm.Start();
    }

    private void TestConfirm()
    {
        if (_overlay is null) return;
        if (_confirm is { Active: true }) return; // one prompt at a time
        if (NotificationPolicy.ShouldSuppress(NotificationPolicy.ForegroundExe(), _settings)) return; // same gate as ShowTest: paused/Quiet test stays silent
        const string fg = "test-game.exe"; // fake exe: receipt reads as a test, can never collide with a real game
        ClearQueue();
        _confirm = new ConfirmPrompt(_overlay, _settings, fg, "Test game", OnTestConfirmDone, persist: false);
        _confirm.Start();
    }

    private void OnTestConfirmDone(string choice, string exe)
    {
        try { _confirm?.Dispose(); } catch { }
        _confirm = null;
        try { _drainTimer?.Stop(); } catch { }
        if (NotificationPolicy.ShouldSuppress(NotificationPolicy.ForegroundExe(), _settings)) { Logger.Log("src=suppressed-at-show orig=confirm-test-done"); return; } // Quiet/Pause entered during confirm: no receipt
        ShowOrQueue("Test popup — nothing saved.", "Games and Ignored lists unchanged.", "confirm-test-done");
    }

    private void ClearQueue()
    {
        _queuedMain = null;
        _queuedSub = null;
        try { _drainTimer?.Stop(); } catch { }
    }

    private void OnConfirmDone(string choice, string exe)
    {
        try { _confirm?.Dispose(); } catch { }
        _confirm = null;
        try { _drainTimer?.Stop(); } catch { } // stale drain must not overwrite the receipt
        var timeoutAccepts = _settings.ConfirmTimeoutAction.Equals("Accept", StringComparison.OrdinalIgnoreCase);
        var accepted = choice.StartsWith("accept-key", StringComparison.OrdinalIgnoreCase)
            || (choice.StartsWith("timeout", StringComparison.OrdinalIgnoreCase) && timeoutAccepts);
        if (accepted) { _detectSeen.Add(exe); if (_settings.IgnoreExes.RemoveAll(l => SameExe(ParseGameEntry(l).Exe, exe)) > 0) _settings.Save(); } // move semantics: Games owns it now
        else if (_settings.GameExes.RemoveAll(l => SameExe(ParseGameEntry(l).Exe, exe)) > 0) _settings.Save(); // move semantics: Ignored owns it now
        var display = GameDisplayName(exe);
        var main = accepted
            ? $"{display} added to Games list."
            : $"{display} is ignored.";
        var sub = accepted ? $"Current time alert interval is {Math.Max(1, _settings.GameMinutes)} min." : "No time alerts while on the ignored list.";
        if (NotificationPolicy.ShouldSuppress(NotificationPolicy.ForegroundExe(), _settings)) { Logger.Log("src=suppressed-at-show orig=confirm-done"); return; } // Quiet/Pause entered during confirm: no receipt
        ShowOrQueue(main, sub, "confirm-done");
    }

    private void ShowTest(string source = "manual", string? modeOverride = null)
    {
        if (_overlay is null) return;
        var fg = NotificationPolicy.ForegroundExe();
        var suppress = NotificationPolicy.ShouldSuppress(fg, _settings);
        var gameActive = false;
        if (!string.IsNullOrWhiteSpace(fg) && !IsIgnored(fg))
        {
            foreach (var g in EffectiveGames())
            {
                if (SameExe(g.Exe, fg)) { gameActive = true; break; }
            }
            if (!gameActive && _heurState == HeurState.Active && SameExe(_heurExe, fg))
                gameActive = true; // Phase 4 heuristic session counts as game-active
            if (!gameActive && _settings.CountWhenRunning && !string.IsNullOrWhiteSpace(_lastActiveGameExe))
                gameActive = true; // running-counts: session on a background game
        }
        var isGame = modeOverride?.Equals("game", StringComparison.OrdinalIgnoreCase) == true
            || (modeOverride?.Equals("general", StringComparison.OrdinalIgnoreCase) != true && gameActive && _settings.GameEnabled);
        Logger.Log($"src={source} mode={(isGame ? "game" : "general")} fg={fg ?? "<null>"} suppress={suppress} pause={_settings.PauseAll} quiet=[{string.Join(",", _settings.QuietModeExes)}]");
        if (suppress) return; // Quiet Mode / paused: silent
        var mins = isGame ? Math.Max(1, _settings.GameMinutes) : Math.Max(1, _settings.GeneralMinutes);
        var nowLocal = DateTime.Now;
        var nowStr = nowLocal.ToString("h:mm tt");
        // Align is general-only (game fires on elapsed time): under Align the
        // shown wait is the true gap to the coming boundary, so duration and
        // time always agree (30m at 1:29 -> "in 1m at 1:30"). Anchored on the
        // last fire when it is still ahead, else the coming boundary.
        var nextLocal = nowLocal.AddMinutes(mins);
        var showMins = mins;
        if (_settings.AlignToClock && !isGame)
        {
            nextLocal = NextBoundary(nowLocal, mins);
            if (_lastGeneral != default)
            {
                var cand = NextBoundary(_lastGeneral.ToLocalTime(), mins);
                if (cand > nowLocal) nextLocal = cand;
            }
            showMins = Math.Max(1, (int)Math.Ceiling((nextLocal - nowLocal).TotalMinutes));
        }
        var nextStr = nextLocal.ToString("h:mm tt");
        var gameDisplay = source == "manual-game" ? "Test game" : ResolveGameDisplay(fg, isGame);
        var elapsed = source == "manual-game" ? "0 min"
            : isGame && _gameSessionExe is not null ? FormatElapsed(DateTime.UtcNow - _gameSessionStart) : (isGame ? "0 min" : "");
        var mainTpl = isGame
            ? (string.IsNullOrWhiteSpace(_settings.GameMainTemplate) ? Settings.DefaultGameMainTemplate : _settings.GameMainTemplate)
            : _settings.MainTemplate;
        var subTpl = isGame
            ? (string.IsNullOrWhiteSpace(_settings.GameSubTemplate) ? Settings.DefaultGameSubTemplate : _settings.GameSubTemplate)
            : _settings.SubTemplate;
        var main = (mainTpl ?? "").Replace("{time}", nowStr).Replace("{next}", nextStr).Replace("{game}", gameDisplay).Replace("{elapsed}", elapsed).Replace("{interval}", showMins.ToString());
        if (string.IsNullOrWhiteSpace(main)) main = nowStr;
        var sub = "";
        if (_settings.ShowSession)
        {
            sub = (subTpl ?? "").Replace("{time}", nowStr).Replace("{next}", nextStr).Replace("{game}", gameDisplay).Replace("{elapsed}", elapsed).Replace("{interval}", showMins.ToString());
        }
        ShowOrQueue(main, sub, source);
    }

    private string ResolveGameDisplay(string? fg, bool isGame)
    {
        if (!string.IsNullOrWhiteSpace(fg))
        {
            foreach (var line in _settings.GameExes)
            {
                var (e, _) = ParseGameEntry(line);
                if (SameExe(e, fg)) return GameDisplayName(e);
            }
            if (isGame && _heurState == HeurState.Active && SameExe(_heurExe, fg))
                return GameDisplayName(fg); // heuristic session: Title-case, never another game's name
            if (isGame && _settings.CountWhenRunning && !string.IsNullOrWhiteSpace(_lastActiveGameExe))
                return GameDisplayName(_lastActiveGameExe); // running-counts: name the session game, not the focused app
        }
        if (isGame)
        {
            if (!string.IsNullOrWhiteSpace(_lastActiveGameExe)) return GameDisplayName(_lastActiveGameExe); // running session names the popup, not the first list entry
            foreach (var line in _settings.GameExes)
            {
                var (e, _) = ParseGameEntry(line);
                if (e.Length > 0) return GameDisplayName(e);
            }
        }
        return "";
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerMode; } catch { }
        try { _gpu?.Dispose(); } catch { }
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        try { _trayIcon?.Dispose(); } catch { }
        try { _trayIconDim?.Dispose(); } catch { }
        try { _singleInstance?.ReleaseMutex(); _singleInstance?.Dispose(); } catch { }
        _singleInstance = null;
        base.OnExit(e);
    }

    private void OnPowerMode(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        try
        {
            Dispatcher.BeginInvoke(() =>
            {
                Logger.Log($"src=power mode={e.Mode} sessions-closed");
                _gameSessionExe = null; // SCOPE §4: sleep/resume closes the session
                _detectSeen.Clear(); // resume re-fires detect for the still-running game
                HeurReset("power");
            });
        }
        catch { }
    }
}
