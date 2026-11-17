# Developer workspace: persistent chat and explicitly permitted source preview

Status (17 November 2026): Windows WPF + CLI with private read-only previews, encrypted chat, and one-file-per-request opt-in AI context. GitHub CI and manual WPF visual acceptance are separate gates. Developer workspace remains **GUI-first**. An optional full-screen TUI, code-editing tools, Git diff approval, MCP/skills/plugins and worktrees are **not yet implemented**.

## User workflow

1. Open Capri chat and click **Dev**. Use **Add folder** to save a project bookmark. This saves metadata only; no source files are opened by selecting a folder.
2. Choose a project and open the **Conversation** tab. Type a message and click **Send to AI**. Capri calls the configured AI provider through its existing model router and cloud/free-model restrictions. **By default it sends only your typed conversation**, not the selected project's files. If the configured provider is cloud-based and cloud consent is enabled, those messages may leave the PC. The **Stop response** button requests cancellation and stops waiting promptly; an uncooperative provider may still continue work after the UI stops waiting.
3. The conversation is saved privately with Windows **DPAPI CurrentUser encryption** and reopens after app restart. CLI can access the same session ID and conversation.
4. In **Read-only project files**, click **Browse folder**. Capri asks for explicit read permission for the **currently selected project and open window**. Approving it allows one-directory-at-a-time filename listing and a separate click to preview one file (maximum 32 KB). Switching projects discards the approval; no file is attached to chat automatically.
5. **Optional source-aware AI question:** after previewing an eligible file, switch to **Conversation**, check **Include selected source file**, and click Send. A separate Yes/No dialog explains that the configured provider will receive its contents and that cloud models may transmit it outside the PC. Declining sends nothing. Capri then re-reads that one file under existing local-read approval and includes up to 8,000 characters with that question only. The preview alone never uploads anything. The selection clears on session change or successful send. No source files are written or executed.

### Terminal commands

```powershell
capri dev new "C:\Path\To\Project" --json
capri dev list --json
capri dev show <session-id> --json
capri dev history <session-id> --json
capri dev ask <session-id> "Explain unit testing for my project" --json
capri dev ask <session-id> "Explain src/Program.cs" --context src/Program.cs --allow-read --send-context --json
capri dev files <session-id> src --allow-read --json
capri dev file <session-id> src/Program.cs --allow-read --json
```

Without `--allow-read`, an **interactive** `dev files`/`dev file` asks whether to permit that specific read. A noninteractive call is rejected rather than reading the project. Explicit script flags grant only that single invocation's read; they do not persist permission or grant code execution. For a developer `ask` with source context, all three `--context <relative-file>`, `--allow-read` and `--send-context` are required together. The last flag explicitly authorizes sharing with the configured AI. Files can contain secrets not detectable from their names. Only one file with at most 8,000 characters is included for a single question; ordinary chat never attaches sources automatically. `--json` prints messages/preview contents if explicitly requested, so avoid redirecting private content to untrusted logs.

### Storage and sharing

- `%LOCALAPPDATA%\Capri\developer\sessions\<id>.json` — UUID, project name, path and creation timestamp. This is private metadata but **not encrypted**; the path should be treated as sensitive.
- `%LOCALAPPDATA%\Capri\developer\conversations\<id>\<ticks>_<message-id>.bin` — encrypted per-message records. Each is atomically created, bounded, with user/assistant identity and date. Decryption is limited to the current Windows user via DPAPI. A new GUI/CLI instance can read the previous turns.
- Files/directories use the existing current-user-owned Windows directory policy, with maximum 160 messages per session, each at most 8000 characters. The UI loads the most recent 40; model requests include at most 24 prior turns (bounded context).
- Attached source is sent as a separate lower-trust JSON-labelled reference message for one provider call. Only the typed user question and AI answer are saved in history, never the source snapshot, and later calls do not reuse it. The AI's answer may quote sensitive text from the source, so generated responses require care.
- History is **not automatically deleted or compacted**. Editing/canceling a failed request does not silently delete previously stored user turns. Backup/export, controlled deletion, conflict control and cross-device migration are later design work. DPAPI data cannot simply be copied to a different Windows account.
- Existing current-Windows-user security restrictions do not defend against every malicious same-user process or TOCTOU path replacement.

### Local project read policy

- A saved project session is **not** authorization to inspect its code. The GUI asks by Yes/No and the CLI requires confirmation or `--allow-read` for each request.
- Browse lists only one directory, at most 120 entries, with hidden/system/reparse paths filtered. Common sensitive directories (`.git`, `node_modules`, `bin`, `obj`, credential/secret folders) are denied. File extensions are allowlisted, and names containing `secret`, `password`, `token`, `credential`, or private-key patterns are excluded.
- Only text files that pass relative-path containment, directory/component reparse checks and bounded strict UTF-8 decoding can be previewed. Rejects `..` traversal, absolute paths, hidden files, symlinks/junctions, binaries and large files. No recursive scan or automatic prompt context injection.
- This is a conservative convenience preview, **not a secret scanner or complete DLP system**. A user-authorized source file can still contain secrets; preview it cautiously. Do not send it to a cloud model unless you explicitly choose to paste it yourself.

### Current verification

Release build and local automated tests cover encryption at rest, cross-instance persistence, chat request history, model failure behavior, consent denial, traversal protection, sensitive path filtering, binary/oversize refusal, UTF-8 BOM handling and CLI parser flag restrictions. An isolated Release CLI smoke uses a temporary Capri data root and disposable project, verifies refusal without consent, confirms one allowed preview, and proves the project folder is unchanged.

**Still required:** GitHub Windows CI for the final branch; manual WPF visual test of folder picker, async send/cancel and cross-client conversation loading; real local-model inference acceptance; a read/write diff approval workflow under the existing M5 task authority. This implementation does **not** deliver OpenCode feature parity or full interactive developer TUI.
