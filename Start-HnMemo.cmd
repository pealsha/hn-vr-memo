@echo off
cd /d "%~dp0"
if not exist "dist\HnMemo\HnMemo.exe" (
    call scripts\Build-App.cmd
    if errorlevel 1 goto failed
)
start "" "%~dp0dist\HnMemo\HnMemo.exe" %*
exit /b 0
:failed
echo HnMemo could not be started. Run Update-HnMemo.cmd after closing HnMemo.
pause
exit /b 1
