# Dalamud Browser

Dalamud Browser is an experimental Dalamud plugin for rendering browser views in-game, with a workflow aimed first at ACT, cactbot, and other local web tools.

The current renderer path uses an external CEF renderer process plus shared D3D11 textures, so the browser no longer owns a child HWND inside the FFXIV process.

## Current Capabilities

- Collections containing zero or more browser views
- Per-view URL, visibility, lock, click-through, sound, zoom, and performance preset settings
- `http://`, `https://`, and `file://` URL support
- Periodic availability checks with retry for endpoints like local ACT pages
- Saved window position and size per view
- Unlocked drag-to-move behavior with corner-only resize handles
- Game-first focus policy so browser views do not intentionally take keyboard focus away from FFXIV

## Current Limitations

- The project is still experimental
- The current target is passive or low-interaction overlays such as ACT/cactbot
- Keyboard input is intentionally not forwarded to the browser surface
- General browser interaction is currently more limited than a mature overlay browser plugin
- Layout is still stored in pixels today, not percentages
- Opacity controls and ACT-specific process integration are not implemented yet

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

`D:\git\AI REPOS\DalamudBrowser\DalamudBrowser\bin\x64\Release\renderer\`

Do not delete that `renderer` directory. The plugin expects the renderer executable and CEF runtime files to exist there.

## Compared To Browsingway

`Browsingway` is the more mature overlay-oriented project today. It is further along in polish for fullscreen browser overlays and already has features such as opacity control, DPI-oriented behavior, and ACT-specific optimizations.

`Dalamud Browser` is different in focus:

- workspace-first model with collections and multiple managed views
- built-in link retry and local `file://` overlay handling
- stricter game-first input policy to avoid browser focus stealing
- explicit per-view lock, click-through, sound, zoom, and performance presets

Right now this project is not a drop-in replacement for `Browsingway`. It is closer to a browser workspace for fixed in-game web views than a fully general-purpose overlay browser.

## Notes

This repository was developed with heavy assistance from Codex, a GPT-5-based coding agent.

The project also draws architectural inspiration from:

- GoatCorp's SamplePlugin template
- Browsingway for the external browser renderer/process direction

## References

- GoatCorp SamplePlugin: https://github.com/goatcorp/SamplePlugin
- Dalamud developer docs: https://dalamud.dev
- Browsingway: https://github.com/Styr1x/Browsingway
