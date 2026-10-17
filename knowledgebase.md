# CycloneHQ knowledge base

A handoff document for whoever works on CycloneHQ next, especially a Claude Code session. Read this first, then
`README.md` and `docs/SETUP.md`. This file covers the facts, decisions and gotchas you can't easily recover from the
code. Last updated 2026-10-16.

## 1. What this is
CycloneHQ is a fantasy football command center, built as an installable Android web app (PWA), for **Chris' Cyclones**
in the **Universal League of American Fantasy Football**. The owner is a .NET developer, so everything is .NET 10 / C#.

It's meant to go past what the MyFantasyLeague (MFL) app shows:
- **Weak spots:** flags players who are underperforming for their lineup slot or underused (snap share, target share,
  rush share, expected vs. actual points).
- **Player cards:** tap any player for his league rank, consensus rank, trade value, trend, usage and a verdict
  (Start / Bench / Hold / Buy low / Sell / Drop), plus ranked **trade targets from the other 11 teams** and free-agent
  replacements.
- **Trades that work for both sides:** suggestions are built around the user's weakest slots and each partner's
  surplus and needs, with trade values roughly balanced.
- **Context:** position strength vs. the league, the live matchup, standings, league scoreboard, transactions, and
  live NFL scores.
- **Other leagues:** pasted or uploaded rosters for leagues that can't be synced.

## 2. Key facts
| Thing | Value |
|---|---|
| Synced league | MFL league **24623**, season 2026, host `https://www44.myfantasyleague.com`, 12 teams |
| User's team | **Chris' Cyclones**, franchise id **0006** |
| League is public? | Yes. Every MFL export works without logging in. |
| Lineup | 7 starters: QB 1, RB 1-2, **WR+TE 3-4 (one combined slot)**, K 1. No DEF in this league. |
| Scoring | TD-heavy, **no PPR**. Sync scores expected points with the league's own MFL rules. |
| Second league | ESPN league **244973**. It's **private** (ESPN returns 401 "not authorized"), so it can't be synced. The user pastes that roster in through Leagues > Add a league. If the commissioner enables "Make League Viewable to Public", ESPN's API could sync it (`lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/2026/segments/0/leagues/244973`). |
| Phone | **Android** (Chrome), not iPhone |
| Hosting | The user's **own** GitHub repo, GitHub Actions and GitHub Pages ($0) |

## 3. Architecture
```
GitHub Actions (schedule / push / manual)
  └─ CycloneHQ.Sync  ── MFL exports + nflverse + DynastyProcess ──> LeagueAnalyzer.Run ──> wwwroot/data/*.json
  └─ dotnet publish CycloneHQ.App ──> set-base-path.ps1 ──> GitHub Pages
Phone (Chrome PWA) ──> CycloneHQ.App (static Blazor WASM) ──> reads data/*.json  +  live ESPN NFL scoreboard
```
- **Why there's a sync job:** MFL's API doesn't allow cross-origin requests (CORS: it only allows its own domain), so a
  page on the phone can't call MFL directly. All MFL data goes through Sync, which runs in Actions.
- **ESPN's NFL scoreboard** (`site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard`) sends
  `Access-Control-Allow-Origin: *`, so the app fetches live NFL scores straight from the phone.
- **Freshness:** fantasy data updates when Actions redeploys, about every 10 minutes during NFL game windows and every
  3 hours otherwise. The app checks `live.json` every 60 seconds, so a new deploy shows up within a minute.

## 4. Repo map
| Path | What it is |
|---|---|
| `CycloneHQ.slnx` | Solution (the .NET 10 `.slnx` format) |
| `Directory.Build.props` | Nullable/ImplicitUsings. If the repo sits inside a OneDrive folder, it sends build output to `%LOCALAPPDATA%\CycloneHQ\artifacts` (`ArtifactsPath`) so bin/obj don't churn OneDrive sync. Elsewhere you get normal per-project bin/obj. |
| `src/CycloneHQ.Core/Models/Contract.cs` | **The data contract.** C# records for every JSON file, plus `DataPaths`, `JsonDefaults.Options` (web defaults, camelCase, nulls omitted) and `PlayerFlags`. Sync writes these types and the app reads the same types. |
| `src/CycloneHQ.Core/Analysis/` | The analysis and trade engine (pure, no I/O). Entry point: `LeagueAnalyzer.Run(league, pool, myFranchiseId)`. |
| `src/CycloneHQ.Sync/` | Console app: `Mfl/` (client, JSON mapping, scoring rules, season), `Enrichment/` (nflverse, DynastyProcess), `PlayerDirectoryBuilder.cs`, `leagues.config.json`, `README.md` |
| `src/CycloneHQ.App/` | Blazor WebAssembly PWA (details in section 7) |
| `tests/CycloneHQ.Core.Tests/` | xUnit tests for the engine (`AnalysisTests.cs`, `TestLeague.cs` fixture) |
| `.github/workflows/deploy.yml` | Sync, publish, base-path fixup and Pages deploy |
| `scripts/` | `setup-george.ps1/.sh` (one-time repo and Pages setup), `run-local.ps1`, `set-base-path.ps1`, `package.ps1` |
| `docs/SETUP.md` | User-facing setup and how-to |

## 5. Data contract (files under `src/CycloneHQ.App/wwwroot/data/`)
| File | Record | Notes |
|---|---|---|
| `leagues.json` | `LeagueIndex` | Synced leagues; `Key` is like `mfl-24623` and doubles as the folder name |
| `directory.json` | `PlayerDirectory` | Every fantasy-relevant NFL player (id, name, position, team, PPG, value, rank), used to match pasted rosters |
| `{key}/league.json` | `LeagueInfo` | Settings, lineup slots, scoring summary, franchises with record, points and roster ids |
| `{key}/players.json` | `PlayerPool` | Rostered players plus the top 80 free agents by points: stats, weekly points, `Usage`, `Rank`, `Flags` |
| `{key}/teams.json` | `TeamProfiles` | Every team's optimal starters, position strength, needs and surplus |
| `{key}/live.json` | `LiveWeek` | Current week's matchups with live per-player scores |
| `{key}/analysis.json` | `TeamAnalysis` | Headline, needs, and one `PlayerInsight` per player on the user's roster, with trade ideas and replacements |
| `{key}/transactions.json` | `TransactionLog` | Recent adds, drops and trades |

Conventions:
- Shares and snap % are **fractions from 0 to 1**.
- `TradeValue` runs **0-100 and is comparable across positions**.
- `NflTeam` uses **ESPN codes** (NE, JAX, GB, KC, LV, NO, SF, TB, WSH) so it joins the ESPN scoreboard.
- Positions are QB/RB/WR/TE/K (MFL's `PK` becomes `K`).
- Curly quotes in MFL text are normalized to straight ones.
- Contract rule: **add fields, never rename or remove them**, because already-deployed data and the app share these types.

## 6. Data sources (all free, no API keys)
| Source | Used for |
|---|---|
| MFL exports (`/2026/export?TYPE=...&L=24623&JSON=1`) | league, rosters, players (`DETAILS=1`), playerScores, projectedScores, liveScoring, leagueStandings, injuries, freeAgents, transactions, rules, playerRanks |
| nflverse `stats_player_week_<year>.csv` | target share |
| nflverse `snap_counts_<year>.csv` | offensive snap % |
| ffopportunity `ep_weekly_<year>.csv` | rush share and expected stat lines, re-scored with the league's MFL rules into expected points |
| DynastyProcess `values-players.csv` | trade values. These are dynasty-based, so they're **averaged 50/50 with MFL's current-season rank value**; `ValueSource` says which source was used. |
| DynastyProcess `db_playerids.csv` | maps MFL ids to gsis, PFR and FantasyPros ids |

Sync is polite to MFL: one request at a time, a short pause, and a `CycloneHQ-Sync` User-Agent. It **fails soft**:
a broken source logs a warning and everything else is still written. The exit code is non-zero only if no league
synced. Not available: `RedZoneTouches` (needs full play-by-play; always null) and MFL `adp` (no drafts this season).

## 7. The app (`src/CycloneHQ.App`)
- **Pages:**
  - `/` Home: headline, matchup card, position-strength diverging bars, flagged players
  - `/team`: starters (from live.json, falling back to the optimal lineup) and bench, with flags and sparklines
  - `/player/{Id}`: player card. The file is `PlayerPage.razor` because a page named `Player` clashes with the `Player` model.
  - `/league`: scores, standings, strength heatmap, moves
  - `/nfl`: live ESPN scoreboard; refreshes every 30 seconds while games are live and marks games with the user's players
  - `/leagues`: league switcher plus **Add a league** (paste or CSV, matched by `RosterMatcher`, saved to `localStorage`)
- **Services:**
  - `LeagueData` (scoped) loads `DataPaths` relative to the base address, polls live.json with a cache-busting query, and treats a 404 as missing data
  - `AppState`: the current league key, `mfl-24623` or `manual-<slug>`
  - `NflTeams`: team codes and colors
  - `Fmt`: formatting helpers
  - `Leagues/*`: manual leagues and roster matching
  - `Nfl/EspnScoreboard`
- **Design system:** `wwwroot/css/app.css` with Barlow Condensed (display) and Inter (body), position colors as
  `--pos-*`, dark-first with full light mode, and reduced motion respected. Entrance animations start from a
  **visible** state (6px slide, no fade from 0), so content is never hidden if animations don't run. Lane E's areas use
  scoped `.razor.css`; `index.html` links `CycloneHQ.App.styles.css`.
- **Culture is pinned to en-US**, so SVG chart coordinates always use "." decimals.
- **Base path:** the app always uses relative URLs and keeps `<base href="/" />` in source. At publish,
  `scripts/set-base-path.ps1` rewrites `<base href>` to `/<repo>/`, recomputes the `index.html` hash in
  `service-worker-assets.js` (otherwise the PWA install breaks), and adds `404.html` (for deep links) and `.nojekyll`.
- **Service worker** (`service-worker.published.js`) works out its base from its own location and **never caches
  `data/`**, so league data is always fresh.

## 8. How the analysis engine thinks (`Core/Analysis`)
| File | Role |
|---|---|
| `PlayerValuation` | A blended per-player score from PPG, last-3 average, projection, weekly expected points and TradeValue (degrades gracefully when inputs are null) |
| `LineupOptimizer` | Best legal lineup from the league's slot rules, including the combined WR+TE 3-4 slot |
| `TeamProfiler` | Position and slot strength per team vs. the league median, plus needs and surplus |
| `PlayerFlagger` | underperforming, underutilized, buy-low (expected > actual), sell-high, breakout, injured, on-bye |
| `TradeFinder` | Searches 1-for-1, 2-for-1, 1-for-2 and 2-for-2 deals with all 11 teams (about 0.2 s) |
| `InsightWriter` | Verdicts, plain-English summaries, reasons and the headline |

Tuning rules, set after reviewing the first output:
- **Needs are measured per lineup slot** (QB, RB, WR/TE #1-3, K, flex) against the league median. TE counts through
  the shared WR/TE and flex slots.
- **Need-fit is 35% of a trade's score.**
- **Minimum gain:** a trade must add at least +1.5 pts/week to the user's best lineup, or +0.75 if it fixes a slot
  that trails the median by 1+ pts. The partner shouldn't get much worse.
- **Value balance:** trades are kept within about 15% on trade value.
- QB-for-QB sideways swaps and low-value throw-ins are dropped.
- **Variety:** each target player appears in at most 2 ideas across the whole analysis, spread across partners.
- A starter on bye or out gets a "Bench" verdict.

Example output (Week 6): "3rd-best lineup of 12, ~56 pts/wk". Top ideas: Parker Washington + Denzel Boston for Bijan
Robinson (+2.3/wk); Emeka Egbuka + Drake Maye for Javonte Williams + Cam Skattebo (+2.1); Jameson Williams + Rhamondre
Stevenson for Saquon Barkley + Davante Adams (+2.2).

## 9. Commands
```bash
dotnet build CycloneHQ.slnx
dotnet test CycloneHQ.slnx
dotnet run --project src/CycloneHQ.Sync -- --out src/CycloneHQ.App/wwwroot/data   # refresh data (~15 s)
dotnet run --project src/CycloneHQ.App                                            # local dev server
pwsh scripts/run-local.ps1                                                        # sync + run, in one step
pwsh scripts/setup-george.ps1                                                     # one-time: repo + Pages on your GitHub
pwsh scripts/package.ps1                                                          # clean zip into dist/
```
Test the phone layout at **412x915** (Android). Check both dark and light mode.

## 10. Deploy and operations
- `deploy.yml` runs on push to main, on manual dispatch, and on a schedule (UTC): every 10 minutes Thu night, all of
  Sunday into Monday UTC, and Mon night; every 3 hours otherwise. GitHub can start scheduled runs a few minutes late.
- **GitHub pauses scheduled workflows after 60 days with no repo activity.** That's around mid-December, which is
  fantasy playoff time. Push an empty commit before then (`git commit --allow-empty -m "keep alive" && git push`).
- Install on Android: open the Pages URL in Chrome, then menu > **Install app**.
- To add another MFL league: add an entry to `src/CycloneHQ.Sync/leagues.config.json` (league id, season, `wwwNN` host,
  franchise id from the roster URL's `F=`) and push. The league must be public.

## 11. Gotchas
- **MSB3030 "CycloneHQ.Core.dll not found"** or file locks during concurrent builds: the shared compiler server can
  get stuck. Use `-p:UseSharedCompilation=false` or `dotnet build-server shutdown`. When several sessions build at
  once, give each its own `-p:ArtifactsPath=...`.
- **Never create a git repo inside a OneDrive folder**: the small-file churn can stall OneDrive sync.
- A running `dotnet run --no-build` dev server breaks with a 404 on `dotnet.*.js` if another build changes the
  framework file names. Restart it with a build.
- Some embedded browser panes refuse service-worker registration; that's not an app bug. Test the PWA in real Chrome.
- Gmail blocks `.js`, `.ps1` and `.dll` files even inside a zip, so share the package as a link, not an attachment.

## 12. Known gaps and backlog
- Not yet verified on a real phone: the Android PWA install.
- The app-bar pill shows "My league" for a pasted league, and `AppState` doesn't remember the selected league across
  reloads (an easy `localStorage` fix).
- `RedZoneTouches` is always null; filling it needs nflverse play-by-play.
- The ESPN league can only sync if it's made public. If it is, add an ESPN source to Sync using the URL in section 2.
- **Optional native Android app (no GitHub needed):** move the Razor pages into a Razor class library shared by the
  WASM app and a new .NET MAUI Blazor Hybrid app. The native app can call MFL directly (native HttpClient has no CORS)
  and run Sync plus `LeagueAnalyzer` on the phone, then ship as a sideloaded APK. It needs the `maui-android` workload,
  the Android SDK and a JDK.

## 13. How it was built
Joseph built CycloneHQ on 2026-10-16 using Claude Code. One manager session wrote the contract and plan, then ran
five parallel sessions:
- **A:** app shell, Home, Team and Player screens
- **B:** Sync and data sources
- **C:** deploy kit
- **D:** analysis and trade engine
- **E:** League, NFL and Add-a-league screens

The manager reviewed each lane's output, sent fixes back (for example, the first trade ideas were repetitive and
ignored needs, which led to the tuning in section 8), then ran a final build, tests, a fresh sync, a GitHub Pages
emulation test at phone size, and packaging.
