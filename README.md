# ktsu.CounterStrikeProgression

> Matchmaking-style ranks, weapon levels, and skill shot challenges for CS2 community servers.

[![License](https://img.shields.io/github/license/ktsu-dev/CounterStrikeProgression.svg?label=License&logo=nuget)](LICENSE.md)
[![GitHub Release](https://img.shields.io/github/v/release/ktsu-dev/CounterStrikeProgression?label=Release&logo=github)](https://github.com/ktsu-dev/CounterStrikeProgression/releases)
[![GitHub commit activity](https://img.shields.io/github/commit-activity/m/ktsu-dev/CounterStrikeProgression?label=Commits&logo=github)](https://github.com/ktsu-dev/CounterStrikeProgression/commits/main)
[![GitHub contributors](https://img.shields.io/github/contributors/ktsu-dev/CounterStrikeProgression?label=Contributors&logo=github)](https://github.com/ktsu-dev/CounterStrikeProgression/graphs/contributors)
[![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/ktsu-dev/CounterStrikeProgression/dotnet.yml?label=Build&logo=github)](https://github.com/ktsu-dev/CounterStrikeProgression/actions)

## Introduction

`ktsu.CounterStrikeProgression` is a [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) plugin that gives players on a small community server a reason to play everything, not just their favourite gun. CS2 never awards Steam profile XP on community servers, so the progression lives on the server.

Players climb an up-only rank ladder from Silver I to Global Elite. Every weapon has its own level and a permanent ladder of tiered challenges. Around 50 skill shots, from jumpshots and wallbangs to flicks and efficient reloads, pay noticeably more than plain kills.

## Features

- **Up-only ranks**: 12 matchmaking-style ranks with five stages each, from Silver · I to Global Elite · V. Numbered ranks such as Silver I to IV are one rank here, because the stages number it. The first stage takes 5,000 XP, each one after costs about 7.7% more, and Global Elite · V takes about 5 million XP in total. Nobody loses progress for a bad night.
- **Weapon levels**: every weapon, grenade, the knife, and the Zeus levels up to 40, and each level-up pays rank XP.
- **Mastery stars**: past level 40, weapon XP keeps counting toward mastery stars, one for each 40 levels' worth of XP. They're cosmetic and pay no rank XP, but they show next to the weapon on the panel, in `!stats` and `!top`, and on the site, and each one is announced.
- **Chickens**: every weapon has a Chickens challenge (1, 5, 15, 40, and 100 chickens), and each chicken pays 2 XP to the weapon and to rank. Twelve chicken skill shots sit on the weapon ladders too, from Nugget (headshot a chicken) and Fowl play (through a wall) to Fried chicken, Rotisserie, and Crossed the road (from long range).
- **Weapon ladders**: Kills, Headshots, and Hits, plus one challenge for every skill shot the weapon can earn, each with five tiers. Skill tiers pay more than volume tiers.
- **Skill shots**: about 50, covering movement, distance, wallbangs and smokes, scopes, utility and melee, timing and aim, multi-kills, and round situations.
- **Daily and weekly contracts**: three daily contracts and one weekly, such as "Get 20 kills with SMGs" or "Get 2 kills through walls", pay 500 and 2,500 rank XP. The picks come from the UTC date, so everyone gets the same ones, and they reset at midnight UTC and on Monday. `!contracts` opens them in the panel, and the site's player page shows your progress.
- **Server records**: a hall of fame of single-moment bests: the longest kill with each gun class, the longest no-scope, the most kills in one life, the best accuracy over one life (from 30 shots), and the most skill shots in one map. Beating one is announced to the server, to the site's feed, and to Discord, naming the holder it was taken from, and the site's Leaderboards page lists every record. A record has to be beaten outright, and kills and skill shots against bots don't count unless `RecordsCountBots` is on. `RecordsEnabled` turns them off.
- **Bounty on the leader**: the human player on the longest current life streak, once it reaches 5 kills, carries a bounty worth 50 rank XP per kill in the streak. Whoever ends the streak collects it, and chat hears when a bounty is placed, every 5 kills after, and when it's collected. Bots never carry one, and `BountyStartKills` and `BountyXpPerKill` tune it (0 turns it off).
- **Bots count, at a reduced rate**: rank XP against bots is scaled down, while weapon levels and ladders still move, so practice against bots isn't wasted.
- **Featured weapon of the day**: one gun class (pistols, SMGs, rifles, snipers, shotguns, or machine guns) earns double weapon XP each UTC day. Challenge ladders move at the usual pace. The pick comes from the date, so the panel overview and the site's live page always agree. `FeaturedWeaponMultiplier` sets the multiplier, and 1 turns it off.
- **Welcome-back bonus**: a player back after 7 or more days away earns double rank XP until they've had 2,000 bonus XP. The join message and the panel overview say how much is left, and what's left is saved, so it carries over between visits. `WelcomeBackDays` sets the gap, and `WelcomeBackXp` the bonus, where 0 turns it off.
- **Nemesis and rivals**: kills between human players are counted per pair. Your nemesis is whoever has killed you most, at least 3 times, and killing them pays 50 bonus rank XP with a floatie. `!stats` names your nemesis and favourite victim, and the site's player page has a Rivals tab with every head-to-head. `NemesisMinimumKills` and `NemesisKillXp` set the numbers, where 0 XP turns the bonus off. Bots never count.
- **In-game panel**: `!rank` opens a side panel to the right of the view. It falls back to a center-screen card and then to chat if a way of drawing fails.
- **XP floaties**: every XP gain floats up as "+XP" text from the center of your screen. Only you see your own. Quick gains add up in one floatie, and skill shots, tiers, and level-ups are named under the number in gold. A `World` mode floats them above the target instead.
- **Daily play streak**: every UTC day in a row with any rank XP adds to the streak. Each new day of a streak is told to the player in chat, and the streak shows on the panel overview and the player page. It's worked out from the daily XP rows already stored, so nothing new is saved.
- **Feats**: twelve really difficult feats, such as Global Elite, Millionaire, Weapon master, Full ladder, Collector, Chicken overlord, Arsenal (every gun to level 10), Polymath (a skill shot tier on 20 weapons), and Regular and Devoted for 7 and 30 day play streaks. They are worked out from the stored progress, announced to the server when earned, and shown as medallions on the progression site.
- **Rank tags**: everyone's rank, such as `[GN III]`, shows as their scoreboard clan tag and before their name in chat.
- **Announcements**: rank-ups, finished tiers, mastery stars, and every fifth weapon level go to the whole server.
- **Map recap**: when the map ends, each player gets one chat line with the XP, kills and deaths, skill shots, levels, and tiers they earned, and their best moment. The recap also goes to the progression site, which shows it as "Last session" on the player page, and a player who leaves early still gets one there. `AnnounceMapRecap` turns the chat line off.
- **Durable storage**: progress is kept in MariaDB or MySQL in `csp_` tables, saved off the game thread, and retried after a failed save without double counting.
- **Configurable**: every XP value, multiplier, and detection threshold is a setting.

## Installation

1. Install [Metamod:Source](https://www.sourcemm.net/downloads.php?branch=dev) 2.0 and [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp/releases) v1.0.376 or newer, with its .NET runtime.
2. Download `CounterStrikeProgression-<version>.zip` from the [releases](https://github.com/ktsu-dev/CounterStrikeProgression/releases).
3. Extract it into the server's `game/csgo/` directory. It contains `addons/counterstrikesharp/plugins/CounterStrikeProgression/`.
4. Start the server once, then edit `addons/counterstrikesharp/configs/plugins/CounterStrikeProgression/CounterStrikeProgression.json`.
5. Restart the server, or run `css_plugins reload CounterStrikeProgression`.

With no database host set, progress is kept in memory and lost when the server restarts.

## Usage Examples

### Player commands

| Command | Effect |
|---|---|
| `!rank`, `!xp`, `!progress` | Open or close the progress panel |
| `!contracts` | Open the progress panel on today's contracts |
| `!top` | Print the 10 players with the most rank XP |
| `!top <weapon>` | Print the 10 players with the most XP with one weapon, such as `!top ak` or `!top awp` |
| `!next` | Print your three challenges closest to their next tier |
| `!feats` | Print every feat, with the ones you've earned and how close you are to the rest |
| `!stats [name]` | Print your rank, XP, kills, headshot share, best weapon, feats, nemesis, and favourite victim, or a connected player's |
| `!site` | Print the link to your page on the progression site |

When a player first spawns, they get a welcome line with their rank, a link to their page on the site (when `Site.PublicUrl` is set), and the commands. `"WelcomeMessage": false` turns it off.

While the panel is open, W and S scroll, A and D move between the overview, the contracts, and each weapon, and R closes it. It also closes when you take damage or die.

### Admin commands

All three require `@css/root`.

| Command | Effect |
|---|---|
| `css_csp_status` | Print the plugin version, the store in use, how many players have loaded, and whether the match is in warmup or free-for-all |
| `css_csp_grant <player> <xp>` | Add rank XP, or take it away with a negative amount. Taking XP away also takes away the ranks it bought, and it stops at 0 |
| `css_csp_reset <player>` | Delete every rank, weapon, and ladder row the player has. Run it twice within 15 seconds to confirm, because it can't be undone |

`<player>` is `#userid`, part of a connected player's name, or a SteamID64, which also works for players who aren't on the server. Both corrections are written to the database straight away and logged with the admin, the player, and the XP and rank before and after.

### Basic configuration

```json
{
  "Database": {
    "Host": "cs2-mariadb.cs2.svc.cluster.local",
    "Port": 3306,
    "Database": "cs2",
    "User": "cs2",
    "Password": "change-me"
  },
  "Progression": {
    "BotXpMultiplier": 0.25,
    "KillXp": 10,
    "RareSkillShotXp": 25
  },
  "Panel": {
    "Mode": "Auto"
  }
}
```

Settings left out keep their defaults.

## Advanced Usage

### Tuning the economy

`Progression` holds every number the plugin uses:
- **XP per event:** hits, kills, headshots, assists, and skill shots by rarity.
- **Bonuses:** weapon level-ups and finished tiers.
- **Curves:** the rank and weapon level costs.
- **Ladders:** the five tier thresholds for each challenge kind.
- **Detection thresholds:** such as `MovingTargetSpeed`, `QuickscopeSeconds`, `FlickDegrees`, and `EfficientReloadFraction`.
- **Record minimums:** a kill must reach `RecordMinimumDistanceShare` (half) of its class's Long shot distance, or `RecordMinimumNoScopeDistance` for a no-scope, before it can set a record, and a life or map must reach `RecordMinimumLifeKills`, `RecordAccuracyMinimumShots`, or `RecordMinimumMapSkillShots`, so the first records aren't ordinary moments.

### Panel placement

`Floaties` picks `World` (the default) or `Hud` mode, turns the floating XP text on or off, and sets how long it lasts, how far it rises, how quickly gains merge, how many one player can have at once, its colors, how big it's drawn by distance (`SizePerUnitOfDistance`, `MinWorldUnitsPerPixel`, `MaxWorldUnitsPerPixel`), and the outline (`StrokeWidthPixels`, `StrokeColor`, `StrokeOpacity`). In `Hud` mode a floatie appears just above the crosshair and stays at that spot as you move, and new XP adds to it while your crosshair stays within `HudReuseDegrees` of where it appeared. In `World` mode a floatie appears above the target, or in front of you for XP with no target. A floatie's size is set when it appears and doesn't change after, and it has a half-opacity black outline for contrast.

`Panel` sets the draw mode (`Auto`, `WorldText`, `CenterHtml`, or `Chat`), how many lines each mode shows, and the side panel's font size, scale, color, and offsets. The default placement suits a 16:9 display. CS2 has no client-predicted anchor for world text, so the side panel trails slightly behind fast turns.

### Rank tags

`RankTags` turns the scoreboard clan tag (`ClanTag`) and the chat prefix (`ChatPrefix`) on or off. Both are on by default. The clan tag replaces any Steam group tag the player has. The chat prefix is added to the name in the chat message itself, so gags, team chat, and dead chat work as before, and CS2-SimpleAdmin's gag hook is unaffected.

### Progression site

`Site` sends the server's live state to the progression site: the map and mode, the scoreboard, the feed of skill shots, rank-ups, level-ups, and finished tiers, and each player's changed progress. With an empty `Url`, the default, the plugin sends nothing.

```json
{
  "Site": {
    "Url": "http://cs2-progression-site.cs2.svc.cluster.local:8081/api/ingest",
    "PublicUrl": "https://cs2-progression.example.com",
    "Token": "change-me",
    "IntervalSeconds": 2,
    "QueueLimit": 20,
    "FullResendEvery": 15
  }
}
```

- **`Url`:** the site's ingest endpoint. A URL that isn't absolute http or https is logged and ignored.
- **`PublicUrl`:** the address players reach the site at, which the welcome line, `!site`, and `!stats` link to. Empty, the default, shows no link.
- **`Token`:** the shared token the site checks on every batch.
- **`IntervalSeconds`:** the seconds between batches. Values under 1 count as 1.
- **`QueueLimit`:** how many batches wait to send. When the site is slow or down, the oldest is dropped, so the game never waits on it.
- **`FullResendEvery`:** how many batches pass between full resends of every connected player's progress, which repairs the site after a dropped batch or a restart. 0 turns it off.

Sending runs on the thread pool with a 3 second timeout, and a failure is logged at most once a minute.

### Discord announcements

`Discord` posts the server's big moments to a Discord channel through a webhook: server records, earned feats, epic skill shots, weapon mastery stars, and rank-ups from Gold Nova up, plus a weekly digest every Monday. It's off by default, because it sends player names and progress outside the server. With an empty `WebhookUrl`, nothing is posted.

```json
{
  "Discord": {
    "WebhookUrl": "https://discord.com/api/webhooks/<id>/<token>",
    "Username": "CS2 Progression",
    "Records": true,
    "Feats": true,
    "EpicSkillShots": true,
    "MasteryStars": true,
    "RankUps": true,
    "MinimumRank": "Gold Nova",
    "WeeklyDigest": true,
    "WeeklyDigestPlayers": 5,
    "QueueLimit": 50
  }
}
```

- **`WebhookUrl`:** the channel's webhook URL, from the channel's Integrations settings in Discord. It must be https, and because it holds the webhook's secret it is never written to the log.
- **`Username`:** the name posts appear under. Empty keeps the name set on the webhook.
- **`Records`, `Feats`, `EpicSkillShots`, `MasteryStars`, `RankUps`:** which moments to post.
- **`MinimumRank`:** the lowest rank whose rank-ups are posted, either a whole rank such as `Gold Nova` (all of its stages) or one stage such as `Gold Nova · III`. A name that matches neither counts as `Gold Nova`.
- **`WeeklyDigest`:** whether to post a digest of the week before at the first map start after Monday 00:00 UTC: the top climbers by rank XP earned, the rank-ups from `MinimumRank` up, and the weekly contract that starts now. A row in `csp_digests` records each week posted, so a restart never posts it twice, and a week nobody played posts nothing.
- **`WeeklyDigestPlayers`:** how many top climbers the digest lists, from 1 to 10.
- **`QueueLimit`:** how many lines wait to post. When Discord is slow or down, the oldest is dropped.

Posting runs on the thread pool, at most one post a second. Lines that arrive meanwhile are joined into the next post, a post Discord rate limits is retried after the wait it asks for, and a failure is logged at most once a minute. Player names are escaped, and posts never ping anyone.

### Kill heatmaps

`Heatmaps` records where kills happen, so the site can draw them over each map's radar at `/heatmaps`, for the whole server or one player (where they got their kills, or where they died). Only kills with a human on either end are kept, each map keeps only its latest `MaxPointsPerMap`, and the oldest are deleted as new ones arrive, so the `csp_kills` table stays a fixed size.

```json
{
  "Heatmaps": {
    "Enabled": true,
    "SampleEvery": 1,
    "MaxPointsPerMap": 30000,
    "QueueLimit": 5000
  }
}
```

- **`Enabled`:** whether kill positions are recorded.
- **`SampleEvery`:** keep one kill in this many on each map. 1 keeps every kill.
- **`MaxPointsPerMap`:** how many kills each map keeps. Values under 100 count as 100.
- **`QueueLimit`:** how many kills wait to be written while the database is down. Past this, the oldest are dropped.

Kills are written in one batch per save interval, on the thread pool. The site sends cell counts on a 128 by 128 grid, never exact positions. The official maps line up with the game's radar from each map's overview file; any other map gets a square fitted around its kills. Radar images aren't shipped, because they're Valve's art: put a 1024 by 1024 PNG at `<DataPath>/radar/<map>.png` (for example `/data/radar/de_dust2.png`, exported from the game's `panorama/images/overheadmaps/<map>_radar_psd` texture with Source 2 Viewer) and the site draws the heat over it. Without one, the page draws an outline from where people fight.

#### The site

The site is a small ASP.NET Core app, published as the image `ghcr.io/ktsu-dev/counterstrikeprogression-site:<version>` with each release (and as `latest`). It shows the live scoreboard and feed on `/`, the rank ladder on `/ranks`, leaderboards for every weapon (by level) and every skill shot (by count), plus the most feats and the rarest skill shots, on `/leaderboards`, a guide to every skill shot with its rarity, weapons, XP, tiers, and how many players have earned it on `/skillshots`, kill heatmaps on `/heatmaps`, and each player's profile on `/players/{steamId}` (a SteamID64), with their XP feed and suggested next challenges. Ranks show as badges: the color and emblem mark the rank's family, and the pips along the bottom fill with the stage. Pages update live over server-sent events, and a server counts as offline after a few seconds without a batch.

The image listens on two ports:

- **8080 (public):** `/`, `/ranks`, `/leaderboards`, `/skillshots`, `/heatmaps`, `/players/{steamId}`, `/radar/{map}.png`, `/api/heatmaps`, `/api/live`, `/api/ranks`, `/api/leaderboards`, `/api/skillshots`, `/api/players/{id}`, `/api/stream`, and `/healthz`. Route this port to visitors.
- **8081 (ingest):** `POST /api/ingest` with the header `Authorization: Bearer <token>`, and `GET /metrics` for Prometheus. Keep this port inside your network, because only the plugin and your scraper need it.

Point the health probe at `/healthz` on port 8080. Port 8081 answers nothing but the ingest and metrics routes, so a probe there always fails.

`/metrics` has these series:

- `csp_site_server_online`: 1 when a batch arrived within the offline window.
- `csp_site_players_online{kind="human"|"bot"}`: players in the last batch.
- `csp_site_last_batch_age_seconds`: seconds since the last batch, or since the site started when none has arrived. Alert on this while the game server is up.
- `csp_site_ingest_batches_total` and `csp_site_ingest_failures_total{reason="unauthorized"|"too_large"|"bad_request"}`: batches applied and turned away.
- `csp_plugin_send_failures_total`, `csp_plugin_batches_dropped_total`, and `csp_plugin_save_failures_total`: the plugin's own counts since it loaded, as of its last batch. They reset when the plugin reloads, and are absent until a batch carries them.

The site reads progress from the plugin's database and never writes to it. Give it a user with `SELECT` on `csp_players`, `csp_weapons`, `csp_challenges`, `csp_seasons`, `csp_records`, `csp_rivals`, and `csp_kills` and nothing else. Until it has `csp_rivals`, player pages still load but show no rivals. The live state and the feed history live in the site's memory and in a file under `/data`, so mount a volume there if the feed should survive a restart. The image runs as the non-root user with UID 1654, so that user must be able to write to the volume (in Kubernetes, set the pod's `fsGroup` to 1654).

Configure the site with environment variables:

| Variable | Default | Meaning |
|---|---|---|
| `Site__Database__Host` | empty | The database host. Empty keeps progress in memory, which starts empty. |
| `Site__Database__Port` | `3306` | The database port. |
| `Site__Database__Database` | `cs2` | The database name. |
| `Site__Database__User` | `cs2` | The read-only database user. |
| `Site__Database__Password` | empty | The database password. |
| `Site__Database__RequireTls` | `false` | Whether the database connection must be encrypted. When it's `false`, the site still uses TLS if the server offers it. |
| `Site__Token` | empty | The bearer token the plugin sends. Empty rejects every batch. |
| `Site__DataPath` | `/data` | The folder for the feed history file, and for radar images in its `radar` folder. It must be writable by UID 1654, the user the image runs as. |
| `Site__OfflineAfterSeconds` | `15` | Seconds without a batch before the server counts as offline. |
| `Site__FeedLength` | `50` | How many feed entries to keep and show. |

Point the plugin's `Site.Url` at port 8081 and set `Site.Token` to the same value as `Site__Token`.

## API Reference

### Skill shots

| Group | Skill shots |
|---|---|
| Movement | Moving target, Airborne target, Jumpshot, Air to air, Run and gun, Crouch kill, Ladder kill, Silent kill, Landing shot, Water kill |
| Distance | Long shot, Point blank, Pistol sniper |
| Penetration and visibility | Wallbang hit, Wallbang, Double wallbang, Through the smoke, Blind kill |
| Scope | No-scope, Quickscope, Flying no-scope, Scoped on the move |
| Utility and melee | Grenade kill, Fire kill, Grenade impact, Knife kill, Backstab, Zeus kill, Flash assist, Long flash |
| Timing and aim | Quick switch, Fresh mag, Last bullet, One tap, Flick, Spray transfer, Efficient reload, Marksman, Sharpshooter, Headhunter |
| Multi-kill and situation | Double kill, Triple kill, Rampage, Untouchable, 10-kill streak, 20-kill streak, Collateral, Opening kill, Trade, Revenge, Last stand, Bounce back, Defuse denial, Clutch |

### Storage tables

| Table | Contents |
|---|---|
| `csp_players` | Steam ID, name, rank XP, rank, first and last seen |
| `csp_weapons` | Steam ID, weapon, weapon XP, level, kills, headshots, hits, shots |
| `csp_challenges` | Steam ID, weapon, challenge, count, tier reached |
| `csp_seasons` | Steam ID, season start, rank XP earned in the season |
| `csp_kills` | Map, killer and victim Steam IDs (0 for a bot), weapon, headshot, both positions, time |
| `csp_rivals` | Killer's Steam ID, victim's Steam ID, kills |
| `csp_records` | Record id, the holder's Steam ID, name, and value, when it was set, and the holder it was taken from |
| `csp_schema` | The schema version |

Skill shot ids double as challenge ids in `csp_challenges`, so an id is never renamed once released.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.
