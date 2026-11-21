@echo off
setlocal
if not exist "%~dp0HnMemo.exe" goto missing
start "" "%~dp0HnMemo.exe" %*
exit /b 0
:missing
echo HnMemo.exe was not found. Extract the whole zip before starting.
pause
exit /b 1
