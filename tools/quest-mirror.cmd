@echo off
rem Double-click to mirror every Quest on this PC over the local LAN. See quest-mirror.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0quest-mirror.ps1" %*
if errorlevel 1 pause
