# TimeNotice

Non-intrusive time-awareness reminders for Windows, built for ADHD time blindness. A small tray app that surfaces the time over games and focused work — informative without interrupting or overwhelming. No focus steal, no accounts, no cloud, no telemetry.

**Lightweight · local-only · no accounts, no cloud, no telemetry · no admin rights · no game injection, hooks, or drivers.**

Latest beta (no install, just download and run): https://github.com/Skeeee343/TimeNotice/releases

Upgrading: replace the old exe in place, or fully exit the old copy before running the new one — the new launch repoints Start-with-Windows at itself (last launcher wins; two side-by-side copies fight over one autostart entry). A browser-renamed download like `TimeNotice (1).exe` is fine: the app always ignores its own binary.

External overlay only. No game injection, hooks, drivers, memory reading, or cloud. See `SCOPE.md` for the locked V1 scope.

## Status
V1 feature-complete. All 6 phases passed on Windows 2026-11-01 (overlay, scheduler, detection, heuristics, safety, hardening). Defender scan clean, fresh-install + corrupt-settings recovery tested. Remaining: final V1 checklist sweep or public release prep. Track in `PROGRESS.md`.

## What it does now
- Tray app with two reminder types: general time reminders (every X min, inside a daily window) and game-session reminders (every Y min while a listed game runs).
- Fading overlay popup (bottom-right etc.) that never steals focus or blocks clicks.
- Game detection: known game list + ignore list + quiet list; unknown games can be spotted by activity signals and confirmed with a Yes/No keypress prompt.
- Pause/resume (persists), Quiet Mode (named apps silence all popups), Start with Windows option.
- Settings window (General / Game / Popup / Quiet tabs). Settings live at `%AppData%/TimeNotice/settings.json`.

## Where TimeNotice lives (healthy install)

- **The exe:** wherever you downloaded it (Downloads, Desktop, a folder — your choice). No installer, one self-contained file. Keep one copy; duplicates fight over the autostart entry below.
- **`%AppData%\TimeNotice\`:** `settings.json` (your config), `debug.log` (what happened, rotates at ~512KB to `debug.log.old`), `settings.corrupt-<timestamp>.json` (backup of an unreadable config, only created if yours ever corrupts).
- **Registry:** `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`, value `TimeNotice` → quoted exe path. Present only while Start with Windows is on; removed when you uncheck it.
- **Nowhere else:** no Program Files entry, no services, no drivers, no HKLM keys, no cloud account. (A `Local\TimeNotice-single` mutex exists only while the app runs, to prevent double-launch.)

## Build (Windows only, WPF does not compile on macOS)
1. Install .NET 8 SDK x64 (`dotnet --version` → 8.x) and Git.
2. Clone: `git clone https://github.com/Skeeee343/TimeNotice.git`
3. Stop any running copy first (the exe locks on rebuild): `Stop-Process -Name TimeNotice`
4. Build: `dotnet build src/TimeNotice/TimeNotice.csproj`
5. Run: `dotnet run --project src/TimeNotice/TimeNotice.csproj`
6. Tray icon → Test general / Test game. Left-click tray opens Settings. Logs at `%AppData%/TimeNotice/debug.log`.

## Beta debugging (send this, nothing else needed)

Files live at `%AppData%\TimeNotice\` (`Win+R` → paste `%AppData%\TimeNotice` → Enter):
- `settings.json` — your config
- `debug.log` — what happened (plus `debug.log.old` if it exists — log rotates at ~512KB)

Each report needs these 3 things:
1. What you were doing (game/app name, fullscreen or borderless, paused or not).
2. What you expected vs what you saw.
3. Both attached files (`settings.json` + `debug.log`).

Common first checks before reporting: Pause unchecked? Focused app not in Quiet list? Game exe listed under Settings → Game? General reminders fire on a schedule even with no game focused; game reminders need the game focused (unless CountWhenRunning is on).

Release binary (self-contained single exe, what the betas ship): `dotnet publish src/TimeNotice/TimeNotice.csproj -c Release --self-contained -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish/beta-X.Y` (output is git-ignored; attach to a GitHub prerelease). SmartScreen warning is expected until signing/reputation — see `SCOPE.md` §2.

## Docs
- `SCOPE.md` — what V1 is and is not
- `PROGRESS.md` — cross-machine build log and phase gates
- `AGENTS.md` — how agents work in this repo
- `COLLABORATOR.md` — build/release commands, repo map, how to contribute
