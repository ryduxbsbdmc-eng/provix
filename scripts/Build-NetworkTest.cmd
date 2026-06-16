@echo off
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\Publish-NetworkTest.ps1"
if errorlevel 1 exit /b 1
echo.
echo Open folder: network-test\
explorer "%~dp0..\network-test"
pause
