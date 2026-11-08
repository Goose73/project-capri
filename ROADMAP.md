# Roadmap

Project Capri is an AI-powered Windows desktop companion (C# / WPF / .NET 10). This roadmap reflects **current** priorities and may change.

## Current focus
- [ ] **Desktop companion & overlay polish** — idle artwork integrated locally as an optional gitignored asset; overlay sizing/position/persistence/monitor-DPI improvements implemented and locally verified. *Needs manual multi-monitor/DPI/hit-test confirmation.* Optional local artwork is excluded from the public repo.
- [ ] **Avatar expressions & animations** — plan lightweight blinking/talking/thinking/listening animations that preserve the established character appearance (no unrelated expression artwork generation).
- [ ] **`/task` autonomy slice (experimental, partial) — Phase 2 progress recorded 2026-11-08, not full Phase 2 completion** — implemented local slice plus Windows-only validation evidence. Exact tool allowlist/planner grammar, independent WPF approval/workspace selection, stop/reset latch (UI controls exist; reset cannot revive an already stopped/cancelled task), metadata-only atomic journal (bounded retries), no replay. Reported tests: **324 → 329 → 337 → 339**; final recorded result **339 tests passed**. Earlier evidence: **337** full suite passed; **7** real in-process WPF approval tests repeated **3×**; concurrent journals covered; recorded debugger builds **0** warnings/errors. `tools/Capri.Acceptance` `windows-only` (mock planner, clearly labeled, `planner=mock-fixed-plan-not-live-inference`): earlier runs showed `windowObserved=false` then timeout/cancel; final interactive JSON evidence **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true, workspaceCreated=true, timedOut=false**. Real WPF confirmation was provided by the user; exact new file readback and a trusted Notepad title/package window were observed (**not** editor contents/AI). Fixes recorded: `GetPackageFamilyName` count handling (terminating NUL), approval foreground/taskbar/budget handling, official package family `Microsoft.WindowsNotepad_8wekyb3d8bbwe` + exact title + HWND snapshot. **Still not full Phase 2 completion:** no live provider/planner production run with configured credential (`config-status`: `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`, **no live AI**); dispatch-only actions not independently UI-verified beyond specific observed cases; cancellation may leave a created note (no automatic delete); no full vision fallback (Anthropic). Running user app never intentionally stopped. Git: no commits/remote, **no push**. See [docs/AUTONOMY.md](docs/AUTONOMY.md).

## Planned
- [ ] **Voice recognition & speech synthesis** — local/lightweight options preferred; clear mic indicators.
- [ ] **Wake-word detection** — “Hey Capri” (optional).
- [ ] **AI provider integrations & model fallback** — capability-aware routing/fallback (OpenAI-compatible, Anthropic, Gemini, OpenRouter adapters as configured; Ollama/LM Studio); verify fallback behavior.
- [ ] **`/task` live validation & autonomy expansion** — live provider/planner run with configured credential and real multi-step UI demo (`config-status` currently: `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`, **no live AI**). Settings path: tray **Settings → AI providers** → Base URL `https://api.openai.com/v1`, supported model such as `gpt-4o-mini`, API key in PasswordBox, **Save**, **Test Connection** (health only, not planner). Never share API keys in chat; ChatGPT Plus is not the same as API access. Stronger outcome verification for dispatch-only actions; broader safe tool registry; full vision fallback (Anthropic) where appropriate.
- [ ] **Windows screen recognition & control** — **architecture only** in [`docs/SCREEN-CONTROL-PLAN.md`](docs/SCREEN-CONTROL-PLAN.md): UIA-first, sensitive exclusions, explicit capture consent, untrusted observations, typed tool permissions (verify/cancel, no replay). **No implemented vision claim.** Future work: permissioned capture + vision model integration.
- [ ] **Mouse & keyboard automation** — permission-controlled, observe→plan→act→verify; typed tool registry.
- [ ] **Brave/browser integration** — open/search/navigate/summarize with consent; no credential capture.
- [ ] **Media controls** — expand from current media-key baseline as needed.
- [ ] **Settings & customization** — personality, voice/avatar packs (user-provided/licensed), startup prefs, appearance.
- [ ] **Security & permissions** — audit history, per-tool permissions, confirmations, emergency stop.
- [ ] **Performance optimization** — low idle CPU/GPU; optional local models with hardware estimates.
- [ ] **Windows installer & release** — packaging, docs, release-readiness review. Git has **no commits/remote** and **no push**.

## Completed foundations
- [x] Phase 1 MVP foundation: WPF tray/companion, chat, OpenAI-compatible provider integration, DPAPI credential storage, settings, local automation intents, test suite.
- [x] Image-backed sprite renderer with placeholder fallback (optional local artwork excluded from the public repo).
- [x] Idle artwork integrated locally as an optional gitignored asset; overlay polish pass implemented.
