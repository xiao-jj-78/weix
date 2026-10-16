@echo off
chcp 65001 >nul
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0腾讯云CloudBase\windows\Stop-CloudBridge.ps1"
if errorlevel 1 pause
