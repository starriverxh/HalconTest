param(
    [ValidateSet("ping", "status", "trigger", "shutdown")]
    [string]$Command = "trigger",
    [string]$HostName = "127.0.0.1",
    [int]$Port = 9100,
    [string]$ImagePath = "",
    [double]$MockX = 0.012,
    [double]$MockY = -0.008,
    [double]$MockTheta = 0.015
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot ".dotnet-home"
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = "1"

$parameters = if ($ImagePath) {
    @{ imagePath = (Resolve-Path $ImagePath).Path }
} else {
    @{
        mockX = $MockX
        mockY = $MockY
        mockTheta = $MockTheta
        toleranceMm = 0.05
    }
}
$paramsJson = [System.Text.Json.JsonSerializer]::Serialize($parameters)

dotnet run --project (Join-Path $projectRoot "src/Trigger.Client/Trigger.Client.csproj") -- `
    --host $HostName --port $Port --command $Command --params $paramsJson
