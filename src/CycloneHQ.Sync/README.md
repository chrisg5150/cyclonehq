# CycloneHQ.Sync

Console app that pulls fantasy data and writes the JSON files the CycloneHQ app reads
(`src/CycloneHQ.App/wwwroot/data/`, shapes in `src/CycloneHQ.Core/Models/Contract.cs`).

```bash
dotnet run --project src/CycloneHQ.Sync -- --out src/CycloneHQ.App/wwwroot/data
```

Options: `--out <folder>` (default `data`), `--config <file>` (default: `leagues.config.json` next to the app).

## Adding a league
Add an entry to `leagues.config.json`: the MFL league id, season, the `wwwNN` host from the league's
URL, and your 4-digit franchise id. The league must be public (MFL exports without login).

## Sources (all free, no API key)
| Source | Used for |
|---|---|
| MyFantasyLeague exports | league, rosters, standings, weekly scores, projections, live scoring, free agents, transactions, rules; plus players, injuries, bye weeks, player ranks (from `api.myfantasyleague.com`) |
| nflverse `stats_player_week_<year>.csv` | target share |
| nflverse `snap_counts_<year>.csv` | offensive snap % |
| ffopportunity `ep_weekly_<year>.csv` | rush share and expected stat lines, scored with each league's own MFL rules into expected points |
| DynastyProcess `values-players.csv` | trade values (averaged with MFL's current-season rank value) |
| DynastyProcess `db_playerids.csv` | maps MFL ids to nflverse/PFR/FantasyPros ids |

Conventions: shares and snap % are fractions (0-1). `TradeValue` is 0-100 across positions. Team codes are
ESPN's (NE, JAX, GB, KC, LV, NO, SF, TB, WSH) so they join ESPN live scores.

Requests to MFL go one at a time with a short pause and a `CycloneHQ-Sync` User-Agent. If any source
fails, it's logged as a warning and everything else is still written; the exit code is non-zero only
when no league synced.
