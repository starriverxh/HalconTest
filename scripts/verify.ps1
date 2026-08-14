$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"

dotnet build (Join-Path $projectRoot "HalconPinInspection.sln") --configuration Release --maxcpucount:1
dotnet run --project (Join-Path $projectRoot "tests/Vision.Service.Tests/Vision.Service.Tests.csproj") --configuration Release --no-build

& (Join-Path $PSScriptRoot "run-halcon-offline.ps1")
