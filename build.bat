@echo off
setlocal EnableExtensions

chcp 65001 >nul
cd /d "%~dp0"

set "APP_PROJECT=src\LaptopThermalHelper.App\LaptopThermalHelper.App.csproj"
set "SOLUTION=LaptopThermalHelper.sln"
set "OUTPUT_DIR=%CD%\dist_windows"

echo [1/6] Checking .NET 10 SDK...
set "REQUESTED_DOTNET=%DOTNET_EXE%"
set "DOTNET_EXE="
set "SDK_VERSION="

if defined REQUESTED_DOTNET call :try_dotnet "%REQUESTED_DOTNET%"
call :try_dotnet "%~dp0..\..\Tools\dotnet10\dotnet.exe"
call :try_dotnet "%~dp0.dotnet\dotnet.exe"
for /f "delims=" %%D in ('where dotnet 2^>nul') do call :try_dotnet "%%D"
call :try_dotnet "%ProgramW6432%\dotnet\dotnet.exe"
call :try_dotnet "%ProgramFiles%\dotnet\dotnet.exe"
call :try_dotnet "%LocalAppData%\Microsoft\dotnet\dotnet.exe"

if not defined DOTNET_EXE goto dotnet_missing
echo Using .NET SDK %SDK_VERSION%.

echo [2/6] Checking LibreHardwareMonitor submodule...
if exist "LibreHardwareMonitor\LibreHardwareMonitorLib\LibreHardwareMonitorLib.csproj" goto restore
where git >nul 2>nul
if errorlevel 1 (
    echo ERROR: The LibreHardwareMonitor submodule is missing and Git was not found.
    goto failed
)
git submodule update --init --recursive
if errorlevel 1 goto command_failed

:restore
echo [3/6] Restoring dependencies...
"%DOTNET_EXE%" restore "%SOLUTION%" --disable-parallel -m:1
if errorlevel 1 goto command_failed

echo [4/6] Building and testing Release configuration...
"%DOTNET_EXE%" build "%SOLUTION%" --configuration Release --no-restore -m:1
if errorlevel 1 goto command_failed
"%DOTNET_EXE%" test "%SOLUTION%" --configuration Release --no-build -m:1
if errorlevel 1 goto command_failed

echo [5/6] Preparing output directory...
set "PROCESS_FILE=%TEMP%\laptop-thermal-helper-process-%RANDOM%-%RANDOM%.tmp"
tasklist /fi "IMAGENAME eq LaptopThermalHelper.App.exe" /fo csv /nh >"%PROCESS_FILE%" 2>nul
findstr /i /l /c:"LaptopThermalHelper.App.exe" "%PROCESS_FILE%" >nul
if not errorlevel 1 goto app_running
del /q "%PROCESS_FILE%" >nul 2>nul
if exist "%OUTPUT_DIR%" rmdir /s /q "%OUTPUT_DIR%"
if exist "%OUTPUT_DIR%" (
    echo ERROR: Unable to clean "%OUTPUT_DIR%".
    goto failed
)
mkdir "%OUTPUT_DIR%"
if errorlevel 1 goto command_failed

echo [6/6] Publishing self-contained Windows x64 application...
"%DOTNET_EXE%" publish "%APP_PROJECT%" --configuration Release --runtime win-x64 --self-contained true --no-restore -p:Platform=x64 --output "%OUTPUT_DIR%"
if errorlevel 1 goto command_failed

copy /y "LICENSE" "%OUTPUT_DIR%\LICENSE.txt" >nul
xcopy "LICENSES" "%OUTPUT_DIR%\LICENSES\" /e /i /y >nul

if not exist "%OUTPUT_DIR%\LaptopThermalHelper.App.exe" (
    echo ERROR: Publish completed without the expected executable.
    goto failed
)

echo.
echo Build succeeded.
echo Executable: "%OUTPUT_DIR%\LaptopThermalHelper.App.exe"
echo.
echo Run with simulated data:
echo   "%OUTPUT_DIR%\LaptopThermalHelper.App.exe"
echo Run with real hardware sensors:
echo   "%OUTPUT_DIR%\LaptopThermalHelper.App.exe" --real-hardware
exit /b 0

:command_failed
echo.
echo ERROR: A build command failed with exit code %ERRORLEVEL%.

:failed
echo Build failed.
pause
exit /b 1

:dotnet_missing
echo ERROR: A compatible .NET 10 SDK was not found.
echo Install it from https://dotnet.microsoft.com/download/dotnet/10.0
goto failed

:app_running
if exist "%PROCESS_FILE%" del /q "%PROCESS_FILE%" >nul 2>nul
echo ERROR: LaptopThermalHelper.App.exe is currently running.
echo Close the application, then run build.bat again.
goto failed

:try_dotnet
if defined DOTNET_EXE exit /b 0
if "%~1"=="" exit /b 0
set "VERSION_FILE=%TEMP%\laptop-thermal-helper-dotnet-%RANDOM%-%RANDOM%.tmp"
set "CANDIDATE_VERSION="
"%~1" --version >"%VERSION_FILE%" 2>nul
if errorlevel 1 goto try_dotnet_failed
set /p "CANDIDATE_VERSION=" <"%VERSION_FILE%"
if not "%CANDIDATE_VERSION:~0,3%"=="10." goto try_dotnet_failed
set "DOTNET_EXE=%~1"
set "SDK_VERSION=%CANDIDATE_VERSION%"

:try_dotnet_failed
if exist "%VERSION_FILE%" del /q "%VERSION_FILE%" >nul 2>nul
exit /b 0
