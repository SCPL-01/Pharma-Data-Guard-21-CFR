@echo off
setlocal
cd /d "%~dp0"

set "CANDLE="
set "LIGHT="
for %%P in (
    "%ProgramFiles(x86)%\WiX Toolset v3.14\bin"
    "%ProgramFiles(x86)%\WiX Toolset v3.11\bin"
    "%ProgramFiles%\WiX Toolset v3.14\bin"
    "%ProgramFiles%\WiX Toolset v3.11\bin"
    "%WIX%bin"
) do (
    if exist "%%~P\candle.exe" set "CANDLE=%%~P\candle.exe"
    if exist "%%~P\light.exe"  set "LIGHT=%%~P\light.exe"
)

if not defined CANDLE (
    echo [ERROR] WiX v3 candle.exe not found. Install WiX v3.11 or v3.14.
    exit /b 1
)
if not defined LIGHT (
    echo [ERROR] WiX v3 light.exe not found.
    exit /b 1
)

echo Using WiX: "%CANDLE%"

if not exist "..\PharmaDataGuard\bin\Release\PharmaDataGuard.exe" (
    echo [ERROR] PharmaDataGuard.exe not built. Run build.cmd at the repo root first.
    exit /b 1
)

"%CANDLE%" -nologo -ext WixUtilExtension PharmaDataGuard.wxs -out PharmaDataGuard.wixobj
if errorlevel 1 exit /b 1

"%LIGHT%" -nologo -ext WixUtilExtension -sice:ICE61 PharmaDataGuard.wixobj -out PharmaDataGuard.msi
if errorlevel 1 exit /b 1

echo.
echo === MSI built: %~dp0PharmaDataGuard.msi ===
exit /b 0
