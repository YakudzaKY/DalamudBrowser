# DalamudBrowser

Workspace-oriented Dalamud plugin for managing collections of in-game browser views.

## Included

* Collections with zero or more browser views
* Per-view URL, visibility, lock and click-through settings
* Periodic URL availability checks with retry for endpoints like local ACT web pages
* Persisted position and size for each view window
* Placeholder renderer backend behind an abstraction for future real HTML/JS rendering

## Prerequisites

* XIVLauncher, FINAL FANTASY XIV, and Dalamud installed and run at least once
* .NET 10 SDK available locally
* Default Dalamud paths, or `DALAMUD_HOME` configured if you use a custom location

## Build

1. Open `DalamudBrowser.sln` in Visual Studio 2022 or Rider.
2. Build the solution in `Debug` or `Release`.
3. For local dev loading, use `DalamudBrowser/bin/x64/<Configuration>/DalamudBrowser.dll`.
4. The packaged release artifact is created under `DalamudBrowser/bin/x64/Release/DalamudBrowser/`.

## Load As Dev Plugin

1. In game, open Dalamud settings with `/xlsettings`.
2. In `Experimental`, add the full path to `DalamudBrowser.dll` from the build output directory.
3. Open `/xlplugins`, go to `Dev Tools > Installed Dev Plugins`, and enable `Dalamud Browser`.
4. Use `/dbrowser` to open the workspace manager.

## Current State

* The plugin already manages collections, view windows, layout persistence and link health checks.
* The actual HTML/JavaScript page surface is not connected yet.
* Recommended production renderer backend for this project: CEF off-screen rendering (OSR).

## References

* GoatCorp template: https://github.com/goatcorp/SamplePlugin
* Dalamud developer docs: https://dalamud.dev
