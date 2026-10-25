@echo off
REM Root entry point; execution-policy bypass applies only to this process.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\build.ps1" %*
exit /b %ERRORLEVEL%