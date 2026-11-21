@echo off
setlocal
chcp 65001 >nul
if not exist "%~dp0HnMemo.exe" goto missing
"%~dp0HnMemo.exe" --autostart disable
if errorlevel 1 goto failed
echo HnMemo autostart disabled.
pause
exit /b 0
:missing
echo HnMemo.exe was not found. Restore this folder before unregistering.
:failed
echo See README.md. Keep the app folder until autostart is disabled.
pause
exit /b 1
