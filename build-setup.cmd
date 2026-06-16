@echo off
setlocal
cd /d "%~dp0"

echo [1/2] Building release...
call build.cmd fast
if errorlevel 1 exit /b 1

if not exist "publish\FileExplorer.exe" (
  echo publish\FileExplorer.exe not found.
  exit /b 1
)

set "ISCC="
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if "%ISCC%"=="" (
  echo.
  echo Inno Setup 6 not found.
  echo Install: winget install JRSoftware.InnoSetup
  echo Or download: https://jrsoftware.org/isinfo.php
  exit /b 1
)

echo [2/2] Creating installer...
"%ISCC%" "setup\Provix.iss"
if errorlevel 1 exit /b 1

echo.
for /f "usebackq tokens=2 delims=^"" %%V in (`findstr /B /C:"#define MyAppVersion " setup\Provix.iss`) do set "APP_VERSION=%%V"
echo Done: %~dp0installer\Provix-Setup-%APP_VERSION%.exe
exit /b 0
