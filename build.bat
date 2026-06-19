@echo off
setlocal
set "PROJECT=SoundManager.csproj"
set "ISS=SoundManager.iss"
set "BASE=%~dp0release"
set "SINGLE=%BASE%\SoundManager_Portable"
set "FOLDER=%BASE%\SoundManager"
set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
set "LOG=%~dp0iscc-log.txt"
echo.
echo === SoundManager build (both variants) + installer ===
echo.
echo Stopping any running SoundManager...
taskkill /IM SoundManager.exe /F >nul 2>&1
echo Cleaning bin, obj, release and old log...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "%BASE%" rmdir /s /q "%BASE%"
if exist "%LOG%" del /q "%LOG%"
echo.
echo === [1/3] Portable build (single file) ===
dotnet publish "%PROJECT%" -c Release -o "%SINGLE%" -p:PublishSingleFile=true
if errorlevel 1 goto fail
echo.
echo === [2/3] Folder build (exe + dlls) ===
dotnet publish "%PROJECT%" -c Release -o "%FOLDER%" -p:PublishSingleFile=false
if errorlevel 1 goto fail
if not exist "%ISCC%" goto noiscc
echo.
echo === [3/3] Compiling installer ===
"%ISCC%" "%~dp0%ISS%" > "%LOG%" 2>&1
type "%LOG%"
if errorlevel 1 goto fail
echo.
echo === DONE ===
echo Portable  : %SINGLE%\SoundManager.exe
echo With dlls : %FOLDER%\SoundManager.exe (+ dlls)
echo Installer : %BASE%\SoundManager_Setup\SoundManager-Setup.exe
echo.
explorer "%BASE%"
pause
endlocal
exit /b 0
:noiscc
echo.
echo *** ISCC.exe not found at:
echo     %ISCC%
echo Edit the ISCC path at the top of this script.
pause
endlocal
exit /b 1
:fail
echo.
echo *** BUILD FAILED ***
echo (If step 3 failed, full ISCC output is in: %LOG%)
echo.
pause
endlocal
exit /b 1