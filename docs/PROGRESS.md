# Development progress

## 2026-11-08 - Cooperative hardening follow-up (not M5 completion)

Exclusive cross-process lock, current-user ACL/reparse validation, real PID start identity, fail-closed corrupt state, shared stop monitor cancellation (coordinator cancels waits/queued work; cannot force uncooperative in-flight completion), cancellable approval input, and independent chat cancellation pump are implemented locally. Setup changes recorded: diagnostics-only path with no saves, guarded provisional saves, exact tag matching. Reduced motion is persisted; Release launcher and CI smoke are recorded.

**Latest actual verified result (after setup fixes):** Release build **0 warnings, 0 errors**; **585/585 tests passed** (exit 0). Previous independent Release QA reported **577 tests passing** with zero warnings, plus **2 separate process tests**. These are bounded automated checks, **not** live supervised task acceptance.

This is **not full M4 or M5 completion** and **not full M5 per-user IPC authority/shared approvals**. Same-user path-swap TOCTOU remains a trust limitation; no malicious same-user protection claimed. An old requested disposable workspace exists but is **empty**; that is **no evidence of completion** and **not proof that no plan/approval ever occurred**. No live M4 supervised end-to-end acceptance or manual real TTY chat acceptance occurred. See [hardening status](HARDENING.md). No source license is granted yet; the owner must choose licensing terms before granting a license/community reuse, but this is not a barrier to the owner publishing their own code.

## 2026-11-08 - M4 terminal autonomy (implemented, partially verified; live supervised acceptance pending)

**Status:** M4 terminal autonomy code implemented in the local working tree (publication prep). **Latest verified (after setup fixes): 585/585 automated tests pass** (Release, 0 failed, exit 0). **Release build succeeds (0 warnings, 0 errors).** **Gitleaks** scan of staged M4 publication candidates: **no leaks found**. Previous independent QA: 577 + 2 separate process tests.

**Implemented (M4):** reuses existing `TaskCoordinator`/`TaskPlanParser`/`SafeTaskExecutor`/journal (no separate autonomous engine). CLI adds `capri task "<request>"` (real configured provider planning via existing coordinator), `capri tasks`, `capri task status <id>`, `capri task stop`, `capri task reset-stop`, `capri task history`, help/dashboard updates, and terminal approval UI.

**Safety verified (implementation/tests + safe CLI control checks):** exact plan shown before execution (request, provider/model, actions in order, exact targets/filenames/destinations, complete proposed note text, read-only vs modifies, limitations/uncertainty). Approval requires explicit interactive confirmation (`approve PLAN` intent as implemented) and **defaults to rejection** for non-interactive/EOF/timeout/cancelled input. Piped/non-interactive `capri task` refuses with no dispatch (verified: exit 2, "requires an interactive terminal for safe approval. No actions were dispatched"). Workspace selection required for note plans (existing disposable directory), safe note names/no-overwrite preserved. No untrusted model output executed directly; allowlist/parser bounds preserved.

**Cross-process coordination (M4):** file-based per-user task control/history under Capri data root (`tasks/` metadata-only, atomic bounded JSON, no secrets/prompts/plan/targets/note text). Single-active-task ownership, shared stop latch/reset generation, metadata history/status. `TaskCoordinator` can honor shared stop; desktop wired to shared control. `capri task stop --json` / `reset-stop --json` verified to latch/reset shared state safely in this session. **Honest limitation:** stop is effective at coordinator checkpoints/queued work; it cannot forcibly interrupt an in-flight blocking operation or undo already-dispatched external actions. Emergency stop cancels queued work; dispatched external actions may already have occurred. Reset never revives/replays cancelled tasks.

**Not completed / pending:** **Live supervised acceptance NOT completed by the agent.** A real local-model Ollama task that generates a genuine plan, receives *explicit user approval*, creates/verifies a note in a disposable workspace, opens Notepad, and observes the window has **not** been run/approved automatically. Manual supervised steps are required. Do not claim that end-to-end live task succeeded.

## 2026-11-08 - CLI Milestone 1 + M3 Ollama model management/terminal inference (verified local; publication prep)

**Status:** M3 real local verification completed (no API key). Local uncommitted changes; publication prep in progress. Optional local artwork excluded. **Latest verified (after setup fixes): 585/585 automated tests pass** (Release, 0 failed, exit 0). **Release build succeeds (0 warnings, 0 errors).** **Gitleaks** scan of staged publication candidates: **no leaks found**. Previous independent QA: 577 + 2 separate process tests.

**M3 verified (real, local Ollama v0.40.9):** approved download `ollama pull qwen3:4b-instruct` completed (~2.5 GB, SHA verified). Installed: `qwen3.5:4b` (3.3 GB) and `qwen3:4b-instruct`. Real CLI proof (Ollama/`qwen3:4b-instruct`, exit 0): `capri models list`, `models recommend`, `models use qwen3:4b-instruct` (persisted), `providers test` (actual inference, 1465 ms warm; response received though probe text not matched exactly), `capri ask "What is Project Capri?"` (genuine local response). `capri chat` correctly refuses non-interactive stdin.

**Fix applied:** CLI model-management targets local Ollama when the active provider is not Ollama.

**Not yet verified/complete:** interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation need manual Windows Terminal acceptance; desktop GUI regression not re-run; M4 live supervised acceptance pending (see M4 entry above); M2 guided setup wizard implemented (automated tests pass; broader manual GUI/UX acceptance still useful); wake-word/"Hey Capri" not implemented/validated (config only); installer not built. `providers test`/cold local inference can hit timeouts (default 60s); local timeout may need tuning (300s used for local testing). OpenRouter/OpenAI live not verified. **Never copy OpenCode/browser tokens. No automatic model downloads by Capri.**

## 2026-11-08 - M1-M6 free/local provider catalog & autonomy hardening (implementation; live proof superseded by M3)

**Status:** implementation and automated verification recorded. M3 real local CLI inference provides live proof for the local Ollama path. Latest automated state (after setup fixes): **585/585 tests pass** (Release, 0 failed, exit 0). **Release build 0 warnings, 0 errors.** Previous independent QA: 577 + 2 separate process tests. Baselines: 296 -> 324 -> 329 -> 337 -> 339 -> 417 -> 540 -> 570.

**Implemented (M1-M6):** provider catalog (Ollama no key, LM Studio, OpenRouter key/cloud off by default, OpenAI optional), schema v2 (`FreeModelsOnly` default true, `AllowCloudInference` default false), profile-specific credentials with fail-closed local auth, consent/free-only guards (OpenRouter `:free`), Settings UI/VM presets/health vs inference/model discovery, acceptance config-status for local loopback. Autonomy: route-aware budget/caps, `response_format json_object` hint, exactly one bounded planner repair **before approval only**.

## 2026-11-08 - Phase 2 `/task` validation status (progress recorded; not full Phase 2 completion)

**Status:** honest progress update. **Full Phase 2 completion criteria are not all met.** Optional local artwork excluded.

**Test/build evidence (current + historical):** **585/585 tests pass** (Release, 0 failed, exit 0, after setup fixes, latest), **Release 0 warnings, 0 errors**. Previous independent QA: 577 + 2 separate process tests. Historical baselines: 296 -> 324 -> 329 -> 337 -> 339 -> 417 -> 540 -> 570.

**Windows-only acceptance harness (`tools/Capri.Acceptance`, mode `windows-only`, prior milestone, not re-run):** mock planner clearly labeled (`planner=mock-fixed-plan-not-live-inference`), no live inference. Final JSON evidence: **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true** (window identity/presence and exact file readback only; **not** editor contents/AI).

**M3/M4 note:** real local CLI inference is verified (M3); `capri task` terminal autonomy is implemented/partially verified (M4) but live supervised acceptance is pending. `/task` UI stop/reset: reset **cannot revive an already stopped/cancelled task**. Screen vision: **architecture only**.

## 2026-11-07 - Desktop overlay polish

**Features (local):** idle sprite as optional gitignored asset; overlay polish (height 150-400px default ~250px, active monitor work area, drag repositioning, position/size persisted, monitor/DPI-aware clamping, chat panel repositioning). CI/docs scaffolding prepared.
**Tests/build (milestone):** Debug 0 errors; **296 passed, 0 failed, 0 skipped**. Manual multi-monitor/DPI/hit-test verification still needed.

## 2026-11-07 - `/task` autonomy slice (experimental, partial)

**Status:** partial/experimental; not full Phase 2 completion. Implemented: `/task` handling, `TaskCoordinator` phases/budgets/stop latch, fail-closed `TaskPlanParser`, exact allowlisted tools, independent WPF approval/workspace selection, `SafeTaskExecutor` bounded dispatch/verification, metadata-only `TaskMetadataJournal` (atomic, no replay). **Update (2026-11-08):** M3 verified real local CLI inference (Ollama); M4 implemented terminal `capri task` (partially verified, live supervised acceptance pending). Launch/open/search dispatch-only beyond specific observed cases; cancellation may leave a created note; reset cannot revive stopped/cancelled task; full vision fallback not implemented.
