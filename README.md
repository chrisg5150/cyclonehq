# CycloneHQ

A fantasy football command center for **Chris' Cyclones** (franchise 0006) in the Universal League of American
Fantasy Football (MFL league 24623). It goes past what the MFL app shows:

- **Weak spots:** flags players who are underperforming or underused for their slot (snap share, target share,
  carries, expected vs. actual points).
- **Player cards:** tap any of your players to see their rankings and trend, plus ranked **trade targets** from the
  other 11 teams. Targets are built around your weakest positions and each partner's surplus and needs.
- **Where you stand:** position strength vs. the league, your live matchup, the league scoreboard, and live NFL scores.
- **More leagues:** sync other MFL leagues, or paste in your ESPN roster.

It's a Blazor WebAssembly PWA, installable on Android, hosted free on **your** GitHub Pages and refreshed by
GitHub Actions.

**To deploy it, see [docs/SETUP.md](docs/SETUP.md).** The short version: `pwsh scripts/setup-george.ps1`.

## How it works

```
GitHub Actions (schedule / push)
  └─ CycloneHQ.Sync ──> MFL exports + rankings + usage ──> LeagueAnalyzer.Run ──> wwwroot/data/*.json
  └─ dotnet publish CycloneHQ.App ──> GitHub Pages
Phone ──> CycloneHQ.App (static) ──> reads data/*.json, plus live ESPN NFL scores
```

MFL blocks browser requests (CORS), so all MFL data goes through Sync at build time. ESPN's NFL scoreboard allows
browser requests, so live scores are fetched on the phone.

| Project | What it is |
|---|---|
| `src/CycloneHQ.Core` | Data contract (`Models/Contract.cs`) and the analysis and trade engine (`Analysis/`) |
| `src/CycloneHQ.Sync` | Console app that pulls data, runs the analysis, and writes JSON. Leagues are listed in `leagues.config.json`. |
| `src/CycloneHQ.App` | Blazor WebAssembly PWA (.NET 10) |
| `tests/CycloneHQ.Core.Tests` | xUnit tests for the engine |
| `.github/workflows/deploy.yml` | Sync, publish, and deploy to Pages |
| `scripts/` | `setup-george` (one-time deploy), `run-local`, `set-base-path` (used by CI), `package` |

## Common tasks

```powershell
pwsh scripts/run-local.ps1       # sync and run locally at http://localhost:5059
dotnet test                      # engine tests
gh workflow run deploy.yml       # sync and deploy right now
```

To add an MFL league, edit `src/CycloneHQ.Sync/leagues.config.json` and push. Details are in
[docs/SETUP.md](docs/SETUP.md).

Built with Claude Code.
