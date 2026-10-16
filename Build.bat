@echo off
rem Builds CompanionAI_Pathfinder and creates dist\CompanionAI_Pathfinder_v<version>.zip.
rem Any arguments are passed to Build.ps1, e.g.:
rem   Build.bat -GamePath "D:\SteamLibrary\steamapps\common\Pathfinder Second Adventure" -Deploy
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Build.ps1" %*
set EXITCODE=%ERRORLEVEL%
if not "%EXITCODE%"=="0" echo. & echo BUILD FAILED (exit code %EXITCODE%)
rem Keep the window open when started by double-click from Explorer.
echo %CMDCMDLINE% | find /i "/c" >nul && pause
exit /b %EXITCODE%
