# Capri Character Asset Specification (required for accurate avatar)

## Status (updated — plan approved with adjustments)
- **Reference images present (local):** `design/references/reference-01.jpg`–`reference-06.webp` (6 files). Keep local; gitignored.
- **PRIMARY idle target:** `design/references/reference-01.jpg` for Capri's **normal idle pose**.
- **Supporting references:** `reference-02.jpg`–`reference-06.webp` for correct facial geometry, secondary angles, and expressions.
- **No suitable image generation/editing tool found** on this machine (`magick`/`inkscape` not installed; `ffmpeg` present but not appropriate for character illustration). **Revised artwork must be produced with a suitable illustration or image-generation tool.** I cannot generate the illustration here.
- **Current `design/assets/capri/idle.png` must NOT be overwritten** until you supply and approve the replacement. I must not claim the sprite has been revised when only its specification has changed.

## Required revised idle deliverable (first, after approval of asset)
Create a **recognisably Capri** transparent character illustration matching references (local prototype use only; do not publish copyrighted extracted artwork).

| Requirement | Value |
|---|---|
| File | `design/assets/capri/idle.png` (PNG with alpha) — **replace only after you supply + approve** |
| Minimum size | **1024 px tall** (full character/silhouette as used in companion) |
| Background | Fully transparent |
| Primary pose target | `reference-01.jpg` (normal idle) |
| Style | Detailed 2D illustrated/animation-style artwork; flat saturated colors, bold black shapes/outlines, sharp stylized anatomy, dramatic asymmetry, fine **grain/speckling** and bold 2D animation appearance |

### Must improve (likeness priority over animation/UI/features)
1. **Enormous, irregular, asymmetrical hot-pink hair**, including overlapping bangs and deep black separations.
2. **Aggressive, fragmented green-and-black facial shapes**.
3. **Oversized blue eyes** and their **angular black surrounds**.
4. **Irregular black mouth** with **uneven white teeth**.
5. **Sharp shoulder silhouette, clothing and proportions**.
6. **Grain, speckling and bold 2D animation appearance**.

### Avoid
- Symmetrical starburst/crown hair, uniform spikes, basic geometric mascot.
- Basic WPF polygons/procedural drawing to recreate the character (renderer is image-backed).
- Circles/rectangles/pentagon face, generic anime styling, invented human facial features, cleaned-up regularized silhouette.

## Optional expression/state assets (after idle approval)
Place alongside `idle.png` (renderer falls back to `idle.png` if missing):
- `listening.png`, `thinking.png`, `talking.png`/`speaking.png`, `happy.png`/`success.png`, `annoyed.png`/`error.png`, `surprised.png`, `sleep.png`/`inactive.png`

## App integration (preserve)
`Capri.Desktop` loads state assets from `design/assets/capri/` (and checks output-relative paths). The character is rendered as an **image sprite in WPF** (transparent, draggable companion window) — **not** constructed from basic WPF polygons. Keep existing image-backed renderer and all working Capri features. Animations/motion applied as lightweight transforms after visual accuracy is established.

## When new image is provided
1. Verify file exists/dimensions/transparency (no overwrite before your approval).
2. Run `dotnet build C:\Project Capri\Capri.sln -c Debug` and relevant tests (independent of artwork).
3. Relaunch and capture render (with approval) to `design/capri-idle-render-v2.png` (or next version).
4. Perform **actual visual comparison** against `reference-01.jpg` (primary) and `reference-02–06` (supporting) — report differences honestly; **no claim of accuracy** without your review. Use `free-vision` with actual image attachments if available; report errors if not.
