# Build Progress — TimeNotice (cross-machine source of truth)

Update this file every session. One row per test run.

## Current
- Phase: 6 — DONE 2026-11-01 (hardening verified: clean install + corrupt recovery live-tested, Defender scan clean per user)
- Next action: V1 exit checklist sweep (§11) or public release prep
- Open (deferred to post-Phase 6): CountWhenRunning ON-path self-test (tick box, alt-tab 2 min); Game-tab crowding polish; repo public release; game+general overlap — both fire by design (late-night time awareness), reconsider game-with-time-only after user testing/feedback; beta debug options in-app (tray Open-logs-folder + Copy-debug-info, startup ver/OS/settings line in debug.log) — docs-only for now, see README Beta debugging
- Open (filed 2026-11-04): #5 Test confirm popup writes minecraft.exe to Games list — fixed in code 2026-11-04, shipped beta 1.2 2026-11-05
- Shipped 2026-11-06: beta 1.4.2 (v1.4.2, commit fb305f2) prerelease + exe (185990578 bytes, true single-file); closed #26, #27, #28 (#27/#28 auto-closed on push via Fix trailers, gate notes commented); pending user gate tests per issue
- Open (filed 2026-11-08): #35 Box opacity drags text down (Border.Opacity multiplies into child TextBlocks, so text 100% renders at 20% when box is 20%; OverlayWindow.xaml.cs:121-138 + SettingsWindow.xaml.cs:448-470) — no code yet, slated v1.6; proposed fix bakes box opacity into brush alpha, Border.Opacity stays 1.0
- Open (filed 2026-11-08): #36 Previews use first Games entry instead of standard Test game example (RefreshPreviews scanned live GameBox/_settings.GameExes via _gameDisplay; #15 only fixed the Test popup, previews intentionally kept real name) — fix in working tree, uncommitted, build 0 warn 0 err (const test-game.exe/Test game, _gameDisplay param removed, hint reworded), slated v1.6, pending gate + commit
- Shipped 2026-11-06 (filed 2026-11-06): #28 Box opacity at 100% should fully block out the background — FIXED in code (ApplyTheme + SetPreview force fill alpha opaque at 100%, identical below), shipped beta 1.4.2, closed, pending user gate test
- Shipped 2026-11-06 (filed 2026-11-06): #27 Match-Windows previews stay dark in light mode + Light preview edge invisible — FIXED in code (shared IsHostLight, fixed 1px #D5D5DC outline when Light in ApplyTheme + SetPreview), shipped beta 1.4.2, closed, pending user gate test
- Open (filed 2026-11-06): #19 Detect readout labels Discord as launcher (catalog wording) — FIXED 2026-11-06 in code (GpuVerdict now "skipped: built-in Ignored list", comment standardized; Epic Games Launcher display name + Settings.LauncherExes compat key intentionally kept, see issue; build 0 warn 0 err), pending user gate test, slated beta 1.4
- Open (filed 2026-11-06): #18 Align-to-hour {interval}/{next} disagree after live interval change / launch (e.g. 30m at 1:29 reads "in 30m at 1:30", true wait ~1 min; scheduler correct, wording only) — FIXED 2026-11-06 in code (NextBoundary rolls to :00 at hour end; Align-general shows ceil(next-now) anchored on last fire; game wording never aligns; previews mirror; build 0 warn 0 err), pending user gate test, slated beta 1.4
- Open (filed 2026-11-06): #15 Test game popup uses first real Games entry — FIXED in code 2026-11-06 (ShowTest manual-game forces Test game + 0 min, build 0 warn 0 err), pending user gate test, slated beta 1.4
- Open (filed 2026-11-06): #16 Settings lists go stale when a game is detected while open — FIXED in code 2026-11-06 (live append-only sync on 1s timer + post-save box reload + Quiet merge, build 0 warn 0 err), pending user gate test, slated beta 1.4
- Open (filed 2026-11-06): #14 separate text opacity + box-off toggle (Popup Opacity 20-100% dims box+text together) — FIXED 2026-11-06 in code (TextOpacity 1.0 default + ShowBox checkbox; box/text fade separately, previews match; build 0 warn 0 err), pending user gate test, slated beta 1.4
- Open (filed 2026-11-06): #17 move Try a popup from Detect to Popup tab (tray → Detect via #11/281af22, now misplaced; Previews stays read-only) — FIXED 2026-11-06 XAML-only move to Popup tab after Confirm card, same handlers; build 0 warn 0 err, pending user gate test
- Open (filed 2026-11-06): #11 tray menu too long, Exit cut off after beta 1.2 added Open-logs + Clear-log rows (App.xaml.cs:56-78, 12 rows -> 16) — keep/cut audit in issue, fix pending owner keep-list decision — FIXED 2026-11-06 (tray now 5 rows; classify + tests to Detect tab, logs to footer; build 0 warn 0 err), pending user gate test
- Open (filed 2026-11-06): #12 renamed self binary (e.g. TimeNotice (1).exe) leaks into Detect readout — FIXED in code 2026-11-06 (NotificationPolicy.IsSelf() dynamic process path+name, all 4 App.xaml.cs sites call it), build 0 warn, closed pending user verify (renamed-copy Detect check)
- Open (filed 2026-11-06): #13 Start-with-Windows Run entry across beta exe swaps — FIXED in code 2026-11-06 (ApplyStartup prefers Environment.ProcessPath + run= log line + README upgrade note), build 0 warn, closed pending user verify (swap-exe Run repoint check)
- Shipped 2026-11-06: beta 1.4 (v1.4.0, commit 6e2468c) prerelease + exe (185986482 bytes, true single-file); closed #14, #15, #17, #18, #19, #21, #22, #23, #24, #25; #16 fix in tree, issue open pending gate retest
- Shipped 2026-11-05: beta 1.2 (v1.2.0, commit 034f101) prerelease + exe; closed #1, #2, #3, #4, #5, #6, #9; beta-1.1 notes marked superseded
- Repo: public since 2026-11-02
- Config: reset to fresh-install defaults 2026-11-01 at user request

## How to sync Mac <-> Windows
```bash
# Mac (primary edits)
git add -A && git commit -m "msg" && git push
# Windows (build/test)
git pull
dotnet build src/TimeNotice/TimeNotice.csproj
dotnet run --project src/TimeNotice/TimeNotice.csproj
```

## Phase gates (check when passed on Windows)
- [x] P1 Overlay: appears bottom-right, fades ~5s, no focus steal (type in Notepad while it shows), works over borderless window — passed 2026-11-01 per user manual test
- [x] P1 Quiet gate: add `notepad.exe` to QuietModeExes in `%AppData%/TimeNotice/settings.json`, focus Notepad → Test reminder shows nothing — passed 2026-11-01 (Notepad suppress=True, Desktop showed; debug.log confirms)
- [x] P2 Scheduler: recurring reminders + pause persists after restart — passed 2026-11-01 per user (pause/unpause confirmed; 1-min recurrence + settings persistence in debug.log across restarts)
- [x] P3 Detection: known exe starts/stops session, ignored app not mistaken — passed 2026-11-01 per user explicit close-out
- [x] P4 Heuristics: unknown sustained session found (AppControl via GPU, live 2026-11-01), suspect grace on focus loss logged, no flap
- [x] P5 Safety: matrix-verified 2026-11-01 — every cell already implemented (P1 Quiet test + P3 ignore-wins test); No Overlay dropped, no new code
- [x] P6 Hardening: clean install, Defender scan, no invasive deps — DONE 2026-11-01 (fresh-profile launch OK, corrupt→backup+defaults OK, Defender clean per user, only Microsoft PerformanceCounter pkg)
- [ ] T-gates (eng review 2026-11-01, before ship): confirm timeout default honors ConfirmTimeoutAction; confirm firing while Settings open keeps both edits; corrupt settings.json shows one balloon with backup path and stays alive

## Behavior matrix (agreed 2026-11-01 — the whole notification model)

Terms: **tracked** = listed game focused (or heuristic session, or running with CountWhenRunning on).
**ignored** = IgnoreExes + self. Time reminders are unconditional; only Quiet/Pause silence them.

| Game mode | Time mode | Focused app | Result |
|---|---|---|---|
| off | off | anything | nothing |
| off | on | anything (not Quiet) | time only |
| off | on | Quiet / paused | nothing |
| on | off | tracked game | game only |
| on | off | ignored / Quiet | nothing |
| on | on | tracked game | both |
| on | on | ignored | time only |
| on | on | Quiet | nothing |

Notes: unknown app never blocks time reminders and never starts a game session by itself
(heuristic promotion is the only path, and it logs + asks). No Overlay mode dropped:
with no database, an invisible untracked clock is unobservable, so it added nothing.

## Session log
| Date (UTC) | Machine | Did | Result |
|---|---|---|---|
| 2026-10-21 | Mac | Scaffolded P1: tray + overlay + gate + settings; added SCOPE/AGENTS/PROGRESS | Not built — needs Windows `dotnet build` |
| 2026-10-21 | Mac | Static review: fixed Settings.Path shadow, ShutdownMode, overlay re-show; `git init` + first commit | Ready for Windows: clone/pull then build |
| 2026-10-21 | Mac | Ponytail audit (full tree): 4 findings, ~8 lines, 0 deps | Cuts logged below, none applied — approve before code/spec change |
| 2026-11-01 | Windows | Installed SDK 8.0.427, added nuget.org source, fixed App ambiguity + missing System.IO, `dotnet build` OK, exe launch-tested | Build passes, app stays running; pending manual P1 tray + Quiet gates |
| 2026-11-01 | Windows | Manual P1: Test reminder showed bottom-right + faded, typing in Notepad uninterrupted | PASS per user — overlay + no-steal confirmed |
| 2026-11-01 | Windows | Quiet gate via Test-in-3s + debug.log: Notepad suppress=True (no popup), Desktop suppress=False (popup) | PASS — P1 DONE; note: Desktop run detected fg=TimeNotice.exe, needs Ignore-self in Phase 3 |
| 2026-11-01 | Windows | P2 scheduler: auto timer every ReminderMinutes + tray Interval 15/30/60 + Next line, 0 warn build + launch OK | Built, pending gate: 1-min recurrence + pause-persist |
| 2026-11-01 | Windows | Settings window: left-click tray opens it (any X 1-480 min, secs, Next toggle, quiet list), right-click keeps menu | Built 0 warn + launch OK, pending user open/save test |
| 2026-11-01 | Windows | Two modes: General (enable + X min + 09:00-23:00 window) + Game (enable + Y min + game exe list), 30s poll, game priority | Built 0 warn + launch OK, pending user test |
| 2026-11-01 | Windows | Wording templates: MainTemplate/SubTemplate with {time} {next} + wrap box, tidied Spotify.exe casing | Built 0 warn + launch OK, pending user open/save test |
| 2026-11-01 | Windows | Overlap queue (single-slot, latest wins, 1.5s gap, queued log tags) + overlap warning in Settings + card-style Settings refresh | Built 0 warn + launch OK, pending user test |
| 2026-11-01 | Windows | Game time window (own Only-between + From/To) + auto collapse/expand of both time ranges on checkbox | Built 0 warn + launch OK, pending user open/save test |
| 2026-11-01 | Windows | Overlay anchor fix: position on rendered Actual size after Show (SizeToContent broke bottom-right) | Built 0 warn + launch OK, pending user confirm bottom-right |
| 2026-11-01 | Windows | Tray trim: removed Test-in-3s + Interval submenu (Settings window is the config UI now) | Built 0 warn + launch OK |
| 2026-11-01 | Windows | Appearance + split test: 4-corner Position, Theme Dark/Light/MatchHost/Custom + bg/text/sub colors + opacity slider, Game main/sub templates (blank=reuse), tray Test general/Test game with mode tag in debug.log | Built 0 warn + launch OK, pending user test |
| 2026-11-01 | Windows | Settings tabs: General / Game / Popup (wording + look) / Quiet, WarnText + Save always visible, taller Quiet box | Built 0 warn + launch OK, pending user test |
| 2026-11-01 | Windows | Pause checkmark: tray Pause item stays checked while paused, persists via settings | Built 0 warn + launch OK |
| 2026-11-01 | Windows | Process-based game clock (StartTime session, overdue fires, {game} {elapsed} {interval} tokens, exe = Name mapping) + detect popup checkbox/template, 5s poll | Built 0 warn + launch OK, pending user test |
| 2026-11-01 | Windows | Log diagnosis: 1-min refires were game pops (opencode.exe listed as game) in reused general wording; set distinct game wording, restarted live | Running, auto-game fired with new wording |
| 2026-11-01 | Windows | Detection two-line format: GameDetectSubTemplate (blank=none), short main + small sub like other pops, live config split | Built 0 warn, restarted live, detects firing |
| 2026-11-01 | Windows | DST-proof scheduler: interval/session math in UTC, time windows + display stay local; observed live clock correction 01:49→01:35 mid-session, self-healed | Built 0 warn, restarted live, firing |
| 2026-11-01 | Windows | P3 Detection: GameCatalog (9 entries ≤12 max: 5 Ignore + cs2/csgo multi-exe + eldenring + Balatro), IgnoreExes setting seeded from catalog + Game-tab box, self-ignore TimeNotice.exe, unknown-exe silent log + tray tooltip, tray classify (Games/Quiet/Ignored) | Built 0 warn; live log-verified: unknown-exe Notepad line + no popup; ignore-wins both directions (opencode in Ignored = 12s silence, removed = detect+auto-game resume); Settings visuals pending user open/save |
| 2026-11-01 | Windows | P4 heuristics: state machine (Idle/Active/Suspect/Inactive) + idle/fullscreen/GPU signals, 5 settings, tray+Settings UI, sleep closes sessions | Built 0 warn; live: heur-init gpuAvail=True, known-game path intact |
| 2026-11-01 | Windows | P4 gate PASS: user held AppControl maximized 30s → heur-cand (gpu=51% idle=0s) → active → auto-game reminder naming AppControl; focus return → suspect grace (no flap, no close). Restored HeuristicStartSeconds 60, rebuilt 0 warn, relaunched, committed | App stable (PID 21728), pushed |
| 2026-11-01 | Windows | Running-counts toggle: CountWhenRunning (default off, Game tab checkbox) — session follows earliest-running listed game without focus; focused still wins; wording names the session game; heuristic still focus-only | Built 0 warn; default-off regression live (detect+game normal); ON-path left to user self-test |
| 2026-11-01 | Windows | Confirm prompt (Z/X/C poll test): ConfirmPrompt via GetAsyncKeyState, sticky overlay, 15s default-accept; tray Test confirm | Built 0 warn, relaunched |
| 2026-11-01 | Windows | Confirm combos + auto path: Ctrl+Alt+Z/X(/C), MaybeAutoConfirm on heur Idle→Active (Z/X only, timeout writes nothing), timeout-dismiss bug fixed | Built 0 warn; live: javaw accept → Games → auto-detect fired |
| 2026-11-01 | Windows | GPU default 5→10, wiped javaw for retest, HeuristicStart 30s this run; live: cand 54% → active in 30s → auto Ctrl+Alt+Z/X prompt → timeout-dismiss, Games still empty | Loop verified, next session re-prompts |
| 2026-11-01 | Windows | Auto-naming: ForegroundWindowText + GenericHostExes (javaw) + title-has-minecraft → prompt shows Minecraft, Accept stores "javaw.exe = Minecraft" | Built 0 warn, relaunched; pending live game session |
| 2026-11-01 | Windows | Confirm fixes: auto timeout now accepts (was dismiss — footer promised Accept), footer dropped "type freely" line | Built 0 warn, relaunched; Games still empty, next session re-prompts |
| 2026-11-01 | Windows | Confirm cleanup: stacked Accept/Reject lines, removed Dismiss keybind entirely (tray test now Z/X) | Built 0 warn, relaunched |
| 2026-11-01 | Windows | P6 hardening: Logger (ensure-dir + 512KB rotate), single-instance mutex, crash handlers, corrupt-backup + atomic save, asInvoker manifest, Start-with-Windows checkbox + HKCU Run | Build 0 warn 0 err; Release publish OK to publish/; pending user gates: clean-install + Defender scan |
| 2026-11-01 | Windows | P6 gates: wiped %AppData%/TimeNotice → fresh launch OK (PID 8900, dir + debug.log recreated, silent unknown-exe); garbage settings.json → corrupt backup + defaults, app alive (PID 12552); Defender clean per user; restored pre-P6 files, relaunched | P6 DONE |
| 2026-11-02 | Windows | Heuristic dip-grace (SuspectSeconds hold, cand-drop log) + sampler refresh-on-new-pid + null-on-no-match; Save stays open (merge baseline re-snapshot); Settings Topmost-pulse to front; build 0 warn 0 err | Pending user pull + Dota retest on gaming PC |
| 2026-11-04 | Windows | Filed #5: tray Test confirm timeout-accept persisted minecraft.exe to GameExes (App.xaml.cs:691 demo fg + ConfirmPrompt.cs:120 AddGameExe via prod OnConfirmDone); AGENTS.md now requires GitHub issue + PROGRESS row for every feature/bug | Issue open; workaround = delete minecraft.exe line from settings.json GameExes; fix pending |
| 2026-11-05 | Windows | Filed #10: last-focus stuck on explorer.exe (ForegroundExe MainModule throws on protected games, returned null) + Discord discovered on fresh install (DefaultExtraIgnored never seeded fresh, IsIgnore ignored it). Fixed: ProcessName fallback in NotificationPolicy.cs, IsIgnore covers Extra in GameCatalog.cs, fresh Settings seeds IgnoreExes; build 0 warn 0 err | Fix built, pending user retest: Rivals focus + Discord silent |
| 2026-11-06 | Windows | Beta 1.3 (v1.3.0 prerelease + exe): #10 fixes plus gaming-PC diag, no dev tools needed there — ForegroundDiag (live exe/pid/title/source), Detect tab Live line + GPU pids + Copy debug info button; publish Release OK | Shipped prerelease v1.3.0, #10 commented; pending user carry exe to gaming PC, paste back Copy output |
| 2026-11-06 | Windows | Fixed v1.3.0 asset: first upload was the 151KB framework-dependent launcher (won't run without SDK); replaced with 178MB self-contained single exe published to publish/beta-1.3 (matches beta-1.2 pattern), verified 177769675 bytes on the release | Corrected, pending user download |
| 2026-11-06 | Windows | Recompiled 1.3 per request (build 0 warn 0 err): tree already carried #10/#11/#12/#13 (concurrent commit 281af22) + diag extras; republished self-contained, re-uploaded v1.3.0 asset (identical bytes — already current), release notes list #10-#13, closed #10 + #11 | Shipped; left #12/#13 open for the other session |
| 2026-11-06 | Windows | Release audit per user: v1.3.0 binary verified byte-identical from clean tree d5eb676; rewrote notes with per-issue user-visible changes; committed dirty tree; added AGENTS.md audit rule (clean tree before publish, notes list every issue, verify rendered) | Audited; notes fixed, rule in place |
| 2026-11-06 | Windows | 1.3 would not start on gaming PC (1.2 fine): root cause was my publish missing IncludeNativeLibrariesForSelfExtract, so the exe depended on sibling DLLs. Republished true single-file (185986482 bytes, only exe+pdb in folder), re-uploaded v1.3.0 asset, verified size on release; AGENTS.md now pins the exact publish command | Fixed, pending user redownload + launch |
| 2026-11-06 | Windows | Fixed #12 (renamed self leaks into Detect): NotificationPolicy.IsSelf() on dynamic process path+name, all 4 App.xaml.cs sites call it. Fixed #13 (Run staleness): ApplyStartup prefers Environment.ProcessPath + run= log line + README upgrade note. Build 0 warn 0 err at fix time, both issues closed | NOTE: tree later red from concurrent #11 edit (App.xaml.cs:131 new 7-arg SettingsWindow call, ctor not yet landed — all 4 errors point there, none in this fix). Pending user verify after green rebuild: renamed-copy Detect check + swap-exe Run repoint check |
| 2026-11-06 | Windows | Filed #18: Align-to-hour {interval}/{next} disagree after live interval change / launch (root-caused to static cadence vs NextBoundary in ShowTest App.xaml.cs:884-887; suggested Align-only ceil(next-now) fix, no code yet) | Issue open, pending owner approval |
| 2026-11-06 | Windows | Filed #14: separate text opacity + box-off toggle (Opacity 20-100% shared; want text slider + box 0%/off) | Issue open, no code yet — pending owner approval before Popup tab change |
| 2026-11-06 | Windows | Fixed #16 (Settings stale while open): live append-only list sync on 1s timer (no dirty-mark, deletes respected) + post-save box reload from _settings + Quiet merge like Games/Ignore; filed #16, build 0 warn 0 err | Fix built, pending user gate test (open Settings, trigger detect, entry appears; Save shows merged lines), slated beta 1.4 with #15 |
| 2026-11-06 | Windows | Filed #15 + fixed Test-game uses first real entry: ShowTest manual-game forces Test game + 0 min (App.xaml.cs:888-890); Test-confirm already dry-run (persist:false); previews keep real name as samples, auto paths untouched; build 0 warn 0 err | Fixed, pending user gate test + push |
| 2026-11-06 | Windows | Filed #17: move Try a popup from Detect to Popup tab (tray → Detect via #11/281af22, now misplaced; Previews stays read-only) | Issue open, no code yet — pending owner approval before XAML-only move |
| 2026-11-06 | Windows | Shipped beta 1.4: bumped Version 1.4.0, published true single-file to publish/beta-1.4 (TimeNotice.exe 185986482 bytes, only exe+pdb in folder), created v1.4.0 prerelease with per-issue notes (#14, #15, #16, #17, #18, #19, #21, #22, #23, #24, #25), verified asset size + rendered notes via gh api | Shipped; pending user download + gate tests |
| 2026-11-06 | Windows | Fixed #18 (align wording): NextBoundary now first grid line strictly after now with :00 hour rollover (7m at 1:57 → 2:00, not 2:03); Align-general {interval} = ceil(next-now) anchored on last fire when ahead; Test-game {next} always now+mins (game fires on elapsed, never aligned); Previews + inline sample mirror; Align-off untouched; verified 30m@1:29→in 1m, auto@1:30→in 30m, 7m@1:57→in 3m at 2:00; build 0 warn 0 err | Fixed, pending user gate test (30m align at x:29, 7m at x:57) + push |
| 2026-11-06 | Windows | Fixed #14 (separate text opacity + box-off): Settings TextOpacity 1.0 default + ShowBox checkbox; OverlayWindow per-element opacity (window stays 1.0 for fade range), previews read live sliders; Popup tab Box/Text sliders; defaults preserve 92% box look; build 0 warn 0 err | Fixed, pending user gate test (box 92 + text 100 default, uncheck box = text-only, drag text slider) + push |
| 2026-11-06 | Windows | Filed #26 + fixed popup misplacement on 125%: Position() divided monitor pixels by the focused game's DPI (javaw.exe reports 96, so BottomRight computed to Left=1576 in a 1536-wide space); now uses our own visual's DPI via VisualTreeHelper, dropped GetDpiForWindow import; ponytail ActualWidth line cleared (unchanged since initial build); build 0 warn 0 err | Fixed, pending user gate test (focus Minecraft, Test confirm must sit bottom-right) |
| 2026-11-06 | Windows | Built + launched beta 1.4.1 locally (Version 1.4.1, true single-file 185986482 bytes in publish/beta-1.4.1, killed beta-1.4 PID 25468, started PID 22016, startup ver=v1.4.1 in debug.log) | Running; gate PASSED 2026-11-06 (user confirms bottom-right with Minecraft focused), #26 closed |
| 2026-11-06 | Windows | Fixed #17 (XAML-only): moved Try a popup card Detect → Popup tab after Confirm card, same TestGeneral/TestGame/TestConfirm handlers; Previews stays read-only; build 0 warn 0 err | Fixed, pending user gate test (Popup tab fires, Detect tab no longer has it) + push |
| 2026-11-06 | Windows | Appended PlateUp wipe log to #16: confirm-accept 01:25:10 then interval-test Saves wiped Games entry; root-caused to Save #2 on pre-8c39272 binary (merge on #1, stale box + re-snapshotted baseline deletes on #2); fix already in tree, gate = Save twice on 1.4 build | Comment live, no code change, pending 1.4 gate retest |
| 2026-11-06 | Windows | Audit fixes for 1.4: closed #21/#22/#23/#24/#25 in code (DPI divide, Quiet ExeKey match, timeout receipt honors setting, Save lock, TitleCaseExe single helper, shared ignore keys, session-exe test wording, drain-timer reuse, Detect 5s throttle, debt notes fixed); #20 tests attempted then reverted (xunit compiles but Application Control blocks the test assembly here, 0x800711C7 — retry on unblocked machine); build 0 warn 0 err | Fix built, pending user gate tests per issue, slated beta 1.4 |

| 2026-11-06 | Windows | Filed #19 + fixed Detect launcher wording (GpuVerdict "skipped: built-in Ignored list", comment standardized; compat key kept per issue), build 0 warn 0 err | Fixed, pending gate test + push, slated beta 1.4 |
| 2026-11-06 | Windows | Filed #27: Match-Windows previews stay dark in light mode + Light preview edge invisible (SetPreview hardcodes MatchHost to Dark, SettingsWindow.xaml.cs:403; preview Borders borderless on #FAFAFC page); proposed fix (share IsHostLight, 1px outline when Light), no code yet | Issue open, pending owner go-ahead |
| 2026-11-06 | Windows | Fixed #27 in code per approval + expanded scope (real Light popup also gets fixed 1px #D5D5DC outline, no size/color option): IsHostLight internal-shared, ApplyTheme + SetPreview outline when Light, Dark/Custom untouched; build 0 warn 0 err | Fixed, pending user gate test (light-mode Match previews white w/ edge, follow OS flip in ~1s, real popup edged over white window) |
| 2026-11-06 | Windows | Filed #28 + fixed Box 100% not fully solid (baked 80% alpha in Dark fill): ApplyTheme + SetPreview force opaque fill at 100%, identical below (default 92% look preserved); build 0 warn 0 err | Fixed, pending user gate test (Box 100% over text = no bleed, Dark + Light, popup + all 7 previews; 92% unchanged) |
| 2026-11-06 | Windows | Shipped beta 1.4.2: bumped Version 1.4.2 (1.4.1 bits ran locally, not reused), committed clean tree fb305f2, published true single-file to publish/beta-1.4.2 (185990578 bytes), created v1.4.2 prerelease with per-issue notes (#26, #27, #28), verified asset size + rendered notes; #27/#28 auto-closed on push via Fix trailers, gate notes commented | Shipped; pending user download + gate tests per issue |
| 2026-11-06 | Windows | Full-repo consistency audit (4 parallel readers: preview-vs-popup, settings sync, scheduler/intent, tray/safety) -> filed #29 preview drift, #30 defaults/seed/compat, #31 Quiet gating+queue, #32 session/list, #33 SCOPE stale, #34 nice bundle; 16 findings Must 1-6 / Should 7-13 / Nice 14-16 | Issues open, 1.5 plan pending owner go-ahead |
| 2026-11-06 | Windows | Shipped beta 1.5: deleted gh release v1.4.2 (--cleanup-tag), bumped Version 1.5.0 (ae561f4), published true single-file to publish/beta-1.5 (185990578 bytes, exe+pdb only), created v1.5.0 prerelease bundling 1.4.1 (#26) + 1.4.2 (#27,#28) + 1.5 (#29-#33), verified asset size + rendered notes, pushed main (auto-closed #29-#33; #30 closed first) | Shipped; pending user download + gate tests per release notes |
| 2026-11-06 | Windows | Fixed #30: XAML 30/10/10 + Display fallback 10, SeedDefaults shared fresh/corrupt, LauncherExes always union-merge + WhenWritingNull (old key no longer saved), Quiet via NormalizeExeList (ExeKey dedupe/sanitize), Classify via AddGame/AddQuiet/AddIgnore; GameMinutes repair 60->30; build 0 warn 0 err | Fixed, pending gate: fresh Save no-diff, corrupt backup still ignores Discord, old both-keys union, Quiet dupes collapse |

| 2026-11-06 | Windows | Fixed #31: central ShouldSuppress gate in ShowOrQueue + drop-on-drain, receipt re-check in OnConfirmDone/OnTestConfirmDone, TestConfirm gated like ShowTest, pause freezes _lastGeneral/_lastGame (no unpause burst), single-slot latest-wins documented; build 0 warn 0 err | Fixed, pending gate: Quiet-during-confirm no receipt, queue-drop in 1.5s gap, pause 10m no burst |
| 2026-11-06 | Windows | Fixed #29: Previews tab reads live boxes (templates/minutes/Align/Show-sub/confirm wait+action) not saved _settings, inline sample agrees; blank main falls back to time string, empty-sub collapse marked intentional, game name placeholder documented, hints list all 5 tokens; build 0 warn 0 err | Fixed, pending gate: edit template/minutes without Save, preview and Test popup agree |
| 2026-11-06 | Windows | Fixed #33: SCOPE docs only — fade 10s, No Overlay dropped noted, single 5s poll, GetWindowRect+monitor, Active→Suspect + grace thresholds, confirm Z/X note, 9 Entries + 6 ExtraIgnored split; build 0 warn 0 err | Fixed, pending owner read-through |
| 2026-11-06 | Windows | Fixed #32: known-game alt-tab gets Suspect grace (HeuristicSuspectSeconds) instead of instant clear, shared _lastGame documented single-clock, EffectiveGames drops user entries via IsIgnore + IsKnownGame guard (dual-listed = time-only), Classify + confirm-accept/reject move semantics (add-to-one removes from others), sleep clears _detectSeen so resume re-fires detect; build 0 warn 0 err | Fixed, pending gate: alt-tab 10s survives, dual-list time-only, sleep/resume re-detect |
| 2026-11-07 | Windows | Filed #37: confirm timeout 3rd action Do-nothing-ask-again (timeout writes nothing, no receipt, re-prompts next heur session; indefinite sticky rejected - would own overlay + silence queue) | Issue open, no code yet — pending owner go-ahead |
| 2026-11-08 | Windows | Filed #36: previews use first Games entry (Dota2) not standard example — confirmed #15 only fixed Test popup, previews intentionally kept real name since 14f9dc3; fix in working tree uncommitted (const test-game.exe/Test game, _gameDisplay removed, hint reworded; App.xaml.cs call site; build 0 warn 0 err), slated v1.6 | Issue open, pending gate (empty + Dota2-first lists show Test game) then commit |
| 2026-11-08 | Windows | Filed #38: tester PC lockups (1fps/disconnect) when alerts fire, Intel+Nvidia + Subnautica 2, every ~30min; AMD+AMD dev cannot repro. Code review done, no code change (root cause unconfirmed): prime suspect Topmost layered overlay breaking flip-model/DWM while visible (10s reminder, 30s sticky confirm — Subnautica 2 unlisted so first session is the 30s sticky); 2nd UI-thread 5s PDH/process poll; 3rd game-EA instability. Confirmation plan in issue (timestamp match, 10s->3s dose test, Pause/Quiet on-off, control run without app). Build verified green 0 warn 0 err | Issue open, awaiting tester debug.log + settings.json + fullscreen/borderless + laptop/desktop answers |
| 2026-11-08 | Windows | #38 dev control test: Subnautica 2 on own AMD+AMD rig, 2-min alerts, 8 min, zero stutter (~4 alerts clean); commented on issue. H3 (game-only instability) now unlikely as whole story, H1 (Nvidia-specific present cost) still prime | Awaiting tester-side confirmation tests |
| 2026-11-08 | Windows | #38 tester log analyzed: was v1.2 (not 1.5), Discord.exe in Games list, dual monitors (Discord maximized + Subnautica fullscreen). Log shows v1.2 blind spots now fixed in 1.3+: fg=<null> in-game (no ProcessName fallback pre-#10), Discord promoted to tracked game via heuristic timeout-accept (no Discord ignore-net pre-#10). Double 30-min overlays on v1.2 fit the report. Dev could not repro on same PC; tester now 1.5 + wiped settings. Commented analysis on #38. Filed #39 (log timestamps need dates - confirmed missing in 1.5 Logger.cs:43, multi-day logs uncorrelatable) | Monitor 1.5: watch for fg=<null>, Discord re-added to Games, recurrence |

## Ponytail debt (audit 2026-10-21, rechecked 2026-11-06: Position + ReminderMinutes are live, SCOPE §8 stands as scope record)
- [ ] `Settings.cs`: delete `LauncherExes` pre-rename key after one release carries the merge (Load merges it into IgnoreExes, then drops it)
- [ ] Overlay: collapse `SubText`/`ShowSession` branch to one static test line until Phase 2 copy
- (empty-row cleanup done in this edit)

## Windows quick start
1. Install .NET 8 SDK x64, verify: `dotnet --version` → 8.x
2. `git pull` (or copy folder), then build/run commands above
3. Tray icon → Test reminder. Edit settings at `%AppData%/TimeNotice/settings.json`
4. Append a row to Session log above with pass/fail before next session.
