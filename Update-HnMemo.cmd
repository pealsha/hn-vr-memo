@echo off
cd /d "%~dp0"
echo Close HnMemo before updating.
call scripts\Build-App.cmd
if errorlevel 1 goto failed
echo HnMemo updated. Use Start-HnMemo.cmd to start.
pause
exit /b 0
:failed
pause
exit /b 1
