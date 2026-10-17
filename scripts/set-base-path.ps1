#Requires -Version 7
# Prepares a published Blazor wwwroot folder for hosting under a subpath (GitHub Pages project site).
#  - rewrites <base href> in index.html and the service worker's base constant
#  - fixes index.html's integrity hash in service-worker-assets.js (otherwise the PWA's offline install fails)
#  - adds .nojekyll and copies index.html to 404.html so deep links load the app
# Used by .github/workflows/deploy.yml (pwsh on ubuntu) and for local Pages emulation.
# Usage: pwsh scripts/set-base-path.ps1 -Root <publish>/wwwroot -BasePath /cyclonehq/
param(
    [Parameter(Mandatory)] [string]$Root,
    [Parameter(Mandatory)] [string]$BasePath
)
$ErrorActionPreference = "Stop"
if (-not $BasePath.StartsWith("/")) { $BasePath = "/$BasePath" }
if (-not $BasePath.EndsWith("/"))   { $BasePath = "$BasePath/" }

$utf8 = [System.Text.UTF8Encoding]::new($false)
function Edit-File([string]$name, [string]$pattern, [string]$replacement) {
    $path = Join-Path $Root $name
    if (-not (Test-Path $path)) { return }
    $text = [IO.File]::ReadAllText($path)
    $new = [regex]::Replace($text, $pattern, $replacement)
    if ($new -ne $text) {
        [IO.File]::WriteAllText($path, $new, $utf8)
        # Pre-compressed copies would now be stale; GitHub Pages doesn't serve them anyway.
        Remove-Item "$path.br", "$path.gz" -ErrorAction SilentlyContinue
    }
}

Edit-File "index.html" '<base href="[^"]*"\s*/?>' "<base href=`"$BasePath`" />"
Edit-File "service-worker.js" 'const base = "[^"]*";' "const base = `"$BasePath`";"

$index = Join-Path $Root "index.html"
$hash = "sha256-" + [Convert]::ToBase64String([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($index)))
Edit-File "service-worker-assets.js" '"hash": "sha256-[^"]*",(\s*)"url": "index.html"' "`"hash`": `"$hash`",`$1`"url`": `"index.html`""

Copy-Item $index (Join-Path $Root "404.html") -Force
New-Item -ItemType File (Join-Path $Root ".nojekyll") -Force | Out-Null
Write-Host "Base path set to $BasePath in $Root"
