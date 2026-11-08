@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

if /I "%~1"=="" goto :cli
if /I "%~1"=="help" goto :help
if /I "%~1"=="/?" goto :help
if /I "%~1"=="start" goto :start
if /I "%~1"=="gui" goto :start

goto :cli

:start
set "EXE=%ROOT%\src\Capri.Desktop\bin\Release\net10.0-windows\Capri.Desktop.exe"
if exist "%EXE%" (
  echo Starting Project Capri...
  start "" /D "%ROOT%\src\Capri.Desktop\bin\Release\net10.0-windows" "%EXE%"
  exit /b 0
)

echo Release desktop executable not found. Building and running Release via dotnet...
dotnet run --project "%ROOT%\src\Capri.Desktop\Capri.Desktop.csproj" -c Release
exit /b %ERRORLEVEL%

:cli
set "CAPRI_REPO_ROOT=%ROOT%"
set "CLI_EXE=%ROOT%\src\Capri.Cli\bin\Release\net10.0-windows\capri.exe"
if exist "%CLI_EXE%" goto :runcli
set "CLI_EXE=%ROOT%\src\Capri.Cli\bin\Release\net10.0-windows\Capri.Cli.exe"
if exist "%CLI_EXE%" goto :runcli

echo Release CLI executable not found. Building and running Release via dotnet...
dotnet run --project "%ROOT%\src\Capri.Cli\Capri.Cli.csproj" -c Release -- %*
exit /b %ERRORLEVEL%

:runcli
"%CLI_EXE%" %*
exit /b %ERRORLEVEL%

:help
echo Project Capri
echo.
echo Usage:
echo   capri start           Start Capri (Desktop app)
echo   capri gui             Alias for start
echo   capri status          Show configuration and provider status
echo   capri doctor          Diagnose settings, endpoints, and credentials
echo   capri config          Open the interactive settings menu
echo   capri setup [--repair] Interactive guided setup (terminal required)
echo   capri help            Show this help
echo.
echo Any other arguments are passed to the Capri CLI.
echo Tip: run from C:\Project Capri, or add C:\Project Capri to your PATH.
echo Launches Release; to run Debug explicitly use dotnet run --project src\Capri.Desktop\Capri.Desktop.csproj -c Debug.
exit /b 0
