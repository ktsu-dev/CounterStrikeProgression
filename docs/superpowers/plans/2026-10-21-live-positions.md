# Live positions implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Live player positions on the site: a live map of everyone on the server on the home page, the player's live or last known position on their fights map, and more visible This match cells.

**Architecture:** The plugin adds position, facing, and alive state to each `LivePlayer` in its 2-second batch. The site turns the latest snapshot into radar pixels (`LivePositions`), publishes them as a `positions` event, and serves them at `/api/positions`. The home page draws a live map from that; the player page's fights panel draws the player's marker.

**Tech stack:** C# on .NET 10, ASP.NET Core minimal APIs, MSTest, vanilla JS and CSS in `wwwroot`.

**Spec:** `docs/superpowers/specs/2026-10-21-live-positions-design.md`

## Global constraints

- `LivePlayer` gains optional members only, appended last and defaulting to null: `float? X = null, float? Y = null, float? Yaw = null, bool? Alive = null`. A site still takes batches from a plugin that sends none (those players get no marker).
- Humans and bots both get positions. Positions are never stored.
- This match cell alpha: 0.6 at 1 fight, rising linearly to 0.95 at 4 fights and above. All time keeps 0.25 at 2 fights or fewer, up to 0.85 at 6.
- A stale marker (dead, or the player not in the latest snapshot) draws at 0.4 opacity at the last known position.
- `Endpoints` is at its CA1506 coupling limit: new handlers and helpers go in their own classes. No warning suppressions.
- MSTest with semantic asserts, tabs, file-scoped namespaces, the copyright header on new C# files, comments that describe the present code.

## Review focus

1. **An older plugin's batch** (no positions): no markers, no errors, and the `positions` event still names the map. Test in Task 1.
2. **A map without a calibrated radar:** `Calibrated == false` and no players in the view, and both pages say there's no radar. Test in Task 1.
3. **A position off the radar image:** left out, not clamped to the edge. Test in Task 1.
4. **The player page on a map other than the player's current one** (All time of an old map): no marker. Checked by hand in Task 2.
5. **The server going offline:** the home map keeps the last positions faded rather than going blank or throwing. Checked by hand in Task 3.

---

### Task 1: Send positions and serve them

**Files:**
- Modify: `CounterStrikeProgression.Core/Live/LiveContracts.cs` (`LivePlayer`)
- Modify: `CounterStrikeProgression/Site/SiteReporter.cs` (`ReadPlayers`)
- Create: `CounterStrikeProgression.Site/Live/LivePositions.cs`, `CounterStrikeProgression.Site/PositionEndpoints.cs`
- Modify: `CounterStrikeProgression.Site/Live/LivePublisher.cs`, `CounterStrikeProgression.Site/Program.cs`
- Test: `CounterStrikeProgression.Test/LivePositionTests.cs`

**Interfaces:**
- Produces: `internal sealed record PlayerPosition(int Slot, string? SteamId, string Name, bool IsBot, bool Alive, double X, double Y, double Facing);` where `X`/`Y` are radar pixels (0 to 1024), `SteamId` is the decimal string for humans and null for bots, and `Facing` is the radar angle in degrees, clockwise from screen up (0 = up, 90 = right).
- Produces: `internal sealed record PositionsView(string? Map, bool Calibrated, string? Radar, int Size, IReadOnlyList<PlayerPosition> Players);`
- Produces: `internal static class LivePositions` with `public static PositionsView From(ServerSnapshot? snapshot, string? radar)`: null snapshot gives `Map == null` and no players; an uncalibrated or unknown map gives `Calibrated == false` and no players; players with no position or off the radar are left out.
- Produces: the `positions` event, published by `LivePublisher` on every batch whose snapshot changed, with `LivePositions.From(batch.Snapshot, null)` as its payload (the page fetches the radar from the API).
- Produces: `GET /api/positions` returning `LivePositions.From(live.Snapshot, radarUrl)`, with the radar URL resolved through `RadarImages` as `HeatmapEndpoints` does.

- [ ] **Step 1: Write the failing tests** in `LivePositionTests`:
  - `From_ConvertsWorldToRadarPixels`: a calibrated `de_dust2` snapshot with one human at a known world position. One player, `X`/`Y` equal to `MapOverview.For("de_dust2").ToPixel(...)`, `SteamId` the decimal string, `IsBot == false`.
  - `From_FacingIsClockwiseFromUp`: yaw 90 (world +Y) gives `Facing == 0` and yaw 0 (world +X) gives `Facing == 90`, within 0.001.
  - `From_IncludesBotsWithoutSteamId`: a bot gets `IsBot == true` and `SteamId == null`.
  - `From_LeavesOutPlayersWithoutPositions`: a player with null `X` is left out, and the view still has `Map == "de_dust2"`.
  - `From_LeavesOutPositionsOffTheRadar`.
  - `From_UncalibratedMapHasNoPlayers`: `Calibrated == false`, empty players.
  - `From_NoSnapshotHasNoMap`.
  - `Ingest_PublishesPositions`: through `LivePublisher.Publish`, a batch with one positioned player yields a `positions` event whose single player matches.

- [ ] **Step 2: Run them and see them fail.** `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~LivePositionTests"`.

- [ ] **Step 3: Implement** the `LivePlayer` members, `LivePositions`, the `positions` event, and `PositionEndpoints.PositionsAsync` mapped in `Program` beside the other server APIs.

- [ ] **Step 4: Wire the plugin.** In `ReadPlayers`, read each player's pawn (`GameState.PawnOf`) for `AbsOrigin` (X, Y), `EyeAngles.Y` (yaw), and alive state (pawn present and `LifeState` alive), for bots and humans. A player with no pawn gets null position members.

- [ ] **Step 5: Build the solution and run the full suite**, then commit (`git commit -m "[minor] Send live player positions to the site"`).

### Task 2: The player's marker and stronger This match cells

**Files:**
- Modify: `CounterStrikeProgression.Site/wwwroot/playerheat.js`, `CounterStrikeProgression.Site/wwwroot/site.js` (subscribe to `positions`), `CounterStrikeProgression.Site/wwwroot/site.css`

**Interfaces:**
- Consumes: the `positions` event and `GET /api/positions` (Task 1), camelCase JSON.

- [ ] **Step 1: Alpha by scope.** This match: 0.6 at 1 fight, linear to 0.95 at 4 and above. All time unchanged.
- [ ] **Step 2: The marker.** When the panel's map equals the positions' map and the player is in the list, draw a dot with a facing wedge above the cells; alive moves with each event; dead, or missing from a later event, stays at the last known spot at 0.4 opacity. Load the starting position from `/api/positions` when the panel loads. No marker when the panel shows another map. The marker doesn't capture pointer events meant for the cells.
- [ ] **Step 3: Check it by hand** against a local site (in-memory stores, `Site__Token` set, batches posted to `:8081/api/ingest`): the marker moves with posted batches, fades on death, stays put when the player leaves the snapshot, disappears on All time for another map, and This match cells look stronger than All time. Screenshots beside the report.
- [ ] **Step 4: Run the full suite and commit** (`git commit -m "[minor] Show the player's live position on their fights map"`).

### Task 3: The live map on the home page

**Files:**
- Create: `CounterStrikeProgression.Site/wwwroot/livemap.js`
- Modify: `CounterStrikeProgression.Site/wwwroot/index.html`, `CounterStrikeProgression.Site/wwwroot/site.js` (`initLive`), `CounterStrikeProgression.Site/wwwroot/site.css`, `README.md`, `CLAUDE.md`

**Interfaces:**
- Consumes: the `positions` event and `GET /api/positions` (Task 1). Follows the factory shape of `playerheat.js` (`window.LiveMap.create(on)` returning `{ element }`).

- [ ] **Step 1: Build the panel** per the spec's "Home page: live map": radar (darkened to 35% like the heatmaps), every player as a dot with a facing wedge; humans brighter with a name label linking to `/players/<steamId>`; bots smaller and dimmer with the name on hover or tap; dead players not drawn; a "no radar" message for an uncalibrated map; the last positions kept, faded, when the server goes offline.
- [ ] **Step 2: Wire it** into `initLive` so it sits with the live content and stacks on narrow screens.
- [ ] **Step 3: Check it by hand** with posted batches: markers move, a map change swaps the radar, bots and humans look different, labels link correctly, and the layout works at 390 px. Screenshots beside the report.
- [ ] **Step 4: Update the docs** (README's home page and player page descriptions, CLAUDE.md's site description and the `positions` event).
- [ ] **Step 5: Run the full suite and commit** (`git commit -m "[minor] Add a live map of every player to the home page"`).
