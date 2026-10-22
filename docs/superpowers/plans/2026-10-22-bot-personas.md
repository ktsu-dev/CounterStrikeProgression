# Bot personas implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every bot name becomes a persistent player that earns, saves, and shows up like a human, marked as a bot everywhere, while staying out of server records, global feeds, announcements, and anything sent to a client.

**Architecture:** A bot's player id is a pure function of its name, in a 17-digit range no SteamID64 can hold, so the plugin, the store, and the site key bots exactly like humans and anyone can tell a bot from its id alone. The plugin splits "track this player" (humans and bot personas, while a human is on) from "talk to this player" (humans only). Core and the site use `BotIds.IsBot` to keep bots out of records, global feeds, Discord, and the digest, and the kill store keeps bot-only fights under their own cap.

**Tech stack:** C# on .NET 10 (Core, Site, plugin), MariaDB through Dapper, ASP.NET Core minimal APIs, MSTest on Microsoft Testing Platform, vanilla JS and CSS in `wwwroot`.

**Spec:** `docs/superpowers/specs/2026-10-22-bot-personas-design.md`

## Global constraints

- A bot is tracked exactly like a human, with the same rules and numbers, including `BotXpMultiplier` for anything done to a bot. A human's XP for killing a bot is unchanged.
- Nemesis XP and bounties work with bots exactly as between humans: a bot can be a nemesis, carry a bounty, and collect one.
- Bots are tracked only while at least one human is on the server.
- A bot's identity is its name. A human with a bot's name is a different player, keyed by SteamID64.
- Bot ids never collide with a real SteamID64, never change, and are recognisable from the id alone.
- Kills with a human on either end keep `MaxPointsPerMap`. Kills between two bots have their own cap and never push human-involved kills out.
- Records stay human-only, even when `RecordsCountBots` is true.
- A bot's moments stay out of the home page feed, Discord (including the weekly digest), and in-game broadcasts, but appear on the bot's own player page feed.
- Nothing is drawn or sent to a bot: no panel, chat, welcome, floaties, or clan tag.
- The CSS-BANS panel and SimpleAdmin stay human-only. `css_csp_grant` and `css_csp_reset` can target a bot persona by name.
- Existing tables keep their shape and human rows. The site needs no new table, so `files/cs2-progression-site-db.sh` in the homelab needs no grant change (`csp_kills` gains a column, which its table grant already covers).
- Nothing slow on the game thread. Bot progress loads and saves through `SessionManager`'s existing chains.
- A bot that joins and leaves often must not lose progress, double count, or create duplicate personas.
- `Endpoints` is at its CA1506 coupling limit: new handlers and helpers go in their own classes, and no warning is suppressed.
- MSTest with semantic asserts, tabs, file-scoped namespaces, the repo's copyright header on new files.

## Builder decisions

- **Id scheme:** `BotIds.For(name)` is `10_000_000_000_000_000 + (FNV-1a 64 of the trimmed name's UTF-16 code units % 100_000_000)`, offset basis `14695981039346656037`, prime `1099511628211`. Ids run from `10000000000000000` to `10000000099999999`: 17 digits (so every existing `^\d{17}$` check accepts them) with universe 0 (so no SteamID64 can be one). Names compare ordinally after `Trim()`, so case matters.
- **Where the mapping lives:** nowhere. The id is computed from the name every time, so it survives every restart with no table, no async assignment, and no way to make a second persona for one name. The cost is a hash collision (about 2.5 in 100,000 for 70 names), which would merge two names, and the plan accepts it.
- **Tracking versus talking:** `GameState.IsTracked` (a human, or a named bot) and `GameState.PlayerIdOf` decide sessions. `GameState.IsHuman` stays for anything sent to a client. `PlayerSession.IsBot` gates the talking paths at their owners: `Announcer`, `PanelPresenter`, `XpFloaties` (through `ProgressionServices.Apply`), `RankTags`, and the welcome line.
- **Bot moments on the site:** the plugin sends bot feed entries in the batch as it does today. `LiveState` adds every entry to its player's feed but only human entries to the global feed and to `LiveChanges.NewFeed`, so the `feed` event and `feed.json` carry no bots, and `players/<id>.json` keeps the bot's.
- **Two kill caps:** `csp_kills` gains `bot_fight TINYINT(1) NOT NULL DEFAULT 0` and index `idx_csp_kills_pair (map, bot_fight, id)`. Prune runs per map and per `bot_fight` value, with `MaxPointsPerMap` for 0 and the new `HeatmapSettings.MaxBotFightsPerMap` (default `10_000`, values under 100 count as 100) for 1. Existing rows default to 0, which is right, because bot-only kills were never stored.
- **Bot marker:** on the site, a pill `<span class="bot-tag">BOT</span>` after the name (uppercase, `font-size: 0.7em`, `letter-spacing: 0.06em`, `padding: 0 0.35em`, `margin-left: 0.35em`, 1px border and text in `--muted`, `border-radius: 3px`, `vertical-align: middle`). Where only text fits (a `<select>` option, a document title, Open Graph tags, chat) the name gets the suffix ` [BOT]`. The share card draws the same pill.
- **Bounty lines:** placed, raised, and unclaimed lines are the board's, not a player's moment, so they still go to everyone whoever holds the bounty. A bot's own collected-bounty notice follows the bot rule and isn't broadcast. A bounty on a bot pays in full, like any bounty.
- **Nemesis XP:** `NemesisKillXp` pays in full when the nemesis is a bot, because it isn't a kill's XP.
- **Admin targets:** `bot:<name>` names a persona, connected or not. Connected bots are also candidates under their names, and a bot id works like a SteamID64.
- **When the last human leaves:** every bot session finishes its recap quietly, saves, and ends, and the bounty board clears. Bots that are present when a human joins get sessions straight away.

## Review focus

1. **Two bots with one name at once** (a quota larger than the name pool): the second gets no session and earns nothing, and the first keeps its persona. Test `BotTracking_RefusesASecondSessionForAnIdInUse` in Task 1.
2. **A human named like a bot** (a human called `Muhlik`): their id is their SteamID64, never `10000000062568863`. Test `PlayerIds_KeysAHumanBySteamIdWhateverTheirName` in Task 1.
3. **A name that differs only by spaces or case:** `" Muhlik "` is the same persona as `"Muhlik"` and `"muhlik"` is another. Test `For_TrimsButKeepsCase` in Task 1.
4. **The last human leaves while bots keep fighting:** bot kills stop counting and bot-only kills stop being stored (both ids are 0 again, which the buffer drops). Tests `BotTracking_StopsWithoutAHuman` in Task 1 and `Add_DropsAKillBetweenTwoUntrackedBots` in Task 4.
5. **A site restart with bot feeds on disk:** `feed.json` holds no bot entries and the bot's page still has its feed. Test `FeedStore_KeepsABotsEntriesInItsOwnFileOnly` in Task 6.

---

### Task 1: Bot ids and when a bot is tracked

**Files:**
- Create: `CounterStrikeProgression.Core/Players/BotIds.cs`
- Test: `CounterStrikeProgression.Test/BotIdTests.cs`

**Interfaces:**
- Produces: `internal static class BotIds` with `public const ulong First = 10_000_000_000_000_000;`, `public const ulong Count = 100_000_000;`, `public static ulong For(string name)` (throws `ArgumentException` for a blank name), and `public static bool IsBot(ulong id)` (`id >= First && id < First + Count`).
- Produces: `internal static class PlayerIds` with `public static ulong Of(bool isBot, ulong steamId, string? name)`: the SteamID64 for a human, `BotIds.For(name)` for a bot with a non-blank name, 0 otherwise.
- Produces: `internal static class BotTracking` with `public static bool MayStart(ulong id, bool humansOnline, IEnumerable<ulong> activeIds)`: true for a human id (not already active), and for a bot id only when `humansOnline` and no active session has that id. Returns false for id 0.

- [ ] **Step 1: Write the failing tests** in `BotIdTests`:
  - `For_IsStableForAName`: `BotIds.For("Muhlik")` is `10000000062568863`, and `BotIds.For("Rock")` is `10000000063859252`.
  - `For_TrimsButKeepsCase`: `For(" Muhlik ")` is `10000000062568863`, and `For("muhlik")` is `10000000075765951`.
  - `For_RejectsABlankName`: `Assert.ThrowsExactly<ArgumentException>(() => BotIds.For("  "))`.
  - `IsBot_MatchesTheRangeOnly`: true for `10000000000000000` and `10000000099999999`, false for `9999999999999999`, `10000000100000000`, `76561197960265729`, and 0.
  - `IsBot_IsTrueForEveryNamedId`: for `"Muhlik"`, `"Rock"`, `"Zed"`, `IsBot(For(name))` is true and `For(name).ToString(CultureInfo.InvariantCulture)` has 17 characters.
  - `PlayerIds_KeysAHumanBySteamIdWhateverTheirName`: `PlayerIds.Of(false, 76561197960265729, "Muhlik")` is `76561197960265729`.
  - `PlayerIds_KeysABotByName`: `Of(true, 0, "Muhlik")` is `10000000062568863`, and `Of(true, 0, "")` is 0.
  - `BotTracking_StopsWithoutAHuman`: `MayStart(For("Muhlik"), humansOnline: false, [])` is false, and with `humansOnline: true` it's true.
  - `BotTracking_RefusesASecondSessionForAnIdInUse`: `MayStart(For("Muhlik"), true, [For("Muhlik")])` is false.
  - `BotTracking_StartsAHumanWithNoOneElseOn`: `MayStart(76561197960265729, false, [])` is true, and `MayStart(0, true, [])` is false.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~BotIdTests"`
Expected: build error, `BotIds` doesn't exist.

- [ ] **Step 3: Implement** the three classes in `BotIds.cs`, namespace `ktsu.CounterStrikeProgression.Core.Players`. `For` hashes `name.Trim()` char by char (`hash ^= c; hash *= 1099511628211;` from `14695981039346656037`, unchecked), with no allocation, because the plugin calls it on every event.

- [ ] **Step 4: Run them and see them pass.** Same command, all pass.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression.Core/Players/BotIds.cs CounterStrikeProgression.Test/BotIdTests.cs
git commit -m "[minor] Give each bot name a stable player id"
```

### Task 2: Records stay human, bounties and nemeses take bots

**Files:**
- Modify: `CounterStrikeProgression.Core/Progression/ProgressionEngine.cs`, `CounterStrikeProgression.Core/Tracking/BountyBoard.cs`, `CounterStrikeProgression.Core/Rivals/Rivalry.cs` (doc comments only)
- Test: `CounterStrikeProgression.Test/RecordTests.cs`, `BountyTests.cs`, `RivalTests.cs`

**Interfaces:**
- Consumes: `BotIds.IsBot`, `BotIds.For` (Task 1).
- Produces: `ProgressionEngine.TryRecord` returns at once when `BotIds.IsBot(player.SteamId)`, which covers kill, life, and map records.
- Produces: `BountyBoard.Kill(int killerSlot, int victimSlot, bool killerUntracked, bool teamkill, bool killerLoaded)`, renamed from `killerIsBot`: an untracked killer (a bot with no persona) neither scores nor collects. Existing tests switch the named argument to `killerUntracked`.

- [ ] **Step 1: Write the failing tests**
  - `RecordTests.BotNeverSetsAKillRecord`: settings with `RecordsCountBots = true`, the record book loaded, a player `new PlayerProgress(BotIds.For("Muhlik"))`, a 60 m AWP kill on a human that sets the record for a human player. The bot's result has no `NoticeKind.RecordBroken` notice, and `engine.Records` holds nothing for `LongestKillId(Sniper)`.
  - `RecordTests.BotNeverSetsALifeOrMapRecord`: the same bot, `ApplyLifeRecords` with 10 kills and `ApplyMapRecords(bot, 20, 20)` both return no `RecordBroken` notice.
  - `RecordTests.HumanStillSetsARecordAgainstBotsWhenTheyCount`: a human, `RecordsCountBots = true`, the same kill with `VictimIsBot = true`, one `RecordBroken` notice.
  - `BountyTests.TrackedBotCarriesAndLosesTheBounty`: kills by `Leader` with `killerUntracked: false` up to `BountyStartKills` give `Placed` for `Leader`, and a kill of `Leader` by `Hunter` with `killerLoaded: true` gives one `Collected` with `CollectorSlot == Hunter` and `Xp == Value(BountyStartKills)`.
  - `BountyTests.UntrackedKillerNeitherScoresNorCollects`: the existing bot cases, renamed to `killerUntracked: true`, unchanged in their asserts.
  - `RivalTests.BotNemesisPaysTheNemesisBonus`: human `76561197960265729` killed `NemesisMinimumKills` times by `PlayerProgress(BotIds.For("Muhlik"))` through `Rivalry.Count`, then `ApplyRivalKill(human, bot)` has one `NemesisKilled` notice with `Xp == Settings.NemesisKillXp` and `PlayerName == "Muhlik"`.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~RecordTests|FullyQualifiedName~BountyTests|FullyQualifiedName~RivalTests"`
Expected: build error on `killerUntracked`, and the bot record tests fail with a `RecordBroken` notice present. The nemesis test already passes, because the engine never checked for bots there; it pins the behaviour.

- [ ] **Step 3: Implement** the guard in `TryRecord` and the rename in `BountyBoard.Kill`. Rewrite the docs that say bots never count: `BountyBoard`'s remarks ("A bot without a persona never carries a bounty"), `ApplyBounty`'s summary ("paid in full, whoever held it"), `ApplyRivalKill`'s summary, and `Rivalry`'s and `HeadToHead`'s "humans" wording.

- [ ] **Step 4: Run them and see them pass.** Same command, then the whole suite.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression.Core CounterStrikeProgression.Test
git commit -m "[minor] Keep records human-only and let bot personas carry bounties"
```

### Task 3: Chat lists and the digest mark or leave out bots

**Files:**
- Modify: `CounterStrikeProgression.Core/Storage/IProgressionStore.cs`, `InMemoryProgressionStore.cs`, `MySqlProgressionStore.cs`, `CounterStrikeProgression.Core/Panel/ChatReports.cs`, `CounterStrikeProgression.Core/Discord/WeeklyDigest.cs`
- Test: `CounterStrikeProgression.Test/ChatReportTests.cs`, `MasteryStarTests.cs`, `WeeklyDigestTests.cs`, `StorageTests.cs`

**Interfaces:**
- Consumes: `BotIds` (Task 1).
- Produces: `LeaderboardEntry(string Name, long RankXp, int Rank, ulong SteamId = 0)` and `WeaponLeaderboardEntry(string Name, long Xp, int Level, ulong SteamId = 0)`. Both stores fill `SteamId` (MySQL: `SELECT steam_id AS SteamId, ...` in the top and top-by-weapon queries, `w.steam_id AS SteamId` in the weapon one).
- Produces: `public static string ChatReports.PlayerName(ulong steamId, string name)`: `name + " [BOT]"` for a bot id, `name` otherwise. `Stats` callers, `StatsFor`, `Rivals` (nemesis and favourite victim), and `WeaponLeader` use it.
- Produces: `WeeklyDigest.Build` drops every `PeriodGain` whose `SteamId` is a bot id before ranking climbers and rank-ups.

- [ ] **Step 1: Write the failing tests**
  - `ChatReportTests.PlayerName_MarksABot`: `PlayerName(BotIds.For("Muhlik"), "Muhlik")` is `"Muhlik [BOT]"`, and `PlayerName(76561197960265729, "Muhlik")` is `"Muhlik"`.
  - `ChatReportTests.Rivals_MarksABotNemesis`: a human killed 5 times by bot `Muhlik` (`NemesisMinimumKills = 3`). `Rivals(human)` contains `"Nemesis Muhlik [BOT] (0-5)."`.
  - `ChatReportTests.StatsFor_FindsAndMarksABot`: online list `("Muhlik", bot progress)`, query `"muh"`. The first line starts with `"Muhlik [BOT]: "`.
  - `MasteryStarTests.WeaponLeaderMarksABot`: `WeaponLeader(1, new WeaponLeaderboardEntry("Muhlik", 25, 3, BotIds.For("Muhlik")))` is `"1. Muhlik [BOT] level 3 (25 XP)"`. The two existing asserts stay as they are.
  - `StorageTests.TopCarriesEachPlayersId`: an in-memory store with a saved human and a saved bot. `TopAsync(10)` and `TopByWeaponAsync(ak47, 10)` carry each row's `SteamId`.
  - `WeeklyDigestTests.LeavesOutBots`: gains for bot `Muhlik` (+9,000) and human `Alice` (+1,000). The post contains `"**Alice**: +1,000 XP"` and doesn't contain `"Muhlik"`.
  - `WeeklyDigestTests.OnlyBotsPostsNothing`: gains for `Muhlik` only. `Build` returns null.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~ChatReportTests|FullyQualifiedName~MasteryStarTests|FullyQualifiedName~WeeklyDigestTests|FullyQualifiedName~StorageTests"`
Expected: build error, `PlayerName` and the `SteamId` members don't exist.

- [ ] **Step 3: Implement** the record members, the store columns, `PlayerName`, its uses, and the digest filter.

- [ ] **Step 4: Run them and see them pass.** Same command, then the whole suite.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression.Core CounterStrikeProgression.Test
git commit -m "[minor] Mark bots in chat lists and leave them out of the weekly digest"
```

### Task 4: Kills between two bots under their own cap

**Files:**
- Modify: `CounterStrikeProgression.Core/Heatmaps/KillPoint.cs`, `HeatmapSettings.cs`, `IKillMapStore.cs`, `InMemoryKillMapStore.cs`, `MySqlKillMapStore.cs`
- Test: `CounterStrikeProgression.Test/HeatmapTests.cs` (and every existing `AddAsync` caller in the tests)

**Interfaces:**
- Consumes: `BotIds.IsBot` (Task 1).
- Produces: `[JsonIgnore] public bool KillPoint.IsBotFight => BotIds.IsBot(KillerId) && BotIds.IsBot(VictimId);`, ignored so the live batch's shape doesn't change.
- Produces: `HeatmapSettings.MaxBotFightsPerMap { get; set; } = 10_000;`, values under 100 counting as 100.
- Produces: `IKillMapStore.AddAsync(IReadOnlyList<KillPoint> points, int maxPerMap, int maxBotFightsPerMap, CancellationToken cancellationToken)`: deletes the oldest past `maxPerMap` among a map's kills with a human, and past `maxBotFightsPerMap` among its bot fights, each counted apart. Existing test callers pass `maxBotFightsPerMap` equal to their `maxPerMap`.
- Produces in `MySqlKillMapStore`: `ALTER TABLE csp_kills ADD COLUMN IF NOT EXISTS bot_fight TINYINT(1) NOT NULL DEFAULT 0;` and `ALTER TABLE csp_kills ADD INDEX IF NOT EXISTS idx_csp_kills_pair (map, bot_fight, id);` after the create, `bot_fight` set from `@IsBotFight` in the insert, and `Prune` taking `@botFight` in both its outer and inner `WHERE`, run once per map for 0 with `maxPerMap` and once for 1 with `maxBotFightsPerMap`.

- [ ] **Step 1: Write the failing tests** in `HeatmapTests`:
  - `Add_KeepsBotFightsUnderTheirOwnCap`: in-memory store, map `de_dust2`, 150 kills by human `76561197960265729` on bot `Muhlik`, then 300 kills of bot `Muhlik` by bot `Rock`, with `maxPerMap: 100, maxBotFightsPerMap: 120`. `FightsAsync("de_dust2", 76561197960265729, null, 1000)` returns 100 kills, and `FightsAsync("de_dust2", BotIds.For("Rock"), null, 1000)` returns 120.
  - `Add_BotFightsNeverPushOutHumanKills`: 100 human kills with cap 100, then 500 bot fights. The human's 100 kills are all still there, and the oldest human kill is the first one added.
  - `IsBotFight_NeedsABotPersonaOnBothEnds`: true for `Rock` on `Muhlik`, false for a human on `Muhlik`, and false for killer 0 on `Muhlik`.
  - `Add_DropsAKillBetweenTwoUntrackedBots`: `KillPointBuffer.Add` of a kill with both ids 0 returns false, and one between two personas returns true.
  - `Settings_BotFightCapDefaultsTo10000`: `new HeatmapSettings().MaxBotFightsPerMap` is `10_000`.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~HeatmapTests"`
Expected: build error, the four-argument `AddAsync` and `IsBotFight` don't exist.

- [ ] **Step 3: Implement** the members, the in-memory prune per class (drop the oldest of each class past its cap), and the MySQL column, index, insert, and prune. Update `HeatmapSettings`' summary, which says only kills with a human are kept, and add `MaxBotFightsPerMap` to the config table in `README.md`.

- [ ] **Step 4: Run them and see them pass.** Same command, then the whole suite.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression.Core CounterStrikeProgression.Test README.md
git commit -m "[minor] Store kills between bot personas under their own per-map cap"
```

### Task 5: Corrections can name a bot persona

**Files:**
- Modify: `CounterStrikeProgression.Core/Admin/CorrectionTargets.cs`
- Test: `CounterStrikeProgression.Test/AdminCorrectionTests.cs`

**Interfaces:**
- Consumes: `BotIds` (Task 1).
- Produces: `CorrectionTargets.Resolve(string? text, IReadOnlyList<TargetCandidate> players)` (renamed from `humans`; the plugin passes humans and tracked bots). A number that's a SteamID64 or a bot id resolves directly. `bot:<name>` resolves to `BotIds.For(name)` with the name as typed after the prefix, trimmed, connected or not, and `bot:` with nothing after it is an error.

- [ ] **Step 1: Write the failing tests**
  - `Resolve_NamesAnOfflineBotByPrefix`: `Resolve("bot:Muhlik", [])` has `SteamId == 10000000062568863`, `Name == "Muhlik"`, `Found`.
  - `Resolve_TakesABotId`: `Resolve("10000000062568863", [])` is found with that id.
  - `Resolve_MatchesAConnectedBotByName`: candidates `(10000000062568863, 3, "Muhlik")` and a human `(76561197960265729, 2, "Alice")`. `Resolve("muh", ...)` is the bot.
  - `Resolve_RejectsAnEmptyBotName`: `Resolve("bot:", [])` isn't found, and its error contains `bot:<name>`.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~AdminCorrectionTests"`
Expected: the bot cases fail (not found).

- [ ] **Step 3: Implement** the prefix, the bot id range, and the error texts, which list `bot:<name>` beside `#userid`, a SteamID64, and part of a name.

- [ ] **Step 4: Run them and see them pass.** Same command.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression.Core/Admin/CorrectionTargets.cs CounterStrikeProgression.Test/AdminCorrectionTests.cs
git commit -m "[minor] Let corrections name a bot persona"
```

### Task 6: The site keeps bots off the global feed and treats them as players

**Files:**
- Modify: `CounterStrikeProgression.Site/Data/ProgressReader.cs`, `Live/LiveState.cs`, `Live/LiveKills.cs`, `Live/LivePositions.cs`, `Live/MatchFights.cs` (doc only), `Endpoints.cs` (one condition, no new types), and each other `IsBot` check under `CounterStrikeProgression.Site` that means "has a player page"
- Test: `CounterStrikeProgression.Test/SiteLiveTests.cs`, `SiteEndpointTests.cs`, `LiveKillTests.cs`, `LivePositionTests.cs`

**Interfaces:**
- Consumes: `BotIds` (Task 1).
- Produces: `ProgressReader.TryParseSteamId` accepts a 17-digit id that's a SteamID64 or `BotIds.IsBot`.
- Produces: `LiveState.Apply` adds a batch's bot entries to their player feeds only. `Feed`, the saved `feed.json`, and `LiveChanges.NewFeed` (so the `feed` event) hold human entries only.
- Produces: `LiveState.IsPlaying(steamId)` is true for any player in the snapshot with that id, bot or not, and false for 0.
- Produces: `LiveKill.KillerIsBot` and `VictimIsBot` are true for 0 or a bot id. `PlayerPosition.SteamId` is the id as text for any non-zero id, bots included.
- Produces: `Endpoints.Live` collects feats for every snapshot player with a non-zero id.

- [ ] **Step 1: Write the failing tests**
  - `SiteEndpointTests.TryParseSteamId_TakesABotId`: `"10000000062568863"` parses to `10000000062568863`, and `"10000000100000000"` and `"10000000062568863 "` don't.
  - `SiteLiveTests.BotMomentsStayOffTheGlobalFeed`: a batch with a `RankUp` entry for bot `Muhlik` and one for human `76561197960265729`. `Feed` has 1 entry (the human's), `NewFeed` has 1, and `PlayerFeed(BotIds.For("Muhlik"))` has 1.
  - `SiteLiveTests.FeedStore_KeepsABotsEntriesInItsOwnFileOnly`: after that batch, a new `LiveState` over the same folder has 1 global entry and 1 entry in the bot's player feed.
  - `SiteLiveTests.IsPlaying_CountsABotPersona`: a snapshot with `LivePlayer(3, BotIds.For("Muhlik"), "Muhlik", IsBot: true, ...)`. `IsPlaying(BotIds.For("Muhlik"))` is true and `IsPlaying(0)` is false.
  - `SiteEndpointTests.PlayerPage_ServesABotPersona`: an in-memory store holding progress for `BotIds.For("Muhlik")`. `GET /api/players/10000000062568863` answers 200 with name `Muhlik`.
  - `LiveKillTests.MarksBotPersonasAsBots`: a kill by `Rock` on human `76561197960265729` has `KillerIsBot == true`, `VictimIsBot == false`.
  - `LivePositionTests.BotPersonaKeepsItsId`: a bot `LivePlayer` with a persona id gives `SteamId == "10000000062568863"`, and one with id 0 gives null.

- [ ] **Step 2: Run them and see them fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~SiteLiveTests|FullyQualifiedName~SiteEndpointTests|FullyQualifiedName~LiveKillTests|FullyQualifiedName~LivePositionTests"`
Expected: the new tests fail (the bot id is refused, the global feed holds 2 entries).

- [ ] **Step 3: Implement.** In `LiveState.Apply`, split `batch.Feed` by `BotIds.IsBot(e.SteamId)`: the human part goes into `entries` and the returned `LiveChanges`, and the whole batch goes to `AddToPlayerFeeds`. Run `grep -n "IsBot" CounterStrikeProgression.Site -r` and change every check that means "a human with a page" to "a non-zero id". Update `MatchFights`' and `LivePublisher`'s docs, which say bot kills are left out of fight maps: only kills with no persona on either end are.

- [ ] **Step 4: Run them and see them pass.** Same command, then the whole suite and `dotnet build`, so CA1506 on `Endpoints` still holds.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression.Site CounterStrikeProgression.Test
git commit -m "[minor] Serve bot personas on the site and keep them off the global feed"
```

### Task 7: The plugin tracks bot personas and talks only to humans

**Files:**
- Modify: `CounterStrikeProgression/GameState.cs`, `PlayerSession.cs`, `SessionManager.cs`, `LifecycleHandlers.cs`, `CombatHandlers.cs`, `ProgressionServices.cs`, `Announcer.cs`, `Panel/PanelPresenter.cs`, `RankTags.cs`, `KillMapRecorder.cs`, `Site/SiteReporter.cs`, `AdminCommands.cs`, `PlayerCommands.cs`

**Interfaces:**
- Consumes: `PlayerIds.Of`, `BotIds.IsBot`, `BotTracking.MayStart` (Task 1), `killerUntracked` (Task 2), `ChatReports.PlayerName` and the entries' `SteamId` (Task 3), the four-argument `AddAsync` and `MaxBotFightsPerMap` (Task 4), `CorrectionTargets.Resolve` over humans and bots (Task 5).
- Produces: `GameState.IsTracked(CCSPlayerController?)` (valid, not HLTV, and a human with a SteamID or a bot with a non-blank name) and `GameState.PlayerIdOf(CCSPlayerController?)` (`PlayerIds.Of(player.IsBot, player.SteamID, player.PlayerName)`). `IsHuman` is unchanged and used only where something reaches a client.
- Produces: `PlayerSession.IsBot => BotIds.IsBot(SteamId)`.
- Produces: `SessionManager.HumansOnline` (any session that isn't a bot), `Get` matching `IsTracked` and `session.SteamId == GameState.PlayerIdOf(player)`, `Connect` refusing with `BotTracking.MayStart(id, HumansOnline, sessions.Values.Select(s => s.SteamId))`, and `public IReadOnlyList<PlayerSession> BotSessions` and `public void EndBotSessions()` (a `DisconnectSlot` for each, so progress is kept in `recentlyLeft` and saved on its chain).
- Produces: `KillMapRecorder.PointOf(string map, CCSPlayerController killer, ulong killerId, CCSPlayerController victim, ulong victimId, WeaponInfo weapon, bool headshot)`, with ids from `Sessions.Get(x)?.SteamId ?? 0`.

- [ ] **Step 1: Wire tracking.** `SessionManager` as above. In `LifecycleHandlers`: after a human's `Connect` in `OnPlayerConnectFull`, connect every bot already on the server (`Utilities.GetPlayers().Where(p => p.IsBot)`). In `OnPlayerSpawn`, connect a bot that has no session before looking it up. In `OnClientDisconnect`, after `DisconnectSlot`, when `!Sessions.HumansOnline`: `FinishRecap(..., announce: false)` and `Site?.Forget` for each of `BotSessions`, then `EndBotSessions()` and `Bounties.Clear()`.

- [ ] **Step 2: Split the talking paths.** `ProgressionServices.Apply` still runs `CheckFeats`, adds `MapXp`, the recap, `Site.Record`, and `SaveRecords` for a bot, and skips Discord, `PanelPresenter.Invalidate`, `Floaties.Show`, and `announcer.Announce` when `session.IsBot`. `Announcer.Announce`, `PanelPresenter.Toggle`, `Close`, and `Tick`, and `RankTags.Refresh` and `Tick` return at once for a bot session. `Welcome` skips bots. `OnTick` still runs `TrackPawn` for bot sessions.

- [ ] **Step 3: Wire combat and the outputs.** `CombatHandlers.OnPlayerDeath` passes `killerUntracked: attacker.IsBot && (Sessions.Get(attacker) is null)`. Rewrite the "Only humans have sessions" comment above `ApplyRivalKill` to say bots count when tracked. `CreditKill` passes the session ids to `PointOf`. `KillMapRecorder.Flush` and `Shutdown` pass `Math.Max(100, settings.MaxBotFightsPerMap)`. `SiteReporter.ReadPlayers` sends a bot with a session as `LivePlayer(slot, session.SteamId, name, IsBot: true, session.Progress?.Rank ?? 0, kills, deaths, session.MapXp)`, and a bot without one as today. `AdminCommands.FindTarget` adds tracked bots as candidates with their session ids, `TellTarget` skips bots, and the usage lines mention `bot:<name>`. `PlayerCommands.ShowTopAsync` writes `services.Reports.PlayerName(entry.SteamId, entry.Name)`.

- [ ] **Step 4: Build and test**

Run: `dotnet build` then `dotnet test --project CounterStrikeProgression.Test`
Expected: no warnings, every test passes. The plugin has no tests, so check by reading each `GameState.IsHuman` caller left (`grep -n "IsHuman" CounterStrikeProgression -r`): each one must be about a client.

- [ ] **Step 5: Commit**

```bash
git add CounterStrikeProgression
git commit -m "[minor] Track bot personas while a human is on, and talk only to humans"
```

### Task 8: Mark bots on every page and the share card

**Files:**
- Modify: `CounterStrikeProgression.Site/wwwroot/site.js`, `livemap.js`, `heatmaps.js`, `site.css`, `CounterStrikeProgression.Site/Cards/PlayerCard.cs`, `Cards/CardRenderer.cs`, `CardEndpoints.cs`, `README.md`, `CLAUDE.md`
- Test: `CounterStrikeProgression.Test/CardTests.cs`

**Interfaces:**
- Consumes: `BotIds` (Task 1), bot ids served by Task 6.
- Produces: `PlayerCard.IsBot` (from `BotIds.IsBot` of the card's id), drawn by `CardRenderer` as the pill, and the Open Graph title `"<name> [BOT]"` in `CardEndpoints`.
- Produces in `site.js`: `const BOT_ID = /^100000000\d{8}$/;`, `function isBotId(id)`, and `function botTag()` returning `el("span", "bot-tag", "BOT")`. `playerLink` appends `botTag()` after the name for a bot id, which covers ranks, ladder, leaderboards, seasons, rivals, records, the feed, and the compare sides.

- [ ] **Step 1: Write the failing test** `CardTests.Card_ForABotPersona_IsMarkedAsABot`: the card built for stored bot progress `BotIds.For("Muhlik")` has `IsBot == true`, a human's has `IsBot == false`, and the rendered PNG for the bot is 1200 by 630.

- [ ] **Step 2: Run it and see it fail**

Run: `dotnet test --project CounterStrikeProgression.Test --filter "FullyQualifiedName~CardTests"`
Expected: build error, `PlayerCard.IsBot` doesn't exist.

- [ ] **Step 3: Implement the card,** then the pages:
  - `site.js`: the player page header appends `botTag()` after `player.name`, and `document.title` adds ` [BOT]`. The scoreboard treats a bot with a non-zero `steamId` like a human (`playerLink`, `rankBadge(player.rank)`, `formatXp(player.mapXp)`, feat icons) and keeps today's plain name, `bot` badge, and `—` for a bot with id `"0"`. The player page's and ranks page's `snapshot` handlers drop `!p.isBot` and keep `id !== "0"`. The compare picker's options and its table headers and title add ` [BOT]` for bot ids.
  - `livemap.js`: a bot marker whose `steamId` matches the 17-digit pattern gets the same `href` as a human's label, keeping its bot pin and label style, plus the `bot-tag` pill.
  - `heatmaps.js`: the player name and title get the pill and ` [BOT]`.
  - `site.css`: `.bot-tag` with the look in **Builder decisions**.

- [ ] **Step 4: Run it and see it pass,** then check the pages by hand: run the site against an in-memory store with one human and one bot persona, and confirm the pill on the player page, ranks, leaderboards, seasons, compare, rivals, the scoreboard, and the live map, and that the home feed has no bot lines.

- [ ] **Step 5: Document.** `README.md`: a "Bot personas" section (bots earn like humans while a human is on, their ids, what they're left out of, `bot:<name>` in the correction commands). `CLAUDE.md`: `Core/Players/BotIds.cs` in Key Files, `Rivalry` and `BountyBoard` and `Core/Heatmaps/` lines updated for bot personas and the two caps, and the Site row's sentence about kills between two bots corrected.

- [ ] **Step 6: Commit**

```bash
git add CounterStrikeProgression.Site CounterStrikeProgression.Test README.md CLAUDE.md
git commit -m "[minor] Mark bot personas on every page and the share card"
```
