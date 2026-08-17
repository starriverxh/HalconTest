@echo off
cd /d "%~dp0.."
set "DOTNET_CLI_HOME=%CD%\.dotnet-home"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet run --project "src\Vision.Service\Vision.Service.csproj" -- --host 127.0.0.1 --port 9100 --runtime-dir "%CD%\runtime" --recipe "%CD%\config\vision.recipe.json" --backend halcon
pause
