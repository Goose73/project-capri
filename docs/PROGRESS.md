# Development progress

## 2026-11-08 — Phase 2 `/task` validation status (progress recorded; not full Phase 2 completion)

**Status:** honest progress update. **Full Phase 2 completion criteria are not all met.** The running user app was **never intentionally stopped**. Optional local artwork is excluded from the public repo. Git has **no commits/remote** and **no push**.

**Test/build evidence (reported):**
- Baselines progressed **324 → 329 → 337 → 339** tests.
- **Final recorded result:** **339 tests passed**.
- Earlier Phase 2 evidence: **337** full-suite tests passed + **7** real in-process WPF approval tests repeated **3×** + concurrent journal coverage.
- Recorded debugger builds: **0** warnings/errors.

**Windows-only acceptance harness (`tools/Capri.Acceptance`, mode `windows-only`):**
- Command/mode: `Capri.Acceptance windows-only` (mock planner; real approval and Windows execution). **Mock planner clearly labeled** (`planner=mock-fixed-plan-not-live-inference`); no live inference/credential inspection in this mode.
- **Final interactive run evidence (JSON):** **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true, workspaceCreated=true, timedOut=false**.
- **Observed/confirmed:** real WPF confirmation was provided by the user; the exact new file was read back and a trusted Notepad title/package window was observed. This confirms window identity/presence, **not** editor contents or AI-generated content.
- **Earlier runs (context):** initial run `Completed`, `approvalShown=true`, `diskVerified=true`, `windowObserved=false`, exit 1; after a modern Notepad handoff fix, approval waited twice and timed out (`Cancelled`, no disk/window observation).

**Fixes recorded (evidence context):**
- `GetPackageFamilyName` handling corrected for a count that includes the terminating NUL.
- Approval foreground/taskbar behavior and approval budget handling fixed.
- Notepad observation used official package family `Microsoft.WindowsNotepad_8wekyb3d8bbwe`, exact title, and HWND snapshot logic (`WindowObservation`/`SafeTaskExecutor`).

**Credentials / API (`config-status`):** `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`; **no live AI** requests in this recorded validation. Live provider/planner validation requires a configured credential. Settings path: tray **Settings → AI providers** → Base URL `https://api.openai.com/v1`, supported model such as `gpt-4o-mini`, API key in PasswordBox, **Save**, **Test Connection** (health only, not planner). Never share API keys in chat; ChatGPT Plus is not the same as API access.

**`/task` UI stop/reset limits (actual behavior):** `EmergencyStop()` calls `TaskCoordinator.Stop()` (latches, increments generation) and cancels the active turn; `ResetTaskStop()` only calls `ResetStop()` (clears latch for future tasks). `ResetStop()` does **not** change generation history for already-running tasks, and `CheckStop` fails closed when generation no longer matches — so reset **cannot revive an already stopped/cancelled task**.

**Screen vision:** architecture only in [`SCREEN-CONTROL-PLAN.md`](SCREEN-CONTROL-PLAN.md) (UIA-first, sensitive exclusions, explicit capture consent, untrusted observations, typed tool permissions verify/cancel, no replay). **No implemented vision claim.** No full vision fallback (Anthropic).

## 2026-11-07 — Desktop overlay polish

**Features completed (local):**
- Integrated idle sprite as active `design/assets/capri/idle.png` (local, gitignored; optional local artwork).
- Overlay polish: default character height ~250px (adjustable 150–400px), bottom-right placement respecting active monitor work area, smooth drag repositioning, position/size saved between launches (`UiSettings` additive), monitor/DPI-aware clamping/restoration, chat panel repositioning using character monitor work area, transparency/hit-test best-effort preserved, idle resource care.
- CI/docs scaffolding prepared for GitHub tracking.

**Tests/build:**
- `dotnet build Capri.sln -c Debug` — **0 errors**.
- `dotnet test tests/Capri.Tests/Capri.Tests.csproj -c Debug` — **296 passed, 0 failed, 0 skipped**.

**Visual/comparison (not a substitute for final review):**
- `free-vision` compared local render/artwork against local reference images; reported required likeness areas as matching and no clear issues, with transparency/accuracy requiring review.
- Overlay polish needs manual multi-monitor/DPI/hit-test confirmation. Optional local artwork is excluded from the public repo.

**Known issues / follow-ups:**
- Multi-monitor/DPI runtime behavior and transparent-pixel click-through need manual verification.
- Plan lightweight blinking/talking/thinking/listening animations preserving approved character appearance (no unrelated expression artwork generation).
- Public repo must exclude references, character artwork, and local desktop captures; placeholder fallback required.

## 2026-11-07 — `/task` autonomy slice (experimental, partial)

**Status:** partial/experimental milestone; **not full Phase 2 completion**. No live provider/planner production run with configured credential yet. No full vision fallback (Anthropic). Git has no commits/remote; **no push**. Optional local artwork is excluded from the public repo.

**Implemented (inspected in code):**
- `/task <goal>` handling in `AssistantOrchestrator` when `TaskCoordinator` is available.
- `TaskCoordinator` phases: Created → Planning → AwaitingPermission → Executing → Observing → Verifying → Completed/Failed/Cancelled; 2-minute task limit, 20-second step limit; emergency stop latch with `Stop()`/`ResetStop()`; reset cannot revive an already stopped task.
- Exact allowlisted planner tools/grammar (`TaskPlanParser`): `launch_app` (notepad/calculator/explorer/paint), `open_url` (safe HTTPS), `brave_search` (query), `create_note` (flat `.txt` + literal text), `open_note` (must be created earlier in same plan). Fail-closed validation; no replay.
- Approval/workspace: `WpfTaskApproval` provides independent WPF approval, exact plan display (including exact note text), and disposable workspace selection for note plans.
- `SafeTaskExecutor`: dispatch-only launch/open/search where applicable; `create_note` uses `FileMode.CreateNew`, reads back contents, and reports verification on disk (no UI typing claimed); `open_note` opens via Notepad when available. Workspace/path safety checks reject traversal/reparse-point workspace paths.
- `TaskMetadataJournal`: metadata only (id, phase, stepIndex, updatedUtc) in `task-latest.json`; atomic temp+move write; no prompt/plan/target/text/credentials; **never read for replay**. Records in-flight executing step before dispatch.

**Evidence/checks (reported):**
- Baseline independently (leader): **296 passed**.
- Tester actual: **324 pass** via `dotnet test Capri.sln -c Debug --no-restore -p:OutputPath=bin/AutonomyQA/` **before journal atomic fix**.
- Security targeted fix build: **0 warnings, 0 errors**.
- Real disposable note create verification/no-overwrite: **tested**.

**Limitations/blockers:**
- No live provider/production WPF approval or real multi-step UI demonstration.
- Launch/open/search outcomes are dispatch-only (not independently UI-verified); Brave search requires supported Brave install.
- **Cancellation may leave a created note; no automatic delete/rollback.** No automatic replay.
- Normal Debug output locked by running app; alternate builds/outputs used only for current validation.
- Full vision fallback (Anthropic) not implemented.
- Re-run/record evidence after journal atomic fix in the target validation/release configuration as needed.
