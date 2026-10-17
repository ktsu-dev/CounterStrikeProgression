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

- **Up-only ranks**: 18 matchmaking-style ranks. Each rank costs 25% more XP than the one before, and nobody loses progress for a bad night.
- **Weapon levels**: every weapon, grenade, the knife, and the Zeus levels up to 40, and each level-up pays rank XP.
- **Weapon ladders**: Kills, Headshots, and Hits, plus one challenge for every skill shot the weapon can earn, each with five tiers. Skill tiers pay more than volume tiers.
- **Skill shots**: about 50, covering movement, distance, wallbangs and smokes, scopes, utility and melee, timing and aim, multi-kills, and round situations.
- **Bots count, at a reduced rate**: rank XP against bots is scaled down, while weapon levels and ladders still move, so practice against bots isn't wasted.
- **In-game panel**: `!rank` opens a side panel to the right of the view. It falls back to a center-screen card and then to chat if a way of drawing fails.
- **XP floaties**: every XP gain floats up in the world as "+XP" text, above the target for hits and kills and in front of you otherwise. Only you see your own. Quick hits on one target add up in one floatie, and skill shots, tiers, and level-ups are named under the number in gold.
- **Announcements**: rank-ups, finished tiers, and every fifth weapon level go to the whole server.
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
| `!top` | Print the 10 players with the most rank XP |

While the panel is open, W and S scroll, A and D move between the overview and each weapon, and R closes it. It also closes when you take damage or die.

### Admin command

`css_csp_status` (requires `@css/root`) prints the plugin version, the store in use, how many players have loaded, and whether the match is in warmup or free-for-all.

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

### Panel placement

`Floaties` turns the floating XP text on or off and sets how long it lasts, how far it rises, how quickly gains merge, how many one player can have at once, its colors, and `ScreenSize`. Floaties keep the same size on screen at any distance.

`Panel` sets the draw mode (`Auto`, `WorldText`, `CenterHtml`, or `Chat`), how many lines each mode shows, and the side panel's font size, scale, color, and offsets. The default placement suits a 16:9 display. CS2 has no client-predicted anchor for world text, so the side panel trails slightly behind fast turns.

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
| Multi-kill and situation | Double kill, Triple kill, Rampage, Collateral, Opening kill, Trade, Revenge, Last stand, Bounce back, Defuse denial, Clutch |

### Storage tables

| Table | Contents |
|---|---|
| `csp_players` | Steam ID, name, rank XP, rank, first and last seen |
| `csp_weapons` | Steam ID, weapon, weapon XP, level, kills, headshots, hits, shots |
| `csp_challenges` | Steam ID, weapon, challenge, count, tier reached |
| `csp_schema` | The schema version |

Skill shot ids double as challenge ids in `csp_challenges`, so an id is never renamed once released.

## Contributing

Contributions are welcome! Feel free to open issues or submit pull requests.

## License

This project is licensed under the MIT License. See the [LICENSE.md](LICENSE.md) file for details.
