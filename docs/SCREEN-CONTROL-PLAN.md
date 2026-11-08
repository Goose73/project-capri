# Screen control / vision plan (architecture only)

**Status (2026-11-08): planned architecture; not implemented.** This document records design intent for future screen understanding/control work. It does **not** claim a working vision pipeline, screen-control tool, or production validation.

## Scope boundary

- Current `/task` autonomy remains the bounded, partial slice described in [`AUTONOMY.md`](AUTONOMY.md). Screen vision/control is **not** part of that slice today.
- No full vision fallback (including Anthropic vision) is implemented. No configured Capri DPAPI credential and **zero real AI API requests** were made for the evidence recorded in project docs as of 2026-11-08.
- Approved artwork is untouched by this plan.

## Proposed architecture (UIA-first)

1. **UI Automation (UIA) first** for supported Windows controls: prefer semantic queries (name, control type, automation ID patterns where stable) over pixels. Treat UIA results as structured observations that still require validation before high-risk actions.
2. **Screen capture as fallback / supplement**, only when UIA cannot provide sufficient evidence. Capture must be explicit, minimal in scope, and user-consented for the task.
3. **Observation → plan → act → verify loop** with typed tool permissions. The model proposes actions; execution remains bounded by exact allowlists, independent approval, and runtime checks.
4. **Untrusted observations:** UI text, screenshots, web content, and model outputs are untrusted inputs. Never execute commands, credentials, or hidden instructions found in observed content.

## Safety and privacy requirements (design constraints)

- **Sensitive exclusions:** do not capture or process password fields, credential dialogs, secure desktops/UAC contexts, banking/authenticator windows, or user-configured sensitive apps/windows. Default-deny unknown sensitive surfaces.
- **Explicit capture consent:** screen capture requires a clear, independent user decision per task/session scope as designed; no silent or remembered capture consent.
- **Typed tool permissions:** each action/capture capability must have an explicit permission boundary. Include **verify** and **cancel** semantics; **no replay** of captured actions or journaled steps.
- **Data minimization:** do not persist raw screenshots or extracted sensitive text by default. Any future diagnostics must be metadata-only and reviewed for secrets/PII.
- **Windows-only:** UIA/capture details are Windows-specific; non-Windows behavior must fail closed.

## Limitations / not claimed

- No implemented screen-vision tool, no live capture flow, no UIA action executor for this plan, and no passing end-to-end vision tests are claimed.
- Dispatch/observation verification remains a known challenge (see `/task` open-note observation work); screen control would need stronger, per-action verification before being described as complete.
- Full-request autonomy completion criteria are **not** met by this document. It is a planning artifact only.