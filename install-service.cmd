@echo off
setlocal
cd /d "%~dp0"

net session >nul 2>&1
if errorlevel 1 (
    echo [ERROR] This script must be run as Administrator.
    exit /b 1
)

set "EXE=%~dp0PharmaDataGuard\bin\Release\PharmaDataGuard.exe"
if not exist "%EXE%" (
    echo [ERROR] Build first: "%EXE%" not found.
    exit /b 1
)

sc query PharmaDataGuard >nul 2>&1
if not errorlevel 1 (
    echo [INFO] Service PharmaDataGuard already exists, stopping and deleting.
    sc stop PharmaDataGuard >nul 2>&1
    sc delete PharmaDataGuard >nul 2>&1
    timeout /t 2 /nobreak >nul
)

sc create PharmaDataGuard binPath= "\"%EXE%\"" start= auto DisplayName= "Pharma Data Guard — 21 CFR Part 11 endpoint lockdown"
if errorlevel 1 (
    echo [ERROR] sc create failed.
    exit /b 1
)
sc description PharmaDataGuard "Pharma compliance endpoint lockdown. Disables copy / cut / paste / delete / drag-drop / print-screen at OS level for 21 CFR Part 11 and EU GMP Annex 11 data integrity. Produces tamper-evident HMAC-chained audit trail."
sc failure PharmaDataGuard reset= 0 actions= restart/5000/restart/5000/restart/5000

echo.
echo Service installed. Start with:  sc start PharmaDataGuard
exit /b 0
