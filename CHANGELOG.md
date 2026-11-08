# Changelog

All notable changes to **Project Capri** are documented here. This project is under active development.

## [Unreleased]

### Added
- Image-backed Capri sprite renderer (`AvatarControl`) loading transparent assets from `design/assets/capri/` with state fallbacks; placeholder shown when optional local artwork is excluded (no artwork included in the public repo).
- Idle artwork integrated locally as an optional gitignored asset (`design/assets/capri/idle.png`).
- Desktop overlay polish: default character height ~250px, adjustable 150–400px; bottom-right placement on active monitor work area; smooth drag repositioning; position/size persisted in UI settings; monitor/DPI-aware clamping; chat panel positioned beside character without off-screen placement; idle resource care.
- GitHub progress-tracking scaffolding: `CHANGELOG.md`, `ROADMAP.md`, `docs/PROGRESS.md`, CI workflow, updated `.gitignore` for local/private assets and captures.
- **Experimental `/task` autonomy slice (partial, not full Phase 2 completion)** — `/task` turn handling, `TaskCoordinator` phases, fail-closed `TaskPlanParser`, exact allowlisted tools (`launch_app`: notepad/calculator/explorer/paint; `open_url`: safe HTTPS; `brave_search`: query; `create_note`: flat `.txt` + literal text; `open_note`: file created earlier in same plan), independent WPF approval with exact plan/workspace selection for note plans, emergency stop/reset latch, metadata-only `TaskMetadataJournal` (atomic latest-state write, bounded retry on transient publish errors; no prompt/plan/target/text/credentials; never read for replay), and no automatic replay. `SafeTaskExecutor` provides bounded dispatch/verification behavior (including real disposable note create verification/no-overwrite path). See `docs/AUTONOMY.md`.
- **Phase 2 validation tooling/evidence context (2026-11-08, progress recorded; not full Phase 2 completion)** — `tools/Capri.Acceptance` Windows-only harness (`windows-only` mode, **mock planner clearly labeled**, `planner=mock-fixed-plan-not-live-inference`, no live inference) exercises parser/exact-plan gate, real `WpfTaskApproval`, coordinator, executor, and Windows observer. Screen-control design captured in `docs/SCREEN-CONTROL-PLAN.md` (**architecture only; no implemented vision claim**).

### Changed
- `UiSettings` extended additively (`CharacterHeight`, `CharacterLeft`, `CharacterTop`) for overlay persistence.
- Appearance/sizing settings UI wired to character size.
- `/task` UI exposes Emergency Stop / Reset Task Stop; reset clears the latch for future tasks but **cannot revive an already stopped/cancelled task**.
- Windows-only acceptance/observation fixes recorded: `GetPackageFamilyName` handling corrected for a count that includes the terminating NUL; approval foreground/taskbar behavior and approval budget handling fixed. Notepad observation uses official package family `Microsoft.WindowsNotepad_8wekyb3d8bbwe`, exact title, and HWND snapshot logic (`WindowObservation`/`SafeTaskExecutor`).

### Verified
- `dotnet build Capri.sln -c Debug` — **0 errors** (recorded milestone).
- `dotnet test tests/Capri.Tests/Capri.Tests.csproj -c Debug` — **296 passed, 0 failed, 0 skipped** (recorded milestone).
- `/task` autonomy evidence (reported): baseline **296 passed**; tester actual **324 pass** via `dotnet test Capri.sln -c Debug --no-restore -p:OutputPath=bin/AutonomyQA/` **before journal atomic fix**. Security targeted fix build **0 warnings, 0 errors**. Real disposable note create verification/no-overwrite **tested**.
- **Phase 2 (2026-11-08, reported):** baselines **324 → 329 → 337 → 339**. Final recorded result: **339 tests passed**. Earlier evidence: **337** full-suite tests passed + **7** real in-process WPF approval tests repeated **3×** + concurrent journal coverage. Recorded debugger builds: **0** warnings/errors. Running user app **never intentionally stopped**.
- **Windows-only acceptance (`Capri.Acceptance windows-only`, mock planner, `planner=mock-fixed-plan-not-live-inference`):** earlier runs showed `windowObserved=false` and later timeout/cancel with no disk/window observation. Final interactive run evidence (JSON): **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true, workspaceCreated=true, timedOut=false**. Real WPF confirmation was provided by the user; exact new file readback and a trusted Notepad title/package window were observed (**not** editor contents/AI).
- **Provider/config status:** `config-status` recorded `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`; **no live AI** requests in this recorded validation.

### Known issues / notes
- Interactive multi-monitor/DPI/hit-testing behavior requires manual verification.
- Optional local artwork is excluded from the public repo; the app falls back to a placeholder.
- `/task` is **experimental/partial** (**not full Phase 2 completion**): no live provider/planner run with a configured credential yet (`credentialExists=false`; **no live AI** recorded). No full vision fallback (Anthropic) implemented. Launch/open/search actions are dispatch-only (not independently UI-verified beyond specific observed cases). **Cancellation may leave a created note; no automatic delete.** UI stop/reset has limits (reset cannot revive stopped/cancelled task). Normal Debug output may be locked by a running app; alternate builds/outputs are used only for current validation.
- **Git:** no commits/remote; **no push**.

## [0.1.0] - Phase 1 MVP foundation (local)

### Added
- .NET 10 / WPF solution: `Capri.Core`, `Capri.Providers`, `Capri.Security`, `Capri.Automation`, `Capri.Voice`, `Capri.Desktop`, `Capri.Tests`.
- Streaming/non-streaming OpenAI-compatible chat with SSE parsing, bounded retry/error mapping, request timeout handling.
- DPAPI-backed credential storage; settings persistence; provider settings UI with secure `PasswordBox` key handling and non-persisting connection probe.
- Deterministic local automation intents (URL/app launch, media keys) with URL/app safety checks and permission gating.
- Tray app + transparent character companion window + compact chat panel; avatar state wiring.
- Automated test suite (296 tests at last recorded milestone).
