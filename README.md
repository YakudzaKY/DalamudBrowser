# DalamudBrowser

Local repository scaffolded from the official GoatCorp `SamplePlugin` template for Dalamud / FFXIV plugin development.

## Included

* Dalamud plugin solution and project renamed to `DalamudBrowser`
* Sample command wired to `/dbrowser`
* Sample main window, config window, plugin JSON and GitHub Actions workflow
* Local git repository initialized for this folder

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
4. Use `/dbrowser` to open the sample window.

## References

* GoatCorp template: https://github.com/goatcorp/SamplePlugin
* Dalamud developer docs: https://dalamud.dev
