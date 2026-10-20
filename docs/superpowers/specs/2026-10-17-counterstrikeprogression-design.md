# CounterStrikeProgression design

A CounterStrikeSharp plugin that gives players on a small community CS2 server a reason to play everything, not just their favourite gun. It replaces UmbrellaRanked on the ktsu.dev deathmatch server.

Steam profile XP and weekly drops can't be earned on community servers, so all progression lives on the server.

## What the user decided

- **Rank:** matchmaking-style rank names, Silver I to Global Elite, that only go up as people play. Nobody loses progress for a bad night.
- **Per-weapon progression:** every weapon levels up separately.
- **Per-weapon challenges:** a permanent ladder of tiered challenges for each weapon. Skill challenges pay more than volume challenges.
- **XP sources:** XP is earned in the moment for hits, kills, headshots, and skill shots. Finishing a challenge tier and levelling a weapon also pay XP. Everything feeds one number, rank XP.
- **Skill shots are the heart of it.** Moving targets, airborne targets, shooting while airborne, efficient reloads, accuracy, and "heaps" more.
- **Rewards:** XP toward rank plus a chat announcement. Nothing unlocks a gameplay change.
- **Bots** earn XP at a reduced rate.
- **Progress screen:** an in-game side panel on the right of the screen, opened with a command.
- **Built the ktsu way:** ktsu.Sdk, MSTest, the ktsu GitHub workflow, commit-tag versioning, and ktsu libraries where they fit.

## Goals

- A steady trickle of XP during play, with skill shots worth noticeably more than plain kills.
- A long, broad set of skill shots (about 50), each detectable from what CS2 gives a server plugin.
- A permanent ladder per weapon, so there's always a next goal on every gun.
- Progress that survives restarts, map changes, and disconnects.
- No noticeable server cost in heavy firefights.

## Non-goals

- Skill-based matchmaking ratings, or anything that goes down.
- Unlocks, perks, or anything that changes gameplay.
- A web page. Progress is in game only.
- Importing UmbrellaRanked's history. Everyone starts fresh.

## Anti-goals

- **Rewarding one-gun farming.** If grinding AK kills is the best XP, the plugin has failed even if it works. That's why skill shots and their ladders pay more.
- **A panel that can get you killed.** The panel closes the moment you take damage.
- **Losing progress** to a crash or a bad write.

## Constraints

- CounterStrikeSharp v1.0.376 on .NET 10. The plugin is a folder of DLLs under `addons/counterstrikesharp/plugins/CounterStrikeProgression/`.
- CS2 removed the viewmodel entity in August 2025, which broke every worldtext screen-menu library. No maintained library draws a side panel.
- Storage is the server's existing MariaDB, shared with CS2-SimpleAdmin and UmbrellaRanked. Tables are prefixed `csp_`.
- ktsu.Sdk treats warnings as errors, requires XML docs on public APIs, central package versions, `Ensure.NotNull`, and direct references for every package used.

## How it's built (calls made by Claude, marked for review)

### Projects

| Project | Contents |
|---|---|
| `CounterStrikeProgression.Core` | Everything that can be tested without a game server: weapons, ranks, the XP economy, the skill shot catalogue and detection rules, challenge ladders, round and accuracy tracking, panel layout, and the storage interface with a MySQL implementation. No CounterStrikeSharp reference. |
| `CounterStrikeProgression` | The plugin. It reads CS2 events and player state into plain snapshots for Core, draws the panel, handles commands, and runs the save timers. |
| `CounterStrikeProgression.Test` | MSTest tests for Core. |

*Claude's call:* splitting Core out keeps almost all the logic testable, because CounterStrikeSharp types can't be loaded outside the server.

*Claude's call:* the plugin's assembly name is overridden to `CounterStrikeProgression`, because CounterStrikeSharp loads `plugins/<Name>/<Name>.dll` and ktsu.Sdk would otherwise call it `ktsu.CounterStrikeProgression`.

### ktsu libraries

- **ktsu.Semantics.Strings:** `WeaponKey` and `SkillShotId` are semantic strings, so a weapon can't be passed where a skill shot is expected.
- **ktsu.Extensions:** general collection and string helpers where they shorten code.
- **Polyfill:** required by ktsu.Sdk.

*Claude's call:* MySqlConnector and Dapper for MariaDB, because no ktsu library does SQL. ktsu.Invoker and ktsu.IntervalAction aren't used, because CounterStrikeSharp's own `Server.NextFrame` and `AddTimer` already run on the game thread.

### Rank

18 ranks: Silver I to IV, Silver Elite, Silver Elite Master, Gold Nova I to III, Gold Nova Master, Master Guardian I and II, Master Guardian Elite, Distinguished Master Guardian, Legendary Eagle, Legendary Eagle Master, Supreme Master First Class, and Global Elite.

*Claude's call:* reaching the next rank costs 1,000 XP at first, and each rank costs 25% more than the one before. Global Elite needs about 174,000 XP in total, which works out to well over 100 hours. The base cost and growth rate are settings.

### XP economy

Every number below is a setting. *Claude's calls:*

| Source | XP |
|---|---|
| Damaging hit (once per bullet) | 1 |
| Kill | 10 |
| Headshot kill | +5 |
| Assist | 3 |
| Common skill shot | 10 |
| Rare skill shot | 25 |
| Epic skill shot | 60 |
| Weapon level-up | 50 + 10 × new level |
| Volume challenge tier | 100 × tier |
| Skill challenge tier | 250 × tier |

- **Bots:** XP from damaging, killing, or skill-shotting a bot is multiplied by 0.25. *Claude's call:* bot events still count fully toward weapon ladders and weapon levels, so practice against bots moves your ladders while rank climbs slower.
- **Warmup:** no XP and no progress.
- **Team damage and suicides:** never pay.
- **Weapon XP:** XP earned with a weapon also goes to that weapon's level. Reaching the next level costs 300 + 100 × the current level, up to level 40.
- **Utility:** grenades, the knife, and the Zeus count as weapons with their own levels and ladders.

### Skill shots

Each skill shot has an id, a name, a rarity, the weapon classes it applies to, and whether it fires on a hit, a kill, or a stat event (a reload, or the end of a life).

*Claude's call on the catalogue.* Moving target means faster than 200 units per second, and distance is measured in game units:

**Movement**
- **Moving target** (Common): hit a target moving fast.
- **Airborne target** (Rare): hit a target in the air.
- **Jumpshot** (Rare): hit while you're in the air.
- **Air to air** (Epic): kill an airborne target while airborne.
- **Run and gun** (Common): kill with a rifle or sniper while moving fast.
- **Crouch kill** (Common).
- **Ladder kill** (Epic).
- **Silent kill** (Common): kill while shift-walking.
- **Landing shot** (Rare): kill within 0.15 s of landing.
- **Water kill** (Rare).

**Distance**
- **Long shot** (Rare): kill from beyond the weapon class's long range.
- **Point blank** (Common): kill within 150 units with a gun.
- **Pistol sniper** (Epic): a pistol headshot kill beyond 1,500 units.

**Penetration and visibility**
- **Wallbang** (Rare): kill through a wall.
- **Double wallbang** (Epic): kill through two or more objects.
- **Through the smoke** (Rare).
- **Blind kill** (Epic): kill while flashed.
- **Wallbang hit** (Common).

**Scope**
- **No-scope** (Rare).
- **Quickscope** (Rare): kill within 0.35 s of scoping in.
- **Flying no-scope** (Epic): a no-scope while airborne.
- **Scoped on the move** (Rare): a scoped kill while moving.

**Utility and melee**
- **Grenade kill** (Rare): HE.
- **Fire kill** (Rare): molotov or incendiary.
- **Knife kill** (Rare).
- **Backstab** (Rare): a knife kill of 90 or more damage.
- **Zeus kill** (Rare).
- **Grenade impact** (Epic): a flash or decoy impact kill.
- **Flash assist** (Common).
- **Long flash** (Common): blind an enemy for over 3 s.

**Timing and aim**
- **Quick switch** (Rare): kill within 0.4 s of switching weapons.
- **Fresh mag** (Common): a kill with the first shot after reloading.
- **Last bullet** (Rare): a kill with the last round in the magazine.
- **One tap** (Rare): a headshot kill with the first shot of a spray.
- **Flick** (Rare): a kill after turning more than 60° in the last 4 ticks.
- **Spray transfer** (Rare): two kills in one spray.
- **Efficient reload** (Common): reload with 30% or less of the magazine left. Shotguns that load shell by shell don't count.
- **Marksman** (Rare): finish a life with at least 10 shots from a weapon and 50% or better accuracy with it.
- **Sharpshooter** (Epic): the same at 70% or better.
- **Headhunter** (Rare): a life with at least 5 hits from a weapon, half of them headshots.

**Multi-kill and situation**
- **Double kill** (Common), **Triple kill** (Rare), and **Rampage** (Epic): 2, 3, or 4 or more kills within 4 s.
- **Untouchable** (Rare): three kills in one life without taking any damage in between. Fall damage counts as damage, and the run starts over after each hit taken.
- **10-kill streak** (Rare) and **20-kill streak** (Epic): the tenth and twentieth kill of one life. They suit deathmatch, where a life can run long.
- **Collateral** (Epic): two kills with one bullet.
- **Opening kill** (Common): the first kill of the round.
- **Trade** (Common): kill a teammate's killer within 3 s.
- **Revenge** (Common): kill a player who has been dominating you.
- **Last stand** (Rare): kill with 10 HP or less.
- **Bounce back** (Common): a kill within 1 s of being hurt.
- **Clutch** (Epic): your team wins the round with you as its last player alive, against at least one enemy. Most useful outside deathmatch.
- **Defuse denial** (Rare): kill a player who is defusing.

### Weapon ladders

Each weapon has volume challenges plus one skill challenge per applicable skill shot. Every weapon gets Kills. Guns also get Headshots and Hits, because grenades and the Zeus land too few hits for a ladder and a knife hit is nearly always the kill. Every challenge has five tiers, I to V.

*Claude's call on thresholds:*

| Kind | Tiers |
|---|---|
| Kills | 25, 100, 250, 500, 1,000 |
| Headshots | 10, 50, 125, 250, 500 |
| Hits | 100, 400, 1,000, 2,500, 5,000 |
| Common skill shot | 5, 20, 50, 100, 200 |
| Rare skill shot | 2, 8, 20, 50, 100 |
| Epic skill shot | 1, 3, 8, 20, 50 |

### Announcements

These go to everyone in chat:
- rank-ups
- weapon levels at every fifth level
- tier completions

Weapon levels between those are announced to the player only. *Claude's call:* this keeps chat readable while plenty is happening.

### Progress panel

The panel is drawn behind one interface with three ways to draw it. The plugin uses the first that works, and switches a player down a level if anything fails:

1. **Side panel:** a `point_worldtext` entity on the right of the player's view, moved with them every tick and hidden from everyone else.
2. **Center-screen card:** CS2's center HTML text, if the side panel fails.
3. **Chat:** if both of the others fail.

Navigation:
- W and S move through the list.
- A and D page between Overview and each weapon.
- R or `!rank` closes it.

It also closes on damage, death, a team change, and map end.

The overview shows rank, an XP bar, totals, and the weapons closest to their next tier. A weapon page shows its level and XP bar, then its volume and skill challenges with progress and the XP each tier pays.

*Claude's call:* the panel doesn't freeze the player. That avoids the trailing text when they turn fast, but freezing in deathmatch would make the panel too dangerous to open.

### Commands

- `!rank` (also `!xp`, `!progress`): opens or closes the panel.
- `!top`: prints the top 10 by rank XP to chat. `!top <weapon>` prints the top 10 by XP with that weapon.
- `!next`, `!feats`, `!stats [name]`, and `!site`: the closest tiers, the feats, a one-line summary of a connected player, and the site's address.
- A welcome line on the first spawn, with the player's rank and the site's address when `Site.PublicUrl` is set. The address, shown as the bare domain, is also announced to everyone at the start of each match (the first round after warmup) and at the win panel.
- `css_csp_status` (root admin only): storage and session diagnostics.
- `css_csp_grant <player> <xp>` and `css_csp_reset <player>` (root admin only, logged): repair a bad save or remove exploited XP. These are the one exception to up-only ranks, since a negative grant puts the rank back to what the remaining XP earns. A correction tells the site to drop its live rows for that player, because the site otherwise keeps the larger of stored and live values.

### Storage

All tables are in the existing MariaDB database:
- **`csp_players`:** Steam ID, name, rank XP, rank, first seen, and last seen.
- **`csp_weapons`:** Steam ID, weapon, weapon XP, level, kills, headshots, hits, and shots.
- **`csp_challenges`:** Steam ID, weapon, challenge, count, and the tier reached.
- **`csp_schema`:** a version row so later changes can migrate.

How data is loaded and saved:
- Each player's data loads on connect.
- Changes stay in memory and are saved at round end, on disconnect, on map end, and every 60 seconds.
- Saves are upserts that run off the game thread.
- A failed save keeps the changes in memory and retries on the next save.
- Bots are never stored.

*Claude's call:* the save is a full upsert of a player's changed rows, never an increment. A save that's retried after a partial failure therefore can't count anything twice.

### Configuration

Configuration is CounterStrikeSharp's standard plugin config at `configs/plugins/CounterStrikeProgression/CounterStrikeProgression.json`. It holds:
- the database connection
- every XP value, multiplier, and threshold in this document
- the panel mode (`Auto`, `WorldText`, `CenterHtml`, or `Chat`)

Terraform writes this file on the server, the same way it writes the other plugins' configs.

### Release and deployment

The ktsu `dotnet.yml` workflow is used with one extra step. After `ktsubuild ci`, it publishes the plugin and zips it into `staging/CounterStrikeProgression-<version>.zip`, which `ktsubuild release` attaches to the GitHub release. Nothing is pushed to NuGet (`IsPackable=false`).

The homelab's `cs2-pre.sh` installs the zip at a pinned version like the other plugins. Moving the server from UmbrellaRanked to this plugin is a separate homelab change.

## Left to the builder

- Tuning numbers after real play. Every value is a setting.
- Exact panel styling and wording.
- Whether the side panel needs per-resolution offsets. It ships with one layout tuned for 16:9 and a setting to nudge it.
