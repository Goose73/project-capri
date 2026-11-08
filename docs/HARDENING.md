# Hardening status (local working tree)

This is a **cooperative single-owner foundation**, not full M5 per-user IPC task authority or shared approvals. Task-control creates missing coordination directories with the current user's owner and a protected, inheritable DACL (plus SYSTEM/Administrators access), then validates existing directories without changing their ownership or ACL. Test temporary roots are created the same way, avoiding elevated-runner inherited ownership. It also uses exclusive cross-process locking around shared task state, reparse checks, real process start identity for ownership (no expiry of a live owner), and fail-closed corrupt-state handling. For cancellation, the coordinator cancels waits and requests queued work to stop, but it **cannot force completion of an uncooperative in-flight provider/blocking operation** and cannot undo already-dispatched external actions. CLI approval input is cancellable, and chat cancellation uses an independent input pump.

**Verified checks (bounded, not live acceptance):** latest local Release build **0 warnings, 0 errors** and **586/586 tests passed** (exit 0), including current-user protected task-directory initialization coverage. CI run 42524076407 previously failed 15/585 tests; the task-control failures show `ValidateDirectory` rejecting ownership at `FileTaskControl.cs:251`. Previous independent Release QA reported **577 tests passing** with zero warnings, plus **2 separate process tests** passed. Setup changes include diagnostics-only behavior with no saves in that path, guarded provisional saves, and exact tag matching. Reduced motion is persisted; Release launcher and CI smoke checks are recorded in publication-prep changes. These are bounded automated checks, **not** live supervised task acceptance.

The same Windows user can still swap a path between validation and use (TOCTOU). These checks **do not** defend against a malicious same-user actor. There is no central per-user IPC authority and CLI/WPF approvals are not unified into one shared approval authority. Do not describe this as complete M4 or M5 security isolation.

An old requested disposable workspace exists but is **empty**. **That is no evidence of completion** and **not proof that no plan/approval ever occurred**. M4 supervised end-to-end acceptance and manual real Windows Terminal TTY chat multi-turn/streaming/Ctrl+C checks remain pending. Run those only with explicit user approval and a suitable disposable workspace.

## Future secure per-user IPC milestones (not implemented)

To move beyond the current cooperative foundation, future work must provide:
- A real per-user IPC/task authority (not file-only cooperative coordination) with authenticated/authorized caller identity.
- Shared, authoritative approval state across CLI/WPF (no split approval authority; explicit approval binding to exact plan/request).
- No replay of task actions from metadata; durable state must remain metadata/control only and fail closed.
- TOCTOU-resistant path/handle validation for sensitive operations, or an explicit threat model accepting same-user limitations.
- Clear dispatch/undo boundaries and idempotency/compensation rules for external actions.

Local model cold loading can exceed the default 60-second inference timeout. If an approved local test times out, configure `provider.timeoutSeconds` to a bounded larger value (e.g. 300), rerun once, and report real elapsed time and outcome. Do not present synthetic progress or infer a successful response from a timeout. No cloud or model download is required for safe CLI smoke checks.

No source license has been granted yet. The owner must choose licensing terms before granting a license or community reuse permission; this is **not a barrier to the owner publishing their own code**. Optional artwork has separate rights and is not covered by a source license decision.
