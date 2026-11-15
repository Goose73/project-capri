# Capri companion animation clips

Status: executable whole-sprite gesture engine implemented in WPF; **optional original illustrated frames are not yet installed**. This is an increment towards the 2026 *Edgerunners 2* Capri animation brief, not a claim of identical production animation.

## What you can see now

- **Appear** when the actual companion becomes visible; optional **Disappear / hide** from the right-click menu.
- **Idle:** occasional look-left, look-right, nod, look-around, lean-in and body-wave gestures, roughly every 13 seconds. These stop when the companion is hidden, sleeping or Reduce Motion is enabled.
- **Real assistant states:** Thinking/Listening/Talking continue using different looping motions. Verified Success plays celebration; Happy hops; Error or Annoyed shakes; Surprised jumps. The existing state machine's idle dwell is unchanged.
- **Right-click Capri:** manual Appear, Disappear, Look left/right/around, Turn towards screen (visual only), Lean, Nod, Shake, Step left/right, Hop, Jump, Body wave and Celebrate.
- **Typed messages:** exact `hey capri`, `look at my screen`, `look around`, `look left`, `look right`, `nod`, `shake your head`, `jump`, `walk left`, `walk right`, `wave`, `celebrate` produce presentation cues alongside normal AI conversation; chat does not need a separate UI mode. Typing **look at my screen** produces a visible warning: no screen is captured or inspected.
- **Visual timing:** angular, exaggerated but bounded keyposes, small staged movement, crisp holds and magenta sprite echo derived from the **same authorized idle image**; avoids continuous high-frequency flicker. No new character artwork was copied or published.

This is **whole-sprite motion**. Leaning, taking steps and waving are an approximation: a static Capri drawing cannot independently move its hair, eyes, mouth, neck, arms or legs. The current optional frame-clip mechanism is the next step towards show-faithful movement.

## Installing approved additional animation frames (local, ignored by Git)

All illustrations remain local under `C:\Project Capri\design\assets\capri\`. Use original user-authorised transparent PNGs, not extracted frames from the show or screenshots. Do **not** replace `idle.png` without user approval.

Illustrated clips are found by **exact directory and frame filenames**:

```text
design/assets/capri/
  idle.png
  clips/
    blink/
      frame-00.png
      frame-01.png
      frame-02.png
    mouthspeak/
      frame-00.png
      frame-01.png
      frame-02.png
      frame-03.png
    sideeye/
      frame-00.png
      frame-01.png
    walkleft/
      frame-00.png
      frame-01.png
    walkright/
      frame-00.png
      frame-01.png
    lookleft/
      frame-00.png
      frame-01.png
```

The runtime accepts optional frame sequences for *any* named gesture (`appear`, `disappear`, `lookleft`, `lookright`, `lookaround`, `nod`, `shake`, `hop`, `jump`, `walkleft`, `walkright`, `turntoscreen`, `leanin`, `wave`, `celebrate`, `blink`, `mouthspeak`, `sideeye`) using lower-case enum names. Minimum **two** images, maximum **twelve** numbered `frame-00.png` to `frame-11.png`. Each file is capped at 8 MB and must reside in a real, non-linked local directory. Malformed or absent frames do not cause shape-based facial drawings or any privileged action. Keep all images aligned on the same transparent canvas, with consistent pose size and identical pivots.

- `blink`: full-frame illustrated lids/eyes closed then open. The renderer plays a brief blink roughly every 4.8 seconds while Idle, Listening or Thinking **only when these images exist**.
- `mouthspeak`: full-frame different mouth shapes, looped while the assistant is actually in Talking state. **This is a visual speech cycle, not phoneme-accurate audio lip sync.** No fake mouth overlays are painted onto a mismatching face.
- `sideeye`: separate illustrated eye-shift frame sequence, manually available only when artwork exists.
- `walkleft`/`walkright`, `lookleft`/`lookright` etc.: optional original keyposes replace the still artwork during the cue; whole-body movement follows the bounded motion track.

The right-click menu disables eye/mouth features if required frames are absent. A missing `idle.png` continues to show the existing approved placeholder; no animation is drawn over that placeholder.

## Permission and safety

An animation can never grant microphone, webcam, screen, window, project or automation permissions. `hey capri` **typed in chat** triggers arrival/attention only. A true always-listening spoken wake-word recogniser with opt-in microphone permission is **not yet implemented**. `turntoscreen` is a visual look cue **not** a proof of screen inspection. The on-screen message explicitly says no capture occurred. Successful work is not inferred from an animation; the core avatar state must actually transition.

All animations stop on hidden/unloaded views and respect **Reduce Motion**, which disables every gesture and face loop. The illustration never moves actual application windows or crosses monitors; bounding movement takes place inside Capri's transparent WPF character window.

## Acceptance remaining

Windows Release compilation and deterministic tests exercise the motion plan, bounds, frame ordering, reduction/disable settings, safe typed cue parsing and right/left locomotion. **Manual visual acceptance on the actual PC is still required** to verify click/drag/right-click behavior, time budget, picture quality, frame alignment and screen scaling. Actual speech wake word, permissioned screen vision, multi-part layer rig (independent eyes/lips/hair/limbs) and high-fidelity show animation remain future milestones. See [animation direction](CAPRI-ANIMATION-DIRECTION.md) and [asset specification](../design/CAPRI-ASSET-SPEC.md).
