$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$program = Join-Path $projectRoot "halcon/programs/offline_production_demo.hdev"
$procedures = Join-Path $projectRoot "halcon/procedures"

hrun -P -p $procedures -c DemoPassed --dump=Summary $program
