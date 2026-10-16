@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start-Workspace.ps1" -Mode Background -NoBrowser
if errorlevel 1 goto failed
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0腾讯云CloudBase\windows\Start-CloudBridge.ps1"
if errorlevel 1 goto failed
start "" "%~dp0maui\WeChatLikeMaui.exe" --workspace-local
exit /b 0
:failed
pause
exit /b 1
