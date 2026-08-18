@echo off
setlocal
cd /d "%~dp0.."
set "DOTNET_CLI_HOME=%CD%\.dotnet-home"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"

echo Building HALCON service and WPF workstation...
dotnet build "HalconPinInspection.sln" --configuration Release --maxcpucount:1
if errorlevel 1 goto :failed

echo Starting Vision.Service on 127.0.0.1:9100...
start "HALCON Vision Service" /D "%CD%" "%CD%\src\Vision.Service\bin\Release\net9.0\Vision.Service.exe" --host 127.0.0.1 --port 9100 --runtime-dir "%CD%\runtime" --recipe "%CD%\config\vision.recipe.json" --backend halcon
timeout /t 2 /nobreak >nul

echo Starting Vision.Desktop...
start "HALCON Vision Workstation" /D "%CD%" "%CD%\src\Vision.Desktop\bin\x64\Release\net9.0-windows\Vision.Desktop.exe"
exit /b 0

:failed
echo Build failed. See the error messages above.
pause
exit /b 1
