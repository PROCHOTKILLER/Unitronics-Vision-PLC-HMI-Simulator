@echo off
chcp 65001 > nul
echo ==============================================================================
echo  Compilation & Versioning: Unitronics Vision PLC/HMI Simulator [virtualplc.exe]
echo  Target Version: v0.0.3
echo ==============================================================================
echo.

set VERSION=0.0.3
set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set OUT_EXE="..\virtualplc.exe"

if not exist %CSC% (
    echo [ERROR] csc.exe compiler not found in %CSC%!
    if not "%1"=="/nopause" pause
    exit /b 1
)

echo [1/3] Archiving previous version and rotating (max 5 versions)...
powershell -NoProfile -ExecutionPolicy Bypass -File "rotate_versions.ps1" -CurrentVersion "%VERSION%"

echo.
echo [2/3] Terminating any running instance of virtualplc.exe...
taskkill /f /im virtualplc.exe >nul 2>&1

echo Compiling C# source files (v%VERSION%) to %OUT_EXE%...
%CSC% /nologo /platform:x86 /codepage:65001 /target:winexe /main:UnitronicsV350Simulator.Program /r:System.Drawing.dll,System.Windows.Forms.dll,System.Data.dll,System.IO.Compression.FileSystem.dll /out:%OUT_EXE% *.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUCCESS] Successfully compiled: %OUT_EXE%
    echo Binary version info:
    powershell -NoProfile -Command "(Get-Item '..\virtualplc.exe').VersionInfo | Format-List FileVersion, ProductVersion"
) else (
    echo.
    echo [ERROR] Compilation failed.
    if not "%1"=="/nopause" pause
    exit /b 1
)

echo.
echo ==============================================================================
echo  Simulator root directory contents:
echo ==============================================================================
dir ".." /b
echo ==============================================================================
echo.
if not "%1"=="/nopause" pause
