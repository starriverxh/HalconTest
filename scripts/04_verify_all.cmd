@echo off
cd /d "%~dp0.."
set "DOTNET_CLI_HOME=%CD%\.dotnet-home"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
set "DOTNET_CLI_TELEMETRY_OPTOUT=1"
dotnet build "HalconPinInspection.sln" --configuration Release --maxcpucount:1
if errorlevel 1 goto :failed
dotnet run --project "tests\Vision.Service.Tests\Vision.Service.Tests.csproj" --configuration Release --no-build
if errorlevel 1 goto :failed
hrun -b -c DemoPassed --dump=HoleCount --dump=CorrectionX --dump=CorrectionY "halcon\programs\01_single_file_learning_demo.hdev"
if errorlevel 1 goto :failed
echo All checks passed.
pause
exit /b 0
:failed
echo Verification failed.
pause
exit /b 1

