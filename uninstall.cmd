@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] Run this script as Administrator.
    exit /b 1
)

set "FORCE="
if /i "%~1"=="/force" set "FORCE=1"

echo Stopping PharmaDataGuard processes...
taskkill /F /IM PharmaDataGuard.exe >nul 2>&1

sc query PharmaDataGuard >nul 2>&1
if not errorlevel 1 (
    echo Stopping PharmaDataGuard service...
    sc stop PharmaDataGuard >nul 2>&1
    sc delete PharmaDataGuard >nul 2>&1
)

if defined FORCE (
    echo Removing DENY ACEs from any protected paths in config...
    set "CFG=%ProgramData%\PharmaDataGuard\config.xml"
    if exist "!CFG!" (
        powershell -NoProfile -ExecutionPolicy Bypass -Command ^
          "[xml]$x = Get-Content '!CFG!'; foreach ($p in $x.PharmaDataGuardConfig.ProtectedPaths.Path) { if ($p -and (Test-Path $p)) { Write-Host 'Clearing ACE on' $p; & icacls $p /remove:d 'Everyone' /T /C | Out-Null } }"
    )
)

reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Run" /v PharmaDataGuard /f >nul 2>&1
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v DisableTaskMgr /f >nul 2>&1
reg delete "HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}" /f >nul 2>&1

if exist "%ProgramData%\PharmaDataGuard\config.xml"  del /q "%ProgramData%\PharmaDataGuard\config.xml"
if exist "%ProgramData%\PharmaDataGuard\AclBackup"   rd /s /q "%ProgramData%\PharmaDataGuard\AclBackup"

echo.
echo Audit log preserved: %ProgramData%\PharmaDataGuard\pharma-data-guard.log
exit /b 0
