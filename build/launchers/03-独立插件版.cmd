@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Workspace.ps1" -Mode Plugins
if errorlevel 1 pause
