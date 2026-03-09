# AGENTS.md

## Purpose

This repository contains `Dalamud Browser`, an experimental Dalamud plugin for rendering in-game browser views, aimed first at ACT, cactbot, and other local web tools.

The project is not a generic website browser yet. The current target is passive or low-interaction overlays inside FFXIV.

## Solution Layout

- `DalamudBrowser/`
  Main plugin project. Contains plugin entrypoint, config, workspace logic, windows, and plugin-side render backend.
- `DalamudBrowser.Common/`
  Shared protocol types used by the plugin and the external renderer process.
- `DalamudBrowser.Renderer/`
  External CEF offscreen renderer process. Owns Chromium/CEF and shared D3D11 texture creation.
- `.codex-temp/`
  Scratch/reference area. It currently contains a local clone of `Browsingway` used only as a reference. Do not treat it as part of the product.

## Runtime Architecture

- The plugin itself runs inside Dalamud/FFXIV.
- Browser rendering is out-of-process:
  - `DalamudBrowser.dll` starts `DalamudBrowser.Renderer.exe`
  - the renderer uses `CefSharp.OffScreen`
  - rendered pages are copied into shared `D3D11` textures
  - the plugin opens those textures and displays them through ImGui
- Keyboard input is intentionally not forwarded to the browser surface. This is deliberate to keep FFXIV keybinds with the game.
- View/window editing is handled in `BrowserWorkspace`.
- URL normalization and `file://` handling are centralized in `BrowserUrlUtility`.

## Important Files

- `DalamudBrowser/Plugin.cs`
  Plugin entrypoint and top-level wiring.
- `DalamudBrowser/Services/BrowserWorkspace.cs`
  Collection/view state, layout editing, availability checks, and render requests.
- `DalamudBrowser/Rendering/RemoteCefRenderBackend.cs`
  Plugin-side host for the external renderer process and shared texture consumption.
- `DalamudBrowser.Renderer/Program.cs`
  External renderer process entrypoint.
- `DalamudBrowser.Renderer/CefRuntime.cs`
  CEF initialization and runtime paths.
- `DalamudBrowser.Renderer/SharedTextureRenderHandler.cs`
  Offscreen paint -> D3D11 shared texture path.

## Build And Load

- Root build helper:
  - `build.bat`
  - `build.bat Debug`
  - `build.bat Release`
  - `build.bat All`
- Default build mode in `build.bat` is `Release`.
- Main dev plugin DLL:
  - `D:\git\AI REPOS\DalamudBrowser\DalamudBrowser\bin\x64\Release\DalamudBrowser.dll`
- The plugin must have a sibling renderer directory:
  - `D:\git\AI REPOS\DalamudBrowser\DalamudBrowser\bin\x64\Release\renderer\`

Do not point Dalamud at `DalamudBrowser.Common.dll` or any DLL inside the renderer folder.

## Build Gotchas

- If `Release` build fails with `DalamudBrowser.json` locked, the plugin is usually still loaded in Dalamud. Unload the dev plugin first, then rebuild.
- `Debug` builds are often easier while the plugin is actively loaded.
- The renderer runtime must remain complete next to the plugin build. In practice that means `renderer\` needs files such as:
  - `DalamudBrowser.Renderer.exe`
  - `CefSharp.Core.dll`
  - `CefSharp.dll`
  - `libcef.dll`
  - `locales\...`
- If you touch renderer packaging or output paths, verify both:
  - `DalamudBrowser\bin\x64\Release\DalamudBrowser.dll`
  - `DalamudBrowser\bin\x64\Release\renderer\...`

## Current Product Constraints

- Layout is still stored in pixels, not percentages.
- Browser interactivity is still limited compared with mature overlay browser plugins.
- Keyboard focus must stay game-first unless the user explicitly asks for a different model.
- The current UX is optimized around fixed overlays like ACT/cactbot, not arbitrary full browser workflows.

## Editing Rules For This Repo

- Keep protocol changes in sync across:
  - `DalamudBrowser.Common`
  - `DalamudBrowser`
  - `DalamudBrowser.Renderer`
- When changing renderer startup, check both assembly resolution and CEF resource paths.
- When changing layout editing, prefer keeping behavior in `BrowserWorkspace` rather than spreading it into UI windows.
- When changing URL handling, use `BrowserUrlUtility` instead of duplicating URI logic.
- Preserve the game-first input policy unless the user explicitly asks to relax it.

## Verification Checklist

After meaningful changes, prefer verifying:

1. `dotnet build DalamudBrowser.sln -c Debug`
2. `dotnet build DalamudBrowser.sln -c Release`
3. Dev plugin loads from `DalamudBrowser.dll`
4. `/dbrowser` opens the manager window
5. A `file://` ACT or cactbot page can be added without crashing the renderer

## Troubleshooting

- If the UI says `Starting browser renderer...` for too long, inspect:
  - `%APPDATA%\XIVLauncher\dalamud.log`
  - filter by `[DalamudBrowser] [Renderer]`
- If the renderer cannot find `CefSharp` assemblies, verify the `renderer` folder next to the plugin DLL.
- If FPS tanks while the renderer is failing, suspect a renderer restart loop first.

## Reference Material

- GoatCorp SamplePlugin template is the original scaffold base.
- `Browsingway` is a useful architectural reference for the external renderer/shared texture approach, but it is not the product source tree here.
