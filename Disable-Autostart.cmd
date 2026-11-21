@echo off
cd /d "%~dp0"
if not exist "dist\HnMemo\HnMemo.dll" goto missing
dotnet "dist\HnMemo\HnMemo.dll" --autostart disable
if errorlevel 1 goto failed
echo HnMemo autostart disabled.
pause
exit /b 0
:missing
echo Installation not found. Restore dist\HnMemo or disable HnMemo in SteamVR startup settings.
:failed
pause
exit /b 1
