# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Restore, build, and test (standard workflow)
dotnet restore
dotnet build
dotnet test --project CounterStrikeProgression.Test

# Run a single test
dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~TestMethodName"

# Publish the plugin folder the way CI does
dotnet publish CounterStrikeProgression/CounterStrikeProgression.csproj -c Release -p:IsPublishable=true -o out/addons/counterstrikesharp/plugins/CounterStrikeProgression
```

## Project Structure

This is a CounterStrikeSharp plugin for CS2 that adds up-only matchmaking-style ranks, per-weapon levels and challenge ladders, and XP for skill shots. The design and the reasoning behind its numbers are in `docs/superpowers/specs/2026-10-17-counterstrikeprogression-design.md`.

The solution uses:
- **ktsu.Sdk:** the shared build configuration.
- **MSTest.Sdk:** the test project, which runs on Microsoft Testing Platform.
- **Targets:** `net10.0` only, because CounterStrikeSharp v1.0.376 hosts plugins on the .NET 10 runtime it bundles. Each project opts out of the SDK's multi-targeting with an empty `TargetFrameworks`.

| Project | Contents |
|---|---|
| `CounterStrikeProgression.Core` | Everything testable without a game server: weapons, ranks, the XP engine, skill shots, ladders, trackers, panel layout, and storage. No CounterStrikeSharp reference. |
| `CounterStrikeProgression` | The plugin. It reads CS2 events and pawn state into Core's plain snapshots, draws the panel, and handles commands and saves. `AssemblyName` is `CounterStrikeProgression`, because CounterStrikeSharp loads `plugins/<Name>/<Name>.dll`. |
| `CounterStrikeProgression.Site` | The progression website, an ASP.NET Core app with static pages. It takes batches from the plugin on port 8081 (`POST /api/ingest`), serves the pages and read-only API on port 8080, and reads stored progress through Core's `IProgressionStore`. It never writes to the database. |
| `CounterStrikeProgression.Test` | MSTest tests for Core. |

The site ships as a container image, built by the .NET SDK container support with no Dockerfile. To build one to a tarball without Docker:

```bash
dotnet publish CounterStrikeProgression.Site/CounterStrikeProgression.Site.csproj -c Release -t:PublishContainer -p:ContainerArchiveOutputPath=./site.tar.gz
```

Use `-t:` rather than `/t:`, because Git Bash rewrites a leading slash. The `Publish Site Image` step in `dotnet.yml` pushes `ghcr.io/ktsu-dev/counterstrikeprogression-site:<version>` and `latest` on release.

Nothing goes to NuGet. ktsu.Sdk marks every library packable from its targets, after the project file is read, so `IsPackable=false` in a csproj has no effect. `Directory.Build.targets` turns packing off in a target that runs just before Pack. The release is a zip of the plugin folder, built by the `Package Plugin` step in `.github/workflows/dotnet.yml`.

### Key Files

- **`Core/Progression/ProgressionEngine.cs`:** turns hits, kills, reloads, life summaries, assists, flashes, and clutches into rank XP, weapon XP, ladder progress, and notices.
- **`Core/SkillShots/SkillShotCatalog.cs`:** every skill shot, with its rarity, the weapons it applies to, and its detection rule. Ids are stored as challenge ids, so never rename one.
- **`Core/Weapons/WeaponCatalog.cs`:** every tracked weapon, and the normalization of CS2's weapon names (prefixes, knife skins, grenade aliases, and alternates told apart by definition index).
- **`Core/Tracking/`:** `RoundTracker` (opening kills, multi-kills, collaterals, trades, clutches), `PlayerCombatTracker` (landing, scope, weapon switch, reload, flick), and `LifeTracker` (per-life accuracy).
- **`Core/Storage/`:** `ProgressSnapshot` (dirty rows copied on the game thread), `MySqlProgressionStore`, and `InMemoryProgressionStore`. Each save also upserts the player's total rank XP for the day into `csp_daily`, and `XpHistory` turns those rows into XP per day and XP since a date (the player page's History tab and the "XP this week" board on Ranks).
- **`Core/Discord/`:** `DiscordMessages` picks the feats, epic skill shots, and rank-ups worth posting, and `DiscordSender` posts them to the optional webhook from the thread pool.
- **`Core/Rotation/Season.cs`:** seasons, which start on 1 January and every `SeasonMonths` months after. Every point of rank XP also counts toward the current season (`PlayerProgress.SeasonXp`, saved to `csp_seasons`), while lifetime rank never resets. The site's `/seasons` page ranks a season, and the player page shows this season's place and every top 3 finish.
- **`Core/Panel/`:** `PanelBuilder` builds the pages, and `PanelFormatter` renders plain text, center HTML, or chat.
- **`Core/Recap/`:** `RecapTracker` adds up what each player earns over a map, and `MapRecap` is the result: a chat line at the win panel (`PanelFormatter.ToRecapChat`), and the "Last session" the site keeps per player in `recaps.json`.
- **`CounterStrikeProgression/CombatHandlers.cs`:** the combat events that become `CombatEvent` snapshots.
- **`CounterStrikeProgression/LifecycleHandlers.cs`:** connects, spawns, rounds, the per-tick tracking, and the transmit filter.
- **`CounterStrikeProgression/Panel/WorldTextPanel.cs`:** the side panel, a `point_worldtext` moved in front of the eyes every tick.
- **`CounterStrikeProgression/Panel/XpFloaties.cs`:** the floating "+XP" text, one billboarded `point_worldtext` per floatie, risen and faded each tick. `Core/Floaties/FloatieMerger.cs` decides when gains on one target merge and what the text says. Panels and floaties are both hidden from everyone but their owner by the transmit filter in `LifecycleHandlers`.

### Dependencies

- **CounterStrikeSharp.API:** compile-only (`ExcludeAssets="runtime"`). The host supplies it, and a second copy would break type identity.
- **MySqlConnector and Dapper:** MariaDB access.
- **ktsu.Semantics.Strings:** `WeaponKey` and `SkillShotId` are semantic strings.
- **ktsu.Extensions:** `GetOrCreate` and other helpers.
- **Polyfill:** required by ktsu.Sdk. Core sets `PolyUseEmbeddedAttribute`, because the plugin sees Core's internals and the two copies would otherwise clash.

## Architecture

CS2 events are read on the game thread into plain records (`CombatEvent`, `ReloadEvent`, and so on) and handed to `ProgressionEngine`. The engine does the following:
- It applies them to a `PlayerProgress` and flags changed rows as dirty.
- It returns a `ProgressionResult` with the XP gained and the notices to announce.
- It scales rank XP against bots by `BotXpMultiplier`, but never weapon XP or ladder counts.

Core types are `internal` and shared with the plugin and tests through `InternalsVisibleTo`, so only config types (`ProgressionSettings` and `DatabaseSettings`) need a public API.

Saving works like this:
- `SessionManager` captures a `ProgressSnapshot` on the game thread and writes it on the thread pool.
- After a failed write, it re-flags the rows. Rows hold full values, so a retried save can't double count.
- A player whose load fails gets no progress until a retry succeeds, so stored totals are never overwritten by a fresh, empty profile.

## Testing

MSTest with semantic asserts (`Assert.Contains`, `Assert.HasCount`, `Assert.IsGreaterThan`). `TestHelpers` builds weapons, engines, players, and combat events. Kills and hits default to a mid-range distance, so neither Point blank nor Long shot fires unless a test asks for it. The plugin project has no tests, because CounterStrikeSharp types can't load outside a server.

## CI/CD

The `dotnet.yml` workflow runs `ktsubuild` (the `ktsu.KtsuBuild.Tool` dotnet tool). Version increments are controlled by commit message tags: `[major]`, `[minor]`, `[patch]`, `[pre]`. VERSION.md, CHANGELOG.md, and LICENSE.md are generated and never edited by hand.

## Code Quality

Do not add global suppressions for warnings. Use explicit suppression attributes with justifications when needed, with preprocessor defines only as fallback. Make the smallest, most targeted suppressions possible.
