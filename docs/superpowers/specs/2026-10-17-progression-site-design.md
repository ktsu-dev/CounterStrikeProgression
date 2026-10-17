# CounterStrikeProgression site design

## Intent

The CS2 server's players want to see their progression outside the game. The site at
`cs2-progression.ktsu.dev` shows what is happening on the server right now, and lets anyone look up
the server-wide ranks and any player's full record. It's public and read-only. Players open it on a
second screen or a phone while they play, or afterwards to see how they're doing.

## Goals

- **Live match:** the current map and game mode, the player count, and a scoreboard of everyone on the
  server with their name, rank, kills, deaths, and XP earned this map. It updates as it happens.
- **XP feed:** skill shots and rank-ups scroll in as players earn them, for example
  "Katsu · Noscope · AWP +40". When the page opens, the feed already shows the last 50 entries.
- **Server ranks:** every human who has played, ordered by rank XP, with their rank, total XP, and when
  they were last on. Players on the server right now show "online". Each name links to their player page.
- **Player page:** one player's rank, a progress bar to the next rank, total XP, first seen and last
  seen, a table with one row per weapon (level, kills, headshot percentage, accuracy), and each weapon's
  challenge tiers, with finished tiers highlighted.
- **Every page is live.** XP and stats climb on screen while the player is playing, without a reload.
  Lifetime totals, weapon stats, and challenge tiers update within a few seconds, not on the plugin's
  database save.

## Non-goals

- No login and no accounts. Every page is public.
- No admin actions. The CSS-BANS panel at `cs2-admin.ktsu.dev` covers administration.
- No match history beyond the current map. The scoreboard resets on map change.
- No graphs or history over time in the first version.

## Anti-goals

- The site must never slow down or break the game server. If the site is down, slow, or
  misconfigured, the plugin carries on exactly as it does today.
- The site must never be able to change progression data. It reads the database with a read-only
  user.
- Nothing shown may reveal anything a player couldn't already see in game (no SteamIDs beyond what a
  Steam profile link needs, no IPs).

## Look

The game HUD style, chosen from mockups: dark backgrounds (#0d0f12, #15181d), a monospace face, orange
accents (#f2a33a) for headers and rank badges, and gold (#f2c94c) for highlights such as skill shots,
rank-ups, and finished challenge tiers. It matches the in-game floaties, whose highlight color is the
same gold. Online players are marked in green. It must work on a phone. The mockups are in
`.superpowers/brainstorm/` in this repo and are a guide, not a pixel spec.

## How it's built

These were decided in conversation.

- **An ASP.NET Core app in this repo**, built the ktsu way under `ktsu.Sdk`, alongside the plugin. Follow
  GitLfsCache's pattern (`C:/dev/ktsu-dev/GitLfsCache`): an ASP.NET project under plain
  `Microsoft.NET.Sdk` plus `ktsu.Sdk` with a `FrameworkReference`, `net10.0` only, and a container image
  through SDK container support (`EnableSdkContainerSupport`, the chiseled `aspnet:10.0` base, linux-x64
  and linux-arm64). The image is published to GHCR by CI, the way GitLfsCache's is.
- **Ranks and player pages read the MariaDB** the plugin already writes: `csp_players`, `csp_weapons`,
  and `csp_challenges` in the `cs2` database that SimpleAdmin also uses.
- **Live data comes from the plugin.** The database only holds lifetime totals, so the plugin sends
  events to the site as they happen, and the site pushes them on to open browsers.
- **It runs as its own pod in the homelab**, deployed by Terraform in `matt-edmondson/homelab`.

## Constraints

- The plugin builds against CounterStrikeSharp v1.0.376 on .NET 10 with `ktsu.Sdk`. Warnings are errors
  and code style is strict (see `CLAUDE.md`).
- All game state is read on the game thread. Anything that leaves the plugin must be captured there and
  sent from the thread pool.
- The Core library holds the rank ladder, the weapon catalog, the skill shot names, and the challenge
  definitions. The site uses them from Core rather than copying them, so names and thresholds can't
  drift. Core is `internal`, so it needs another `InternalsVisibleTo` for the site.
- The site project must not be packed to NuGet, like the plugin. See `Directory.Build.targets`.
- The release zip for the server stays plugin-only. The site ships as an image.

## Plugin side

- A new optional `Site` section in `PluginConfig`, with the site's URL and a shared token. With no URL
  set, nothing is sent, which is the default.
- The plugin sends two kinds of message:
  - **A live snapshot** of the server: map, game mode, the player list with each player's slot, name,
    SteamID, rank, kills, deaths, XP this map, and whether they're a bot. It's sent every couple of
    seconds and straight after a map change.
  - **Lifetime progress for online humans,** so player pages and the ranks page are live within
    seconds rather than waiting for the plugin's once-a-minute database save (the user's choice). This
    covers rank XP, rank, and each weapon's and challenge's rows. Sending only what changed since the
    last message keeps it small. The site overlays these on what it reads from the database, and the
    newer value wins.
  - **Feed events:** a skill shot (player, skill shot name, weapon, XP) and a rank-up (player, new rank).
    Weapon level-ups and finished challenge tiers are good candidates for the feed too, in the
    highlight color.
- Sending is fire and forget: a bounded queue drained on the thread pool, with a short timeout. When the
  queue is full or the site doesn't answer, messages are dropped and a failure is logged at most once a
  minute. A save never waits on the site.
- Kills, deaths, and XP this map need counting per player per map, since nothing tracks them today
  outside the database totals.
- The rank ladder, weapon level curve, and challenge tiers can be changed in the plugin's
  `Progression` settings. The plugin sends the values it's using, and the site draws progress bars and
  "next tier" targets from them. Until it has heard from the plugin, the site uses Core's defaults. This
  was the design author's call.

## Site side

- An endpoint for the plugin's messages, which rejects any request without the shared token. It's
  reachable inside the cluster only, never through the public route.
- The latest snapshot is kept in memory. The feed's history is kept so a page opened, or a site
  restart, still shows the last 50 entries.
- Browsers get live updates over a server push channel. Snapshots and feed entries go to every open
  page. The ranks page and player pages refresh what changed, so an online player's XP climbs.
- When no snapshot has arrived for a while, the live page shows the server as offline instead of a
  frozen scoreboard.
- Player pages are addressed by SteamID64, so a link survives a name change.

## Homelab side

- Terraform in the homelab repo next to `cs2.tf`: a Deployment, a ClusterIP Service, a public Traefik
  route at `cs2-progression.<domain>` behind crowdsec only (no oauth), the `.local` bypass route, and an
  Azure DNS record, like the other public sites.
- A read-only MariaDB user for the site with `SELECT` on the `csp_*` tables, created and kept in step by
  Terraform, with its password from `terraform.tfvars`.
- The plugin config's `Site` section is rendered by Terraform into the existing
  `CounterStrikeProgression.json` secret, pointing at the site's in-cluster Service.
- A homepage tile, like the CS2 server and admin panel tiles.
- The homelab `CLAUDE.md` gets an entry for the new file.

## Left to the builder

- The push channel (server-sent events or SignalR), and the wire format between the plugin and the site.
- Where the feed history lives (a site-owned table in the MariaDB, or the site's own storage), as long as
  it survives a site restart.
- Whether the pages are server-rendered with live patches or a small client-side app. Keep the
  front-end free of a build step if that's practical.
- Caching of ranks and player pages, sized for a 16-player server and a few hundred players in total.
- Page and endpoint names.
- Tests: MSTest for the parts that can be tested without a game server, such as snapshot and feed
  handling, ordering, and the token check.
