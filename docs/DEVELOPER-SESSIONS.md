# Capri developer workspace: first foundation

Status: **implemented metadata-only prototype** on feature branch `feat/developer-session-foundation-20261114`; no arbitrary shell or coding agent enabled.

## Why this exists

Capri's default interface is the friendly WPF assistant. Developers will later have a project-focused GUI and an optional interactive terminal UI (`capri dev`) connected to the same sessions, model providers, and safety/approval authority. The initial slice establishes a stable project-session identity and local metadata that **both GUI and CLI can already read**.

## What works now

- In Capri's small chat panel, select **Dev** to open **Developer Workspace**.
- Choose **Add project folder**. Capri confirms the selected folder exists and stores a bookmark in the current user's private Capri data directory. It does **not** list or read files inside that folder, launch git, invoke AI, or run commands.
- The GUI lists previously registered folders, their names, and stable UUIDs.
- CLI commands:
  - `capri dev list [--json]` — list existing sessions.
  - `capri dev new "<existing-folder>" [--json]` — explicitly register a new project folder.
  - `capri dev show <uuid> [--json]` — retrieve one bookmark.
- A future TUI can use `IDeveloperSessionStore` via the same service contract; this does not claim a working full-screen TUI.
- Source model: `DeveloperSession` with UUID, label, canonical workspace path and UTC creation time.
- Storage: `%LOCALAPPDATA%\Capri\developer\sessions\<uuid>.json` unless `CAPRI_DATA_DIR` is set; atomic create-new files, private current-user/SYSTEM/Administrators directory rules reused from task metadata. A selected project directory is never changed.

## Boundaries

This creates **only a bookmark**. It does not grant Capri permission to browse a folder, inspect secrets, edit code, execute terminals, install extensions, send files to AI, or approve a task. The existing per-user task authority and exact-plan human approval remain the only supported path for executable automation. Future project tools need their own explicit consent and least-privilege scoping.

- Reparse/junction-linked project folders are refused to avoid silently following redirects.
- Metadata has a bounded filename/id schema, maximum document size and session limit. Invalid JSON fails visibly, rather than silently removing a session.
- Read-only session listing doesn't create any new directories.
- Folder paths are sensitive metadata. Do not send them to a cloud provider or log them automatically. Existing Windows same-user trust limitations still apply.
- This does not implement shared live coding conversations, Git diff approval, session compaction, Git worktrees, remote access, plugins, agents, or the full-screen developer TUI. See [OpenCode capability roadmap](OPENCODE-CAPABILITY-ROADMAP.md) for the staged implementation.

## Acceptance completed and remaining

Unit tests verify persistence across service instances, metadata-only creation, missing folder refusal, corrupt JSON failure, bounded naming, and shared CLI JSON output. The solution must pass a Windows Release build and all automated tests. **Manual WPF acceptance** remains required to inspect layout, select a real project via the folder picker and verify cross-client consistency.

Next: introduce a deliberately scoped, read-only `workspace.inspect` permission and user-visible project contents preview; then durable conversation sessions and diff proposals with exact-plan approval. Keep the GUI primary, while reusing the same session store in the future TUI.
