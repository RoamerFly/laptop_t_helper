@echo off
setlocal EnableExtensions

chcp 65001 >nul
cd /d "%~dp0"

set "APP_PROJECT=src\LaptopThermalHelper.App\LaptopThermalHelper.App.csproj"
set "SOLUTION=LaptopThermalHelper.sln"
set "OUTPUT_DIR=%CD%\dist_windows"

echo [1/6] Checking .NET 10 SDK...
if defined DOTNET_EXE goto check_dotnet

where dotnet >nul 2>nul
if not errorlevel 1 (
    set "DOTNET_EXE=dotnet"
    goto check_dotnet
)

if exist "%ProgramFiles%\dotnet\dotnet.exe" (
    set "DOTNET_EXE=%ProgramFiles%\dotnet\dotnet.exe"
    goto check_dotnet
)

echo ERROR: .NET 10 SDK was not found.
echo Install it from https://dotnet.microsoft.com/download/dotnet/10.0
goto failed

:check_dotnet
set "SDK_VERSION="
for /f "delims=" %%V in ('"%DOTNET_EXE%" --version 2^>nul') do set "SDK_VERSION=%%V"
if not defined SDK_VERSION (
    echo ERROR: Unable to run "%DOTNET_EXE%".
    goto failed
)
if not "%SDK_VERSION:~0,3%"=="10." (
    echo ERROR: .NET 10 SDK is required, but %SDK_VERSION% was selected.
    echo Install it from https://dotnet.microsoft.com/download/dotnet/10.0
    goto failed
)
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
