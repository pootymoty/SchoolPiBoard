@echo off
setlocal

echo ============================================
echo   Building SchoolPiBoard (WPF, .NET 8)
echo ============================================
echo.

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [ERROR] .NET SDK not found.
    echo Install .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
    echo.
    pause
    exit /b 1
)

echo Using SDK version:
dotnet --version
echo.

echo [1/3] Cleaning previous build artifacts...
if exist obj rmdir /s /q obj
if exist bin rmdir /s /q bin
if exist publish rmdir /s /q publish
echo Done.
echo.

echo [2/3] Restoring packages...
dotnet restore
if errorlevel 1 goto fail

echo.
echo [3/3] Publishing self-contained EXE...
dotnet publish -c Release -r win-x64 --self-contained true -o publish
if errorlevel 1 goto fail

echo.
echo ============================================
echo   DONE
echo   Output: publish\SchoolPiBoard.exe
echo ============================================
echo.
pause
exit /b 0

:fail
echo.
echo [ERROR] Build failed. See messages above.
echo.
pause
exit /b 1
