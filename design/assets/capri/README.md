# Capri character assets (optional, local)

The public repository does **not** include copyrighted/proprietary Capri artwork.  
`Capri.Desktop` uses an image-backed sprite renderer and shows a built-in placeholder when no asset is present.

## Local installation (not committed)
Place an approved transparent sprite here (gitignored):
- `idle.png` (required for idle display)
- Optional state sprites: `listening.png`, `thinking.png`, `talking.png`/`speaking.png`, `happy.png`/`success.png`, `annoyed.png`/`error.png`, `surprised.png`, `sleep.png`/`inactive.png`

For extra animation expressions, use private numbered PNG frames such as `clips/blink/frame-00.png`, `clips/blink/frame-01.png`, `clips/mouthspeak/frame-00.png` etc. The renderer supports up to twelve frames per clip; all source art stays gitignored. See [clip format](../../../docs/AVATAR-ANIMATION-CLIPS.md) and `../CAPRI-ASSET-SPEC.md` for requirements. Review artwork rights before any public distribution.
