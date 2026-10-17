#Requires -Version 7
# Builds a clean zip of the project for George: dist/CycloneHQ.zip (unzips to a CycloneHQ/ folder).
# Leaves out the retired prototypes (web/, dotnet/, hosting/), internal notes (NOTES.md, docs/PLAN.md), dist/,
# and build output (bin, obj, .vs).
# The zip name never changes, so a OneDrive share link to it keeps working after a re-package.
# Usage: pwsh scripts/package.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$topExcludes = "web", "dotnet", "hosting", "dist", "NOTES.md", ".git", ".claude"
$anyExcludes = "bin", "obj", ".vs", "node_modules"
$pathExcludes = @("docs\PLAN.md") | ForEach-Object { Join-Path $root $_ }

$stage = Join-Path ([IO.Path]::GetTempPath()) "CycloneHQ-package"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
$dest = Join-Path $stage "CycloneHQ"

function Copy-Clean([string]$from, [string]$to, [bool]$top) {
    New-Item -ItemType Directory -Force $to | Out-Null
    foreach ($item in Get-ChildItem $from -Force) {
        if ($anyExcludes -contains $item.Name -or ($top -and $topExcludes -contains $item.Name)) { continue }
        if ($item.Name -like "*.user" -or $pathExcludes -contains $item.FullName) { continue }
        $target = Join-Path $to $item.Name
        if ($item.PSIsContainer) { Copy-Clean $item.FullName $target $false }
        else { Copy-Item $item.FullName $target }
    }
}
Copy-Clean $root $dest $true

New-Item -ItemType Directory -Force (Join-Path $root "dist") | Out-Null
$zip = Join-Path $root "dist\CycloneHQ.zip"
Remove-Item $zip -ErrorAction SilentlyContinue
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item $stage -Recurse -Force

$entries = [IO.Compression.ZipFile]::OpenRead($zip)
try {
    $count = $entries.Entries.Count
    # Gmail rejects attachments containing these types, even inside a zip (support.google.com/mail/answer/6590).
    $gmailBlocked = $entries.Entries.FullName | Where-Object { $_ -match '\.(js|mjs|ps1|dll|exe|bat|cmd|jar)$' }
} finally { $entries.Dispose() }

$size = "{0:N1} MB" -f ((Get-Item $zip).Length / 1MB)
Write-Host "Packaged $count files ($size): $zip"
if ($gmailBlocked) {
    Write-Host "Gmail won't accept this zip as an attachment because of: $($gmailBlocked -join ', ')"
    Write-Host "Share it as a link instead: right-click dist\CycloneHQ.zip in File Explorer > Share > Copy link."
}
