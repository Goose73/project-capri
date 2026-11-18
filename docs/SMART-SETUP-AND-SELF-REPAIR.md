# Smart setup and conservative self-repair

Status: first-stage **safe Windows startup preflight** implemented; automatic model setup and AI-driven repair are **not** yet implemented.

Capri's goal is to reduce manual setup while **never changing deliberate custom configuration just because it differs from Capri's default**.

## Startup repair now

Before the WPF service host reads settings, `SettingsStartupRecovery` performs a local, non-network file preflight:

| Case | What Capri does |
| --- | --- |
| First boot with no settings or recovery metadata | Creates safe, cloud-disabled default `settings.json` and a digest-verified last-good snapshot |
| Existing valid settings | Reads them without rewriting. Does not fix unusual providers/values on its own |
| Verified Capri-managed settings file has been deleted | Restores exact last-good snapshot; no changes to selected model, cloud consent or other saved fields |
| Verified Capri-managed settings file becomes zero bytes | Archives the damaged file and restores the verified last-good snapshot |
| Non-empty malformed config, even Capri-managed | Leaves it unchanged and displays a review warning; may contain manually edited custom data |
| Existing untracked/custom config | Preserves it; no adoption of ownership or automatic write |
| Valid managed file changes outside Capri | Retires its automatic recovery authority via a marker, leaving contents exactly as authored |
| Recovery snapshot/digest is missing or tampered | Refuses automatic restoration |
| Concurrent manual edits while Capri is running | Settings save is rejected unless refreshed, preventing stale GUI writes |
| Unknown JSON fields or custom provider ID | Preserved through normal settings save and reload |

The files under `%LOCALAPPDATA%\Capri` (or `CAPRI_DATA_DIR` for a custom data root) are:
- `settings.json` — non-secret Capri settings (user may add custom values)
- `settings.managed.json` — digest-only record proving Capri created/tracked the current config
- `settings.last-good.json` — exact last successfully tracked settings snapshot
- `settings.user-managed` — records manual ownership after an externally changed managed config is observed
- `settings.json.damaged-*.bak` — archival proof of a zero-byte file before restoring it

**The manifest and snapshot are not credentials.** Actual API keys remain in DPAPI's separate credential store. Since manually added extra JSON may still contain private text, keep the backup files private, too.

## Safe defaults and protected user choices

The startup preflight never changes a selected local model, custom provider endpoint or tag, UI/animation/voice preference, saved plugin-like JSON extension, cloud opt-in, microphone/webcam permission or emergency task-stop latch. It never downloads models, launches Ollama automatically, scans the PC, or runs a shell command.

A missing supported field can still be filled by Capri's existing **in-memory** schema migration, but the file is not overwritten by startup. A deliberate custom setting is not treated as broken merely because the service is unreachable (that may be intentional or temporary).

A warning about malformed custom settings is not an autonomous recovery claim. Review the file or run:

```powershell
capri doctor
capri setup --repair
```

The setup wizard currently remains an *interactive* process with real model consent and no automatic downloads. The next guided-onboarding phase should recommend an already-installed model based on user-approved VRAM/RAM inspection, run a bounded local inference test and suggest exact reversible fixes. Missing software or cloud account access must remain opt-in.

## Operational safety and limitations

- On any failed or ambiguous recovery, the original settings are not rewritten. Users may still need to repair a malformed file explicitly.
- During a managed settings save, Capri tries to update the snapshot atomically. If that metadata update fails, the changed config remains valid but automatic recovery may be disabled until a future verified write.
- A digest marker is an integrity and *provenance hint*, not a secure tamper-proof signature; same-user processes can change metadata. It must never be used as a privilege or secret-access boundary.
- This first increment does not reconcile competing edits across multiple running instances. It detects byte changes between load and save, and asks for a reload instead of silently replacing the other edit.
- Recovery always uses the last complete saved snapshot; unsaved changes cannot be recovered.
- On first boot the safe default may still require a model to be selected before AI chat can work. There is no claim that setup finishes unattended.

## Upcoming implementation

- Smart local default recommendation based on consented hardware probes, available model tags and real warm/cold inference times, without surprise downloads.
- Explicit repair screen with *before/after preview*, recoverable backup and one-click user confirmation for nontrivial changes.
- Persistent managed/custom ownership controls in GUI and CLI, together with health checks that never silently reinterpret unusual provider URLs.
- Reversible update/install repair and automatic migration tests for custom settings and extensions.
