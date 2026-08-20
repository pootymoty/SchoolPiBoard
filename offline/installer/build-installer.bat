@echo off
setlocal

echo ============================================
echo   Building Whiteboard installer
echo ============================================
echo.
echo This script only BUILDS the installer.
echo The file you give to customers is dist\WhiteboardSetup.exe
echo.

pushd "%~dp0.."

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK not found.
    echo Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    popd
    pause
    exit /b 1
)

echo [1/3] Cleaning previous output...
if exist publish rmdir /s /q publish
if exist dist rmdir /s /q dist
echo Done.
echo.

echo [2/3] Publishing self-contained application...
dotnet publish -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 goto fail

popd

echo.
echo [3/3] Compiling installer with Inno Setup...

set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo [ERROR] Inno Setup 6 not found.
    echo Install it from https://jrsoftware.org/isdl.php
    pause
    exit /b 1
)

"%ISCC%" "%~dp0Whiteboard.iss"
if errorlevel 1 goto fail

echo.
echo ============================================
echo   DONE
echo   Installer: dist\WhiteboardSetup.exe
echo   This is the file to publish for download.
echo ============================================
echo.
pause
exit /b 0

:fail
echo.
echo [ERROR] Build failed. See messages above.
echo.
popd 2>nul
pause
exit /b 1
