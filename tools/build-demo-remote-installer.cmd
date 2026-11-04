@echo off
rem Double-click to build the Demo Remote installer for Windows. See build-demo-remote-installer.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-demo-remote-installer.ps1" %*
if errorlevel 1 pause
