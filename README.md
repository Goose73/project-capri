# Project Capri

**Project Capri** is an AI-powered Windows desktop companion (C# / WPF / .NET 10) inspired by a stylized digital character aesthetic. It aims to provide voice/text interaction, configurable AI providers, permission-controlled Windows automation, and an animated character overlay.

> **Unofficial fan-inspired project.** Not affiliated with or endorsed by CD PROJEKT RED or Netflix.

## Current development status

**Active development (local uncommitted changes; publication prep in progress).** The desktop companion, image-backed sprite renderer, chat/settings foundations, multi-provider catalog integration (M1-M6), an **experimental WPF `/task` autonomy slice**, and **CLI Milestone 1 + M3 Ollama model management/terminal inference** are implemented in the local workspace.

**Current verified state (2026-11-08, local):** **540/540 automated tests pass** (Release, 0 failed). **Release build succeeds (0 warnings/errors).** **Gitleaks 8.30.1** scan of publication candidates: **no leaks found**. Optional local artwork is excluded from the public repo.

**M3 real CLI proof (local Ollama v0.40.9, no API key):** approved `ollama pull qwen3:4b-instruct` completed (~2.5 GB, SHA verified). Installed: `qwen3.5:4b`, `qwen3:4b-instruct`. Real CLI proof (exit 0): `capri models list`, `models recommend`, `models use qwen3:4b-instruct` (persisted), `providers test` (actual inference, 1465 ms warm; response received though probe text not matched exactly), `capri ask "What is Project Capri?"` (genuine local response). `capri chat` correctly refuses non-interactive stdin. Fix: CLI model-management targets local Ollama when active provider is not Ollama.

**Not yet verified/complete:** interactive TTY `capri chat` multi-turn, streaming UX, and Ctrl+C in-session generation cancellation need manual Windows Terminal acceptance; desktop GUI regression not re-run; **`capri task`/terminal autonomy not implemented (M4)** (CLI approval pending); **M2 guided setup not implemented**; wake-word/"Hey Capri" not implemented/validated; installer not built. `providers test`/cold local inference can hit timeouts (default 60s) - local timeout may need tuning (300s used for local testing).

Screen vision is **architecture only** - see [docs/SCREEN-CONTROL-PLAN.md](docs/SCREEN-CONTROL-PLAN.md). **No implemented vision claim.**

- [Roadmap](ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [Progress log](docs/PROGRESS.md)
- [Autonomy slice (`/task`, experimental)](docs/AUTONOMY.md)

## Implemented features

- **CLI (Milestone 1 + M3, partial)** - `capri start/ask/chat/status/logs/providers test/models list|recommend|use|pull|remove` with Ollama model management and terminal inference (M3 verified). `capri chat` refuses non-interactive stdin (TTY multi-turn/streaming/Ctrl+C cancellation need manual acceptance). **`capri task` / terminal autonomy not implemented (M4).**
- **WPF desktop companion** - transparent character window + compact chat panel (tray integration, hide/show).
- **Image-backed avatar renderer** - loads local transparent sprite assets from `design/assets/capri/` with state fallbacks; placeholder when optional local artwork is excluded.
- **Overlay polish** - configurable character height (~250px default, 150-400px), active-monitor placement, drag repositioning, saved position/size, monitor/DPI-aware clamping.
- **Text chat + OpenAI-compatible provider** - streaming/non-streaming requests (SSE), bounded retry/error/timeout handling.
- **Secure settings** - DPAPI-backed API key storage (Windows); settings separate from secrets; non-persisting connection probe.
- **Local automation intents (permission-gated)** - open URL (http/https), launch allowed apps, media keys.
- **Experimental WPF `/task` autonomy slice (partial)** - exact allowlisted tools, independent WPF approval/workspace selection, emergency stop/reset latch, metadata-only journal (atomic, no replay). Dispatch-only actions not independently UI-verified; note creation workspace-bounded and verifies contents on disk. Reset **cannot revive** stopped/cancelled task. **Terminal `capri task` (M4) not implemented.** See [docs/AUTONOMY.md](docs/AUTONOMY.md).
- **Automated tests** - **540/540 pass** (Release, 0 failed, 2026-11-08). **Release build 0 warnings/errors.** **Gitleaks 8.30.1** no leaks found. Prior mock-planner Windows acceptance passed (not re-run).

## In development / not yet verified

- Interactive TTY `capri chat` multi-turn, streaming UX, Ctrl+C in-session generation cancellation (manual Windows Terminal acceptance needed).
- **M4 terminal autonomy:** `capri task` with explicit CLI approval/workspace/stop controls (not implemented).
- **M2 guided setup** (not implemented). Desktop GUI regression not re-run in this slice.
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
capri start  # desktop app (or: dotnet run --project src/Capri.Desktop/Capri.Desktop.csproj -c Debug)
```

﻿CLI examples (M3 verified, local Ollama / `qwen3:4b-instruct`):
```powershell
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- status
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- models list
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- models recommend
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- models use qwen3:4b-instruct
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- providers test
dotnet run --project src/Capri.Cli/Capri.Cli.csproj -c Release -- ask "What is Project Capri?"
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
- **Gitleaks 8.30.1** scan of publication candidates: **no leaks found** (2026-11-08).

## Known limitations

- Visual/avatar quality depends on **optional local artwork excluded** from the public repo (placeholder fallback).
- Multi-monitor/DPI and transparent-pixel hit-testing need broader manual validation. Desktop GUI regression not re-run in this slice.
- **CLI (M1+M3):** interactive TTY `capri chat` multi-turn/streaming/Ctrl+C in-session cancellation need manual Windows Terminal acceptance. `capri chat` refuses non-interactive stdin. `providers test`/cold local inference can hit the 60s default timeout (300s used for local testing).
- **Terminal autonomy:** `capri task` is **not implemented (M4)**; CLI approval mechanism pending.
- **WPF `/task`** is experimental/partial: launch/open/search actions dispatch-only beyond specific observed cases. Cancellation may leave a created note (no automatic delete); no replay. Reset **cannot revive** a stopped/cancelled task. Real local model inference verified via CLI; locally planned/approved desktop `/task` end-to-end acceptance may still be needed.
- **M2 guided setup** not implemented. Wake-word/"Hey Capri" not implemented/validated. Voice, screen vision (architecture only, no implemented claim), full fallback routing, and installer are incomplete.
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
| `src/Capri.Cli` | CLI (start/ask/chat/status/logs/providers/models) |
| `tests/Capri.Tests` | Automated tests |
| `design/` | Local design docs/assets/captures (sensitive/copyrighted items gitignored) |

## License / assets

Source code licensing should be confirmed before/with the initial public release (add a `LICENSE` file if you want an open-source license). **Optional local artwork is excluded** from the public repo by default; review rights before publishing any artwork.
