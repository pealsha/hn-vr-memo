@echo off
setlocal
chcp 65001 >nul
if not exist "%~dp0HnMemo.exe" goto missing
"%~dp0HnMemo.exe" --autostart enable
if errorlevel 1 goto failed
echo HnMemo will start hidden with SteamVR. Keep this folder in place.
pause
exit /b 0
:missing
echo HnMemo.exe was not found. Extract the whole zip first.
:failed
echo See README.md for the installation path and SteamVR requirements.
pause
exit /b 1
