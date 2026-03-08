@echo off
setlocal

set "CONFIG=%~1"
if "%CONFIG%"=="" set "CONFIG=Debug"

pushd "%~dp0" >nul

if /I "%CONFIG%"=="Debug" goto build_one
if /I "%CONFIG%"=="Release" goto build_one
if /I "%CONFIG%"=="All" goto build_all

echo Usage: build.bat [Debug^|Release^|All]
popd >nul
exit /b 1

:build_one
echo Building DalamudBrowser.sln (%CONFIG%)...
dotnet build "DalamudBrowser.sln" -c %CONFIG%
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul
exit /b %EXIT_CODE%

:build_all
echo Building DalamudBrowser.sln (Debug)...
dotnet build "DalamudBrowser.sln" -c Debug
if errorlevel 1 goto fail

echo Building DalamudBrowser.sln (Release)...
dotnet build "DalamudBrowser.sln" -c Release
if errorlevel 1 goto fail

popd >nul
exit /b 0

:fail
set "EXIT_CODE=%ERRORLEVEL%"
popd >nul
exit /b %EXIT_CODE%
