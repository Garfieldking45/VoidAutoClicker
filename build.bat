@echo off
setlocal
title Void AutoClicker - Build

echo ============================================
echo   VOID AUTOCLICKER - one-click builder
echo ============================================
echo.

cd /d "%~dp0"

where dotnet >nul 2>nul
if errorlevel 1 (
    echo [X] .NET SDK not found.
    echo     Install the .NET 8 SDK from https://dotnet.microsoft.com/download
    echo.
    pause
    exit /b 1
)

echo [1/3] Building the single portable .exe ^(this can take a minute^)...
echo.

dotnet publish -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:DebugType=none ^
  -p:DebugSymbols=false

if errorlevel 1 (
    echo.
    echo [X] Build failed. Scroll up to see the error.
    echo.
    pause
    exit /b 1
)

set "EXE=bin\Release\net8.0-windows\win-x64\publish\VoidAutoClicker.exe"

if not exist "%EXE%" (
    echo.
    echo [X] Build finished but the .exe was not found where expected:
    echo     %EXE%
    echo.
    pause
    exit /b 1
)

echo.
echo [2/3] Copying VoidAutoClicker.exe here and to your Desktop...
copy /Y "%EXE%" "VoidAutoClicker.exe" >nul
copy /Y "%EXE%" "%USERPROFILE%\Desktop\VoidAutoClicker.exe" >nul

echo [3/3] Done!
echo.
echo ============================================
echo   Your app:
echo     - %~dp0VoidAutoClicker.exe
echo     - %USERPROFILE%\Desktop\VoidAutoClicker.exe
echo ============================================
echo.

echo What would you like to do with the leftover files?
echo.
echo   [1] Keep everything, just remove the bin/obj build folders  ^(recommended^)
echo   [2] STRIP this folder down to ONLY VoidAutoClicker.exe
echo       ^(permanently deletes Program.cs, the project file, icon,
echo        readme, demo, and this script - keep a backup of your source!^)
echo   [3] Leave everything as-is
echo.

choice /C 123 /N /M "Choose 1, 2, or 3: "

if errorlevel 3 goto leave
if errorlevel 2 goto strip
if errorlevel 1 goto lightclean

:lightclean
echo.
echo Removing bin and obj...
rmdir /S /Q bin 2>nul
rmdir /S /Q obj 2>nul
echo Cleaned up. Your source files are kept.
echo.
pause
goto end

:strip
echo.
echo ====================  WARNING  ====================
echo  This will PERMANENTLY DELETE your source code and
echo  this build script. You will NOT be able to rebuild
echo  or edit the app afterward. A copy of the .exe is on
echo  your Desktop as a backup.
echo ==================================================
echo.
choice /M "Are you absolutely sure"
if errorlevel 2 goto lightclean
echo.
echo Stripping folder down to just the .exe...
del /Q "Program.cs" "VoidAutoClicker.csproj" "void.ico" "README.md" "demo.html" "icon_preview.png" 2>nul
rmdir /S /Q bin 2>nul
rmdir /S /Q obj 2>nul
echo Done. Only VoidAutoClicker.exe remains.
echo.
echo This window will close and delete the build script.
timeout /t 3 >nul
(goto) 2>nul & del "%~f0"

:leave
echo.
echo Left everything as-is. The .exe is in the publish folder, here, and on your Desktop.
echo.
pause

:end
endlocal
