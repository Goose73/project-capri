# Project Capri

**Project Capri** is an AI-powered Windows desktop companion (C# / WPF / .NET 10) inspired by a stylized digital character aesthetic. It aims to provide voice/text interaction, configurable AI providers, permission-controlled Windows automation, and an animated character overlay.

> **Unofficial fan-inspired project.** Not affiliated with or endorsed by CD PROJEKT RED or Netflix.

## Current development status

**Active development (local build/test verified).** The desktop companion, image-backed sprite renderer, chat/settings foundations, OpenAI-compatible provider integration, recent overlay polish, and an **experimental `/task` autonomy slice** are implemented in the local workspace.

**Phase 2 status (2026-11-08, honest):** progress recorded, **not full Phase 2 completion**. Baselines progressed **324 → 329 → 337 → 339** (reported). Final recorded result: **339 tests passed**. Earlier Phase 2 evidence included **337** full-suite tests, **7** real in-process WPF approval tests repeated **3×**, and concurrent journal coverage. Recorded debugger builds reported **0** warnings/errors. The running user app was **never intentionally stopped**. Optional local artwork is excluded from the public repo.

`/task` Windows-only validation used the `tools/Capri.Acceptance` harness (`windows-only` mode): a **mock planner** (clearly labeled, `planner=mock-fixed-plan-not-live-inference`, no live inference) drove the normal parser, exact-plan gate, real approval, real coordinator/executor, and Windows observer. Final interactive run evidence (JSON): **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true, workspaceCreated=true, timedOut=false**. Real WPF confirmation was provided by the user; the exact new file was read back and a trusted Notepad title/package window was observed. This confirms window identity/presence, **not** editor contents or AI-generated content.

Implementation fixes recorded in this evidence: `GetPackageFamilyName` handling corrected for a count that includes the terminating NUL; approval foreground/taskbar behavior and approval budget handling were fixed.

Provider/config status (`config-status`): `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`; **no live AI** requests were made in this recorded validation. Live provider/planner validation still requires a configured credential. No full vision fallback (Anthropic) is implemented. Git has **no commits/remote** and there has been **no push**.

Screen vision is **architecture only** — see [`docs/SCREEN-CONTROL-PLAN.md`](docs/SCREEN-CONTROL-PLAN.md). **No implemented vision claim.** Voice wake word, full model fallback UI/routing across all providers, screen-vision automation, and installer/release remain in progress or planned.

- [Roadmap](ROADMAP.md)
- [Changelog](CHANGELOG.md)
- [Progress log](docs/PROGRESS.md)
- [Autonomy slice (`/task`, experimental)](docs/AUTONOMY.md)

## Implemented features

- **WPF desktop companion** — transparent character window + compact chat panel (tray integration, hide/show).
- **Image-backed avatar renderer** — loads local transparent sprite assets from `design/assets/capri/` with state fallbacks; shows a placeholder when no local artwork is present (optional local artwork is excluded from the public repo).
- **Overlay polish** — configurable character height (~250px default, 150–400px), active-monitor bottom-right placement respecting work area, drag repositioning, saved position/size, monitor/DPI-aware clamping, chat panel positioning beside character.
- **Text chat + OpenAI-compatible provider** — streaming/non-streaming requests (SSE), bounded retry/error/timeout handling.
- **Secure settings** — DPAPI-backed API key storage (Windows), `PasswordBox` key entry, settings stored separately from secrets; connection probe without persisting unsaved settings/key.
- **Local automation intents (permission-gated)** — open URL (http/https policy), launch allowed apps, media keys (SendInput baseline).
- **Experimental `/task` autonomy slice (partial)** — `/task <goal>` turn path with planner grammar, exact allowlisted tools (`launch_app`, `open_url`, `brave_search`, `create_note`, `open_note`), independent WPF approval flow for exact plan/workspace, emergency stop/reset latch, metadata-only task journal (atomic latest-state write, bounded retries), and **no replay**. Dispatch-only actions are not independently UI-verified; note creation is workspace-bounded and verifies contents on disk. Desktop UI exposes **Emergency Stop** and **Reset Task Stop**: stop latches the coordinator and cancels the active chat/task turn; reset only clears the latch for future tasks and **cannot revive an already stopped/cancelled task**. See [docs/AUTONOMY.md](docs/AUTONOMY.md).
- **Automated tests** — xUnit test suite. Latest recorded milestones (reported): **296 → 324 → 329 → 337 → 339** passed. **2026-11-08** final recorded result: **339 tests passed**. Phase 2 evidence also included **337** full-suite tests, **7** real in-process WPF approval tests repeated **3×**, and concurrent journal coverage. Recorded debugger builds reported **0** warnings/errors. Normal Debug output may be locked by a running app; alternate outputs are for current validation only.

## In development

- Avatar expressions/animations preserving the approved character appearance.
- Voice recognition/speech synthesis and optional wake-word (“Hey Capri”).
- Broader AI-provider adapters and verified model fallback/routing.
- Permissioned screen recognition and richer Windows/browser automation.

## Planned features

- Multi-provider AI management (OpenAI-compatible, Anthropic, Gemini, OpenRouter, Ollama, LM Studio) with capability-aware fallback.
- Observe→plan→act→verify Windows control with explicit permissions and confirmations.
- Brave/browser integration for navigation/search/summarization (no credential capture).
- Security/permissions center, audit history, emergency stop.
- Performance optimization and optional local models.
- Windows installer and release packaging.

## Screenshots / demos

Screenshots and local renders may be added where publication rights and privacy permit. Local desktop captures are **not** committed by default (`.gitignore` excludes local capture/render artifacts). Public docs avoid machine-specific paths or personal identifiers.

## Requirements

- Windows 11 (WPF/Desktop targets; some automation/media features are Windows-specific)
- [.NET SDK 10](https://dotnet.microsoft.com/) (pinned via `global.json`, `rollForward: latestFeature`)

## Build

```powershell
dotnet restore Capri.sln
dotnet build Capri.sln -c Debug
```

## Run

```powershell
capri start
```

Or:

```powershell
dotnet run --project src/Capri.Desktop/Capri.Desktop.csproj -c Debug
```

`capri.cmd` launches the built Debug app when present, otherwise uses `dotnet run`. Add the project directory to your user `PATH` if you want `capri` available globally.

## Test

```powershell
dotnet test tests/Capri.Tests/Capri.Tests.csproj -c Debug
```

## AI provider compatibility & setup

Current implementation focuses on **OpenAI-compatible** chat endpoints (configurable base URL/model, API key stored via Windows DPAPI). The architecture includes provider abstractions for future adapters (Anthropic, Gemini, OpenRouter, Ollama, LM Studio).

**Exact settings (tray UI):** open tray **Settings → AI providers**. Set **Base URL** to `https://api.openai.com/v1`. Set **Model** to a supported model such as `gpt-4o-mini`. Enter the **API key** in the **PasswordBox** field, then use **Save**. Use **Test Connection** to run a connection/health check only (it does **not** run the `/task` planner). Never share or paste API keys into chat or source files. **ChatGPT Plus is not the same as API access** — an API key/billing-enabled API account is required for live API calls. Recorded `config-status` example: `provider=openai`, `model=gpt-4o-mini`, `credentialExists=false`, `base=https://api.openai.com/v1`, `no live AI`.

## Privacy & security

- API keys are stored using **Windows DPAPI** (not in `settings.json` / repo). Never share API keys in chat.
- Local settings/captures and optional local artwork are gitignored by default; review before publishing anything. Public docs avoid machine-specific paths or personal identifiers.
- Automation is intended to be **permission-controlled**; do not bypass Windows security prompts.
- No telemetry is added by default in the current MVP; verify behavior before trusting sensitive workflows.

## Known limitations

- Visual/avatar quality depends on **optional local artwork excluded** from the public repo. The app falls back to a placeholder.
- Multi-monitor/DPI and transparent-pixel hit-testing need broader manual validation.
- Voice, wake word, screen vision, full fallback routing, and installer are incomplete. Screen vision is **architecture only** ([`docs/SCREEN-CONTROL-PLAN.md`](docs/SCREEN-CONTROL-PLAN.md)); **no implemented vision claim**.
- `/task` is **experimental/partial** (Phase 2 evidence recorded **2026-11-08**, **not full Phase 2 completion**): no live provider/planner production run with configured credential yet (recorded `config-status`: `credentialExists=false`, `no live AI`). Launch/open/search actions are dispatch-only (not independently UI-verified beyond specific observed cases). `create_note` verifies file contents, but cancellation may leave a created note with **no automatic delete**; metadata journal is not used for replay. UI Emergency Stop/Reset controls exist but reset **cannot revive an already stopped/cancelled task**.
- **Windows-only acceptance harness (mock planner, clearly labeled):** `tools/Capri.Acceptance` `windows-only` (`planner=mock-fixed-plan-not-live-inference`) used real approval/coordinator/executor/observer. Earlier runs exposed failures (window observation false, then timeout/cancel with no disk/window). Final interactive evidence (JSON): **PASS, exit 0, phaseCompleted, approvalShown=true, diskVerified=true, windowObserved=true, workspaceCreated=true, timedOut=false**. Real WPF confirmation was provided by the user; exact new file readback and a trusted Notepad title/package window were observed (**not** editor contents/AI). Fixes included `GetPackageFamilyName` count handling (terminating NUL) and approval foreground/taskbar/budget handling.
- Git has **no commits/remote** and **no push**. The running user app was **never intentionally stopped**. Normal Debug output may be locked by a running app.

## Repository layout

| Path | Purpose |
|---|---|
| `src/Capri.Core` | Core abstractions/orchestration/models |
| `src/Capri.Providers` | AI provider integrations |
| `src/Capri.Security` | DPAPI credentials, settings, permissions |
| `src/Capri.Automation` | Windows automation (URL/app/media) |
| `src/Capri.Voice` | Voice services (scaffold/partial) |
| `src/Capri.Desktop` | WPF desktop app |
| `tests/Capri.Tests` | Automated tests |
| `design/` | Local design docs/assets/captures (sensitive/copyrighted items gitignored) |

## License / assets

Source code licensing should be confirmed before/with the initial public release (add a `LICENSE` file if you want an open-source license). **Optional local artwork is excluded** from the public repo by default; review rights before publishing any artwork.
