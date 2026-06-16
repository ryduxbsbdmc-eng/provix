@echo off
cd /d "%~dp0.."
powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\Publish-NetworkTest.ps1" -SkipBuild -StartServer
pause
