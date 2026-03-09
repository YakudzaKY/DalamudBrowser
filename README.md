# Dalamud Browser

Dalamud Browser is an experimental Dalamud plugin for rendering browser views in-game, with a workflow aimed first at ACT, cactbot, and other local web tools.

The current renderer path uses an external CEF renderer process plus shared D3D11 textures, so the browser no longer owns a child HWND inside the FFXIV process.

## Current Capabilities

- Collections containing zero or more browser views
- Per-view URL, visibility, lock, click-through, sound, zoom, opacity, and performance settings
- Optional per-view custom FPS controls for active, passive, and hidden states
- `http://`, `https://`, and `file://` URL support
- Periodic availability checks with retry for endpoints like local ACT pages
- ACT-aware recovery with process watching and `OVERLAY_WS` probing
- DPI-aware browser scale factor for the off-screen renderer
- Saved window position and size per view in viewport-relative percentages
- Unlocked drag-to-move behavior with corner-only resize handles
- Game-first focus policy so browser views do not intentionally take keyboard focus away from FFXIV

## Current Limitations

- The project is still experimental
- The current target is passive or low-interaction overlays such as ACT/cactbot
- Keyboard input is intentionally not forwarded to the browser surface
- General browser interaction is currently more limited than a mature overlay browser plugin
- CEF windowless rendering is capped at 60 FPS, so higher visible rates are not possible in the current backend
- Linux support is not implemented

## Build

Use the root build script:

- `build.bat` builds `Release`
- `build.bat Debug` builds `Debug`
- `build.bat All` builds both
- `build.bat --no-pause` disables the final pause

## Load As Dev Plugin

For dev plugin loading, use:

`D:\git\AI REPOS\DalamudBrowser\DalamudBrowser\bin\Release\DalamudBrowser.dll`

The external renderer files are produced under:

`D:\git\AI REPOS\DalamudBrowser\DalamudBrowser\bin\Release\renderer\`

The renderer also has an intermediate build output under:

`D:\git\AI REPOS\DalamudBrowser\DalamudBrowser\bin\x64\Release\renderer\`

Do not delete the `bin\Release\renderer` directory. The dev plugin path expects the renderer executable and CEF runtime files to exist there.

## Compared To Browsingway

This project is inspired by `Browsingway`, especially in the external browser renderer / process direction.

Key differences:

- workspace-first model with collections and multiple managed views
- built-in link retry and local `file://` overlay handling
- game-first input model that keeps keyboard input with FFXIV instead of forwarding it into browser views
- explicit per-view lock, click-through, sound, zoom, opacity, and performance controls
- viewport-percentage layout so views survive `windowed -> fullscreen` transitions more cleanly
- ACT-aware process watching plus websocket recovery
- optional custom per-view FPS for active, passive, and hidden states, within the 60 FPS ceiling of CEF OSR

`Dalamud Browser` is currently oriented more toward managed in-game web views and fixed overlays such as ACT/cactbot, rather than a broad fully interactive browser overlay workflow.

## Notes

This repository was developed with heavy assistance from Codex, a GPT-5-based coding agent.

The project also draws architectural inspiration from:

- GoatCorp's SamplePlugin template
- Browsingway for the external browser renderer/process direction

## References

- GoatCorp SamplePlugin: https://github.com/goatcorp/SamplePlugin
- Dalamud developer docs: https://dalamud.dev
- Browsingway: https://github.com/Styr1x/Browsingway
