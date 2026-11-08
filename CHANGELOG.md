# Changelog

All notable changes to **Project Capri** are documented here. This project is under active development.

## [Unreleased] (publication prep, local uncommitted)

### Added
- **CLI Milestone 1 + M3 Ollama model management/terminal inference (M3 real local proof verified):** `capri start/ask/chat/status/logs/providers test/models list|recommend|use|pull|remove`. `capri chat` supports single-shot use and correctly refuses non-interactive stdin (interactive TTY multi-turn/streaming/Ctrl+C in-session cancellation require manual Windows Terminal acceptance). `capri task` / terminal autonomy **not implemented** (M4; CLI approval pending).
- **M1-M6 free/local provider catalog (M3 live local proof verified):** Ollama/LM Studio local (no keys sent), OpenRouter/OpenAI cloud (key/consent; OpenRouter off by default). Schema v2 additive (`FreeModelsOnly` default true, `AllowCloudInference` default false). Profile-specific credentials `provider.{profileId}.apiKey`, fail-closed local auth. Legacy OpenAI key migration. Consent/free-only guards (OpenRouter `:free`). Settings UI/VM presets, health check vs test inference, model discovery `/v1/models`. Autonomy: route-aware budget/caps, `response_format json_object` hint, exactly one bounded planner repair before approval only.
- Image-backed Capri sprite renderer with state fallbacks; placeholder when optional local artwork is excluded (not in public repo).
- Desktop overlay polish: ~250px default (150-400px), active-monitor placement, drag/position/size persistence, monitor/DPI-aware clamping.
- GitHub progress-tracking scaffolding: `CHANGELOG.md`, `ROADMAP.md`, `docs/PROGRESS.md`, CI workflow, `.gitignore` updates.
- **Experimental WPF `/task` autonomy slice (partial):** `/task` handling, `TaskCoordinator` phases, fail-closed `TaskPlanParser`, exact allowlisted tools, independent WPF approval/workspace selection, emergency stop/reset latch, metadata-only `TaskMetadataJournal` (atomic latest-state, bounded retry; never read for replay), no automatic replay. See `docs/AUTONOMY.md`.
- **Phase 2 validation tooling/evidence context (preserved):** `tools/Capri.Acceptance` Windows-only harness (mock planner clearly labeled, `planner=mock-fixed-plan-not-live-inference`, no live inference). Screen-control design in `docs/SCREEN-CONTROL-PLAN.md` (**architecture only**).

### Changed
- CLI model-management now targets local Ollama when the active provider is not Ollama (fix applied).
- Provider/settings/autonomy/CLI code paths updated for multi-profile catalog, consent/free-only defaults, bounded planner repair, and terminal model management (local uncommitted).
- `UiSettings` extended additively (`CharacterHeight`, `CharacterLeft`, `CharacterTop`) for overlay persistence.
- `/task` UI exposes Emergency Stop / Reset Task Stop; reset clears the latch for future tasks but **cannot revive an already stopped/cancelled task**.
- Prior-milestone acceptance/observation fixes preserved: `GetPackageFamilyName` NUL-count handling, approval foreground/taskbar/budget handling, Notepad observation (package family `Microsoft.WindowsNotepad_8wekyb3d8bbwe`).

### Verified
- **M3 real local Ollama proof (this machine, 2026-11-08, no API key):** Ollama **v0.40.9**. Approved `ollama pull qwen3:4b-instruct` completed (~2.5 GB, SHA verified). Installed: `qwen3.5:4b` (3.3 GB) and `qwen3:4b-instruct`. Real CLI proof (Ollama/`qwen3:4b-instruct`, exit 0): `capri models list`, `models recommend`, `models use qwen3:4b-instruct` (persisted), `providers test` (actual inference, 1465 ms warm; response received though probe text not matched exactly), `capri ask "What is Project Capri?"` (genuine local response). `capri chat` correctly refuses non-interactive stdin.
- **Windows-only mock-planner acceptance (prior milestone, preserved; not re-run):** PASS with `windowObserved=true` (mock planner, no live inference).
- OpenRouter/OpenAI live not verified (no keys/consent). No model downloads performed by Capri; approved user-initiated pull recorded above.

### Known issues / notes
- **Current local verified state (2026-11-08):** **540/540 automated tests pass** (Release, 0 failed). **Release build succeeds (0 warnings/errors).** **Gitleaks 8.30.1** scan of publication candidates: **no leaks found**. Historical baselines: 296 -> 324 -> 329 -> 337 -> 339 -> 417 -> 540.
- **M3 real local Ollama proof (this machine, 2026-11-08, no API key):** Ollama **v0.40.9**. Approved `ollama pull qwen3:4b-instruct` completed (~2.5 GB, SHA verified). Installed: `qwen3.5:4b` (3.3 GB) and `qwen3:4b-instruct`. Real CLI proof (Ollama/`qwen3:4b-instruct`, exit 0): `capri models list`, `models recommend`, `models use qwen3:4b-instruct` (persisted), `providers test` (actual inference, 1465 ms warm; response received though probe text not matched exactly), `capri ask "What is Project Capri?"` (genuine local response). `capri chat` correctly refuses non-interactive stdin.
- **Windows-only mock-planner acceptance (prior milestone, preserved; not re-run):** PASS with `windowObserved=true` (mock planner, no live inference).
- OpenRouter/OpenAI live not verified (no keys/consent). No model downloads performed by Capri; approved user-initiated pull recorded above.

### Known issues / notes
- `providers test`/cold local inference can hit timeouts (default 60s); local timeout may need tuning (300s used for local testing).
- **Cloud options:** OpenRouter/OpenAI are cloud/keyed; OpenRouter is off by default and can incur charges despite free-model guards - verify consent and model pricing. **Never copy OpenCode/browser tokens.**
- **WPF `/task`** is experimental/partial: launch/open/search actions dispatch-only beyond specific observed cases. Cancellation may leave a created note (no automatic delete). Reset cannot revive stopped/cancelled task. **Terminal `capri task` (M4) not implemented.**
- Not implemented: hardware/model download wizard, LM Studio loaded-model metadata, automatic fallback routing UI beyond guards, Sign in with ChatGPT official integration. Screen vision is **architecture only**.
- Optional local artwork excluded from the public repo; app falls back to placeholder. Multi-monitor/DPI/hit-testing requires manual verification.
- Local uncommitted changes exist (publication prep). Do not invent a published tag/SHA.

## [0.1.0] - Phase 1 MVP foundation (local)
- **Not yet verified/complete:** interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation need manual Windows Terminal acceptance; desktop GUI regression not re-run; `capri task`/terminal autonomy not implemented (M4, CLI approval mechanism pending); M2 guided setup not implemented; wake-word/"Hey Capri" not implemented/validated; installer not built.
- `providers test`/cold local inference can hit timeouts (default 60s); local timeout may need tuning (300s used for local testing).
- **Cloud options:** OpenRouter/OpenAI are cloud/keyed; OpenRouter is off by default and can incur charges despite free-model guards - verify consent and model pricing. **Never copy OpenCode/browser tokens.**
- **WPF `/task`** is experimental/partial: launch/open/search actions dispatch-only. Cancellation may leave a created note (no automatic delete). Reset cannot revive stopped/cancelled task. **Terminal `capri task` (M4) not implemented.**
- Not implemented: hardware/model download wizard, LM Studio loaded-model metadata, automatic fallback routing UI beyond guards, Sign in with ChatGPT official integration. Screen vision is **architecture only**.
- Optional local artwork excluded from the public repo; app falls back to placeholder. Multi-monitor/DPI/hit-testing requires manual verification.
- Local uncommitted changes exist (publication prep). Do not invent a published tag/SHA.

## [0.1.0] - Phase 1 MVP foundation (local)

### Added
- .NET 10 / WPF solution: `Capri.Core`, `Capri.Providers`, `Capri.Security`, `Capri.Automation`, `Capri.Voice`, `Capri.Desktop`, `Capri.Tests`.
- Streaming/non-streaming OpenAI-compatible chat with SSE parsing, bounded retry/error mapping, request timeout handling.
- DPAPI-backed credential storage; settings persistence; provider settings UI with secure `PasswordBox` key handling and non-persisting connection probe.
- Deterministic local automation intents (URL/app launch, media keys) with URL/app safety checks and permission gating.
- Tray app + transparent character companion window + compact chat panel; avatar state wiring.
- Automated test suite (296 tests at last recorded milestone).
