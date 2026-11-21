@echo off
cd /d "%~dp0"
call scripts\Build-App.cmd
if errorlevel 1 goto failed
dotnet "dist\HnMemo\HnMemo.dll" --autostart enable
if errorlevel 1 goto failed
echo HnMemo will start hidden with SteamVR.
pause
exit /b 0
:failed
pause
exit /b 1
