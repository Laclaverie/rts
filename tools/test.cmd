@echo off
REM One click: double-click this, or run `tools	est` from anywhere in the repo.
REM Arguments pass straight through, so `tools	est -All` works too.
REM
REM The .cmd exists so the .ps1 runs without touching the machine's execution policy.
REM Keep this file CRLF - cmd.exe misparses LF-only batch files.

setlocal
set "SCRIPT=%~dp0Run-Tests.ps1"
set "SHELL_EXE=powershell"
where pwsh >nul 2>&1
if %ERRORLEVEL%==0 set "SHELL_EXE=pwsh"

REM CMDCMDLINE is a dynamic variable and is not inherited by child processes, so copy it
REM into a real one. Run-Tests.ps1 reads it to decide whether to hold the window open:
REM Explorer launches this as cmd /c ""C:\path	est.cmd" ", and the doubled quotes are
REM the tell. Doing that test here in batch breaks on quoted arguments.
set "RTS_CMDLINE=%CMDCMDLINE%"

%SHELL_EXE% -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*
exit /b %ERRORLEVEL%
