# Live player heatmap implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A heatmap above the live feed on each player page that colors every area green to red by fights won against lost on the current map, updates within a batch of each fight, and filters to this match or all time.

**Architecture:** The plugin puts each kill it records into its 2-second live batch, with the time the map started. The site keeps the current match's kills in memory (rebuilt from `csp_kills` after a restart), publishes each new kill over `/api/stream` as a `fights` event, and serves a player's won and lost cells from a new `/api/players/{id}/fights`. The player page draws those cells over the radar on a canvas and adds live events as they arrive.

**Tech stack:** C# on .NET 10 (Core, Site, plugin), ASP.NET Core minimal APIs, MSTest on Microsoft Testing Platform, vanilla JS and CSS in `wwwroot`.

**Spec:** `docs/superpowers/specs/2026-10-21-live-player-heatmap-design.md`

## Global constraints

- Fights against bots count. A won fight sits at the player's own position (`KillerX`, `KillerY`), and a lost fight at the player's position when they died (`VictimX`, `VictimY`). A kill where killer and victim are the same id counts as neither.
- Cells are 32 radar pixels (a 32 by 32 grid on the 1024 px radar).
- Areas with 2 fights or fewer draw faint (alpha 0.25). Alpha reaches 0.85 at 6 fights.
- Color: balance `(won - lost) / (won + lost)` runs from red `#e5484d` (-1) through grey `#8a8f98` (0) to green `#30a46c` (+1).
- The filter's choice is kept in `localStorage` under `csp.heat.scope.online` and `csp.heat.scope.offline`, defaulting to `match` and `all`, with every read and write in try/catch.
- `LiveBatch` and `ServerSnapshot` gain optional members only (`Kills`, `MapStartedAt`), defaulting to `null`, so a site still takes batches from a plugin that sends neither.
- The site never writes the database. `csp_site` already has `SELECT` on `csp_kills`.
- `Endpoints` is at its CA1506 coupling limit. New handlers and helpers go in their own classes.
- MSTest, semantic asserts, tabs, file-scoped namespaces, the repo's copyright header on new files, no warnings suppressed globally.

## Review focus

1. **A map without a calibrated radar** (`MapOverview.For(map)` is null or not calibrated): the panel says the map has no radar and draws nothing. Test in Task 4.
2. **A site restart mid-match:** the first `fights` request after startup still returns the kills from before the restart, read from `csp_kills` since `MapStartedAt`. Test in Task 3.
3. **A map change while the page is open:** the panel switches to the new map's radar and starts **This match** empty, and a late kill from the old map isn't drawn on the new one. Test in Task 3 (the site drops kills whose map isn't the current one) and checked by hand in Task 5.
4. **A player who has never fought on the current map, or never at all:** the endpoint answers 200 with no cells (not 404), and the panel shows an empty radar with a short note. Test in Task 4.
5. **A batch from an older plugin** (no `Kills`, no `MapStartedAt`): ingest still succeeds and nothing about fights changes. Test in Task 3.

---

### Task 1: Count a player's fights into cells

**Files:**
- Create: `CounterStrikeProgression.Core/Heatmaps/FightGrid.cs`
- Test: `CounterStrikeProgression.Test/FightGridTests.cs`

**Interfaces:**
- Produces: `internal readonly record struct FightCell(int X, int Y, int Won, int Lost);`
- Produces: `internal static class FightGrid` with `public const int CellPixels = 32;`, `public const int Cells = MapOverview.Size / CellPixels;`, `public static IReadOnlyList<FightCell> Build(IEnumerable<KillPoint> kills, ulong steamId, MapOverview overview)`, and `public static FightCell? CellOf(KillPoint kill, ulong steamId, MapOverview overview)` (one fight's cell with `Won` or `Lost` set to 1, or null when the player isn't in it, it's a suicide, or it falls off the radar).

- [ ] **Step 1: Write the failing tests** in `FightGridTests`, using a calibrated overview from `MapOverview.For("de_dust2")` and a helper that builds a `KillPoint` at a given world position:
  - `Build_CountsAKillAsWonAtTheKillersSpot`: one kill by player 7. One cell, `Won == 1`, `Lost == 0`, at the cell holding `(KillerX, KillerY)`.
  - `Build_CountsADeathAsLostAtTheVictimsSpot`: one kill of player 7 by a bot (killer id 0). One cell, `Lost == 1`, at the cell holding `(VictimX, VictimY)`.
  - `Build_IgnoresOtherPlayersFights`: a kill between players 8 and 9. `Build(..., 7, ...)` is empty.
  - `Build_IgnoresSuicides`: killer and victim both 7. Empty.
  - `Build_LeavesOutPointsOffTheRadar`: a world position far outside the map. Empty.
  - `Build_AddsUpFightsInOneCell`: two kills and one death by player 7 at the same spot. One cell, `Won == 2`, `Lost == 1`.
  - `CellOf_MatchesBuildForOneFight`: for the kill and the death above, `CellOf` returns the same cell `Build` does.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~FightGridTests"`
Expected: build error, `FightGrid` doesn't exist.

- [ ] **Step 3: Implement `FightGrid`** the way `HeatmapGrid.Build` converts positions (`overview.ToPixel(new HeatPoint(x, y))`, skip outside `0..Size`), with cells ordered by row then column.

- [ ] **Step 4: Run them and see them pass.** Same command, all pass.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression.Core/Heatmaps/FightGrid.cs CounterStrikeProgression.Test/FightGridTests.cs
git commit -m "Count a player's won and lost fights into radar cells"
```

### Task 2: Read a player's fights from the kill store

**Files:**
- Modify: `CounterStrikeProgression.Core/Heatmaps/IKillMapStore.cs`, `InMemoryKillMapStore.cs`, `MySqlKillMapStore.cs`
- Test: `CounterStrikeProgression.Test/HeatmapTests.cs`

**Interfaces:**
- Produces on `IKillMapStore`:
  - `Task<IReadOnlyList<KillPoint>> FightsAsync(string map, ulong? steamId, DateTime? since, int limit, CancellationToken cancellationToken)`: the newest kills on `map` where `steamId` is the killer or the victim (every kill when null), at or after `since` when given, newest first, at most `limit`.
  - `Task<string?> LastMapAsync(ulong steamId, CancellationToken cancellationToken)`: the map of the player's newest kill or death, or null.

- [ ] **Step 1: Write the failing tests** against `InMemoryKillMapStore`:
  - `InMemory_FightsIncludeKillsAndDeaths`: player 7 kills a bot and is killed by a bot on `de_dust2`, and players 8 and 9 fight there too. `FightsAsync("de_dust2", 7, null, 100, ...)` returns the two fights player 7 is in.
  - `InMemory_FightsSinceLeavesOutOlderKills`: kills at `Start` and `Start.AddMinutes(10)`. `since: Start.AddMinutes(5)` returns only the later one.
  - `InMemory_FightsForTheWholeServerWhenNoPlayer`: `steamId: null` returns every kill on that map, whoever is in it.
  - `InMemory_LastMapIsTheNewestFight`: fights on `de_nuke` then `de_dust2`. `LastMapAsync(7)` is `de_dust2`, and for an unknown player it's null.
  - `MySql_FightsMatchInMemory`: gated on `CSP_TEST_MYSQL` exactly as `MySql_KeepsTheNewestPerMap` is, it writes the same kills to both stores and asserts the same results.

- [ ] **Step 2: Run them and see them fail.** `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~HeatmapTests"`, build error on the missing members.

- [ ] **Step 3: Implement both methods in both stores.** MySQL reads `map, killer_id, victim_id, weapon, headshot, killer_x, killer_y, killer_z, victim_x, victim_y, victim_z, at` into `KillPoint`, with `(killer_id = @steamId OR victim_id = @steamId)` when a player is given and `at >= @since` when a time is given, `ORDER BY id DESC LIMIT @limit`. `LastMapAsync` is `SELECT map ... WHERE killer_id = @steamId OR victim_id = @steamId ORDER BY id DESC LIMIT 1`.

- [ ] **Step 4: Run them and see them pass.**

- [ ] **Step 5: Commit** (`git commit -m "Read a player's fights from the kill store"`).

### Task 3: Send kills live and keep the current match on the site

**Files:**
- Modify: `CounterStrikeProgression.Core/Live/LiveContracts.cs` (optional members)
- Modify: `CounterStrikeProgression/KillMapRecorder.cs`, `CounterStrikeProgression/CombatHandlers.cs:268-272`, `CounterStrikeProgression/Site/SiteReporter.cs`, `CounterStrikeProgression/LifecycleHandlers.cs:53`
- Create: `CounterStrikeProgression.Site/Live/MatchFights.cs`, `CounterStrikeProgression.Site/Live/LivePublisher.cs`
- Modify: `CounterStrikeProgression.Site/Live/LiveState.cs`, `CounterStrikeProgression.Site/Endpoints.cs` (ingest publishing moves out), `CounterStrikeProgression.Site/Program.cs` (register `MatchFights`)
- Test: `CounterStrikeProgression.Test/SiteLiveTests.cs`, `CounterStrikeProgression.Test/LiveSenderTests.cs`

**Interfaces:**
- Consumes: `FightGrid.CellOf` (Task 1), `IKillMapStore.FightsAsync` (Task 2).
- Produces in Core: `ServerSnapshot(..., DateTime? MapStartedAt = null)` and `LiveBatch(..., IReadOnlyList<KillPoint>? Kills = null)`, both appended as the last positional parameter.
- Produces in the plugin: `static KillPoint? KillMapRecorder.PointOf(string map, CCSPlayerController killer, CCSPlayerController victim, WeaponInfo weapon, bool headshot)` (the body of today's `Record` up to the `KillPoint`), `void KillMapRecorder.Record(KillPoint point)`, `void SiteReporter.Record(KillPoint point)` (queued like feed entries, capped at `PendingFeedLimit`), and `void SiteReporter.MapStarted()` (stores `DateTime.UtcNow` for `MapStartedAt`).
- Produces in the site: `internal sealed class MatchFights(IKillMapStore store, ILogger<MatchFights> logger)` with `IReadOnlyList<KillPoint> Apply(ServerSnapshot snapshot, IReadOnlyList<KillPoint>? kills)` (returns the kills it kept), `Task<IReadOnlyList<KillPoint>> CurrentAsync(CancellationToken cancellationToken)` (the current match, rebuilt from the store on the first call after startup or a map change when `MapStartedAt` is known), and `string? Map { get; }`.
- Produces: `internal sealed record FightEvent(string Map, string SteamId, int X, int Y, bool Won);` published as `fights` with an array payload, one entry per human in each kept kill whose cell is on the radar.
- Produces: `internal static class LivePublisher` with `public static void Publish(Broadcaster broadcaster, LiveBatch batch, LiveChanges changes, IReadOnlyList<KillPoint> fights)`, taking over `Endpoints.Publish` and adding the `fights` event.

- [ ] **Step 1: Write the failing site tests** in `SiteLiveTests`:
  - `MatchFights_KeepsKillsOnTheCurrentMap`: a snapshot on `de_dust2` with two kills there. `Apply` returns both, and `CurrentAsync` returns both.
  - `MatchFights_DropsKillsFromAnotherMap`: snapshot on `de_dust2`, one kill tagged `de_nuke`. Not kept.
  - `MatchFights_StartsEmptyOnAMapChange`: kills on `de_dust2`, then a snapshot on `de_mirage` with a later `MapStartedAt`. `CurrentAsync` is empty and `Map` is `de_mirage`.
  - `MatchFights_RebuildsFromTheStoreAfterARestart`: an `InMemoryKillMapStore` holding three kills on `de_dust2` at or after `MapStartedAt` and one before. A fresh `MatchFights` given a snapshot with that `MapStartedAt` and no kills returns the three from `CurrentAsync`.
  - `MatchFights_IgnoresABatchWithoutKillsOrStart`: `Apply(snapshot, null)` with `MapStartedAt` null returns empty and doesn't throw, and `CurrentAsync` is empty.
  - `Ingest_PublishesFightsForEachHuman`: through `LivePublisher.Publish`, a kill by player 7 on a bot yields one `fights` event entry with `SteamId == "7"`, `Won == true`, and a kill between players 7 and 8 yields two entries.

- [ ] **Step 2: Write the failing sender test** in `LiveSenderTests`: `Batch_RoundTripsKillsAndMapStart` serializes a `LiveBatch` with one `KillPoint` and a `MapStartedAt` through the sender's JSON options and reads both back.

- [ ] **Step 3: Run them and see them fail.** `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~SiteLiveTests|FullyQualifiedName~LiveSenderTests"`.

- [ ] **Step 4: Implement the Core contract changes, `MatchFights`, and `LivePublisher`.** `MatchFights` locks around its list (ingest and page requests run on different threads, as `LiveState` notes), caps the list at `HeatmapReader.ServerLimit`, and resets on a new map name or a later `MapStartedAt`. Register it as a singleton in `Program.AddServices`. Ingest calls `matchFights.Apply(batch.Snapshot, batch.Kills)` and passes the result to `LivePublisher.Publish`. Delete `Endpoints.Publish`.

- [ ] **Step 5: Wire the plugin.** In `CombatHandlers`, build the point once with `KillMapRecorder.PointOf` and pass it to `services.KillMaps?.Record(point)` and `services.Site?.Record(point)`. The `KillPointBuffer` rules (no bot-on-bot, sampling) stay on the database path only. `SiteReporter.CollectBatch` drains its pending kills into `LiveBatch.Kills` and sets `MapStartedAt` on the snapshot. `LifecycleHandlers` calls `site.MapStarted()` in the `OnMapStart` listener it already registers, before scheduling `Collect`.

- [ ] **Step 6: Run the full suite** (`dotnet test --project CounterStrikeProgression.Test`) and see it pass.

- [ ] **Step 7: Commit** (`git commit -m "[minor] Send kills to the site live and keep the current match"`).

### Task 4: Serve a player's fights

**Files:**
- Create: `CounterStrikeProgression.Site/FightEndpoints.cs`
- Modify: `CounterStrikeProgression.Site/Program.cs` (`MapPlayerApi` gains the route)
- Test: `CounterStrikeProgression.Test/SiteEndpointTests.cs`

**Interfaces:**
- Consumes: `FightGrid.Build` (Task 1), `IKillMapStore.FightsAsync` and `LastMapAsync` (Task 2), `MatchFights.CurrentAsync` and `Map` (Task 3), `LiveState.Snapshot`, `RadarImages` as `HeatmapEndpoints` uses it.
- Produces: `GET /api/players/{id}/fights?scope=match|all&map=<name>` returning `internal sealed record FightView(string? Map, string Scope, bool Calibrated, string? Radar, int Size, int CellPixels, IReadOnlyList<FightCell> Cells);`.
  - `scope=match`: the current server map and `MatchFights.CurrentAsync()`.
  - `scope=all` (the default for anything else): `map` when given, else the current server map when the player is online, else `LastMapAsync`, then `FightsAsync(map, id, null, HeatmapReader.PlayerLimit)`.
  - 404 for an id that isn't a SteamID64 (as `ProgressReader.TryParseSteamId` decides), 503 when the store throws a `DbException`, as `HeatmapEndpoints` does.

- [ ] **Step 1: Write the failing tests:**
  - `Fights_MatchScopeCountsTheCurrentMatch`: a `MatchFights` holding two kills and one death for player 7 on `de_dust2`. `scope=match` returns `Map == "de_dust2"`, `Calibrated == true`, cells summing to `Won == 2` and `Lost == 1`, `CellPixels == 32`.
  - `Fights_AllScopeReadsTheStore`: kills only in the store. `scope=all&map=de_dust2` counts them.
  - `Fights_EmptyForAPlayerWithNoFights`: an unknown but valid SteamID64 gets 200 with no cells.
  - `Fights_UncalibratedMapHasNoCells`: a map name with no `MapOverview`. 200, `Calibrated == false`, no cells.
  - `Fights_BadIdIs404`.

- [ ] **Step 2: Run them and see them fail.** `--filter "FullyQualifiedName~SiteEndpointTests"`.

- [ ] **Step 3: Implement `FightEndpoints.FightsAsync`** and map the route in `MapPlayerApi`.

- [ ] **Step 4: Run the full suite and see it pass.**

- [ ] **Step 5: Commit** (`git commit -m "Serve a player's won and lost fights per cell"`).

### Task 5: Draw it on the player page

**Files:**
- Create: `CounterStrikeProgression.Site/wwwroot/playerheat.js`
- Modify: `CounterStrikeProgression.Site/wwwroot/player.html` (load `playerheat.js` before `site.js`, `defer`), `CounterStrikeProgression.Site/wwwroot/site.js` (`initPlayer`'s `render`), `CounterStrikeProgression.Site/wwwroot/site.css`
- Modify: `README.md` and `CLAUDE.md` (the player page and `Core/Heatmaps/` entries)

**Interfaces:**
- Consumes: `GET /api/players/{id}/fights` (Task 4), the `fights` and `snapshot` events (Task 3).
- Produces: `window.PlayerHeat.create(steamId)` returning `{ element, setOnline(online) }`. `element` is created once per page and reused by every `render`, so a refetch never rebuilds the canvas. `site.js` passes its event subscription in, as `create(steamId, on)`, using the existing `on(name, handler)`.

- [ ] **Step 1: Build the panel.** A heading with the map name and a live dot, two filter buttons (**This match**, **All time**) with `aria-pressed`, a square canvas drawn at 1024 px and scaled by CSS, a note line, and a hover or tap readout ("7 won, 1 lost"). Draw the radar, then each cell as a rounded square in the color and alpha from the global constraints. With `Calibrated == false`, show "This map has no radar yet." and no cells. With no cells, show "No fights here yet."

- [ ] **Step 2: Wire live updates.** On `fights`, add each entry whose `steamId` is this player and whose `map` is the panel's map to the matching cell when the scope is `match`, or `all` and the map matches, and redraw. On `snapshot` with a new map, refetch for the new map. Choose the starting scope from the stored choice for the player's online state, and store a choice when a button is pressed.

- [ ] **Step 3: Put it in the right column.** In `render`, the right column becomes a wrapper holding `heat.element` above `renderFeed(player)`. Call `heat.setOnline(online)` on each render. Narrow screens keep stacking through the existing `.player-grid` media rule.

- [ ] **Step 4: Check it by hand.** Run the site against a local store (`dotnet run` in `CounterStrikeProgression.Site`), post two batches to `/api/ingest` with kills by and on a test player on `de_dust2`, open `/players/<id>`, and confirm: green and red cells over the radar, a new posted kill appears without a refresh, **All time** and **This match** switch, a batch on `de_mirage` swaps the radar and empties **This match**, and the layout stacks at 390 px wide.

- [ ] **Step 5: Update the docs.** In `README.md`, describe the panel under the player page. In `CLAUDE.md`, add `FightGrid` to the `Core/Heatmaps/` entry and `MatchFights` and the `fights` event to the site description.

- [ ] **Step 6: Run the full suite** and see it pass.

- [ ] **Step 7: Commit** (`git commit -m "[minor] Show where a player wins and loses fights on their page, live"`).
