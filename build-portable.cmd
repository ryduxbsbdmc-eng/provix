@echo off
setlocal EnableExtensions
cd /d "%~dp0"

for /f "delims=" %%V in ('powershell -NoProfile -Command "(Select-String -Path 'FileExplorer.csproj' -Pattern '<Version>([^<]+)</Version>').Matches.Groups[1].Value"') do set "APP_VERSION=%%V"

if "%APP_VERSION%"=="" (
  echo Could not read version from FileExplorer.csproj
  exit /b 1
)

echo [1/3] Publishing self-contained build (fast)...
call build.cmd fast
if errorlevel 1 exit /b 1

if not exist "publish\FileExplorer.exe" (
  echo publish\FileExplorer.exe not found.
  exit /b 1
)

set "PORTABLE_DIR=provix-portable\Provix-Portable-%APP_VERSION%"
set "ZIP_PATH=installer\Provix-Portable-%APP_VERSION%.zip"

echo [2/3] Staging portable folder...
if exist "provix-portable" rmdir /s /q "provix-portable"
if not exist "provix-portable" mkdir "provix-portable"
mkdir "%PORTABLE_DIR%"

robocopy "publish" "%PORTABLE_DIR%" /E /NFL /NDL /NJH /NJS /NC /NS /NP >nul
set "ROBOCOPY_EXIT=%ERRORLEVEL%"
if %ROBOCOPY_EXIT% GEQ 8 (
  echo Failed to copy publish files. Robocopy exit code: %ROBOCOPY_EXIT%
  exit /b 1
)

> "%PORTABLE_DIR%\README-portable.txt" (
  echo Provix %APP_VERSION% - portable build
  echo.
  echo 1. Unzip anywhere you have write access.
  echo 2. Run FileExplorer.exe
  echo 3. Settings and cache are stored in your user profile, not in this folder.
  echo.
  echo No installer required. Delete the folder to uninstall.
)

if not exist "installer" mkdir "installer"
if exist "%ZIP_PATH%" del /f /q "%ZIP_PATH%"

echo [3/3] Creating zip archive...
powershell -NoProfile -Command "Compress-Archive -LiteralPath '%PORTABLE_DIR%' -DestinationPath '%ZIP_PATH%' -CompressionLevel Optimal -Force"
if errorlevel 1 exit /b 1

for %%A in ("%ZIP_PATH%") do set "ZIP_SIZE=%%~zA"
set /a ZIP_MB=%ZIP_SIZE% / 1048576

echo.
echo Version:         %APP_VERSION%
echo Portable folder: %~dp0%PORTABLE_DIR%
echo Portable zip:    %~dp0%ZIP_PATH% (~%ZIP_MB% MB)
exit /b 0
