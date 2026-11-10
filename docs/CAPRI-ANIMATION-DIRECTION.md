# Capri character animation direction and event safety

Research date: **10 November 2026**. Reference: **Capri in Cyberpunk: Edgerunners 2**, not the unrelated character Rebecca from the original season.

## Canonical behavioural and visual reference

Contemporary coverage describes Capri as an **effect-like, digital entity with deliberately fluid form**, rather than a normal physical person. Interviewed production staff described an initial red-silhouette/green-element concept, intentionally unconstrained shape and 2D abstraction. Commentary on her motion describes Kaneda-influenced dynamic, jagged visual effects, bold poses, highly contrasted timing, stark stillness followed by sudden movement, variable frame cadence and full-screen geometric compositions.

- [Director/staff interview, Game*Spark, 27 Oct 2026](https://en.gamespark.jp/article/2026/10/27/1670.html)
- [Review and animation analysis, Game*Spark, 19 Oct 2026](https://en.gamespark.jp/article/2026/10/19/1342.html)

**Creative direction for Capri desktop:** combine recognisable Capri likeness and animated effect-entity movement, not generic floaty anime breathing. Prioritise expressive key poses, abrupt but legible silhouette changes, angular/light-like transitions and deliberate hold-to-snap timing. Default poses can be readable with few frames; transitions should feel intentional. Walking or jumping around a desktop is an optional stylised performance layer, not a claim that the canonical character always moves like an ordinary human.

**Artwork and rights:** retain approved, user-supplied private sprite assets from `design/assets/capri/` and [the asset specification](../design/CAPRI-ASSET-SPEC.md). Do **not** copy animation frames/clips from the series, publish private reference images or add unlicensed character artwork to the public repo. Adapt mannerisms using authorised original keyframes/rigs supplied or approved by the user. No new artwork has been created or shipped in this increment.

## Implemented in the current source increment

- Pure `Capri.Core.Avatar.AvatarMotionPlanner` selects bounded, different whole-sprite movements for idle, listening, thinking, talking, success/happy, error/annoyed and surprised states. State selection is deterministic, testable and never asserts a tool was used.
- WPF `AvatarControl` applies modest X/Y sway, tilt, vertical stretch and a brief **arrival/reveal** when a real approved sprite becomes visible. It does not animate the missing-artwork placeholder.
- Existing **Reduce Motion** setting disables these animations. Hidden/unused views stop repeat motion. Tests cover the mapping, amplitude and accessibility settings.
- This is a **single-sprite animation fallback**, not complete character rigging; the app does not yet have independent eyes/mouth/limbs or real wake-word/screen-reading inputs.

## Animation and trigger design

Each cue requires a **real, authoritative event**. Never animate apparent observation of a user screen, microphone input or successful execution merely because the model mentioned it.

| Event / cue | Intended Capri performance | Preconditions |
| --- | --- | --- |
| Companion shown manually | Short abstract glow/cut-in, silhouette resolves into idle | Visible companion, approved sprite; implemented as basic fade/scale |
| “Hey Capri” wake word | Compact arrival effect, facing user, head/eye lock, attentive idle | **Actual** opt-in local wake-word detector and mic access; not yet implemented |
| Listening | Lean/tilt, alert eyes, short held pose | Active, disclosed microphone session, recognised listening state |
| Thinking / generating | Alternating glance, compressed pose, irregular short holds; scan pulses | Actual pending model inference; implemented as small scan-like whole-sprite motion |
| “Look at my screen” received | Turn towards monitor, gesture that access permission is needed | **Request received** only; no claim that screen was inspected |
| Screen permission granted | Side-step / pivot, lean towards selected monitor, scan pose | Explicit screen/window scope and OS capture permission |
| Screen image actually captured / analysed | Controlled eye/head tracking and screen-focused effect, then response | Real successful screenshot or UI Automation observation, model inspection and verification |
| Screen permission refused / capture fails | Look back to user, stop scanning, truthful verbal/visible explanation | Refusal or failure; must not use the “observed” animation |
| AI response begins | Eye focus, speaking posture, mouth animation keyed to output | Real streaming text or TTS; independent mouth rigs not yet implemented |
| Success | Single short hop/strong angular pose then settle | Verified outcome, **not** simply a dispatched command |
| Error / uncertainty | Head shake, asymmetric frustrated pose, then recover | Real error/unverified outcome |
| No speech for a while | Breathing, blinks, micro-glances, idle variants | Animation budget and inactivity window |
| User hides Capri | Collapse/dissolve, stop timers | Explicit hide; not yet animated |
| Developer session active | Different calm work/scan stance; do not block code review | Coding session actually active |

### Required animation library (production target)

**Movement:** appear, disappear, look-left, look-right, face-screen, turn-around, step-left, step-right, two-step walk, short hop, settle, lean-back, lean-in, stop-on-edge, celebratory jump.

**Head and eyes:** quick glance, sustained gaze, side-eye, head tilt, short nod, double nod, head shake, blink, double blink, squint, wide-eyed surprise, wink (when suitable), follow-attention, recover.

**Expression and speech:** smirk, grin, puzzled, emphatic, happy, annoyed, surprised, error/embarrassment; independent eye/brow/mouth channels, phoneme or viseme mouth shapes, speaking holds between phonemes, emphasis beats and conversational idle.

**Capri-like effect layer:** angular red/green silhouette fragments, bold light-like impact shapes, split-frame smear effects, snap/hold timing, rare geometric transition poses. Build from original cleared assets rather than ripped show footage. Avoid incessant strobe/flashing.

### Rig/asset minimums for authentic movement

- Approved original 2D keyframes or rig components for front / 3-quarter / side / turned-away poses, plus full-body walk/jump silhouettes.
- Separately registered **eyes, eyelids, eyebrows and mouth** layers with common anchor points; no hard-coded eyes painted over unmodified single images.
- Named animation clips with frame cadence, intended screen direction and pivots; scale to monitor DPI and character height.
- Reversible timing when suitable, but do **not** mirror asymmetric character details without an approved mirrored sprite.
- Use WPF Storyboards for lightweight fallback only. Graduate to a sprite-atlas or layered skeletal compositor as genuine assets arrive; preserve the original silhouette and avoid procedural geometric avatar substitution.

## Motion controller proposal

1. **Event source:** UI visibility, AI inference lifecycle, task-authority state, wake word, speech, screen permission/observation. Voice and screen events must be implemented and tested before wiring those animations.
2. **Policy selector:** select candidate cue based on event, confidence, permission and user motion settings. Explicit user action and stop/error states preempt idle flair.
3. **Sequence scheduler:** a small deterministic priority queue: `stop/security` > `permission` > `response` > `task` > `idle`. Drop stale cues after cancellation or authority reconnect.
4. **Mixer:** pose/locomotion channel + facial channel + mouth channel + effect channel. Enable independently only if the approved art manifest supports them.
5. **Renderer:** an image-backed WPF/2D compositor with reduced-motion fallback, frame budget, offscreen pause and no automatic monitor movement without the user's permission.

**Animation is presentation, not authorization.** It cannot read the screen, execute tasks or alter permissions. It must not invent successful actions or AI understanding, and cannot override the exact-plan approval/stop/no-replay authority.

## Quality and acceptance

- No unlicensed visual or audio assets in public commits. The existing current reference assets remain private.
- `ReduceMotion` produces an unchanged still pose for every cue; avoid flicker and harmful high-frequency flashes. Offer separate toggles for idle animation and large travel when the UI ships.
- No automatic animation at startup if no approved character sprite exists. The missing-artwork placeholder remains still.
- Rendering should pause when companion is hidden, running at a reasonable frame/power budget; check multi-monitor, DPI, screen edges and mouse hit-testing.
- Automatic tests cover event mapping, cross-session event isolation, stale cue cancellation, permission gates and reduced-motion; **manual Windows visual acceptance** needed for real sprite timing, character likeness, pose readability and faithful reference motion.
- Wake word and screen reaction acceptance requires genuine user-controlled microphone and screen capture; the current Null voice services and architecture-only screen plan are explicitly **not** acceptance evidence.

### Next development steps

1. Add an image animation manifest and clip scheduler without changing user-supplied art.
2. Build approved sprite/rig pipeline for front/side/back + independent eyes/mouth/limbs; add real arrival, look-around, nod/shake, hop, paced walk clips.
3. Add verified lifecycle events and permissions (wake-word recogniser, screen inspection) before binding response-specific cues.
4. WPF visual acceptance with user-supplied/approved references; optimise performance, reduced-motion and transitions, then broaden expression vocabulary.
