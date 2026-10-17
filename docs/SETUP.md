# CycloneHQ setup

This takes about 10 minutes. Once it's done, the app runs on your own GitHub account for $0 and keeps itself up to date.

## What you get

- A Blazor WebAssembly PWA at `https://<your-github-username>.github.io/cyclonehq/`, installable on Android.
- A GitHub Actions workflow ([.github/workflows/deploy.yml](../.github/workflows/deploy.yml)) that runs
  `CycloneHQ.Sync` to pull MyFantasyLeague data (plus rankings and usage), runs the analysis and trade engine,
  publishes the app, and deploys it to GitHub Pages:
  - every 10 minutes during NFL game windows (Thursday night, Sunday, Sunday night, Monday night)
  - every 3 hours the rest of the week
  - on every push to `main`, and whenever you click **Run workflow** in the Actions tab

Live NFL scores come straight from ESPN in the browser. Fantasy data is as fresh as the last sync (10 minutes or
less on game days).

## Prerequisites

| Tool | Check | Get it |
|---|---|---|
| .NET 10 SDK | `dotnet --list-sdks` | https://dotnet.microsoft.com/download |
| Git | `git --version` | https://git-scm.com/downloads |
| GitHub CLI | `gh --version` | `winget install GitHub.cli` or https://cli.github.com |
| PowerShell 7 (Windows) | `pwsh --version` | `winget install Microsoft.PowerShell` |
| A free GitHub account | | https://github.com/signup |

## Deploy (one time)

1. Unzip `CycloneHQ.zip` somewhere **outside** OneDrive or Dropbox, e.g. `C:\src\CycloneHQ`.
2. From that folder, run:

   ```powershell
   pwsh scripts/setup-george.ps1
   ```

   On macOS or Linux, run `bash scripts/setup-george.sh` instead.

   The script:
   - checks for `dotnet`, `git` and `gh`
   - signs you in to GitHub in the browser if needed
   - builds the solution
   - creates a **public** repo `cyclonehq` under your account and pushes the code
   - turns on GitHub Pages with **GitHub Actions** as the source
   - starts the first deploy, watches it, and prints your URL

   To use a different repo name, run `pwsh scripts/setup-george.ps1 -RepoName my-name`. The workflow sets the
   app's `<base href>` from the repo name automatically.

The first deploy takes about 3 to 5 minutes. If it fails, the script prints the Actions log link. Fix the problem,
then re-run the deploy with `gh workflow run deploy.yml`. The setup script is also safe to re-run.

## Install on Android

1. Open your URL in **Chrome**.
2. Tap the **⋮** menu, then **Install app**. On some phones it says **Add to Home screen**, then **Install**.

CycloneHQ gets its own icon and opens full-screen like a native app. After a new deploy, close and reopen the app to
pick up the latest version.

## Run it locally

```powershell
pwsh scripts/run-local.ps1            # sync fresh data, then serve at http://localhost:5059
pwsh scripts/run-local.ps1 -SkipSync  # reuse the data already in src/CycloneHQ.App/wwwroot/data
```

## Add another MyFantasyLeague league

Add an entry to [src/CycloneHQ.Sync/leagues.config.json](../src/CycloneHQ.Sync/leagues.config.json):

```jsonc
{
  "platform": "mfl",
  "leagueId": "12345",                              // from the league URL
  "season": 2026,
  "host": "https://www44.myfantasyleague.com",      // the wwwNN server in the league URL
  "myFranchiseId": "0003"                            // your team: the F=00NN in your roster URL
}
```

Then commit and push. The push triggers a deploy, and the league appears in the app's league switcher:

```powershell
git commit -am "Add league 12345"; git push
```

MFL leagues must allow public access to their exports, which most do.

## Add your ESPN league (paste your roster)

ESPN league 244973 is private, so Sync can't pull it. Add it in the app instead:

1. On ESPN (website or app), open **My Team** and copy your roster. Selecting the whole roster table works.
2. In CycloneHQ, tap the league chip at the top right (**ULAFF ▾**), then **Add a league**. Name the league, pick
   **ESPN**, and paste the roster. You can also upload a CSV. If you type names yourself, put one player per line.
3. Tap **Match players**, check the matches (add anyone it missed with the search box), and tap **Save league**.
   CycloneHQ matches the players to its player directory, so you get player cards, rankings and position needs.

Trade ideas need every team's roster, so they're only available for synced (MFL) leagues. A pasted league is saved
in the app on that phone and isn't committed to the repo.

## Cost: $0

- GitHub Actions minutes are free and unlimited for public repos on standard runners.
- GitHub Pages is free for public repos.
- MFL exports and the ESPN scoreboard are free and need no API key.

The repo has to stay **public** to keep Pages free. Private-repo Pages needs a paid GitHub plan. Everything in it is
public league data anyway.

## Keep the schedule alive (60-day rule)

GitHub **pauses scheduled workflows after 60 days with no activity in a public repo**. Scheduled syncs don't count as
activity, because they don't commit anything. If you set up in mid-October, the schedule would stop in mid-December,
right in the fantasy playoffs.

Push something before then. An empty commit is enough:

```powershell
git commit --allow-empty -m "Keep schedule alive"; git push
```

GitHub emails a warning before it pauses the schedule. If it already paused, go to **Actions**, open
**Deploy CycloneHQ**, and click **Enable workflow**.

## Troubleshooting

- **The app shows old data.** Go to **Actions** and look at the latest run. If Sync failed (for example, MFL was down),
  the last good deploy stays live. Re-run the workflow with **Run workflow**.
- **You get a 404 at the URL right after setup.** The first deploy hasn't finished yet. Check the Actions tab.
- **Scheduled runs start late.** GitHub's cron is best-effort and can lag a few minutes at busy times.
