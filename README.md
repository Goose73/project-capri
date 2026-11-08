# Project Capri

**Project Capri** is an AI-powered Windows desktop companion (C# / WPF / .NET 10) inspired by a stylized digital character aesthetic. It aims to provide voice/text interaction, configurable AI providers, permission-controlled Windows automation, and an animated character overlay.

> **Unofficial fan-inspired project.** Not affiliated with or endorsed by CD PROJEKT RED or Netflix.

## Current development status

**Active development (local uncommitted changes; publication prep in progress).** The desktop companion, image-backed sprite renderer, chat/settings foundations, multi-provider catalog integration (M1-M6), an **experimental WPF `/task` autonomy slice**, and **CLI Milestone 1 + M3 Ollama model management/terminal inference + M4 terminal autonomy (implemented, partially verified)** are implemented in the local workspace.

**Current verified state (2026-11-08, local, after setup fixes):** **585/585 automated tests pass** (Release, 0 failed, exit 0). **Release build succeeds (0 warnings, 0 errors).** **Gitleaks** scan of staged M4 publication candidates: **no leaks found**. Previous independent QA: 577 Release tests + 2 separate process tests. Setup safeguards recorded (diagnostics-only/no saves, guarded provisional saves, exact tag matching); reduced motion persisted; Release launcher and CI smoke recorded. Optional local artwork is excluded from the public repo.

**M3 real CLI proof (local Ollama v0.40.9, no API key):** approved `ollama pull qwen3:4b-instruct` completed (~2.5 GB, SHA verified). Installed: `qwen3.5:4b`, `qwen3:4b-instruct`. Real CLI proof (exit 0): `capri models list`, `models recommend`, `models use qwen3:4b-instruct` (persisted), `providers test` (actual inference, 1465 ms warm; response received though probe text not matched exactly), `capri ask "What is Project Capri?"` (genuine local response). `capri chat` correctly refuses non-interactive stdin. Fix: CLI model-management targets local Ollama when active provider is not Ollama.

**M4 terminal autonomy (implemented, partially verified):** reuses existing `TaskCoordinator`/`TaskPlanParser`/`SafeTaskExecutor`/journal (no separate autonomous engine). CLI adds `capri task "<request>"` (real configured provider planning via existing coordinator), `capri tasks`, `capri task status <id>`, `capri task stop`, `capri task reset-stop`, `capri task history`, help/dashboard updates, and terminal approval UI. **Safety verified by implementation/tests and CLI control checks:** exact plan shown before execution (request, provider/model, actions in order, exact targets/filenames/destinations, complete proposed note text, read-only vs modifies, limitations/uncertainty). Approval requires explicit interactive confirmation (`approve PLAN` intent as implemented) and **defaults to rejection** for non-interactive/EOF/timeout/cancelled input. Piped/non-interactive `capri task` refuses with no dispatch (verified: exit 2, "requires an interactive terminal for safe approval. No actions were dispatched"). Workspace selection required for note plans (existing disposable directory), safe note names/no-overwrite preserved. No untrusted model output executed directly; allowlist/parser bounds preserved. `capri task stop --json` / `reset-stop --json` verified to latch/reset shared state safely in this session.

**Not yet verified/complete:** interactive TTY `capri chat` multi-turn, streaming UX, and Ctrl+C in-session generation cancellation need manual Windows Terminal acceptance; desktop GUI regression not re-run; **M2 guided setup wizard implemented** (automated tests pass; broader manual acceptance useful); wake-word/"Hey Capri" not implemented/validated (config only); installer not built. `providers test`/cold local inference can hit timeouts (default 60s) - local timeout may need tuning (300s used for local testing). **Live supervised M4 acceptance NOT completed by the agent:** a real local-model Ollama task that generates a genuine plan, receives *explicit user approval*, creates/verifies a note in a disposable workspace, opens Notepad, and observes the window has **not** been run/approved automatically. Manual supervised steps are required.

Screen vision is **architecture only** - see [docs/SCREEN-CONTROL-PLAN.md](docs/SCREEN-CONTROL-PLAN.md). **No implemented vision claim.**

- [Roadmap](ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [Progress log](docs/PROGRESS.md)
- [Autonomy slice (`/task` + `capri task`, experimental)](docs/AUTONOMY.md)
- [Hardening status and trust limitations](docs/HARDENING.md)

## Implemented features

- **CLI (Milestone 1 + M3 + M4, partial/verified core path)** - `capri start/ask/chat/status/providers test/models list|recommend|use|pull` with Ollama model management and terminal inference (M3 verified). M4 adds `capri task`/`tasks`/`task status|stop|reset-stop|history` with terminal approval UI (implemented; automated + safe CLI control checks verified; **live supervised acceptance pending**). `capri chat` refuses non-interactive stdin (TTY multi-turn/streaming/Ctrl+C cancellation need manual acceptance).
- **WPF desktop companion** - transparent character window + compact chat panel (tray integration, hide/show).
- **Image-backed avatar renderer** - loads local transparent sprite assets from `design/assets/capri/` with state fallbacks; placeholder when optional local artwork is excluded.
- **Overlay polish** - configurable character height (~250px default, 150-400px), active-monitor placement, drag repositioning, saved position/size, monitor/DPI-aware clamping.
- **Text chat + OpenAI-compatible provider** - streaming/non-streaming requests (SSE), bounded retry/error/timeout handling.
- **Secure settings** - DPAPI-backed API key storage (Windows); settings separate from secrets; non-persisting connection probe.
- **Local automation intents (permission-gated)** - open URL (http/https), launch allowed apps, media keys.
- **Experimental autonomy slice (partial, WPF `/task` + M4 `capri task`)** - exact allowlisted tools, independent approval (WPF/terminal), workspace selection for note plans, emergency stop/reset, metadata-only journal (atomic, no replay). M4 adds file-based per-user task control/history under Capri data root (`tasks/` metadata-only, atomic bounded JSON, no secrets/prompts/plan/targets/note text) with single-active-task ownership, shared stop latch/reset generation, and metadata history/status. `TaskCoordinator` can honor shared stop; desktop wired to shared control. **Honest limitation:** cross-process stop is effective at coordinator checkpoints/queued work; it cannot forcibly interrupt an in-flight blocking operation or undo already-dispatched external actions. Emergency stop cancels queued work; dispatched external actions may already have occurred. Reset never revives/replays cancelled tasks. Dispatch-only actions not independently UI-verified; note creation workspace-bounded and verifies contents on disk. **Live supervised M4 acceptance pending.** See [docs/AUTONOMY.md](docs/AUTONOMY.md).
- **Automated tests** - **585/585 pass** (Release, 0 failed, exit 0, 2026-11-08, after setup fixes). **Release build 0 warnings, 0 errors.** **Gitleaks** clean on staged M4 publication candidates. Previous independent QA: 577 + 2 separate process tests. Prior mock-planner Windows acceptance passed (not re-run).

## In development / not yet verified

- **M4 live supervised acceptance:** real local-model Ollama `capri task` with explicit user approval, disposable workspace note create/verify, Notepad open, and window observation (manual supervised; not completed by agent).
- Interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation (manual Windows Terminal acceptance needed).
- **M2 guided setup wizard** implemented in CLI (diagnostics-only/no saves where applicable, guarded provisional saves, exact tag matching recorded; broader manual/Desktop integration acceptance still useful). Desktop GUI regression not re-run in this slice.
- Avatar expressions/animations; voice/speech synthesis; wake-word "Hey Capri" (not implemented/validated).
- Broader provider adapters/verified fallback routing UI; permissioned screen recognition; installer.

## Requirements

- Windows 11 (WPF/Desktop targets; some automation/media features are Windows-specific)
- [.NET SDK 10](https://dotnet.microsoft.com/) (pinned via `global.json`, `rollForward: latestFeature`)
- For verified local/free inference: [Ollama](https://ollama.com/) with an approved, locally installed model (e.g., `qwen3:4b-instruct`). **No automatic model downloads by Capri.**

## Build

```powershell
dotnet restore Capri.sln
dotnet build Capri.sln -c Release
```

## Run

```powershell
capri start  # Release desktop app; builds Release if no Release executable exists
```

`capri.cmd` and `capri start` select Release only; they never silently run a stale Debug executable. To select Debug explicitly: `dotnet run --project src/Capri.Desktop/Capri.Desktop.csproj -c Debug` (CLI: `dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Debug -- help`).

CLI examples (M3/M4 implemented; M4 live supervised acceptance pending):
```powershell
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- status
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- models list
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- models recommend
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- models use qwen3:4b-instruct
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- providers test
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- ask "What is Project Capri?"
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- task "Create a note in my selected workspace"  # interactive terminal approval required
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- task status <id>
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- task stop
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- task reset-stop
```

## Test

```powershell
dotnet test tests/Capri.Tests/Capri.Tests.csproj -c Release
```

## AI provider compatibility & setup

**Implemented (M1-M6 catalog) with M3 real local CLI proof (Ollama).** OpenAI-compatible endpoints, profile-specific credentials, fail-closed local-key behavior (**local profiles never read/send API keys**).

| Preset | Base URL | Key | Cost/safety note |
|---|---|---|---|
| **Ollama (local, free)** | `http://localhost:11434/v1` | No API key | Local; **no surprise cloud charges**. **M3 verified with real inference.** |
| **LM Studio (local)** | `http://127.0.0.1:1234/v1` | No API key (local) | Local loopback only. |
| **OpenRouter (cloud)** | `https://openrouter.ai/api/v1` | API key required | **Cloud/off by default.** Can incur charges. Consent required; `FreeModelsOnly` default true and `:free` heuristic guard applies - verify pricing. |
| **OpenAI (optional)** | `https://api.openai.com/v1` | API key required | **Cloud/paid API.** ChatGPT Plus is not the same as API access. |

**Cloud/free safety:** local avoids cloud charges. OpenRouter/OpenAI can incur charges. **Never copy OpenCode/browser tokens. No automatic model downloads by Capri** (model pulls require explicit user approval, e.g. `ollama pull ...` / `capri models pull ...`). `providers test`/cold local inference may need timeout tuning (300s used for local testing vs 60s default).

## Privacy & security

- API keys stored using **Windows DPAPI** (not in `settings.json` / repo). Never share API keys in chat.
- Local settings/captures and optional local artwork are gitignored by default; public docs avoid machine-specific paths or personal identifiers.
- Automation is intended to be **permission-controlled**; do not bypass Windows security prompts.
- **Gitleaks** scan of staged M4 publication candidates: **no leaks found** (2026-11-08).

## Known limitations

- Visual/avatar quality depends on **optional local artwork excluded** from the public repo (placeholder fallback).
- Multi-monitor/DPI and transparent-pixel hit-testing need broader manual validation. Desktop GUI regression not re-run in this slice.
- **CLI (M1+M3):** interactive TTY `capri chat` multi-turn/streaming/Ctrl+C in-session cancellation need manual Windows Terminal acceptance. `capri chat` refuses non-interactive stdin. `providers test`/cold local inference can hit the 60s default timeout (300s used for local testing).
- **Terminal autonomy (M4):** implemented and partially verified (automated + safe CLI control checks, including non-interactive refusal and `--json` stop/reset control checks). **Live supervised acceptance not completed by the agent** (real local-model Ollama plan + explicit user approval + disposable workspace note create/verify + Notepad open + window observation). Manual supervised steps required.
- **Cross-process task control (M4):** cooperative single-owner foundation, **not full M5 per-user IPC authority/shared approvals**. Stop is effective at coordinator checkpoints/queued work; it cannot undo already-dispatched external actions. Same-user path swap (TOCTOU) remains a trust limitation; do not treat it as malicious same-user protection. See [hardening status](docs/HARDENING.md).
- **WPF `/task` and `capri task` actions** are experimental/partial: launch/open/search actions dispatch-only beyond specific observed cases. Cancellation may leave a created note (no automatic delete); no replay. Reset **cannot revive** a stopped/cancelled task. Real local model inference verified via CLI; locally planned/approved supervised end-to-end acceptance may still be needed.
- Wake-word/"Hey Capri" not implemented/validated (config only). Voice, screen vision (architecture only, no implemented claim), full fallback routing, and installer are incomplete.
- Prior Windows-only mock-planner acceptance passed (not re-run). Local uncommitted changes exist (publication prep); no claim about remote/push state.

## Repository layout

| Path | Purpose |
|---|---|
| `src/Capri.Core` | Core abstractions/orchestration/models |
| `src/Capri.Providers` | AI provider integrations |
| `src/Capri.Security` | DPAPI credentials, settings, permissions |
| `src/Capri.Automation` | Windows automation (URL/app/media) |
| `src/Capri.Voice` | Voice services (scaffold/partial) |
| `src/Capri.Desktop` | WPF desktop app |
| `src/Capri.Cli` | CLI (start/ask/chat/status/providers/models/task) |
| `tests/Capri.Tests` | Automated tests |
| `design/` | Local design docs/assets/captures (sensitive/copyrighted items gitignored) |

## License / assets

No source license has been granted yet. The owner must choose licensing terms before granting a license or community reuse permission; this is **not a barrier to the owner publishing their own code**. Do not assume open-source reuse permission. **Optional local artwork is excluded** from the public repo by default and has separate rights; review rights before publishing any artwork.
