@echo off
chcp 65001 > nul
echo ==============================================================================
echo  Kompilacja i Wersjonowanie: Symulator Unitronics Vision V350 [virtualplc.exe]
echo  Wersja docelowa: v0.0.3
echo ==============================================================================
echo.

set VERSION=0.0.3
set CSC="C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
set OUT_EXE="..\virtualplc.exe"

if not exist %CSC% (
    echo [BLAD] Nie znaleziono kompilatora csc.exe w %CSC%!
    if not "%1"=="/nopause" pause
    exit /b 1
)

echo [1/3] Archiwizacja poprzedniej wersji i rotacja (max 5 wersji)...
powershell -NoProfile -ExecutionPolicy Bypass -File "rotate_versions.ps1" -CurrentVersion "%VERSION%"

echo.
echo [2/3] Zamykanie ewentualnie uruchomionego procesu virtualplc.exe...
taskkill /f /im virtualplc.exe >nul 2>&1

echo Kompilacja zrodel C# (v%VERSION%) do %OUT_EXE%...
%CSC% /nologo /platform:x86 /codepage:65001 /target:winexe /main:UnitronicsV350Simulator.Program /r:System.Drawing.dll,System.Windows.Forms.dll,System.Data.dll,System.IO.Compression.FileSystem.dll /out:%OUT_EXE% *.cs

if %ERRORLEVEL% EQU 0 (
    echo.
    echo [SUKCES] Pomyslnie skompilowano: %OUT_EXE%
    echo Informacje o wersji binarnej:
    powershell -NoProfile -Command "(Get-Item '..\virtualplc.exe').VersionInfo | Format-List FileVersion, ProductVersion"
    
    echo.
    echo [3/3] Aktualizacja kopii w glownym katalogu projektu...
    copy /y %OUT_EXE% "..\..\..\virtualplc.exe" > nul
    if exist "..\..\..\virtualplc.exe" (
        echo [OK] Skopiowano do: Projekt PLC Unitronics V350 Komora 9\virtualplc.exe
    )
) else (
    echo.
    echo [BLAD] Wystapily bledy podczas kompilacji.
    if not "%1"=="/nopause" pause
    exit /b 1
)

echo.
echo ==============================================================================
echo  Zawartosc katalogu glownego symulatora:
echo ==============================================================================
dir ".." /b
echo ==============================================================================
echo.
if not "%1"=="/nopause" pause
