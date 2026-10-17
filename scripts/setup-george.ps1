#Requires -Version 7
# One-time setup: puts CycloneHQ in a new GitHub repo under YOUR account and turns on GitHub Pages.
# Run from the unzipped CycloneHQ folder:  pwsh scripts/setup-george.ps1 [-RepoName cyclonehq]
# Needs: git, GitHub CLI (gh), .NET 10 SDK. Safe to re-run.
param(
    [string]$RepoName = "cyclonehq"
)
$ErrorActionPreference = "Stop"
Set-Location (Split-Path $PSScriptRoot -Parent)

function Need([string]$cmd, [string]$hint) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) { throw "$cmd not found. $hint" }
}
#Need git    "Install from https://git-scm.com/downloads"
Need gh     "Install from https://cli.github.com (winget install GitHub.cli)"
Need dotnet "Install the .NET 10 SDK from https://dotnet.microsoft.com/download"
if (-not (dotnet --list-sdks | Select-String '^10\.')) { throw ".NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download" }

# Sign in to GitHub. The 'workflow' scope is needed to push .github/workflows/deploy.yml.
gh auth status *> $null
if ($LASTEXITCODE) { gh auth login --web --git-protocol https --scopes workflow }
elseif (-not ((gh auth status 2>&1) -match 'workflow')) { gh auth refresh --scopes workflow }
gh auth setup-git
$owner = gh api user --jq .login
$repo = "$owner/$RepoName"
Write-Host "Setting up $repo ..."

# Quick local check that the solution builds before anything goes online.
dotnet build CycloneHQ.slnx -c Release --nologo -v quiet
if ($LASTEXITCODE) { throw "Build failed; fix the errors above and re-run." }

# Local git repo with one commit.
if (-not (Test-Path .gitignore)) { dotnet new gitignore | Out-Null; Add-Content .gitignore "`ndist/" }
if (-not (Test-Path .git)) { git init -b main | Out-Null }
if (-not (git config user.email)) {
    $id = gh api user --jq .id
    git config user.name $owner
    git config user.email "$id+$owner@users.noreply.github.com"
}
git add -A
git diff --cached --quiet
if ($LASTEXITCODE) { git commit -m "CycloneHQ" | Out-Null }

# Create the GitHub repo (public: GitHub Pages is free for public repos) and push.
gh repo view $repo *> $null
if ($LASTEXITCODE) {
    gh repo create $repo --public --description "CycloneHQ fantasy football command center" --source . --remote origin --push
} else {
    if (-not (git remote | Select-String '^origin$')) { git remote add origin "https://github.com/$repo.git" }
    git push -u origin main
}
if ($LASTEXITCODE) { throw "Push failed." }

# Turn on Pages with GitHub Actions as the source.
gh api "repos/$repo/pages" *> $null
if ($LASTEXITCODE) { gh api -X POST "repos/$repo/pages" -f build_type=workflow | Out-Null }
else               { gh api -X PUT  "repos/$repo/pages" -f build_type=workflow | Out-Null }
if ($LASTEXITCODE) { throw "Couldn't enable GitHub Pages. Do it by hand: repo Settings > Pages > Source: GitHub Actions." }

# The push started the first deploy; if Pages wasn't on yet when it reached the deploy step, run it again.
Start-Sleep -Seconds 5
gh workflow run deploy.yml --repo $repo | Out-Null
Write-Host "Started a deploy. Watching it (about 3-5 minutes)..."
Start-Sleep -Seconds 5
$run = gh run list --repo $repo --workflow deploy.yml --event workflow_dispatch --limit 1 --json databaseId --jq '.[0].databaseId'
if ($run) {
    gh run watch $run --repo $repo --exit-status
    if ($LASTEXITCODE) { Write-Warning "The deploy failed. Logs: https://github.com/$repo/actions  (re-run: gh workflow run deploy.yml --repo $repo)" }
}

$url = gh api "repos/$repo/pages" --jq .html_url
Write-Host ""
Write-Host "CycloneHQ: $url"
Write-Host "On Android: open it in Chrome, tap the menu (three dots), then 'Install app'."
