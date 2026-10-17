#!/usr/bin/env bash
# One-time setup: puts CycloneHQ in a new GitHub repo under YOUR account and turns on GitHub Pages.
# Run from the unzipped CycloneHQ folder:  bash scripts/setup-george.sh [repo-name]   (default: cyclonehq)
# Needs: git, GitHub CLI (gh), .NET 10 SDK. Safe to re-run.
set -euo pipefail
cd "$(dirname "$0")/.."
REPO_NAME="${1:-cyclonehq}"

need() { command -v "$1" >/dev/null 2>&1 || { echo "$1 not found. $2" >&2; exit 1; }; }
need git    "Install from https://git-scm.com/downloads"
need gh     "Install from https://cli.github.com"
need dotnet "Install the .NET 10 SDK from https://dotnet.microsoft.com/download"
dotnet --list-sdks | grep -q '^10\.' || { echo ".NET 10 SDK not found. Install it from https://dotnet.microsoft.com/download" >&2; exit 1; }

# Sign in to GitHub. The 'workflow' scope is needed to push .github/workflows/deploy.yml.
if ! gh auth status >/dev/null 2>&1; then
  gh auth login --web --git-protocol https --scopes workflow
elif ! gh auth status 2>&1 | grep -q workflow; then
  gh auth refresh --scopes workflow
fi
gh auth setup-git
OWNER="$(gh api user --jq .login)"
REPO="$OWNER/$REPO_NAME"
echo "Setting up $REPO ..."

# Quick local check that the solution builds before anything goes online.
dotnet build CycloneHQ.slnx -c Release --nologo -v quiet

# Local git repo with one commit.
if [ ! -f .gitignore ]; then dotnet new gitignore >/dev/null; printf '\ndist/\n' >> .gitignore; fi
[ -d .git ] || git init -b main >/dev/null
if [ -z "$(git config user.email || true)" ]; then
  git config user.name "$OWNER"
  git config user.email "$(gh api user --jq .id)+$OWNER@users.noreply.github.com"
fi
git add -A
git diff --cached --quiet || git commit -m "CycloneHQ" >/dev/null

# Create the GitHub repo (public: GitHub Pages is free for public repos) and push.
if ! gh repo view "$REPO" >/dev/null 2>&1; then
  gh repo create "$REPO" --public --description "CycloneHQ fantasy football command center" --source . --remote origin --push
else
  git remote | grep -qx origin || git remote add origin "https://github.com/$REPO.git"
  git push -u origin main
fi

# Turn on Pages with GitHub Actions as the source.
if gh api "repos/$REPO/pages" >/dev/null 2>&1; then
  gh api -X PUT "repos/$REPO/pages" -f build_type=workflow >/dev/null
else
  gh api -X POST "repos/$REPO/pages" -f build_type=workflow >/dev/null
fi

# The push started the first deploy; if Pages wasn't on yet when it reached the deploy step, run it again.
sleep 5
gh workflow run deploy.yml --repo "$REPO" >/dev/null
echo "Started a deploy. Watching it (about 3-5 minutes)..."
sleep 5
RUN="$(gh run list --repo "$REPO" --workflow deploy.yml --event workflow_dispatch --limit 1 --json databaseId --jq '.[0].databaseId')"
if [ -n "$RUN" ] && ! gh run watch "$RUN" --repo "$REPO" --exit-status; then
  echo "The deploy failed. Logs: https://github.com/$REPO/actions  (re-run: gh workflow run deploy.yml --repo $REPO)"
fi

URL="$(gh api "repos/$REPO/pages" --jq .html_url)"
echo
echo "CycloneHQ: $URL"
echo "On Android: open it in Chrome, tap the menu (three dots), then 'Install app'."
