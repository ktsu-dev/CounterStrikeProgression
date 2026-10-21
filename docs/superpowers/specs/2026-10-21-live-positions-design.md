# Live positions on the site

Shows where players are right now: a live map of everyone on the server on the home page, and the player's own live (or last known) position on their player page's fights map. Also makes This match cells easier to see. It builds on the live player heatmap (`2026-10-21-live-player-heatmap-design.md`).

## Why

The owner watches the site on a second screen while playing. They want to see where they are on the fights map, and a live view of the whole server on the home page.

## What it looks like

### Home page: live map

- A radar of the current map with a marker for **every player on the server, humans and bots**.
- Each marker is a dot with a short wedge pointing the way the player faces.
- Humans: a brighter dot with their name next to it, linking to their player page.
- Bots: a smaller, dimmer dot, with the name shown on hover or tap only.
- Dead players aren't drawn until they respawn.
- The map moves every batch (about 2 seconds) with no refresh, and switches radar when the server changes map.
- On a map without a calibrated radar, the panel says so instead of drawing markers.
- Where it sits on the home page, and how it stacks on a narrow screen, is the builder's call. It sits with the live content, not below the fold.

### Player page: live position on the fights map

- A marker for that player (dot plus facing wedge) on the fights map, only when the map shown is the map the player is on.
- While alive, it moves every batch.
- While dead or disconnected, it stays at the last known position, faded, so it always shows the last known location.
- It sits above the fight cells and doesn't block hovering the cell under it.

### This match cells

- In **This match**, cells start at 0.6 opacity with even one fight and reach 0.95 at 4 fights.
- **All time** keeps its current curve (0.25 at 2 fights or fewer, up to 0.85 at 6).

## How it behaves

- The plugin adds each player's world position (X, Y), facing (yaw), and whether they're alive to the `LivePlayer` entries it already sends in every batch, for humans and bots. These are optional members defaulting to null, so a site still takes batches from a plugin that sends none.
- The site converts positions to radar pixels with `MapOverview` and pushes them over `/api/stream` as a `positions` event each batch, and serves the same data for a page's first load. Exact positions are public. The owner accepted that.
- Positions are never stored, only held in memory as the latest batch.
- Reading positions stays a cheap read of pawn state on the game thread, done in the existing batch collection, as `ReadPlayers` already reads stats there.

## Non-goals

- Trails, history, or replay of movement.
- Positions on the `/heatmaps` page.
- Team colors (the server runs deathmatch).

## Constraints

- Plugin and site ship together from this repo.
- `Endpoints` is at its CA1506 coupling limit, so new handlers go in their own classes.
- MSTest with semantic asserts, tabs, file-scoped namespaces, no global warning suppressions.

## Left to the builder

- The event and API shapes, the marker sizes, and the exact fade for a stale marker.
- Converting yaw to a radar angle (the radar's Y axis runs opposite to the world's).
