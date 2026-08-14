@echo off
cd /d "%~dp0.."
set "DOTNET_CLI_HOME=%CD%\.dotnet-home"
set "DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1"
dotnet run --project "src\Trigger.Client\Trigger.Client.csproj" -- --host 127.0.0.1 --port 9100 --command trigger --params "{\"mockX\":0.018,\"mockY\":-0.012,\"mockTheta\":0.006,\"toleranceMm\":0.05}"
pause

