# Project Capri — Visual Design Spec (text-derived)

**Status:** Implemented in `src/Capri.Desktop/**` from the owner’s detailed textual brief. **No reference image files were accessible/present** in `C:\Project Capri\design\references` during this pass, so visual comparison against the six supplied screenshots was **not possible**. The avatar is an original, stylized 2D vector/XAML interpretation targeting the described likeness (this is a known limitation; human visual review against the references is still needed).

## Implementation notes (Desktop)
- **Avatar:** `Controls/AvatarControl.xaml(.cs)` redesigned as layered XAML vector art (Polygons/Polylines/Ellipses/Grids) with `VisualStateManager` states for `Idle, Listening, Thinking, Talking, Happy, Success, Annoyed, Error, Surprised, Sleep`. Keeps compatible `State` and `ReduceMotion` dependency properties for `AvatarPresentationService`. `Success`/`Happy` share happy visuals; `Error`/`Annoyed` share annoyed/glitch visuals.
- **Companion UX:** `Views/CharacterWindow` is a transparent, borderless, topmost tool window showing only the avatar (~240x280 default, Viewbox-scaled character ~200–300px tall). Draggable via mouse movement threshold; click toggles `Views/ChatPanelWindow`. `CharacterWindow` implements `IOverlayController` (tray show/hide/toggle controls the character companion; hiding character hides chat). Best-effort hit testing: window/root background is null so rendered character elements receive input; exact silhouette click-through depends on WPF transparent-window behavior.
- **Chat panel:** compact dark translucent panel (`#101015` surfaces, magenta outlines, blue/green highlights), positioned beside the character and repositioned when the character moves. Preserves existing `OverlayChatViewModel` bindings/commands: message history, text input, Send, Stop (`CancelCommand`), model indicator, status, busy progress. Adds a mic button with placeholder action (voice UI not wired).
- **Settings:** dark themed with magenta outlines and organized sections for AI providers, Voice/mic, Character/animations, Windows automation permissions, Appearance/sizing, Startup preferences, Privacy/screen control. Existing provider/automation settings, secure `PasswordBox` API-key behavior, test connection, and save flow remain functional. New sections that lack backing Core settings are presented as grouped placeholders/disabled controls and noted as integration-dependent where appropriate.
- **DI/App wiring:** `DesktopServiceCollectionExtensions` registers `CharacterWindow`/`ChatPanelWindow`; `IOverlayController` resolves to `CharacterWindow`. `App.xaml.cs` creates the character companion, honors `Ui.StartMinimized`, and allows shutdown close for companion/chat windows. `TrayIconService` opens settings via `CharacterWindow`.

## Character (Capri)
- **Hair:** explosive jagged hot-pink/magenta (`#FF176C`) silhouette, large asymmetric spikes, thick black separations (`#150B19`), subtle speckle/noise texture feel; hair dominates silhouette. No realistic strands/curls.
- **Face:** angular neon-green (`#00FF65`) face plane, sharp/triangular geometry, not human skin.
- **Eyes:** very large circular electric-blue (`#388FFF`) irises, dark outlines, geometric black eye shapes/accents, small white catchlights; exaggerated expressions.
- **Mouth/lower face:** mostly black area, jagged mouth line, small sharp white teeth; expressive.
- **Body/clothing (bust/shoulders visible in companion mode):** slim angular black clothing, magenta/green/purple (`#974BFF`) geometric accents, white (`#F8F5FF`) angular shoulder shapes, bold outlines, flat 2D.
- **Style:** thick bold outlines, flat saturated color, minimal soft shading, sharp polygons, strong silhouette, optional subtle glitch/grain accents, lightweight animation.

## Palette
| Element | Hex |
|---|---|
| Hair | `#FF176C` |
| Face | `#00FF65` |
| Eyes | `#388FFF` |
| Outlines | `#150B19` |
| Purple accents | `#974BFF` |
| White highlights | `#F8F5FF` |
| Interface background | `#101015` |

## Desktop companion
- Transparent character window, ~200–300px tall default, draggable, bottom-right initial placement, no permanent large dashboard.
- Click/toggle opens a compact dark translucent chat panel beside/near the character.
- Themed settings (dark, magenta outlines, readable text).

## States
Idle, Listening, Thinking, Speaking/Talking, Happy/Success, Annoyed/Error, Surprised, Sleep/Inactive. Smooth transitions; reduced motion lowers animation.
