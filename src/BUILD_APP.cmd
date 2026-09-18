@echo off
setlocal EnableExtensions
cd /d "%~dp0"
title Sabby Optimizer - Build Real App
color 0F

echo ============================================================
echo             SABBY OPTIMIZER - REAL APP BUILDER
echo ============================================================
echo.
echo This builds the Release app and the Windows Setup.exe installer.
echo.

where powershell.exe >nul 2>nul
if errorlevel 1 (
  echo [ERROR] Windows PowerShell was not found.
  goto :fail
)

where dotnet.exe >nul 2>nul
if errorlevel 1 (
  echo [MISSING] .NET 10 SDK is not installed or not on PATH.
  echo.
  echo Open Windows Terminal as Administrator and run:
  echo   winget install Microsoft.DotNet.SDK.10 --source winget
  echo.
  goto :fail
)

set "HAS_DOTNET10="
for /f "tokens=1" %%V in ('dotnet --list-sdks 2^>nul') do (
  echo %%V | findstr /b /c:"10." >nul && set "HAS_DOTNET10=1"
)
if not defined HAS_DOTNET10 (
  echo [MISSING] .NET 10 SDK was not detected.
  echo.
  echo Open Windows Terminal as Administrator and run:
  echo   winget install Microsoft.DotNet.SDK.10 --source winget
  echo.
  goto :fail
)

set "INNO="
for /f "usebackq delims=" %%I in (`powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Release\Find-InnoSetup.ps1" 2^>nul`) do if not defined INNO set "INNO=%%I"

if not defined INNO (
  echo [MISSING] Inno Setup 7 or 6 could not be located.
  echo.
  echo If you already installed it, verify this file exists:
  echo   %%LOCALAPPDATA%%\Programs\Inno Setup 7\ISCC.exe
  echo.
  echo Or install the current release with:
  echo   winget install --id JRSoftware.InnoSetup.7 -e -s winget -i
  echo.
  goto :fail
)

echo [OK] .NET 10 SDK detected.
echo [OK] Inno Setup detected:
echo      %INNO%
echo.
for /f "usebackq delims=" %%V in (`powershell.exe -NoProfile -Command "[xml]$p=Get-Content -LiteralPath '.\SabbyOptimizer\SabbyOptimizer.csproj'; [string]$p.Project.PropertyGroup.Version"`) do set "SABBY_VERSION=%%V"
if not defined SABBY_VERSION set "SABBY_VERSION=unknown"
echo Building Sabby Optimizer %SABBY_VERSION%...
echo.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Release\Build-Release.ps1" -RequireInstaller
if errorlevel 1 goto :buildfail

echo.
echo ============================================================
echo BUILD COMPLETE
echo ============================================================
echo.
echo App EXE folder:
echo   artifacts\publish\win-x64
echo.
echo Installer folder:
echo   artifacts\installer
echo.
echo Portable ZIP / update manifest:
echo   artifacts\packages
echo.
if exist ".\artifacts\installer" start "" explorer.exe ".\artifacts\installer"
pause
exit /b 0

:buildfail
echo.
echo [ERROR] The build failed. Scroll up to the FIRST red error.
echo Do not delete the project or rebuild from scratch; the error above is the item to fix.
echo.
pause
exit /b 1

:fail
echo Close this window after fixing the missing prerequisite, then run BUILD_APP.cmd again.
echo.
pause
exit /b 1
