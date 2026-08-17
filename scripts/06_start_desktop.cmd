@echo off
cd /d "%~dp0.."
set "DOTNET_CLI_HOME=%CD%\.dotnet-home"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet run --project "src\Vision.Desktop\Vision.Desktop.csproj" --configuration Debug
pause
