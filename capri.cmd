@echo off
setlocal EnableExtensions
set "ROOT=%~dp0"
if "%ROOT:~-1%"=="\" set "ROOT=%ROOT:~0,-1%"

if /I "%~1"=="" goto :help
if /I "%~1"=="help" goto :help
if /I "%~1"=="/?" goto :help
if /I "%~1"=="start" goto :start

echo Unknown command: %~1
echo.
goto :help

:start
set "EXE=%ROOT%\src\Capri.Desktop\bin\Debug\net10.0-windows\Capri.Desktop.exe"
if exist "%EXE%" (
  echo Starting Project Capri...
  start "" /D "%ROOT%\src\Capri.Desktop\bin\Debug\net10.0-windows" "%EXE%"
  exit /b 0
)

echo Debug executable not found. Building and running via dotnet...
dotnet run --project "%ROOT%\src\Capri.Desktop\Capri.Desktop.csproj" -c Debug
exit /b %ERRORLEVEL%

:help
echo Project Capri
echo.
echo Usage:
echo   capri start   Start Capri (Desktop app)
echo   capri help    Show this help
echo.
echo Tip: run from C:\Project Capri, or add C:\Project Capri to your PATH.
exit /b 0
