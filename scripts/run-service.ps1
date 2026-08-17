param(
    [string]$Configuration = "Debug",
    [int]$Port = 9100,
    [ValidateSet("mock", "halcon")]
    [string]$Backend = "halcon"
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
    "--backend", $Backend,
    "--recipe", (Join-Path $projectRoot "config/vision.recipe.json")
)

dotnet @arguments
