# Roadmap

Project Capri is an AI-powered Windows desktop companion (C# / WPF / .NET 10). This roadmap reflects **current** priorities and may change.

## Current focus
- [x] **Free/local AI live validation (M3 verified, 2026-11-08)** - M1-M6 catalog implemented. Real local proof (Ollama v0.40.9, no API key): approved `ollama pull qwen3:4b-instruct` completed (~2.5 GB, SHA verified). Installed: `qwen3.5:4b`, `qwen3:4b-instruct`.
  CLI verified (exit 0): `capri models list`, `models recommend`, `models use qwen3:4b-instruct` (persisted), `providers test` (actual inference, 1465 ms warm; probe text not matched exactly), `capri ask "What is Project Capri?"` (genuine local response). `capri chat` correctly refuses non-interactive stdin. Fix: CLI model-management targets local Ollama when active provider is not Ollama. **540/540 tests pass (Release, 0 failed), Release build 0 warnings/errors, Gitleaks 8.30.1 no leaks found.**
- [x] **CLI Milestone 1 + M3 (partial, verified core path)** - `capri start/ask/chat/status/logs/providers test/models list|recommend|use|pull|remove`. Pending manual acceptance: interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation (Windows Terminal). `providers test`/cold local inference may need timeout tuning (300s used locally vs 60s default).
- [ ] **M4 terminal autonomy (not implemented)** - `capri task` with explicit CLI approval/workspace/stop controls; CLI approval mechanism pending.
- [ ] **M2 guided setup (not implemented)** - first-run/provider/model setup flow.
- [ ] **WPF `/task` autonomy slice (experimental, partial)** - not full Phase 2 completion. Route-aware budget/caps, `response_format json_object` hint, exactly one bounded planner repair before approval only. Real local model inference verified via CLI; locally planned/approved desktop `/task` end-to-end acceptance may still be needed. Prior mock-planner Windows acceptance passed (`windowObserved=true`, not re-run). Still needed: stronger dispatch-only verification, full vision fallback (Anthropic). Cancellation may leave a created note; reset cannot revive stopped/cancelled task.
- [ ] **Desktop companion & overlay polish** - needs manual multi-monitor/DPI/hit-test confirmation. Desktop GUI regression not re-run in this slice.
- [ ] **Avatar expressions & animations** - lightweight blinking/talking/thinking/listening preserving established character appearance.

## Planned
- [ ] **Voice recognition & speech synthesis** - local/lightweight options preferred; clear mic indicators.
- [ ] **Wake-word detection** - "Hey Capri" (optional; not implemented/validated).
- [ ] **Provider expansion & verified fallback routing** - beyond implemented presets: Anthropic/Gemini adapters, capability-aware fallback/routing UI beyond current guards, LM Studio loaded-model metadata. **No automatic model downloads by Capri.**
- [ ] **Cloud provider live validation (explicit consent; avoid surprise charges)** - OpenRouter/OpenAI require keys and can incur charges. OpenRouter off by default; FreeModelsOnly default true and `:free` heuristic guards apply, but verify model/pricing. **No live cloud verification in this change set.**
- [ ] **Desktop release packaging & installer** (not built).

## Completed (evidence-based milestones)
- [x] **CLI Milestone 1 + M3 core local inference** - real Ollama model management and terminal inference verified (2026-11-08); 540/540 tests (Release).
- [x] **M1-M6 provider catalog/autonomy hardening implementation** - schema v2, profile-specific credentials, fail-closed local auth, consent/free-only guards, health vs inference, model discovery (2026-11-08).
- [x] **Phase 1 MVP foundation** - WPF companion, OpenAI-compatible chat, DPAPI settings, local automation intents, automated tests (local milestone; see changelog).
