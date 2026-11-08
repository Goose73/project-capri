# `/task` autonomy slice (experimental, partial)

This document describes the **experimental `/task` autonomy slice** implemented in the current local workspace. It is **not** a completed autonomy/request-fulfillment milestone.

## Status

- **Milestone:** partial / experimental; **not full Phase 2 completion** (Phase 2 progress recorded **2026-11-08**).
- **No live provider/planner production run with configured credential yet.** Recorded `config-status`: `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`; **no live AI** requests in recorded validation.
- **No full vision fallback (Anthropic) implemented.** Screen vision is **architecture only** ([`SCREEN-CONTROL-PLAN.md`](SCREEN-CONTROL-PLAN.md)); **no implemented vision claim**.
- **Git:** no commits/remote; **no push**.
- Optional local artwork is excluded from the public repo. Running user app **never intentionally stopped**.

## Usage

In chat input, prefix a request with `/task` (case-insensitive), followed by the task description:

```text
/task create a note called plan.txt in the selected workspace with the text "hello"
```

Behavior (from `AssistantOrchestrator`):
- `/task` is handled as a dedicated turn when `TaskCoordinator` is available.
- The goal is `input[5..].Trim()` after `/task`.
- Progress is streamed as orchestrator updates:
  - planning/executing/observing/verifying progress → `ToolExecuted`-style progress text,
  - `Completed` phase → `Completed`,
  - `Failed`/`Cancelled` phase → `Failed` with the progress message.
- If the task service is unavailable, the orchestrator reports `Task service unavailable.`

Normal deterministic automation intents and chat streaming remain separate paths; model output is never parsed as automation intent.

## Exact tool list (planner grammar)

The planner prompt requests **only** one JSON object with `steps` (1–8). `TaskPlanParser` accepts only these tools and enforces fail-closed validation:

| Planner tool | `TaskTool` | Target | Text | Notes / validation |
|---|---|---|---|---|
| `launch_app` | `LaunchApp` | `notepad` \| `calculator` \| `explorer` \| `paint` | not allowed | App must be in the fixed allowlist. Launch is **dispatch-only** (window not verified). |
| `open_url` | `OpenUrl` | safe absolute HTTPS URL | not allowed | Must be absolute `https`, no userinfo, no `@`, no `\`, no whitespace. Dispatched to default browser; page not verified. |
| `brave_search` | `BraveSearch` | query (≤ 200 chars) | not allowed | Uses Brave only if found in supported install locations; dispatched with encoded query to Brave Search; results not verified. If Brave unavailable, no search dispatched. |
| `create_note` | `CreateNote` | flat `.txt` filename | required, literal text (≤ 4096 chars) | Requires approved workspace. Safe filename rules apply. Uses `FileMode.CreateNew` (no overwrite) and verifies contents on disk. **No UI typing claimed.** |
| `open_note` | `OpenNote` | filename created earlier in the same plan | not allowed | Requires approved workspace and that the file was created earlier in the **same plan**. Opens via Notepad if available; editor contents not verified. Rejects reparse points/unavailable files. |

Plan shape constraints: root object must contain only `steps`; each step allows only `tool`, `target`, and optional `text` (only for `create_note`); target is 1–2048 chars with no control chars; plan JSON is rejected if empty/too long (> 16000 chars); duplicate created note names rejected.

If a request cannot be expressed with these tools, the planner is instructed to return `{"steps":[]}` (parser requires 1–8 steps, so an empty plan is treated as invalid/fail-closed by parsing rules).

## Approval

`ITaskApproval.ApproveAsync` returns a selected existing disposable workspace for note plans, or `null` to deny.

WPF implementation (`WpfTaskApproval`):
- Marshals the complete permission flow onto the WPF dispatcher (independent user decision; no remembered or model-supplied consent).
- For plans containing `CreateNote`/`OpenNote`, warns the user to select an **existing, disposable folder for NEW note files only** and never a folder containing important files; shows a folder picker (`OpenFolderDialog`). Cancel denies.
- Shows a dedicated selectable, scrollable approval dialog with exact plan details and, for note steps, the **exact note text**.
- Buttons: **Approve exact plan** / **Deny**. Denial performs zero dispatched actions.
- Cancellation can close the dialog and throw `OperationCanceledException`.

**Not yet demonstrated:** live provider-driven run with production WPF approval and real multi-step UI.

### Windows-only acceptance harness (Phase 2 evidence, 2026-11-08)

`tools/Capri.Acceptance` provides a **Windows-only** harness (runs separately from the production app; never acquires its mutex or stops its processes):
- **Command/mode:** `Capri.Acceptance windows-only` (mock planner; real approval and Windows execution). **Mock planner clearly labeled** (`planner=mock-fixed-plan-not-live-inference`); no live inference, no credential inspection in this mode.
- Uses normal parser/exact-plan gate, real `WpfTaskApproval`, `TaskCoordinator`, `SafeTaskExecutor`, and Windows observer.
- **Final interactive run evidence (JSON):** **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true, workspaceCreated=true, timedOut=false**.
- **Observed/confirmed:** real WPF confirmation was provided by the user; the exact new file was read back and a trusted Notepad title/package window was observed. This confirms window identity/presence, **not** editor contents or AI-generated content.
- **Earlier runs (context):** initial run `Completed`, `approvalShown=true`, `diskVerified=true`, `windowObserved=false`, exit 1; after a modern Notepad handoff fix, approval waited twice and timed out (`Cancelled`, no disk/window observation).
- **Fixes recorded:** `GetPackageFamilyName` handling corrected for a count that includes the terminating NUL; approval foreground/taskbar behavior and approval budget handling fixed; Notepad observation used official package family `Microsoft.WindowsNotepad_8wekyb3d8bbwe`, exact title, and HWND snapshot logic (`WindowObservation`/`SafeTaskExecutor`).
- Recorded `config-status`: `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`; **no live AI** requests in this recorded validation. Result metadata excludes note text, destination, provider response, or credential.

## Workspace

- Workspace is selected by the user during approval for note plans; it is not invented by the model.
- `TaskCoordinator` requires a selected, fully qualified workspace when any step is `CreateNote`/`OpenNote`.
- `SafeTaskExecutor.NotePath` requires an approved, existing, fully qualified workspace and a safe note name; rejects directory traversal; rejects workspace paths where the root or any ancestor directory is a reparse point.
- Workspace is used only for the bounded note operations implemented (`create_note`/`open_note`); it is not a general file-system grant.

## Stop / reset

`TaskCoordinator` provides an emergency stop latch:
- `Stop()` sets `_stopped = true` and increments `_stopGeneration`.
- `ResetStop()` clears `_stopped` (does **not** change generation history for already-running tasks).
- `IsStopped` exposes current latch state.
- `CheckStop(generation, ct)` throws `OperationCanceledException("Emergency stop latched.")` if cancellation is requested, the stop latch is set, or the task's captured generation no longer matches.

Reset cannot revive an already stopped task; a stopped task remains cancelled/fails closed for that run.

**UI behavior/limits (`OverlayChatViewModel`):** `EmergencyStop` calls `TaskCoordinator.Stop()` and cancels the active chat/task turn, setting status to “Task STOP latched. Reset explicitly to allow tasks.” `ResetTaskStop` only calls `TaskCoordinator.ResetStop()` and sets status to “Task stop reset.” Because reset does not change generation history and the coordinator checks generation, reset **cannot revive an already stopped/cancelled task**; it only allows future task runs. Cancelled turns may show `(Cancelled)`/cancelled status in chat; external actions already dispatched may still have occurred.

## Task metadata / journal

`TaskMetadataJournal` (`ITaskJournal`, `Capri.Security`) records **metadata only**:
- Stored fields: task `id` (GUID), `phase` (string), `stepIndex` (int), `updatedUtc`.
- File: `task-latest.json` in `ICapriPaths.Root` (one bounded latest-state slot).
- **No prompt, plan, target, text, or credentials** are stored.
- Writes are atomic/same-directory replace: writes to a temporary file then `File.Move(..., overwrite: true)` with bounded retry on transient `IOException`/`UnauthorizedAccessException` (publish metadata only; never retry an external task action); temp cleanup on failure. This avoids leaving a partially written latest snapshot.
- The journal records an in-flight executing step **before** dispatch.
- Concurrent journal coverage is included in reported Phase 2 test evidence.

## No replay

- The journal is **never read for replay**.
- Coordinator errors/cancellation paths emit “No automatic replay.”
- There is no automatic retry, resume, or re-dispatch of journaled steps.

## Cancellation and failure caveats

- Cancellation/failure do not replay steps.
- **Cancellation may leave a created note** if `create_note` completed its file operation before cancellation was observed; **no automatic delete/rollback** is performed.
- Launch/open/search actions are dispatch-only and not independently verified as completed UI outcomes.
- Real disposable note create verification/no-overwrite was tested (see evidence below); broader real desktop multi-step behavior is not fully demonstrated.

## Evidence / checks (as reported)

- Baseline (independently reported): **296 passed**.
- Tester actual (earlier): **324 pass** via `dotnet test Capri.sln -c Debug --no-restore -p:OutputPath=bin/AutonomyQA/` (run **before journal atomic fix**).
- Security targeted fix build: **0 warnings, 0 errors**.
- Real disposable note create verification/no-overwrite: **tested**.
- **Phase 2 (2026-11-08):** baselines progressed **324 → 329 → 337 → 339**. **Final recorded result:** **339 tests passed**. Earlier Phase 2 evidence: **337** full-suite tests + **7** real in-process WPF approval tests repeated **3×** + concurrent journal coverage. Recorded debugger builds: **0** warnings/errors. Running user app **never intentionally stopped**.
- **Windows-only acceptance (`Capri.Acceptance windows-only`, mock planner, `planner=mock-fixed-plan-not-live-inference`):** earlier runs showed `windowObserved=false` and later timeout/cancel. **Final interactive JSON evidence:** **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true, workspaceCreated=true, timedOut=false**. Real WPF confirmation was provided by the user; exact new file readback and a trusted Notepad title/package window were observed (**not** editor contents/AI).
- **Fixes recorded:** `GetPackageFamilyName` count handling (terminating NUL); approval foreground/taskbar and budget handling; official package family `Microsoft.WindowsNotepad_8wekyb3d8bbwe`, exact title, HWND snapshot (`WindowObservation`/`SafeTaskExecutor`).
- **Credentials/API (`config-status`):** `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`; **no live AI** requests in this recorded validation.

> Numbers above are reported evidence for this slice; they are **not** a claim that full Phase 2 or all full-request autonomy behavior is complete or production-validated.

## Blockers / follow-ups

- Live LLM provider/planner run through `/task` with configured credential and real multi-step UI demonstration. Current recorded `config-status`: `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`, **no live AI**.
- **Exact settings for live API setup (tray UI):** **Settings → AI providers** → Base URL `https://api.openai.com/v1` → supported model such as `gpt-4o-mini` → enter API key in the **PasswordBox** → **Save** → **Test Connection** (health/connection check only; does **not** run the `/task` planner). Never ask for or share API keys in chat; never include credentials in docs/source. **ChatGPT Plus is not the same as API access**.
- Broader, repeatable verification for dispatch-only actions across scenarios (windows/browser/results), beyond the specific recorded Notepad observation case.
- Full vision fallback (Anthropic) remains unimplemented; screen vision is **architecture only** ([`SCREEN-CONTROL-PLAN.md`](SCREEN-CONTROL-PLAN.md)).
- Normal Debug output may be locked by a running app; alternate builds/outputs are used only for current validation.
- **Git:** no commits/remote; **no push**. Re-run/record evidence as needed for release decisions; do not treat recorded progress as full Phase 2 completion.
