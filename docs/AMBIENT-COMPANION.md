# Ambient Companion: opt-in local webcam awareness

Status: **privacy/scheduling foundation in source; camera capture and proactive speech are NOT implemented or enabled**.

## Goal

Make Capri feel present during gaming, work and quiet time without requiring a
request every time. With informed consent, a webcam can provide occasional
visual observations that produce restrained, relevant reactions and optional
spoken remarks. The webcam sees the person and environment, **not game pixels**
or desktop windows; those would require an independent, separately consented
screen-capture feature.

This should complement Capri's existing image-backed animated companion, not
turn her into a hidden video recorder or an autonomous desktop action engine.

## Free/local architecture

1. **Explicit opt-in:** webcam awareness is off by default. Settings preference,
   Windows camera permission and a *fresh per-launch session consent* are all
   required before capturing anything. A stored preference alone never starts
   the camera. Provide a clear ON/PAUSED/OFF indicator and immediate stop.
2. **Select device:** offer a webcam selector and real preview only after
   initiating a consented session; no silent device probing/capture loop.
3. **Sample occasionally:** target 640×360 or 640×480 snapshots once every
   20 seconds initially (hard minimum 10 seconds), with no overlapping inference.
   Stop when locked, paused, webcam unplugged, or local model unavailable. Allow
   adaptive backoff while gaming or when inference competes with the GPU.
4. **Use local image understanding:** a compatible Ollama vision model, initially
   `qwen3.5:4b` when verified locally, can receive a temporary JPEG image via
   `/api/chat` with `messages[].images`. Validate image modality before
   enabling the feature and target the local trusted Ollama loopback endpoint,
   not an arbitrary remote/cloud provider. Do not silently switch to cloud AI.
5. **Discard raw images:** do not persist frames, video, camera thumbnails,
   user-specific observations or inferred profiles by default; no automatic
   screenshots, uploads, debug image logs, history records or telemetry.
6. **Commentary is separately optional:** don't speak every observation.
   Require genuine, fresh, meaningful information; limit spontaneous remarks to
   at most one every 5 minutes by default (hard minimum 2 minutes), with a
   quiet/working mode, a manual silence toggle and session-aware interruption.
   Don't infer emotions, identity, private conversations or other people's
   intentions from a webcam frame; acknowledge uncertainty.
7. **Animate based on actual evidence:** when capture begins, Capri can look
   toward the user, and after successful analysis show a reaction; screen-turning
   never asserts screen access. Silence and reduced-motion settings override
   presentation. A webcam view alone never triggers action execution.
8. **Keep control local:** camera frames and vision results must not flow into
   automation/task plans, logging, cloud providers or voice services without
   distinct, explicit user action and an applicable permission flow.

## What this increment implements

- `AppSettings.AmbientVision` saves only **disabled-by-default** preferences.
  Nothing auto-starts.
- `AmbientVisionPolicy.Evaluate(...)` is a pure, testable decision function that
  requires current-session consent, Windows permission, an active visible cue,
  a local vision model and no lock/pause/overlapping work.
- Sampling and commentary cooldowns have hard minimums; spontaneous comments
  additionally require a recent successful meaningful observation and an idle
  conversation state.
- Automated tests cover default-off behaviour, consent and privacy gates,
  cadence, cooldown, locked sessions and local-only restrictions.

**Not implemented:** real webcam capture, image/video sampling loop, active
camera status UI, local image inference integration, streaming model context,
speech synthesis, frame/content safety classification, multi-monitor awareness,
game detection or production Windows acceptance. No user webcam is accessed.

## Future acceptance milestones

A. Implement per-session user-controlled camera preview and capture adapter
   using supported Windows camera APIs; verify OS permission prompts, LED /
   Windows indicators and reliable teardown.
B. Add local Ollama image adapter with strict loopback checks, bounded images,
   predictable timeouts, context minimisation and frame disposal.
C. Implement lifecycle coordination (pause/lock/restart/multitasking), adaptive
   resource budgeting, visible camera/processing status and accessible controls.
D. Add opt-in remarks, interruption rules, audio output and expressive
   animation cues tied only to actual completed vision inference.
E. Perform supervised testing on the user's Windows PC and webcam (including
   games running on the RTX 3050 6GB); verify no capture after pause or lock,
   no cloud transfers, correct consent, reasonable GPU load and no misleading
   observations.

## References

- [Ollama official vision documentation](https://github.com/ollama/ollama/blob/main/docs/capabilities/vision.mdx)
- [Ollama qwen3.5 model library](https://ollama.com/library/qwen3.5)
- [Windows webcam privacy settings and user controls](https://learn.microsoft.com/en-us/windows/apps/develop/camera/camera-privacy-setting)
- [Windows MediaCapture API](https://learn.microsoft.com/en-us/uwp/api/windows.media.capture.mediacapture)
