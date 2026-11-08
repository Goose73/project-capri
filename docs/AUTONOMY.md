# Autonomy slice (`/task` + `capri task`, experimental, partial)

This document describes the **experimental autonomy slice** implemented in the current local workspace (WPF `/task` and CLI `capri task`, M4). It is **not** a completed autonomy/request-fulfillment milestone.

## Status

**Hardening boundary:** cooperative single-owner foundation only, not full M5 per-user IPC authority/shared approvals. Same-user path-swap TOCTOU remains a trust limitation; no malicious same-user protection claimed. Prior independent QA reported 577 Release tests passing (including two actual subprocess tests), zero warnings, but not live acceptance. An old requested disposable workspace exists but is empty; that is no evidence of completion and not proof that no plan/approval ever occurred. See [HARDENING.md](HARDENING.md).

- **Milestone:** partial / experimental; **not full Phase 2 completion**.
- **Current verified state (2026-11-08, local publication prep):** **585/585 automated tests pass** (Release, 0 failed, exit 0, after setup fixes). **Release build succeeds (0 warnings, 0 errors).** **Gitleaks** scan of staged M4 publication candidates: **no leaks found**. Prior independent QA reported 577 Release tests passing plus 2 separate process tests. Setup changes recorded as diagnostics-only (no saves), guarded provisional saves, exact tag matching; reduced motion persisted; Release launcher and CI smoke recorded.
- **M3 real local inference verified (CLI, no API key):** Ollama v0.40.9 with approved `qwen3:4b-instruct` (SHA verified). `capri models list/recommend/use`, `providers test` (actual inference, exit 0), and `capri ask` (genuine local response) verified. `capri chat` correctly refuses non-interactive stdin. This confirms the local model/inference path.
- **M4 terminal autonomy (implemented, partially verified):** reuses existing `TaskCoordinator`/`TaskPlanParser`/`SafeTaskExecutor`/journal (no separate autonomous engine). CLI adds `capri task "<request>"` (real configured provider planning via existing coordinator), `capri tasks`, `capri task status <id>`, `capri task stop`, `capri task reset-stop`, `capri task history`, help/dashboard updates, and terminal approval UI. Automated tests and safe CLI control checks are verified (including non-interactive refusal and `--json` stop/reset control checks). **Live supervised acceptance NOT completed by the agent** - a real local-model Ollama task that generates a genuine plan, receives *explicit user approval*, creates/verifies a note in a disposable workspace, opens Notepad, and observes the window has **not** been run/approved automatically. Manual supervised steps are required.
- **WPF autonomy behavior (implemented in code):** route-aware budget/caps, `response_format json_object` hint when requested, and **exactly one bounded planner repair before approval only** (never after approval/dispatch). Existing fail-closed plan/approval/workspace/journal/no-replay constraints still apply. Desktop is wired to shared task control.
- **Not yet verified/complete:** interactive TTY `capri chat` multi-turn/streaming/Ctrl+C cancellation (manual Windows Terminal acceptance), desktop GUI regression in this slice, locally planned/approved supervised WPF `/task` and/or CLI `capri task` end-to-end acceptance, M2 guided setup manual acceptance, wake-word/"Hey Capri", installer.
- **Prior evidence (not re-run):** Windows-only mock-planner acceptance passed (`windowObserved=true`, mock planner, no live inference).
- **No full vision fallback (Anthropic) implemented.** Screen vision is **architecture only** ([SCREEN-CONTROL-PLAN.md](SCREEN-CONTROL-PLAN.md)); **no implemented vision claim.**
- Optional local artwork is excluded from the public repo. **Never copy OpenCode/browser tokens. No automatic model downloads by Capri.**

## Usage

### WPF `/task`
In the **desktop** chat input, prefix a request with `/task` (case-insensitive), followed by the task description:

```text
/task create a note called plan.txt in the selected workspace with the text "hello"
```

Behavior (from `AssistantOrchestrator`): `/task` is handled as a dedicated turn when `TaskCoordinator` is available; the goal is `input[5..].Trim()` after `/task`. Progress is streamed as orchestrator updates (planning/executing/observing/verifying progress, `Completed`, or `Failed`/`Cancelled`). If the task service is unavailable, the orchestrator reports `Task service unavailable.`

### CLI `capri task` (M4, implemented)
```powershell
capri task "<request>"                 # interactive terminal approval required
capri tasks                            # metadata status/history listing
capri task status <id>
capri task stop [--json]
capri task reset-stop [--json]
capri task history
```

`capri task` uses real configured provider planning via the existing coordinator. **Non-interactive/piped execution is refused for safety** (verified: exit 2, "requires an interactive terminal for safe approval. No actions were dispatched"). `capri chat` remains a separate path and correctly refuses non-interactive stdin for chat.

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

### WPF
`ITaskApproval.ApproveAsync` returns a selected existing disposable workspace for note plans, or `null` to deny. WPF implementation (`WpfTaskApproval`) provides independent user decision (no remembered/model-supplied consent), warns to select an **existing, disposable folder for NEW note files only**, shows exact plan and exact note text, and offers **Approve exact plan** / **Deny**. Denial performs zero dispatched actions. Cancellation can throw `OperationCanceledException`.

### CLI terminal approval (M4)
The exact plan is shown before execution (request, provider/model, actions in order, exact targets/filenames/destinations, complete proposed note text, read-only vs modifies, limitations/uncertainty). Approval requires explicit interactive confirmation (`approve PLAN` intent as implemented) and **defaults to rejection** for non-interactive/EOF/timeout/cancelled input. Piped/non-interactive `capri task` refuses with no dispatch (verified: exit 2). Workspace selection is required for note plans (existing disposable directory). No untrusted model output is executed directly; allowlist/parser bounds are preserved.

## Workspace

Workspace is selected by the user during approval for note plans (not invented by the model). `TaskCoordinator` requires a selected, fully qualified workspace for note steps. `SafeTaskExecutor` enforces safe filename/path rules and rejects workspace paths where root/ancestors are reparse points. Workspace is used only for bounded note operations.

## Stop / reset / cross-process control (M4)

- **In-process:** `TaskCoordinator.Stop()` latches and increments generation; `ResetStop()` clears the latch only. `CheckStop` fails closed if cancelled, latched, or generation mismatched. **Reset cannot revive an already stopped/cancelled task** (generation history unchanged).
- **UI/CLI controls:** WPF `EmergencyStop` latches+cancels active turn; `ResetTaskStop` clears latch for future tasks only. CLI `capri task stop`/`reset-stop` (including `--json`) control shared state; `--json` stop/reset verified to latch/reset shared state safely.
- **Cross-process coordination:** file-based per-user task control/history under Capri data root (`tasks/` metadata-only, atomic bounded JSON, no secrets/prompts/plan/targets/note text). Provides single-active-task ownership, shared stop latch/reset generation, and metadata history/status. `TaskCoordinator` can honor shared stop; desktop is wired to shared control.
- **Honest limitation:** cross-process stop is effective at coordinator checkpoints/queued work; it **cannot forcibly interrupt an in-flight blocking operation or undo already-dispatched external actions**. Emergency stop cancels queued work; dispatched external actions may already have occurred. Reset never revives/replays cancelled tasks.

## Task metadata / journal

`TaskMetadataJournal` records **metadata only**: task `id`, `phase`, `stepIndex`, `updatedUtc` in `task-latest.json` (one bounded latest-state slot). **No prompt, plan, target, text, or credentials** stored. Writes are atomic/same-directory replace (temp + move) with bounded retry on transient publish errors; never retry an external task action. Records in-flight executing step before dispatch.

## No replay

The journal is **never read for replay**. Coordinator errors/cancellation emit "No automatic replay." No automatic retry, resume, or re-dispatch of journaled steps.

## Cancellation and failure caveats

Cancellation/failure do not replay steps. **Cancellation may leave a created note** if `create_note` completed before cancellation was observed; **no automatic delete/rollback**. Launch/open/search actions are dispatch-only and not independently verified as completed UI outcomes.

## Evidence / checks (reported/verified)

- **Current (2026-11-08, after setup fixes):** **585/585 automated tests pass** (Release, 0 failed, exit 0). **Release 0 warnings, 0 errors.** **Gitleaks** no leaks found (staged M4 publication candidates). Prior independent QA: 577 Release tests + 2 separate process tests. Historical: 296 -> 324 -> 329 -> 337 -> 339 -> 417 -> 540 -> 570.
- **M4 safe CLI control checks:** piped/non-interactive `capri task` refuses with no dispatch (exit 2). `capri task stop --json` / `reset-stop --json` verified to latch/reset shared state safely.
- **M3 real local CLI proof:** Ollama v0.40.9, `qwen3.5:4b` + `qwen3:4b-instruct` installed (approved pull, SHA verified). `capri models list/recommend/use`, `providers test` (actual inference, 1465 ms warm; probe text not matched exactly), `capri ask` genuine response (exit 0). `capri chat` refuses non-interactive stdin. Confirms local inference path.
- **Prior mock-planner acceptance (not re-run):** `Capri.Acceptance windows-only` PASS, `windowObserved=true` (mock planner, no live inference).

> Numbers above are reported/verified evidence for specific slices; they are **not** a claim that full Phase 2 or all full-request autonomy behavior is complete or production-validated.

## Blockers / follow-ups

- **M4 live supervised acceptance (manual, required):** real local-model Ollama `capri task` generating a genuine plan, receiving *explicit user approval*, creating/verifying a note in a disposable workspace, opening Notepad, and observing the window. **Not completed by the agent; do not claim end-to-end live task succeeded.**
- **Manual CLI acceptance:** interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation (Windows Terminal).
- **Supervised WPF `/task` acceptance as needed:** real local model verified via CLI; locally planned/approved end-to-end demonstration may still need explicit acceptance.
- **M2 guided setup:** implemented in the CLI; manual terminal/Desktop acceptance pending.
- **Local inference timeouts:** `providers test`/cold local inference can hit 60s default; local timeout may need tuning (300s used for local testing).
- Broader repeatable verification for dispatch-only actions; full vision fallback (Anthropic) remains unimplemented (screen vision **architecture only**).
- Cloud validation optional/explicit consent: OpenRouter/OpenAI can incur charges; OpenRouter off by default, `:free` heuristic guards apply but verify pricing. **Never copy OpenCode/browser tokens.**
- Not implemented: LM Studio loaded-model metadata, automatic fallback routing UI beyond guards, Sign in with ChatGPT official integration, wake-word/"Hey Capri", installer. Desktop GUI regression not re-run in this slice.
