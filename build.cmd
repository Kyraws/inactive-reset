@echo off
REM Wrapper for build.ps1.
REM
REM Windows blocks .ps1 files by default (execution policy Restricted), so
REM `.\build.ps1` fails on a fresh machine. Invoking PowerShell explicitly with
REM -ExecutionPolicy Bypass applies to this process only: it changes nothing on
REM the machine and needs no administrator rights.
REM
REM Usage:  build            build (Debug)
REM         build test       build and run the tests
REM         build ship       Release build, tests, then dist\
REM         build clean      delete artifacts\ and dist\
REM In PowerShell, use .\build.cmd explicitly to avoid selecting build.ps1.

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
exit /b %ERRORLEVEL%
