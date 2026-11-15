# Collaborator guide — TimeNotice

One page for a new contributor. Details live in the linked files; this page is the index, not a copy.

## 1. Build + release pipeline

Prereqs: **Windows** (WPF does not compile on macOS — Mac edits are docs-only), .NET 8 SDK x64 (`dotnet --version` → 8.x), Git.

Dev loop, from repo root (stop the running copy first — the exe locks on rebuild):

```powershell
Stop-Process -Name TimeNotice
dotnet build src/TimeNotice/TimeNotice.csproj   # must stay 0 warnings, 0 errors
dotnet run --project src/TimeNotice/TimeNotice.csproj
```

Logs: `%AppData%/TimeNotice/debug.log`. Settings: `%AppData%/TimeNotice/settings.json`. Tray → Test general / Test game.

Release exe (maintainer only). Gaming PCs have no SDK, so **never** ship `publish/TimeNotice.exe` (151 KB framework-dependent launcher — won't run standalone). Always publish self-contained single-file first, with all flags (without `IncludeNativeLibraries` the exe silently depends on sibling DLLs):

```powershell
dotnet publish src/TimeNotice/TimeNotice.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish/beta-X.Y
```

Then: `git status` must be clean (commit first, never publish from a dirty tree), `git log` since last beta determines the notes (one line per issue with `(#N)` tags, mirror the beta-1.2 format), attach the ~185 MB exe to a GitHub prerelease, and verify with `gh api repos/Skeeee343/TimeNotice/releases/tags/<tag> --jq '.assets[] | {name, size}'` — expect ~180 MB, not ~150 KB. SmartScreen warning is expected until signing/reputation (`SCOPE.md` §2).

CI status: `.github/workflows/dotnet-desktop.yml` exists but is **not** the source of truth — it is still the MS template (`dotnet test` with no test project, WapProj MSIX packaging with no signing secrets). See issue #41 for the packaging/CI follow-up. Releases are manual publish + `gh release` until that lands.

## 2. Repo hierarchy + components

```text
src/TimeNotice/          # the whole app (single WPF project, net8.0-windows10.0.17763, C# 12)
  App.xaml(.cs)          # tray icon + menu, timers (1s/5s polls), scheduler, wiring
  OverlayWindow.xaml(.cs)# click-through fading popup (ShowActivated=false, NOACTIVATE|TRANSPARENT)
  SettingsWindow.xaml(.cs) # General / Game / Popup / Quiet tabs + Detect tab + previews
  Settings.cs            # JSON config model, load/save, defaults seed, corrupt-backup recovery
  NotificationPolicy.cs  # central ShouldSuppress gate (Quiet / Pause / Ignore) before any popup
  GameCatalog.cs         # 9 shipped entries (5 Ignore launchers + 4 Normal) + 6 ExtraIgnored safety net
  Heuristics.cs          # unknown-game state machine IDLE→ACTIVE→SUSPECT→INACTIVE (GPU/idle/fullscreen)
  ConfirmPrompt.cs       # Ctrl+Alt+Z/X sticky Yes/No prompt for unknown sustained games
  Logger.cs              # %AppData% debug.log, ~512KB rotation, cold-path src= tokens only
  AppIcon.cs             # tray icon drawing
  app.manifest           # asInvoker (never elevate), TimeNotice.csproj (one dep: PerformanceCounter)
  WapProj/               # MSIX packaging stub — no ProjectReference yet, see #41
.github/                 # dotnet-desktop.yml (see CI note above), PR template, bug/feature templates
publish/                 # local publish output, git-ignored; beta-X.Y folders per release
SCOPE.md / PROGRESS.md / AGENTS.md / CONTRIBUTING.md / SECURITY.md / CODE_OF_CONDUCT.md
```

Runtime footprint of a healthy install (no installer): the exe wherever the user put it; `%AppData%\TimeNotice\` (`settings.json`, `debug.log` [+`.old`], `settings.corrupt-<ts>.json` only on corruption); one `HKCU\...\Run\TimeNotice` value while Start-with-Windows is on; a `Local\TimeNotice-single` mutex while running. Nothing in Program Files, HKLM, services, drivers, or cloud.

## 3. How to collaborate

- Scope is locked (`SCOPE.md`): informational reminders only. No injection, hooks, drivers, memory inspection, cloud sync, lockouts, dashboards — PRs adding these are declined. Open an issue before any large change.
- Every feature/bug gets a GitHub issue (`gh issue create` with the template) **and** a `PROGRESS.md` session row — never track work only in chat (`AGENTS.md`).
- PR checklist (enforced by the PR template): `dotnet build` 0 warnings; name the manual gate you ran (`SCOPE.md` §10–11, e.g. "borderless no-steal", "Quiet suppress/resume"); no hardcoded thresholds/exe names; settings persist after restart; overlay still never steals focus or blocks clicks.
- Bug reports need exactly three things (`CONTRIBUTING.md`): what you were doing (app, fullscreen/borderless, paused?), expected vs seen, plus `settings.json` + `debug.log` from `%AppData%\TimeNotice`.
- Mac ↔ Windows sync: Mac commits/pushes (`git add -A && git commit -m "msg" && git push`), Windows pulls then builds/runs (commands above). Full log in `PROGRESS.md`.
- Security bugs go private (Security tab → Report a vulnerability, or `@Skeeee343` directly), never a public issue (`SECURITY.md`).
