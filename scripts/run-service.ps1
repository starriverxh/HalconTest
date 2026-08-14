param(
    [string]$Configuration = "Debug",
    [int]$Port = 9100,
    [ValidateSet("mock", "hdevengine")]
    [string]$Backend = "mock"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

$arguments = @(
    "run",
    "--project", (Join-Path $projectRoot "src/Vision.Service/Vision.Service.csproj"),
    "--configuration", $Configuration,
    "--",
    "--host", "127.0.0.1",
    "--port", $Port,
    "--runtime-dir", (Join-Path $projectRoot "runtime"),
    "--backend", $Backend
)

if ($Backend -eq "hdevengine") {
    if (-not $env:HALCONROOT) {
        throw "HALCONROOT is not set. Start the MVTec HALCON command environment first."
    }
    $arguments += @(
        "--halcon-assembly", (Join-Path $env:HALCONROOT "bin/dotnet35/hdevenginedotnet.dll"),
        "--procedure-path", (Join-Path $projectRoot "halcon/procedures"),
        "--procedure-name", "inspect_pin_row"
    )
}

dotnet @arguments
