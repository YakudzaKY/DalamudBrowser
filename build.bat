@echo off
setlocal

set "CONFIG=%~1"
set "NO_PAUSE="
if /I "%~1"=="--no-pause" (
    set "CONFIG="
    set "NO_PAUSE=1"
)
if /I "%~2"=="--no-pause" set "NO_PAUSE=1"
if "%CONFIG%"=="" set "CONFIG=Release"

pushd "%~dp0" >nul

if /I "%CONFIG%"=="Debug" goto build_one
if /I "%CONFIG%"=="Release" goto build_one
if /I "%CONFIG%"=="All" goto build_all

echo Usage: build.bat [Debug^|Release^|All] [--no-pause]
set "EXIT_CODE=1"
goto finish

:build_one
echo Building DalamudBrowser.sln (%CONFIG%)...
dotnet build "DalamudBrowser.sln" -c %CONFIG%
set "EXIT_CODE=%ERRORLEVEL%"
goto finish

:build_all
echo Building DalamudBrowser.sln (Debug)...
dotnet build "DalamudBrowser.sln" -c Debug
if errorlevel 1 goto fail

echo Building DalamudBrowser.sln (Release)...
dotnet build "DalamudBrowser.sln" -c Release
if errorlevel 1 goto fail

set "EXIT_CODE=0"
goto finish

:fail
set "EXIT_CODE=%ERRORLEVEL%"
goto finish

:finish
popd >nul
if not defined NO_PAUSE pause
exit /b %EXIT_CODE%
