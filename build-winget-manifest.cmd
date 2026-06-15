@echo off
setlocal
cd /d "%~dp0"

for /f "usebackq tokens=2 delims=^"" %%V in (`findstr /B "#define MyAppVersion" setup\Provix.iss`) do set "VERSION=%%V"

if "%VERSION%"=="" (
  echo Could not read version from setup\Provix.iss
  exit /b 1
)

if not exist "installer\Provix-Setup-%VERSION%.exe" (
  echo Installer not found. Run build-setup.cmd first.
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\New-WingetManifest.ps1" -PackageVersion %VERSION% -ReleaseTag v%VERSION% -Validate
exit /b %ERRORLEVEL%
