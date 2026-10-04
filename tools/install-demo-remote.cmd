@echo off
rem Double-click: installs the Android Demo Remote on every phone/tablet on USB or Wireless debugging. See install-demo-remote.ps1.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-demo-remote.ps1" %*
pause
