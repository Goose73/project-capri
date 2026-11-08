# Development progress

## 2026-11-08 - CLI Milestone 1 + M3 Ollama model management/terminal inference (verified local; publication prep)

**Status:** M3 real local verification completed (no API key). Local uncommitted changes; publication prep in progress. Optional local artwork excluded. **540/540 automated tests pass** (Release, 0 failed). **Release build succeeds (0 warnings/errors).** **Gitleaks 8.30.1** scan of publication candidates: **no leaks found**.

**M3 verified (real, local Ollama v0.40.9):** approved download `ollama pull qwen3:4b-instruct` completed (~2.5 GB, SHA verified). Installed: `qwen3.5:4b` (3.3 GB) and `qwen3:4b-instruct`. Real CLI proof (Ollama/`qwen3:4b-instruct`, exit 0): `capri models list`, `models recommend`, `models use qwen3:4b-instruct` (persisted), `providers test` (actual inference, 1465 ms warm; response received though probe text not matched exactly), `capri ask "What is Project Capri?"` (genuine local response). `capri chat` correctly refuses non-interactive stdin.

**Fix applied:** CLI model-management targets local Ollama when the active provider is not Ollama.

**Not yet verified/complete:** interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation need manual Windows Terminal acceptance; desktop GUI regression not re-run; `capri task`/terminal autonomy not implemented (M4, CLI approval mechanism pending); M2 guided setup not implemented; wake-word/"Hey Capri" not implemented/validated; installer not built. `providers test`/cold local inference can hit timeouts (default 60s); local timeout may need tuning (300s used for local testing). OpenRouter/OpenAI live not verified. **Never copy OpenCode/browser tokens. No automatic model downloads by Capri.**

## 2026-11-08 - M1-M6 free/local provider catalog & autonomy hardening (implementation; live proof superseded by M3)

**Status:** implementation and automated verification recorded. M3 real local CLI inference provides live proof for the local Ollama path. **540/540 automated tests pass** (Release, 0 failed). **Release build 0 warnings/errors.** Baselines: 296 -> 324 -> 329 -> 337 -> 339 -> 417 -> 540.

**Implemented (M1-M6):** provider catalog (Ollama no key, LM Studio, OpenRouter key/cloud off by default, OpenAI optional), schema v2 (`FreeModelsOnly` default true, `AllowCloudInference` default false), profile-specific credentials with fail-closed local auth, consent/free-only guards (OpenRouter `:free`), Settings UI/VM presets/health vs inference/model discovery, acceptance config-status for local loopback. Autonomy: route-aware budget/caps, `response_format json_object` hint, exactly one bounded planner repair **before approval only**.

## 2026-11-08 - Phase 2 `/task` validation status (progress recorded; not full Phase 2 completion)

**Status:** honest progress update. **Full Phase 2 completion criteria are not all met.** Optional local artwork excluded.

**Test/build evidence (current + historical):** **540/540 tests pass** (Release, 0 failed), **Release 0 warnings/errors**. Historical baselines: 296 -> 324 -> 329 -> 337 -> 339 -> 417 -> 540.

**Windows-only acceptance harness (`tools/Capri.Acceptance`, mode `windows-only`, prior milestone, not re-run):** mock planner clearly labeled (`planner=mock-fixed-plan-not-live-inference`), no live inference. Final JSON evidence: **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true** (window identity/presence and exact file readback only; **not** editor contents/AI).

**M3 note:** real local CLI inference is now verified; `capri task` terminal autonomy (M4) remains not implemented. `/task` UI stop/reset: reset **cannot revive an already stopped/cancelled task**. Screen vision: **architecture only**.

## 2026-11-07 - Desktop overlay polish

**Features (local):** idle sprite as optional gitignored asset; overlay polish (height 150-400px default ~250px, active monitor work area, drag repositioning, position/size persisted, monitor/DPI-aware clamping, chat panel repositioning). CI/docs scaffolding prepared.
**Tests/build (milestone):** Debug 0 errors; **296 passed, 0 failed, 0 skipped**. Manual multi-monitor/DPI/hit-test verification still needed.

## 2026-11-07 - `/task` autonomy slice (experimental, partial)

**Status:** partial/experimental; not full Phase 2 completion. Implemented: `/task` handling, `TaskCoordinator` phases/budgets/stop latch, fail-closed `TaskPlanParser`, exact allowlisted tools, independent WPF approval/workspace selection, `SafeTaskExecutor` bounded dispatch/verification, metadata-only `TaskMetadataJournal` (atomic, no replay). **Update (2026-11-08):** M3 verified real local CLI inference (Ollama); **`capri task` terminal autonomy (M4) not implemented**. Launch/open/search dispatch-only beyond specific observed cases; cancellation may leave a created note; reset cannot revive stopped/cancelled task; full vision fallback not implemented.
