# AGENTS.md — TimeNotice

## Project
Time Awareness for Windows. WPF tray app, external overlay only. See `SCOPE.md` (locked).

## Owner model
User is PM (no app-dev experience). Agent owns all code/docs. Explain in plain language, no jargon without gloss. Confirm scope changes before coding. Never add injection, hooks, drivers, memory inspection, cloud sync — all forbidden by scope.

## Stack
- C# 12, .NET 8 LTS, WPF, `net8.0-windows10.0.17763` (W11 primary, W10 1809+ free)
- Win32 only: foreground, idle, window state, PDH GPU counters. No vendor SDKs.
- JSON settings in `%AppData%/TimeNotice/`. No DB in V1.

## Planned layout (Phase 1)
- `src/TimeNotice/` — WPF tray + overlay + scheduler + gate
- `src/TimeNotice/settings.json` — defaults + catalog (12 max)
- No test framework until Phase 3; manual gates in SCOPE.md §10-11 govern.

## Commands (when code exists)
- `dotnet build src/TimeNotice/TimeNotice.csproj`
- `dotnet run --project src/TimeNotice/TimeNotice.csproj`
- Manual gate per phase before next phase. `dotnet publish -c Release` for Defender-scan binary.

## Release uploads (recurring mistake — read before any `gh release`)
- Gaming PCs have no SDK: NEVER upload `publish/TimeNotice.exe` (151KB framework-dependent launcher, won't run standalone).
- Always publish self-contained single-file first, then upload THAT exe (exact command, all flags required — without IncludeNativeLibraries the exe silently depends on sibling DLLs and won't start):
  `dotnet publish src/TimeNotice/TimeNotice.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish/beta-X.Y`
- Verify after upload: `gh api repos/Skeeee343/TimeNotice/releases/tags/<tag> --jq '.assets[] | {name, size}'` — expect ~180MB, not ~150KB.
- Audit before every release: `git log` since last beta + `git status` must be clean (commit first, never publish from a dirty tree), then write notes listing every included issue with its user-visible change. Mirror the beta-1.2 notes format (title line, Fixes list with (#N) tags). Verify notes rendered on the release page after editing.

## Conventions
- Standard user, `asInvoker`. Topmost overlay must set `ShowActivated=false` + `WS_EX_NOACTIVATE|TRANSPARENT`.
- Gate every overlay through NotificationPolicy/Quiet Mode check.
- Thresholds + exe names configurable, never hardcoded. Silent unknown-exe handling (log + tray hint).
- Keep diffs minimal, fewest files, stdlib/Win32 first. Mark shortcuts with `// ponytail: <ceiling>, <upgrade path>`.

## Reliability bar (corporate-grade: users must trust this on their gaming PC)
- No silent failures: every `catch` either logs (`Logger.Log` with a `src=` token, cold paths only) or carries a `// silent: <reason>` / `// fail-safe:` comment. Never log from inside `Logger` itself (recursion) or from 1s/5s hot-path ticks (spam — fail-open with reason instead). Teardown/`Dispose` catches stay silent with reason. No PII beyond local exe names already in debug.log; no telemetry, no cloud.
- No nested `try` in one method: extract a helper with its own single try (see `ApplyStartup`/`TryBackupCorrupt`). Documented fallback-value nesting (e.g. `ForegroundDiag` module→name→unresolved) is the one exception, with a comment saying so.
- `dotnet build` must stay 0 warnings 0 errors; verify before every commit and release.

## Skill routing

When the user's request matches an available skill, invoke it via the Skill tool. When in doubt, invoke the skill.

Key routing rules:
- Product ideas/brainstorming → invoke /office-hours
- Strategy/scope → invoke /plan-ceo-review
- Architecture → invoke /plan-eng-review
- Design system/plan review → invoke /design-consultation or /plan-design-review
- Full review pipeline → invoke /autoplan
- Bugs/errors → invoke /investigate
- QA/testing site behavior → invoke /qa or /qa-only
- Code review/diff check → invoke /review
- Visual polish → invoke /design-review
- Ship/deploy/PR → invoke /ship or /land-and-deploy
- Save progress → invoke /context-save
- Resume context → invoke /context-restore
- Author a backlog-ready spec/issue → invoke /spec
- Log every feature add and bug as a GitHub issue (gh issue create with bug/feature template) + PROGRESS.md session row; never track work only in chat.
