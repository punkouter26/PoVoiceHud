# ADR-0005: Effects are native GPU shaders and compositor animations, asleep unless something moves

Status: Accepted
Deciders: punkouter26
Date: 2026-10-20

## Context

The owner asked for ten graphics and sound options (G1–G10: glass, voice ribbon, verdict shockwave, thinking comet, spring physics, evidence particles, a sound palette, the answer materialising, frost and shatter on mute, and a session constellation) and chose all of them. The HUD is a WinUI 3 window that stays on top of the owner's work all day while Whisper uses the CPU. Effects must therefore cost nothing at rest, little while the voice moves, and never get in the way of reading a card.

## Decision

- **Shaders:** HLSL pixel shaders written in C# with **ComputeSharp.D2D1** (compiled at build time) and drawn by **Win2D** (`src/PoVoiceHud.App/Fx/Shaders.cs`). Not WebView2 with WebGPU/WebGL: that would add a browser process (~100 MB) to an always-on app, and it could not sit under the system acrylic the way a swap chain does.
- **Two surfaces:** a `CanvasSwapChainPanel` behind the pill's content (glass and ribbon), presented straight to the compositor because it changes every frame while someone talks, and a `CanvasControl` over the whole window (comet, shockwave, frost, shards, sparks), which is redrawn only during an effect.
- **Frame loop:** a `DispatcherQueueTimer` in `HudEffects`, 60 frames a second during an effect and 30 while only the voice moves, stopped otherwise. `CompositionTarget.Rendering` was tried first and cost three times as much, because it keeps all of XAML rendering at 60 Hz.
- **Motion:** springs, the history list's layout animation, the card swipe and the answer's blur-to-sharp run as Composition animations (`Fx/Motion.cs`), on the compositor's thread. Only the window height is stepped by hand (`Core/Fx/Spring`), because `AppWindow` has no animated resize.
- **Physics is in Core:** particles, shards, the constellation's force layout and the spring are plain `System.Numerics` code in `PoVoiceHud.Core.Fx`, with no reference to WinUI.
- **Sounds:** `SoundPalette` synthesizes the cues (FM, rendered once) and cuts every buffer at 240 ms. The segmenter drops anything under 300 ms, so a cue that call-audio capture picks up never turns into a transcript.
- **Levels:** `Settings.Effects` is `Full`, `Subtle` or `Off`. Windows' "Animation effects" switch and high contrast force Off; battery saver caps Full at Subtle. `Settings.Sounds` is a separate switch. Changing either one applies at once, without rebuilding the pipeline.

## Consequences

- Two new packages: `Microsoft.Graphics.Win2D` 1.4.0 and `ComputeSharp.D2D1.WinUI` 3.2.0 (App only).
- Measured on the Debug build (`docs/perf.md`): about 2 % of one core at rest at every level, and 8 % (Full) or 7 % (Subtle) while the voice moves.
- The glass cannot bend what is behind the window. WinUI 3 cannot read desktop pixels except through the system backdrop, so the ripple refracts the HUD's own aurora only.
- Swiping a card away has only been checked in code. Mouse and touch drags could not be tried, because synthetic input to the owner's desktop is off limits.
- Screenshots: `--demo --fx <moment>` plays one effect and holds a frame of it. `--effects Off|Subtle|Full` overrides the saved level (Debug builds only).
