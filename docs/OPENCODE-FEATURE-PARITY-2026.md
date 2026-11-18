# OpenCode capability parity target — November 2026

**Product decision:** Project Capri remains its own Windows GUI-first companion for normal users, with an optional future power-user full-screen TUI, a common durable session engine, and a consistent permission/approval ledger. OpenCode is a **capability reference**, not a request to copy its UI, source, tokens, settings or brand.

**Implementation status:** This is an auditable backlog. Only entries marked **Working** represent code currently present; the rest are **Not yet implemented** even if Capri has a roadmap or placeholder interface. Running an AI model and showing a character is not sufficient to claim coding-agent parity.

## Feature-by-feature comparison and acceptance gates

| OpenCode category | Capri current capability | Next acceptance gate |
| --- | --- | --- |
| Desktop GUI, per-project sessions and chat history | **Working foundation:** WPF Dev workspace, encrypted per-user session history and explicit folder bookmarks | Project tabs, multiple simultaneous sessions, session rename/delete/export/import, reliably ordered turns |
| Full-screen developer terminal UI | **Not implemented:** CLI commands and interactive chat exist, but not a full coding TUI | Shared sessions, project navigation, approvals, streamed tool traces, keyboard controls, undo controls, recovery |
| Multi-model/provider switching | **Partial:** local Ollama/LM Studio and selected OpenAI-compatible profiles with consent/free-only checks | Per-session dynamic model selection, more provider adapters, provider capability routing, token/cost visibility |
| Account authentication | **Partial:** API keys stored with DPAPI; no generic paid ChatGPT account entitlement | Only use documented OAuth/provider auth flows; never scrape browser cookies, credentials or tokens |
| Read/search project sources | **Partial:** manually selected bounded source preview and opt-in single-file context | Permissioned multi-file search, glob, grep, symbol navigation, context limits, sensitive-data filtering |
| Git status and diff review | **Working initial preview (this increment):** fixed read-only Git commands, tracked status and staged/unstaged diffs of one eligible file, WPF and CLI | Diff across selected files, inline review comments, patch proposal UI, before/after verification, no unsafe Git extensions |
| Agent-proposed code edits | **Not implemented** | Typed patch protocol; exact diff shown before modification, user acceptance bound to target digest, symlink/TOCTOU defense, edit journal |
| Undo and redo | **Not implemented** | Transaction log, conflict detection, user-directed replay/reversal, never overwrite independent changes |
| Git worktrees/branches | **Not implemented** | Isolated per-session worktrees, explicit create/remove, dirty-worktree protections, trusted Git executable |
| Command and terminal execution | **Not implemented for coding sessions:** separate bounded task automation exists | Typed tool allowlists, per-command approval, sandbox and timeouts, streaming bounded logs; never pass unchecked model text to shell |
| Custom agents/subagents/parallel work | **Not implemented** | Task trees, bounded concurrency and per-agent permissions, audit trails, cancellation, cost and token limits |
| MCP servers, plugin hooks and agent skills | **Not implemented** | Per-project declarative discovery, allowlisted connections, typed tool permissions, hostile-plugin isolation, explicit install/enable |
| Slash/custom commands and prompt templates | **Not implemented** | User-defined local templates without unsafe implicit shell expansion; opt-in commands per project |
| LSP diagnostics and tests | **Not implemented** | Language-server adapters, diagnosed compile issues, supervised test runner, filtered tool output |
| Browser-powered workflow/review | **Not implemented in developer workspace** | User-approved browsing with origin separation, browser session status and results, no silent browser credential access |
| SDK/API/web server/ACP and remote companion | **Not implemented:** current IPC authority is local user-only task metadata/approvals, not an OpenCode API | Versioned authenticated APIs, permission parity in every client, optional paired remote access with short-lived tokens |
| Context compaction and session continuation | **Partial:** bounded persisted chat; no compression or branch/fork | Token-aware context summaries, testable retention, session replay/import/export |
| Usage statistics/cost reporting | **Not implemented** | Real measured tokens, per-provider estimates, no invented balances or fake limits |
| Setup, self-diagnostics and repair | **Partial and improved this increment:** tracked Capri-owned safe defaults and verified recovery, custom settings retention | Guided one-click local onboarding, safe provider/model probe and interactive user-directed repair; never silently alter custom models |
| Voice/companion visuals | **Partial:** animated sprite, typed cues, optional consented local webcam pipeline; live voice trigger requires separate work | True opt-in microphone hotword, TTS timing and illustrated facial/limb assets with visible use indicators |

OpenCode's official docs and current site describe desktop/multiple sessions, TUI/mini, APIs/SDK, agents, permissions, skills, MCP/plugins, terminal/browser tools, remote pairing and Git diff/undo:
- https://opencode.ai/
- https://opencode.ai/docs/cli/commands/
- https://opencode.ai/docs/tools/
- https://opencode.ai/docs/permissions/
- https://dev.opencode.ai/desktop
- https://opencode.ai/docs/commands/

## Implementation order

1. **P0 — Stable configuration and trust:** protect user-edited JSON and provider IDs, atomic writes with external-edit detection, backed-up Capri-managed defaults, safe boot-time recovery, explicit repair review. No hidden cloud/camera/privilege escalation.
2. **P1 — Developer inspection:** shared session store, deliberate single-source prompt context, Git status + staged/unstaged diffs for eligible files, safe project navigation, reproducible tests.
3. **P2 — Exact patch transaction:** typed file-edit tool with path validation, previewable diff, explicit one-time approval tied to path + content hash, no truncation of proposed edits, journal and conflict-safe undo.
4. **P3 — GUI coding experience:** multi-project tabs, threaded tool results, editor/diff viewer, rich markdown, progress and cancellation, model/session selection.
5. **P4 — Real full-screen TUI:** same service and permissions as GUI; keyboard-first session navigation, diff review, approvals, tool/test output.
6. **P5 — Extensibility:** MCP, skills, plugins, custom slash commands, carefully bounded agents and worktree isolation; test hostile repository/tool output.
7. **P6 — Provider breadth, platform/remote:** more compliant provider adapters, cost/token visibility, secure pairing/web/API/ACP where practical, editor integration.
8. **P7 — Quality and companion:** opt-in voice/wake word, original Capri rigged animation layers, polished onboarding and installer, visual acceptance.

## Non-negotiable behavior

- A custom provider ID, URL, model tag, theme, UI setting, plugin setting, extension JSON property or manually edited settings file must **not** be auto-classified as an error and silently replaced.
- Auto-repair writes only provably Capri-managed and reversible state; no model download, cloud opt-in, mic/camera activation, permission escalation, arbitrary shell execution or emergency-stop reset.
- Source previews and Git diff are local only until the user separately approves using the source in an AI request. Repository contents and Git output are untrusted data.
- Every state-changing coding tool must expose the exact proposed effect, require approval, verify the source has not changed and preserve a recovery path.
- Do not claim feature parity until each category has working GUI/TUI/CLI behavior, tests, and at least one real Windows acceptance run.

See also the initial [architecture roadmap](OPENCODE-CAPABILITY-ROADMAP.md), [developer sessions](DEVELOPER-SESSIONS.md), [settings and recovery guide](SMART-SETUP-AND-SELF-REPAIR.md) and [security hardening](HARDENING.md).
