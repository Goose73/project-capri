# OpenCode capability audit and Capri product architecture

Research date: **10 November 2026**. OpenCode is a moving target; revalidate APIs, model eligibility and project licensing before implementation. This audit compares the current public Capri repository against official [OpenCode](https://opencode.ai/) documentation. It does not treat a planned feature as implemented.

## Product decision: one agent engine, two user experiences

**Default:** Capri is a Windows-first, accessible WPF desktop assistant with a guided setup, animated companion, approachable task cards, and transparent consent/permissions. Most users should never need to open a terminal.

**Developer mode:** Offer a project workspace in the GUI (sessions, code review, terminal, diffs, git, tests) and an optional **interactive full-screen TUI** (proposed `capri dev`). Developers should be able to switch between GUI and TUI without losing conversations, project sessions, pending approvals, or tool results. Do not copy OpenCode's GUI, TUI layout, branding, icons, or artwork.

**Core:** Existing per-user M5 named-pipe task authority and exact-plan approval form the security nucleus; extend them to sessions and coding tools instead of introducing a parallel unapproved executor. The authority already owns task approval and emergency stop, but a persistent coding-session server does **not** yet exist in Capri.

## Audited feature inventory

| OpenCode capability (official source) | Capri today | Planned Capri implementation |
| --- | --- | --- |
| Shared service used by TUI, desktop and web ([CLI](https://opencode.ai/docs/cli), [Troubleshooting](https://opencode.ai/docs/troubleshooting)) | **Partial**: per-user Windows task authority, not full coding sessions | One per-user agent/session service and shared IPC/event stream, with explicit reconnect/resume semantics |
| Full-screen TUI, mini TUI, noninteractive CLI, SDK ([CLI](https://opencode.ai/docs/cli), [commands](https://opencode.ai/docs/cli/commands/)) | **Partial**: commands and interactive chat; no full-screen TUI or SDK | GUI-first WPF, optional `capri dev` TUI, scripted operations, documented API/SDK |
| Desktop multi-session/tabs, diff review, browser and terminal ([Desktop](https://opencode.ai/desktop)) | **Partial**: compact WPF chat/overlay, basic settings | Developer workspace tab in WPF: projects, multiple sessions, diff review, terminal, Git integration |
| Provider catalog, ChatGPT Plus/Pro sign-in, Copilot OAuth, Ollama/LM Studio/vLLM ([Providers](https://opencode.ai/docs/providers)) | **Partial**: OpenAI-compatible provider, Ollama model management, LM Studio probing; API credentials | Provider adapter registry with model capabilities, multiple accounts, approved browser sign-in (where authorized), local discovery and offline fallback |
| Specialized plan/build agents and subagents ([Agents](https://opencode.ai/docs/agents/)) | **Partial**: one task-planning coordinator, not project agents | Read-only planning agent, code-change agent, test/review agents, bounded child jobs and cross-agent task visibility |
| File read/search/edit, patch, shell, web, LSP, browser, worktrees ([Tools](https://opencode.ai/docs/tools/)) | **Small safe subset**: approved app launch, URL opening, notes, Brave search; no general repo editor or arbitrary shell | Typed tools under workspace boundary, previewable file diffs, explicit per-effect approval, cancellation and post-action verification |
| Per-tool and path permissions, ask/deny/allow ([Permissions](https://opencode.ai/docs/en/permissions/)) | **Partial / security-first**: exact-plan approval, current-user IPC and emergency-stop | Add scoped read/write/terminal/network policies, secret exclusions, deny overrides, expiring permissions; no blanket auto-approve on user-device actions |
| MCP servers, plugin hooks, reusable skills, custom commands ([MCP](https://opencode.ai/docs/mcp-servers), [plugins](https://opencode.ai/docs/plugins/)) | **Absent** general extension runtime | Permissioned MCP capability discovery; signed/verified plugin sources; local SKILL.md workflows; user-custom commands |
| Undo/redo, worktrees, Git diffs ([OpenCode](https://opencode.ai/), [Tools](https://opencode.ai/docs/tools/)) | **Absent** generalized coding snapshots | Snapshot/transaction journal, isolated worktrees, preview/accept/revert; never claim external effects are undoable |
| Context compaction, session persistence, export ([Compaction](https://opencode.ai/docs/compaction), [CLI commands](https://opencode.ai/docs/cli/commands/)) | **Partial** session-only chat + metadata task history | Durable local transcript store, crash-safe recovery, compaction with provenance, export with redaction |
| Mobile / remote pairing / web ([Web](https://opencode.ai/docs/cli/web), [commands](https://opencode.ai/docs/cli/commands/)) | **Absent** | Future opt-in secure remote controller only after local authority and identity hardening; never bind an unauthenticated remote control API |
| Cost/model usage and tooling feedback ([CLI commands](https://opencode.ai/docs/cli/commands/)) | **Partial** model health/errors | Per-provider model/limits, context use, costs when available, transparent inference or permission failures |

Documentation supports the comparison, **not** claims of equivalence. No OpenCode source code or credentials are copied into Capri.

## What makes Capri more than a coding agent

1. **Everyday Windows assistant:** tasks, accessible controls, windows, notifications, personal workflows and software help for non-developers; real verification rather than pretending a dispatched key command succeeded.
2. **Avatar and voice:** responsive Capri effect-entity animation, explicit microphone status, offline optional speech/wake word, animation tied to real task state rather than hallucinated actions.
3. **Guided first-run UX:** optional hardware inspection with consent, local model identification/recommendations, ChatGPT account vs API key clearly separated, simple explanations and troubleshooting.
4. **Security:** explicit exact-plan human approval, durable stop latch, task-specific cancellation and no replay; maintain the current Windows current-user isolation and document same-user trust gaps.
5. **Local-first operation:** honest capability detection, measured CPU/RAM/GPU/VRAM/disk, no invented VRAM, model install confirmations, clear offline mode and no surprise cloud billing.
6. **Developer experience without sacrificing simplicity:** WPF project workspace and code review, optional integrated terminal and dedicated TUI, and policy-equivalent GUI/CLI control.

## Architecture proposal

```text
                 WPF companion & beginner setup
                         |
        WPF Developer Workspace --- optional TUI / CLI
                         |                |
                         +----------------+
                                  |
                      Capri session/agent service
                                  |
            +---------------------+------------------+
            |                     |                  |
    Provider adapters       Project/session DB   Extension registry
    (auth & model policy)    (provenance + diff) (skills/MCP/plugins)
            |                     |                  |
            +---------------------+------------------+
                                  |
                Shared current-user task authority
                    |             |          |
             Exact-plan gate   Stop/cancel  Journal
                    |
         Typed and scoped Windows/project tools
```

**Important:** the session layer may generate plans and previews, but only the authority can authorize user-device effects. Tools must never bypass the authority because a plugin, remote client or model requests it. Do not assume an OpenCode-style `--auto` mode is acceptable: preserve Capri's explicit user approval default. A read-only plan can be generated without action approval, but should not silently read private files, screens or microphones beyond granted scope.

## Build priorities and acceptance gates

**P0 — Reliable foundation (current):** Keep CI green; finish supervised M4/M5 Windows acceptance with the user's own interactive approval. Test authority restart, stale plans, cross-session event isolation, emergency-stop, no-replay and GUI/CLI parity. Do not simulate acceptance.

**P1 — Shared coding sessions (next major code milestone):** session IDs and projects, resilient storage, read-only workspace discovery with consent, event streaming, worktree policy, model choice. Ship one working WPF project-session screen and a small CLI `dev session list` client of the same service. No shell/file writes yet.

**P2 — Controlled coding tools:** repository read/search, path filtering and secret exclusions; generate code diffs; transactional patches with exact-diff approval, atomic conflict detection, tests and rollback for files (not arbitrary external actions). Each step must be observable, cancellable and securely permissioned.

**P3 — Developer interfaces:** WPF code workspace as the primary rich UI, optional fullscreen TUI for interactive vibe coding; shared sessions/approval inbox, diff viewer, terminal, git status/test output and keyboard shortcuts. Terminal commands require scoped approval; no silent administrator elevation.

**P4 — Provider breadth:** compliant ChatGPT browser sign-in if a documented authorized third-party integration is available, separately from API keys; Ollama/LM Studio/vLLM discovery, context/tool-capability registry, robust failure recovery and user-approved fallback. Never scrape browser cookies or reuse OpenCode tokens.

**P5 — Extensibility:** capability-scoped MCP, plugins, skills, custom agents/commands; trust-store and audit log, no ambient unrestricted tool permissions.

**P6 — Companion UX and voice:** fully event-driven avatar, multiple gestures/sprite rigs, optional local wake-word/STT/TTS, permissioned screen inspection, accessibility, installer, rollback and opt-in updates.

**P7 — Remote/multi-device:** only after local trust model and auth review; require encryption, scoped pairing and revocation, no anonymous network control surface.

## Measurable acceptance for each milestone

- CI builds and tests on Windows; no regression to M4/M5 human approval, no auto-execution and no unsafe defaults.
- Two clients watch the same session/task by immutable ID and cannot steal another's events or pending approvals.
- A coding agent can propose edits, show a full exact diff and tests, then **wait** for explicit approval before applying; stale/different diff hashes are rejected; stop prevents queued edits.
- Browser auth uses only provider-authorized protocols; application tokens are protected, no secrets logged or committed; account logout revokes locally stored access.
- Offline model recommendation never guesses a user's GPU configuration; denied hardware consent means no hardware-based recommendations.
- Screen-looking avatar motion only claims actual observation when permission **and** real capture/inspection succeeded; failure and denial produce distinct truthful states.
- User can complete common tasks in WPF without learning CLI/TUI; developer can pick up the same session from TUI.
- Reuse existing approved private artwork; never publish unauthorized show artwork, transcripts, assets or model credentials.

## Sources (primary, current)

- https://opencode.ai/ — product capabilities
- https://opencode.ai/desktop — desktop capabilities
- https://opencode.ai/docs/cli — shared service, terminal modes
- https://opencode.ai/docs/cli/commands/ — CLI, session, API, MCP commands
- https://opencode.ai/docs/providers — OpenAI account sign-in and local providers
- https://opencode.ai/docs/agents/ — specialised agents
- https://opencode.ai/docs/tools/ — typed tools and worktrees
- https://opencode.ai/docs/en/permissions/ — scoped permissions
- https://opencode.ai/docs/mcp-servers — MCP
- https://opencode.ai/docs/plugins/ — plugins
- https://opencode.ai/docs/compaction — long-session context management
