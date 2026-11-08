# `/task` autonomy slice (experimental, partial)

This document describes the **experimental `/task` autonomy slice** implemented in the current local workspace. It is **not** a completed autonomy/request-fulfillment milestone.

## Status

- **Milestone:** partial / experimental; **not full Phase 2 completion**.
- **Current verified state (2026-11-08, local publication prep):** **540/540 automated tests pass** (Release, 0 failed). **Release build succeeds (0 warnings/errors).** **Gitleaks 8.30.1** scan of publication candidates: **no leaks found**.
- **M3 real local inference verified (CLI, no API key):** Ollama v0.40.9 with approved `qwen3:4b-instruct` (SHA verified). `capri models list/recommend/use`, `providers test` (actual inference, exit 0), and `capri ask` (genuine local response) verified. `capri chat` correctly refuses non-interactive stdin. This confirms the local model/inference path; it is **not** the same as a fully verified end-to-end desktop `/task` or terminal `capri task` autonomy flow.
- **Terminal autonomy:** **`capri task` is not implemented (M4)**; CLI approval mechanism pending.
- **WPF autonomy behavior (implemented in code):** route-aware budget/caps, `response_format json_object` hint when requested, and **exactly one bounded planner repair before approval only** (never after approval/dispatch). Existing fail-closed plan/approval/workspace/journal/no-replay constraints still apply.
- **Not yet verified/complete:** interactive TTY `capri chat` multi-turn/streaming/Ctrl+C cancellation (manual Windows Terminal acceptance), desktop GUI regression in this slice, locally planned/approved desktop `/task` end-to-end acceptance as needed, M2 guided setup, wake-word/"Hey Capri", installer.
- **Prior evidence (not re-run):** Windows-only mock-planner acceptance passed (`windowObserved=true`, mock planner, no live inference).
- **No full vision fallback (Anthropic) implemented.** Screen vision is **architecture only** ([SCREEN-CONTROL-PLAN.md](SCREEN-CONTROL-PLAN.md)); **no implemented vision claim.**
- Optional local artwork is excluded from the public repo. **Never copy OpenCode/browser tokens. No automatic model downloads by Capri.**

## Usage

In the **desktop** chat input, prefix a request with `/task` (case-insensitive), followed by the task description:

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

**CLI:** `capri ask`/local inference are verified (M3), but **`capri task` (terminal autonomy) is not implemented (M4)**.

Normal deterministic automation intents and chat streaming remain separate paths; model output is never parsed as automation intent.

## Exact tool list (planner grammar)

The planner prompt requests **only** one JSON object with `steps` (1-8). `TaskPlanParser` accepts only these tools and enforces fail-closed validation:

| Planner tool | `TaskTool` | Target | Text | Notes / validation |
|---|---|---|---|---|
| `launch_app` | `LaunchApp` | notepad/calculator/explorer/paint | not allowed | Fixed allowlist. Launch is **dispatch-only** (window not verified). |
| `open_url` | `OpenUrl` | safe absolute HTTPS URL | not allowed | Absolute `https`, no userinfo/`@`/`\`/whitespace. Dispatched to default browser; page not verified. |
| `brave_search` | `BraveSearch` | query (<= 200 chars) | not allowed | Uses Brave only if found in supported install locations; dispatched with encoded query to Brave Search; results not verified. |
| `create_note` | `CreateNote` | flat `.txt` filename | required, literal (<= 4096 chars) | Requires approved workspace. `FileMode.CreateNew` (no overwrite) and verifies contents on disk. **No UI typing claimed.** |
| `open_note` | `OpenNote` | filename created earlier in same plan | not allowed | Requires approved workspace and file created earlier in the **same plan**. Opens via Notepad if available; editor contents not verified. |

Plan shape constraints: root object must contain only `steps`; each step allows only `tool`, `target`, and optional `text` (only for `create_note`); fail-closed validation; duplicate created note names rejected.

## Approval

`ITaskApproval.ApproveAsync` returns a selected existing disposable workspace for note plans, or `null` to deny. WPF implementation (`WpfTaskApproval`) provides independent user decision (no remembered/model-supplied consent), warns to select an **existing, disposable folder for NEW note files only**, shows exact plan and exact note text, and offers **Approve exact plan** / **Deny**. Denial performs zero dispatched actions. Cancellation can throw `OperationCanceledException`.

**Windows-only acceptance harness (prior milestone, preserved, not re-run):** `tools/Capri.Acceptance` `windows-only` used mock planner (clearly labeled, `planner=mock-fixed-plan-not-live-inference`, no live inference), real approval/coordinator/executor/observer. Final JSON evidence: **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true** (window identity/presence and exact file readback only; **not** editor contents/AI).

## Workspace

Workspace is selected by the user during approval for note plans (not invented by the model). `TaskCoordinator` requires a selected, fully qualified workspace for note steps. `SafeTaskExecutor` enforces safe filename/path rules and rejects workspace paths where root/ancestors are reparse points. Workspace is used only for bounded note operations.

## Stop / reset

`TaskCoordinator.Stop()` latches and increments generation; `ResetStop()` clears the latch only. `CheckStop` fails closed if cancelled, latched, or generation mismatched. **Reset cannot revive an already stopped/cancelled task** (generation history unchanged). UI: `EmergencyStop` latches+cancels active turn; `ResetTaskStop` clears latch for future tasks only. Cancelled turns may show cancelled status; external actions already dispatched may still have occurred.

## Task metadata / journal

`TaskMetadataJournal` records **metadata only**: task `id`, `phase`, `stepIndex`, `updatedUtc` in `task-latest.json` (one bounded latest-state slot). **No prompt, plan, target, text, or credentials** stored. Writes are atomic/same-directory replace (temp + move) with bounded retry on transient publish errors; never retry an external task action. Records in-flight executing step before dispatch.

## No replay

The journal is **never read for replay**. Coordinator errors/cancellation emit "No automatic replay." No automatic retry, resume, or re-dispatch of journaled steps.

## Cancellation and failure caveats

Cancellation/failure do not replay steps. **Cancellation may leave a created note** if `create_note` completed before cancellation was observed; **no automatic delete/rollback**. Launch/open/search actions are dispatch-only and not independently verified as completed UI outcomes. Real disposable note create verification/no-overwrite was tested in earlier evidence.

## Evidence / checks (reported/verified)

- **Current (2026-11-08):** **540/540 automated tests pass** (Release, 0 failed). **Release 0 warnings/errors.** **Gitleaks 8.30.1 no leaks found** (publication candidates). Historical: 296 -> 324 -> 329 -> 337 -> 339 -> 417 -> 540.
- **M3 real local CLI proof:** Ollama v0.40.9, `qwen3.5:4b` + `qwen3:4b-instruct` installed (approved pull, SHA verified). `capri models list/recommend/use`, `providers test` (actual inference, 1465 ms warm; probe text not matched exactly), `capri ask` genuine response (exit 0). `capri chat` refuses non-interactive stdin. Confirms local inference path; not full desktop `/task` or terminal `capri task` acceptance.
- **Prior mock-planner acceptance (not re-run):** `Capri.Acceptance windows-only` PASS, `windowObserved=true` (mock planner, no live inference).

> Numbers above are reported/verified evidence for specific slices; they are **not** a claim that full Phase 2 or all full-request autonomy behavior is complete or production-validated.
- **Desktop `/task` acceptance as needed:** real local model is verified via CLI; locally planned/approved WPF `/task` end-to-end demonstration may still need explicit acceptance.
- **M2 guided setup:** not implemented.
- **Local inference timeouts:** `providers test`/cold local inference can hit 60s default; local timeout may need tuning (300s used for local testing).
- Broader repeatable verification for dispatch-only actions; full vision fallback (Anthropic) remains unimplemented (screen vision **architecture only**).
- Cloud validation optional/explicit consent: OpenRouter/OpenAI can incur charges; OpenRouter off by default, `:free` heuristic guards apply but verify pricing. **Never copy OpenCode/browser tokens.**
- Not implemented: hardware/model download wizard, LM Studio loaded-model metadata, automatic fallback routing UI beyond guards, Sign in with ChatGPT official integration, wake-word/"Hey Capri", installer. Desktop GUI regression not re-run in this slice.

## Blockers / follow-ups

- **M4 terminal autonomy:** implement `capri task` with explicit CLI approval/workspace/stop controls (CLI approval mechanism pending).
- **Manual CLI acceptance:** interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation (Windows Terminal).
- **Desktop `/task` acceptance as needed:** real local model verified via CLI; locally planned/approved WPF `/task` end-to-end demonstration may still need explicit acceptance.
- **M2 guided setup:** not implemented.
- **Local inference timeouts:** `providers test`/cold local inference can hit 60s default; local timeout may need tuning (300s used for local testing).
- Broader repeatable verification for dispatch-only actions; full vision fallback (Anthropic) remains unimplemented (screen vision **architecture only**).
- Cloud validation optional/explicit consent: OpenRouter/OpenAI can incur charges; OpenRouter off by default, `:free` heuristic guards apply but verify pricing. **Never copy OpenCode/browser tokens.**
- Not implemented: hardware/model download wizard, LM Studio loaded-model metadata, automatic fallback routing UI beyond guards, Sign in with ChatGPT official integration, wake-word/"Hey Capri", installer. Desktop GUI regression not re-run in this slice.
