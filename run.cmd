@echo off
setlocal
cd /d "%~dp0"
dotnet build FileExplorer.csproj -v q -nologo
if errorlevel 1 exit /b 1
start "" "%~dp0bin\Debug\net8.0-windows\FileExplorer.exe"
