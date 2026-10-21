# Live heatmap on the player page

A heatmap on each player's page that shows where that player wins and loses fights on the map being played, updates as kills happen, and can be narrowed to the current match.

## Why

The owner keeps their player page open on a second screen while they play. They want their challenges, the live feed, and a heatmap on screen together, so a glance mid-game tells them where on the map they're doing well and where they're doing badly. Today the heatmap lives on its own page (`/heatmaps`), shows kills or deaths one layer at a time, and only changes when the plugin writes kills to the database, about once a minute.

## What it looks like

The player page keeps its two columns:

- **Left column:** unchanged. The tabs (Next up, Weapons, Feats, History, Maps, Rivals), opening on Next up as now.
- **Right column:** the heatmap on top, the existing live feed underneath. On a narrow screen the columns stack, as the page already does.

The heatmap panel shows:

- The radar of the map, from the existing `/radar/<map>.png`.
- One layer, colored per area by **fights won against fights lost** there. Green is where the player wins most fights, red where they lose most, and grey is about even.
- A filter with two choices: **This match** and **All time** (on this map).
- The map's name, and a small sign that it's updating live.
- Hovering (or tapping) an area shows its counts, such as "7 won, 1 lost".

The mockup the owner chose is in `.superpowers/brainstorm/` (`layout-v2.html` and `best-worst.html`), as a reference for layout only.

## How it behaves

### What counts as a fight

- A **won** fight is a kill by the player, placed where the player stood when they fired.
- A **lost** fight is the player's death, placed where the player was when they died.
- **Fights against bots count**, both ways. (The server runs 10 bots, so most of a player's fights are against bots.)
- Suicides and world deaths don't count as either.

### Color and confidence

- An area's color comes from the balance of won and lost fights in it, not the raw count, so a busy area and a quiet one with the same balance look alike.
- Areas with only one or two fights stay faint, so a single death doesn't paint a spot solid red. The exact threshold and curve are the builder's call, tuned against real data.
- Areas should be about the size of a callout (a site, a corridor, a doorway), not single pixels. The existing grid is 8 px cells on a 1024 px radar, which is too fine for a balance per area. A coarser grid, smoothing, or both are the builder's call. The intent is that a glance reads as a few clear green and red regions.

### The filter

- **This match** means since the current map started. In deathmatch a match is one map.
- **All time** means every stored fight for this player on this map. It's limited by what `csp_kills` keeps (the latest `MaxPointsPerMap`, default 30,000, per map across all players), and that's acceptable.
- While the player is online, the panel defaults to **This match**. While offline, it defaults to **All time** and shows the map the player last played. Which map that is and how it's found is the builder's call.
- A choice is remembered per browser, using browser storage, guarded as the site's other pages guard it. It's stored separately for online and offline, and shared across every player page, so a choice of **All time** made while offline doesn't stop the panel opening on **This match** mid-game. (Design author's call.)

### Live updates

- While the page is open, each fight the player is in appears on the heatmap within about the plugin's batch interval (`Site.IntervalSeconds`, 2 seconds by default). The page never needs a refresh.
- When the server changes map, the panel switches to the new map's radar and starts **This match** empty.
- Today kills reach the database only on the plugin's save timer (`SaveIntervalSeconds`, 60 by default), so the live path can't come from the database. The plugin sends the kills it records to the site in its existing live batches (`LiveBatch`, every 2 seconds), and the site pushes them to open pages over its existing event stream (`/api/stream`), the same way the feed and progress updates already travel.
- The database write stays as it is, so the `/heatmaps` page and **All time** keep working.
- The event stream is public, so anyone with the site open sees each fight's position within a batch. The owner accepted that: there's no delay and no private stream.

### This match after a site restart

The site holds the current match's fights in memory. If the site restarts mid-match, **This match** must still show the fights from before the restart. The plugin's batch carries when the current map started, and the site rebuilds the match from `csp_kills` (`at` on or after that time) plus whatever arrives live afterward. A kill that's briefly missing or counted twice around a restart is acceptable. A match that shows nothing until the map changes is not.

## Goals

- See at a glance, mid-game, where on the current map the player wins and loses fights.
- Challenges, feed, and heatmap visible together on one screen without switching tabs.
- Live, with no refresh.

## Non-goals

- Changing the `/heatmaps` page. It keeps its kills and deaths layers. Offering the green to red view there too is a later, separate change.
- Other players' heatmaps on someone's page, or comparing two players.
- Weapon, round, or time filters beyond **This match** and **All time**.
- Storing anything new about past matches. "This match" is only ever the current one.

## Anti-goals

- A heatmap that looks alarming from tiny samples (one death turning an area red).
- Slowing the game server. Recording and sending kills stays off the game thread, as the existing buffer and sender already ensure.
- Breaking older pairings. A site must accept batches from a plugin that doesn't send kills or a map start (both optional, as `Recaps` and `Stats` already are), and the plugin and site ship together from this repo, so no other compatibility is needed.

## Constraints

- The plugin and the site release together (one version, `cs2_plugin_versions.progression` in the homelab). The site's database user is read-only and has `SELECT` on `csp_kills` already, so no new grant is needed unless a new table is read.
- The site reads the database only through `IKillMapStore` and `IProgressionStore`, never writes.
- `Endpoints` is at its CA1506 coupling limit. New handlers go in their own classes, as `HeatmapEndpoints`, `RivalEndpoints`, and the others do.
- The radar calibration (`MapOverview`) exists only for official maps. On a map without one, the panel says the map has no radar rather than drawing uncalibrated points.
- Tests use MSTest with semantic asserts, as the rest of the suite does.

## Left to the builder

- The shape of kills in the batch and on the event stream, and how the page merges them.
- The grid size, smoothing, color scale, and the confidence threshold for fading.
- How the panel looks at narrow widths, within the stacking the page already does.
- Whether the player page's existing `progress` refetch and the new kill events share any plumbing.
- The API shape for the panel's initial load (a new parameter on `/api/heatmaps` or a new route).
