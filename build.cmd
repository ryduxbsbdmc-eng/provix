@echo off
setlocal
cd /d "%~dp0"

set "MODE=%~1"
if "%MODE%"=="" set "MODE=single"
if /i "%MODE%"=="help" goto :help
if /i "%MODE%"=="-h" goto :help
if /i "%MODE%"=="--help" goto :help

if /i "%MODE%"=="single" goto :single
if /i "%MODE%"=="fast" goto :fast
if /i "%MODE%"=="slim" goto :slim

echo Unknown mode: %MODE%
goto :help

:single
echo Publishing self-contained single-file exe...
dotnet publish FileExplorer.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish
if errorlevel 1 exit /b 1
echo.
echo Done: %~dp0publish\FileExplorer.exe
exit /b 0

:fast
echo Publishing self-contained (fast startup, multiple files)...
dotnet publish FileExplorer.csproj -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 exit /b 1
echo.
echo Done: %~dp0publish\FileExplorer.exe
exit /b 0

:slim
echo Publishing framework-dependent (requires .NET 8)...
dotnet publish FileExplorer.csproj -c Release -o publish-slim
if errorlevel 1 exit /b 1
echo.
echo Done: %~dp0publish-slim\FileExplorer.exe
exit /b 0

:help
echo Usage: build.cmd [mode]
echo.
echo   single  - self-contained, one large exe ~160 MB (default)
echo   fast    - self-contained, many files, faster startup (~300+ MB)
echo   slim    - small build, needs .NET 8 runtime installed
exit /b 0
