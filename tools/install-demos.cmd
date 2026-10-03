@echo off
rem Install the demos on the connected Quest. Example: install-demos.cmd -Group 2   (see install-demos.ps1)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-demos.ps1" %*
if errorlevel 1 pause
