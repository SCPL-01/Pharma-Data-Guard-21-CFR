@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"

set "MSBUILD="
set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.Component.MSBuild -find MSBuild\**\Bin\MSBuild.exe`) do (
        set "MSBUILD=%%i"
    )
)
if not defined MSBUILD (
    if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe" (
        set "MSBUILD=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe"
    )
)
if not defined MSBUILD (
    echo [ERROR] Could not locate MSBuild.exe. Install Visual Studio Build Tools or .NET Framework SDK.
    exit /b 1
)

echo Using MSBuild: "%MSBUILD%"
"%MSBUILD%" PharmaDataGuard.sln /p:Configuration=Release /p:Platform="Any CPU" /m /nologo /verbosity:minimal
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

echo.
echo === Build successful ===
echo   PharmaDataGuard\bin\Release\PharmaDataGuard.exe
echo   PharmaDataGuard.LogVerifier\bin\Release\PharmaDataGuard.LogVerifier.exe
exit /b 0
