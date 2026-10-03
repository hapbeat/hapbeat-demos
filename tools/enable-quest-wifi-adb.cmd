@echo off
rem Double-click with the Quest(s) on USB: switches them to Wi-Fi adb. See enable-quest-wifi-adb.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0enable-quest-wifi-adb.ps1" %*
pause
