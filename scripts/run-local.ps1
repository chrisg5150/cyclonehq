#Requires -Version 7
# Pulls fresh league data with Sync, then runs the app locally at http://localhost:5059
# Usage: pwsh scripts/run-local.ps1 [-SkipSync]
param(
    [switch]$SkipSync
)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

if (-not $SkipSync) {
    dotnet run --project src/CycloneHQ.Sync -- --out src/CycloneHQ.App/wwwroot/data
    if ($LASTEXITCODE) { throw "Sync failed. Run with -SkipSync to use the data already in wwwroot/data." }
}
dotnet run --project src/CycloneHQ.App --launch-profile http
