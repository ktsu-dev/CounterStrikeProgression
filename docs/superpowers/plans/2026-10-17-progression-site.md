# Progression site implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A public, read-only, live site at `cs2-progression.ktsu.dev` with the live match, a skill shot feed, server ranks, and player pages, fed by the CounterStrikeProgression plugin.

**Architecture:** The plugin collects a batch every 2 seconds on the game thread (the server snapshot, new feed entries, and changed lifetime rows for online humans) and hands it to a bounded, drop-oldest sender that POSTs it from the thread pool. An ASP.NET Core site in this repo receives batches on an in-cluster-only port, keeps live state in memory, overlays live rows on what it reads from the MariaDB, and pushes changes to browsers over server-sent events. The pages are static HTML and plain JavaScript in the game HUD style. Terraform in the homelab repo deploys the site, a read-only database user, the public route, DNS, and a homepage tile.

**Tech stack:** .NET 10, `ktsu.Sdk` 2.31.2, ASP.NET Core minimal APIs, System.Text.Json, MySqlConnector and Dapper (already used), MSTest, SDK container support, GHCR, Terraform with the Kubernetes provider, Traefik IngressRoutes.

**Spec:** `docs/superpowers/specs/2026-10-17-progression-site-design.md`

## Global constraints

- Work on branch `feat/progression-site` in `C:/dev/ktsu-dev/CounterStrikeProgression`, and merge it with one `[minor]` commit at the end. Pushing task commits to `main` races CI's metadata push.
- C# style per `CLAUDE.md`: tabs, CRLF, file-scoped namespaces, usings inside the namespace, braces always, explicit accessibility, no `this.`, nullable on, warnings as errors, `Ensure.NotNull()` for argument checks, MSTest with semantic asserts.
- No global suppressions. Use targeted `[SuppressMessage]` attributes with justifications.
- No journaling comments. Commit messages never carry a Co-Authored-By line.
- Core and the site target `net10.0` only (`<TargetFrameworks></TargetFrameworks>`, as Core does today).
- Nothing new is packed to NuGet. `Directory.Build.targets` already switches packing off for every non-test project, so it covers the site.
- The plugin's release zip stays plugin-only. The site ships as the image `ghcr.io/ktsu-dev/counterstrikeprogression-site:<version>`.
- With `Site.Url` empty (the default), the plugin sends nothing and behaves exactly as it does today.
- The plugin never waits on the site. All network work runs on the thread pool, and a full queue drops the oldest batch.
- The site reads the database only, with a user that has `SELECT` on `csp_players`, `csp_weapons`, and `csp_challenges` and nothing else. The site never calls `IProgressionStore.InitializeAsync` or `SaveAsync`.
- The ingest endpoint listens on port 8081 only. The public route targets port 8080 only.
- Look: backgrounds `#0d0f12` and `#15181d`, a monospace face, orange `#f2a33a` for headers and rank badges, gold `#f2c94c` for highlights, and green `#4cd17a` for online. It works at phone width.
- The feed shows the last 50 entries, and that history survives a site restart.
- Player pages are addressed by SteamID64, at `/players/{steamId}`.

## Review focus

1. **The site is down or hangs while players are on.** The game must not stall. `LiveSender.TryEnqueue` returns at once, and a hung POST times out after 3 seconds. Tested in Task 2.
2. **A player name containing HTML** such as `<img src=x onerror=alert(1)>`. The public pages must show it as text. The pages set names with `textContent` only, and Task 8 has a manual check that posts that name.
3. **A batch arriving after a newer one,** or a lifetime row that's older than what the database holds. Counters only grow, so merging takes the larger value field by field, and an older snapshot never replaces a newer one. Tested in Tasks 5 and 6.
4. **A bad player id,** non-numeric or unknown at `/players/{id}`. The API returns 404 and the page shows "Player not found", never a 500. Tested in Task 7.
5. **A map change.** The scoreboard resets and shows the new map, and this map's XP starts from zero. Tested in Task 5. Task 4 resets the plugin's per-map XP.

---

### Task 1: Live wire contracts, feed entries, and change tracking in Core

**Files:**
- Create: `CounterStrikeProgression.Core/Live/LiveContracts.cs`
- Create: `CounterStrikeProgression.Core/Live/LiveJson.cs`
- Create: `CounterStrikeProgression.Core/Live/FeedBuilder.cs`
- Create: `CounterStrikeProgression.Core/Live/GameModes.cs`
- Create: `CounterStrikeProgression.Core/Live/ProgressChangeTracker.cs`
- Modify: `CounterStrikeProgression.Core/Storage/ProgressSnapshot.cs` (add `CaptureAll`)
- Test: `CounterStrikeProgression.Test/LiveTests.cs`

**Interfaces:**
- Produces, all `internal` in namespace `ktsu.CounterStrikeProgression.Core.Live`:
  - `enum FeedKind { SkillShot = 0, RankUp, WeaponLevelUp, TierCompleted }`
  - `sealed record FeedEntry(DateTime At, ulong SteamId, string PlayerName, FeedKind Kind, string Title, string? Weapon, long Xp)`. `Weapon` is the weapon's display name.
  - `sealed record LivePlayer(int Slot, ulong SteamId, string Name, bool IsBot, int Rank, int Kills, int Deaths, double MapXp)`
  - `sealed record ServerSnapshot(DateTime SentAt, string Map, string GameMode, int MaxPlayers, IReadOnlyList<LivePlayer> Players)`
  - `sealed record LiveBatch(ServerSnapshot Snapshot, IReadOnlyList<FeedEntry> Feed, IReadOnlyList<ProgressSnapshot> Progress, ProgressionSettings Settings)`
  - `static class LiveJson { public static JsonSerializerOptions Options { get; } }`: web defaults, enums as strings.
  - `static class FeedBuilder { public static IReadOnlyList<FeedEntry> From(ProgressionResult result, ulong steamId, string playerName, DateTime at) }`
  - `static class GameModes { public static string Name(int gameType, int gameMode) }`
  - `sealed class ProgressChangeTracker { public ProgressSnapshot? Changes(PlayerProgress progress); public void Forget(ulong steamId); public void Reset(); }`
  - `ProgressSnapshot.CaptureAll(PlayerProgress progress)`: every row, ignoring and leaving untouched the `IsDirty` flags that saving relies on.

- [ ] **Step 1: Write the failing tests** in `LiveTests.cs`:
  - `FeedBuilder_SkillShotNotice_MakesSkillShotEntry`: a result with one `NoticeKind.SkillShot` notice (a skill shot from `SkillShotCatalog` and the AWP from `WeaponCatalog`, `Xp = 40`) gives one entry with `Kind == FeedKind.SkillShot`, `Title ==` the skill shot's `Name`, `Weapon == "AWP"`, and `Xp == 40`.
  - `FeedBuilder_RankUp_TitleNamesTheRank`: a `RankUp` notice with `Value = 5` gives `Title == "Rank up: " + RankLadder.Names[5]`.
  - `FeedBuilder_WeaponLevelUpAndTier_AreIncluded`: these give `Title == "AWP level 7"` and `Title == "<ChallengeName> III"` (using `PanelBuilder.Numeral`).
  - `FeedBuilder_NoNotices_GivesNothing`
  - `GameModes_KnownPairs`: `Name(1, 2) == "Deathmatch"`, `Name(0, 1) == "Competitive"`, `Name(0, 0) == "Casual"`, `Name(0, 2) == "Wingman"`, `Name(1, 0) == "Arms Race"`, and `Name(9, 9) == "Custom"`.
  - `ProgressChangeTracker_FirstCall_ReturnsEverything`, then `_NoChange_ReturnsNull`, then `_OneWeaponChanged_ReturnsOnlyThatRow` (only the changed `WeaponRow`, with `Player` null when the player row is unchanged).
  - `ProgressChangeTracker_Reset_ReturnsEverythingAgain` and `_Forget_ReturnsEverythingForThatPlayer`
  - `ProgressSnapshot_CaptureAll_LeavesDirtyFlagsAlone`: with `IsDirty` true on a player and on one weapon, the flags are still true afterwards, and clean rows are included too.
  - `LiveJson_RoundTripsABatch`: serialize and deserialize a `LiveBatch` and assert the fields match, including `FeedKind` written as `"SkillShot"`.

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test --filter "FullyQualifiedName~LiveTests"`. It should fail to build because the types don't exist.

- [ ] **Step 3: Implement the files above.** `ProgressChangeTracker` keeps the last sent `PlayerRow`, `WeaponRow`, and `ChallengeRow` per SteamId, keyed by weapon and by (weapon, challenge), and compares with record equality. For `PlayerRow`, leave `LastSeen` out of the comparison, so a player standing still doesn't count as a change. `GameModes` maps the pairs above, and anything else is "Custom".

- [ ] **Step 4: Run them and see them pass.** Run `dotnet test`. All tests pass, the existing 131 included.

- [ ] **Step 5: Commit.** `git add` the files, then commit with the message "Add live wire contracts and change tracking".

### Task 2: Bounded, drop-oldest batch sender in Core

**Files:**
- Create: `CounterStrikeProgression.Core/Live/LiveSender.cs`
- Test: `CounterStrikeProgression.Test/LiveSenderTests.cs`

**Interfaces:**
- Consumes: `LiveBatch` and `LiveJson.Options` from Task 1.
- Produces: `internal sealed class LiveSender : IDisposable` with:
  - Constructor: `LiveSender(Uri endpoint, string token, int queueLimit, TimeSpan timeout, Action<Exception> onFailure, HttpMessageHandler? handler = null)`
  - `public bool TryEnqueue(LiveBatch batch)`
  - `public Task StopAsync(TimeSpan wait)`
  - `public void Dispose()`
- Each POST goes to `endpoint` with header `Authorization: Bearer <token>` and a JSON body.

- [ ] **Step 1: Write the failing tests** with a fake `HttpMessageHandler`:
  - `Send_PostsJsonWithBearerToken`: the handler sees `POST`, the right URI, `Authorization` equal to `Bearer secret`, and a body that deserializes to the batch.
  - `TryEnqueue_ReturnsImmediately_WhenTheSiteHangs`: the handler never completes. Twenty `TryEnqueue` calls finish in under 100 ms.
  - `FullQueue_DropsOldest`: the handler is blocked, and the queue limit is 2. Enqueue batches A, B, C, D, then unblock. The handler receives A, which was already in flight, then C and D.
  - `HungRequest_TimesOutAndReportsFailure`: the timeout is 200 ms. `onFailure` is called with an `OperationCanceledException` or `TaskCanceledException`.
  - `Failures_AreReportedAtMostOncePerMinute`: the handler always throws. Five quick batches lead to exactly one `onFailure` call.

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test --filter "FullyQualifiedName~LiveSenderTests"`. It should fail to build.

- [ ] **Step 3: Implement `LiveSender`.** Use `Channel.CreateBounded<LiveBatch>(new BoundedChannelOptions(queueLimit) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true })` and one reader loop started with `Task.Run`. Give the `HttpClient` its own `Timeout = timeout`. Rate-limit failures with a `DateTime` of the last report, read through a `TimeProvider` passed as an optional last constructor parameter (default `TimeProvider.System`), so the once-a-minute test is deterministic. The reader catches everything, because nothing may escape onto the thread pool. Use a targeted CA1031 suppression with a justification, as `SessionManager.SaveAfterAsync` does.

- [ ] **Step 4: Run them and see them pass.** Run `dotnet test`.

- [ ] **Step 5: Commit** with the message "Add the bounded live batch sender".

### Task 3: Server rankings from the store

**Files:**
- Modify: `CounterStrikeProgression.Core/Storage/IProgressionStore.cs`
- Modify: `CounterStrikeProgression.Core/Storage/InMemoryProgressionStore.cs`
- Modify: `CounterStrikeProgression.Core/Storage/MySqlProgressionStore.cs`
- Test: `CounterStrikeProgression.Test/StorageTests.cs`

**Interfaces:**
- Produces:
  - `internal sealed record RankingEntry(ulong SteamId, string Name, long RankXp, int Rank, DateTime LastSeen)`
  - `IProgressionStore.RankingsAsync(int count, CancellationToken cancellationToken)`, returning `Task<IReadOnlyList<RankingEntry>>`. Results are ordered by `RankXp` descending, then `SteamId` ascending.

- [ ] **Step 1: Write the failing test.** `InMemory_Rankings_OrderedByXpThenSteamId`: three saved players, two of them tied on XP. They come back in the expected order with the right `LastSeen`, and `count` caps the list.
- [ ] **Step 2: Run it and see it fail.** Run `dotnet test --filter "FullyQualifiedName~StorageTests"`.
- [ ] **Step 3: Implement it in both stores.** The MySQL query is `SELECT steam_id AS SteamId, name AS Name, rank_xp AS RankXp, rank_index AS Rank, last_seen AS LastSeen FROM csp_players ORDER BY rank_xp DESC, steam_id ASC LIMIT @count`. Its `DateTime` values come back as UTC, the same way `LoadAsync` reads them.
- [ ] **Step 4: Run it and see it pass.** Run `dotnet test`.
- [ ] **Step 5: Commit** with the message "Add server rankings to the progression store".

### Task 4: The plugin sends live batches

**Files:**
- Modify: `CounterStrikeProgression/PluginConfig.cs` (add `SiteSettings` and a `Site` property)
- Modify: `CounterStrikeProgression/PlayerSession.cs` (add `MapXp`)
- Create: `CounterStrikeProgression/Site/SiteReporter.cs`
- Modify: `CounterStrikeProgression/ProgressionServices.cs` (record feed entries and count XP this map in `Apply`)
- Modify: `CounterStrikeProgression/ProgressionPlugin.cs` (create, time, and dispose the reporter)
- Modify: `CounterStrikeProgression/LifecycleHandlers.cs` (reset per-map counts on map end, and forget players on disconnect)
- Modify: `README.md` (a `Site` section in the configuration docs)

**Interfaces:**
- Consumes: Task 1 (`LiveBatch`, `ServerSnapshot`, `LivePlayer`, `FeedBuilder`, `GameModes`, `ProgressChangeTracker`), Task 2 (`LiveSender`).
- Produces:
  - `public sealed class SiteSettings`, with:
    - `string Url { get; set; } = ""`
    - `string Token { get; set; } = ""`
    - `double IntervalSeconds { get; set; } = 2`
    - `int QueueLimit { get; set; } = 20`
    - `int FullResendEvery { get; set; } = 15`
  - `PluginConfig.Site`, of type `SiteSettings`, defaulting to `new()`.
  - `PlayerSession.MapXp`, a `double` with get and set.
  - `internal sealed class SiteReporter : IDisposable`:
    - Constructor: `SiteReporter(PluginConfig config, SessionManager sessions, ILogger logger)`
    - `void Record(PlayerSession session, string name, ProgressionResult result)`
    - `void Collect()`
    - `void Forget(ulong steamId)`
    - `void Dispose()`
  - `ProgressionServices` gains `SiteReporter? Site { get; }`, which is null when `Config.Site.Url` is blank.

- [ ] **Step 1: Implement `SiteSettings` and `PlayerSession.MapXp`.** Use XML docs in the same style as `FloatieSettings`.

- [ ] **Step 2: Implement `SiteReporter`.**
  - `Record` adds `FeedBuilder.From(...)` to a pending list, capped at 200 with the oldest dropped.
  - `Collect` runs on the game thread from a repeating `AddTimer(Config.Site.IntervalSeconds, ...)`. It builds a `ServerSnapshot` from:
    - `Server.MapName`
    - `GameModes.Name(ConVar game_type, ConVar game_mode)`
    - `Server.MaxPlayers`
    - each connected `CCSPlayerController` from `Utilities.GetPlayers()`, with kills and deaths from `controller.ActionTrackingServices?.MatchStats`, and rank and `MapXp` from the session (0 for bots and players still loading)
  - It then takes `ProgressChangeTracker.Changes` for each session with `Progress`, calls `Reset()` every `FullResendEvery` batches, and hands the batch to `LiveSender.TryEnqueue`.
  - Failures log through a new `Log.SiteFailed` source-generated message in `Log.cs`.

- [ ] **Step 3: Wire it in.**
  - `ProgressionServices.Apply` adds `result.XpEarned` to `session.MapXp` and calls `Site?.Record(...)`.
  - `OnMapEnd` sets `MapXp = 0` for every session and calls `Collect()` straight after the next map starts. Use `Listeners.OnMapStart` plus `AddTimer(1, Collect)`, so the scoreboard switches maps at once.
  - `OnClientDisconnect` calls `Site?.Forget(steamId)`.
  - `Unload` disposes the reporter after `sessions.Shutdown`.

- [ ] **Step 4: Build and test.** Run `dotnet build -c Release`, then `dotnet test`. It builds with no warnings, and all tests pass.

- [ ] **Step 5: Commit** with the message "Send live batches to the progression site".

### Task 5: Site project, live state, and feed history

**Files:**
- Create: `CounterStrikeProgression.Site/CounterStrikeProgression.Site.csproj`
- Create: `CounterStrikeProgression.Site/AssemblyInfo.cs`
- Create: `CounterStrikeProgression.Site/SiteOptions.cs`
- Create: `CounterStrikeProgression.Site/Live/LiveState.cs`
- Create: `CounterStrikeProgression.Site/Live/FeedStore.cs`
- Create: `CounterStrikeProgression.Site/Live/ProgressMerge.cs`
- Modify: `CounterStrikeProgression.Core/AssemblyInfo.cs` (add `InternalsVisibleTo` for the site's assembly name)
- Modify: `CounterStrikeProgression.sln` (add the project)
- Modify: `CounterStrikeProgression.Test/CounterStrikeProgression.Test.csproj` (reference the site)
- Test: `CounterStrikeProgression.Test/SiteLiveTests.cs`

**Interfaces:**
- Consumes: Task 1 contracts. `ProgressSnapshot.Rebuild` already exists.
- Produces, `internal` in `ktsu.CounterStrikeProgression.Site`:
  - `sealed class SiteOptions` with:
    - `ConnectionString` (string, default "")
    - `Token` (string, default "")
    - `DataPath` (string, default "/data")
    - `OfflineAfterSeconds` (double, default 15)
    - `FeedLength` (int, default 50)
  - `sealed class FeedStore(string path)`:
    - `IReadOnlyList<FeedEntry> Load()`
    - `void Save(IReadOnlyList<FeedEntry> entries)`, writing a temporary file and moving it over the old one
  - `static class ProgressMerge`:
    - `static PlayerProgress Merge(PlayerProgress? stored, ProgressSnapshot? live)`. Every counter is the maximum of the two (`RankXp`, `Rank`, `Xp`, `Level`, `Kills`, `Headshots`, `Hits`, `Shots`, `Count`, `Tier`). `Name` comes from live when present. `LastSeen` is the maximum, and `FirstSeen` the minimum.
    - `static ProgressSnapshot Combine(ProgressSnapshot older, ProgressSnapshot newer)`, for building the overlay.
  - `sealed record LiveChanges(bool SnapshotChanged, IReadOnlyList<FeedEntry> NewFeed, IReadOnlyList<ulong> ChangedPlayers)`
  - `sealed class LiveState(SiteOptions options, FeedStore feed, TimeProvider time)`:
    - `LiveChanges Apply(LiveBatch batch)`
    - `ServerSnapshot? Snapshot { get; }`
    - `bool IsOnline { get; }`
    - `IReadOnlyList<FeedEntry> Feed { get; }`, newest first, at most `FeedLength`
    - `ProgressSnapshot? Overlay(ulong steamId)`
    - `ProgressionSettings Settings { get; }`, which is Core's defaults until a batch arrives
    - `bool IsPlaying(ulong steamId)`

- [ ] **Step 1: Create the project.** Mirror `C:/dev/ktsu-dev/GitLfsCache/GitLfsCache.Service/GitLfsCache.Service.csproj`:
  - `Microsoft.NET.Sdk` plus `ktsu.Sdk`
  - `net10.0` only, with `OutputType Exe`
  - `<FrameworkReference Include="Microsoft.AspNetCore.App" />`
  - a reference to Core
  - `EnableSdkContainerSupport` with `ContainerBaseImage mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`, `ContainerRuntimeIdentifier linux-x64`, `ContainerRepository ktsu-dev/counterstrikeprogression-site`, and `ContainerPort` 8080 and 8081

  Get the assembly name with `dotnet msbuild CounterStrikeProgression.Site -getProperty:AssemblyName` and put exactly that in Core's `InternalsVisibleTo`. Add `InternalsVisibleTo("ktsu.CounterStrikeProgression.Test")` to the site. A `Program.cs` that only builds and runs an empty `WebApplication` is enough for this task.

- [ ] **Step 2: Write the failing tests.** Use a fake `TimeProvider`.
  - `Apply_FirstBatch_SetsSnapshotAndOnline`
  - `Apply_OlderSnapshot_IsIgnored`: the batch's `SentAt` is earlier than the current one. The snapshot stays the same, and `SnapshotChanged` is false.
  - `Apply_NewMap_ReplacesPlayersWholesale`: the second batch's map is `de_mirage` with one player. `Snapshot.Players.Count == 1`.
  - `IsOnline_FalseAfterOfflineAfterSeconds`: 16 seconds after the last batch it's false, and at 14 seconds it's true.
  - `Feed_KeepsNewestFiftyNewestFirst`: apply 60 entries. Exactly 50 are kept, and the first is the latest.
  - `Feed_SurvivesRestart`: a second `LiveState` built on the same `FeedStore` path starts with the same 50 entries.
  - `FeedStore_MissingOrCorruptFile_LoadsEmpty`
  - `ProgressMerge_TakesLargerValuesFieldByField`: the stored data has `Kills = 10, Shots = 500`, and live has `Kills = 12, Shots = 480`. The result is `Kills = 12, Shots = 500`.
  - `ProgressMerge_LiveOnlyWeapon_IsAdded` and `ProgressMerge_NullStored_UsesLive`
  - `Apply_ProgressRows_CombineIntoOverlay_AndReportChangedPlayers`

- [ ] **Step 3: Run them and see them fail.** Run `dotnet test --filter "FullyQualifiedName~SiteLiveTests"`.

- [ ] **Step 4: Implement `SiteOptions`, `FeedStore`, `ProgressMerge`, and `LiveState`.** `LiveState` locks around its state, because ingest and reads run on different threads. `FeedStore.Save` runs only when a batch adds feed entries.

- [ ] **Step 5: Run them and see them pass.** Run `dotnet test`.

- [ ] **Step 6: Commit** with the message "Add the progression site's live state and feed history".

### Task 6: Player and ranks views

**Files:**
- Create: `CounterStrikeProgression.Site/Views/ViewModels.cs`
- Create: `CounterStrikeProgression.Site/Views/PlayerViewBuilder.cs`
- Create: `CounterStrikeProgression.Site/Views/RanksViewBuilder.cs`
- Test: `CounterStrikeProgression.Test/SiteViewTests.cs`

**Interfaces:**
- Consumes: Core's `RankLadder`, `WeaponLevelCurve`, `ChallengeCatalog`, `WeaponCatalog`, and `RankingEntry` (Task 3). From Task 5: `ProgressMerge` and `LiveState.Settings`.
- Produces:
  - `sealed record ChallengeView(string Id, string Name, long Count, int Tier, int TierCount, int? NextTarget)`
  - `sealed record WeaponView(string Key, string DisplayName, int Level, int MaxLevel, double LevelProgress, long Kills, double HeadshotPercent, double Accuracy, IReadOnlyList<ChallengeView> Challenges)`
  - `sealed record PlayerView(string SteamId, string Name, int Rank, string RankName, string? NextRankName, long RankXp, long XpToNextRank, double RankProgress, DateTime FirstSeen, DateTime LastSeen, bool Online, IReadOnlyList<WeaponView> Weapons)`
  - `sealed record RankRow(int Position, string SteamId, string Name, int Rank, string RankName, long RankXp, DateTime LastSeen, bool Online)`
  - `static class PlayerViewBuilder { static PlayerView Build(PlayerProgress merged, ProgressionSettings settings, bool online) }`
  - `static class RanksViewBuilder { static IReadOnlyList<RankRow> Build(IReadOnlyList<RankingEntry> stored, Func<ulong, ProgressSnapshot?> overlay, Func<ulong, bool> online) }`
- SteamIds are strings in the views, because JavaScript numbers lose precision above 2^53.

- [ ] **Step 1: Write the failing tests.**
  - `Player_RankProgressUsesSettingsLadder`: with `FirstRankXp = 1000` and `RankGrowth = 1.25`, an XP halfway between thresholds gives `RankProgress` of about 0.5 (delta 0.01). `XpToNextRank` equals the next threshold minus `RankXp`.
  - `Player_TopRank_HasNoNextRank`: `NextRankName` is null, and `RankProgress == 1`.
  - `Player_WeaponStats`: `Kills = 50, Headshots = 20, Hits = 300, Shots = 1000` gives `HeadshotPercent == 40` and `Accuracy == 30`. `Shots = 0` gives `Accuracy == 0`, never NaN.
  - `Player_WeaponsOrderedByXpDescending`
  - `Player_UnknownWeaponKey_UsesKeyAsDisplayName`: a weapon `weapon_removed` that isn't in `WeaponCatalog` still shows, with `DisplayName == "weapon_removed"` and no challenges.
  - `Player_ChallengesListEveryDefinitionForTheWeapon`: unstarted ones show `Count = 0`, `Tier = 0`, and `NextTarget` equal to the first tier. Finished ones show `NextTarget == null`.
  - `Ranks_OverlayRaisesXpAndReorders`: a stored player at 900 XP with a live overlay at 1,500 moves above a stored player at 1,200. Positions are renumbered from 1.

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test --filter "FullyQualifiedName~SiteViewTests"`.
- [ ] **Step 3: Implement the builders** in the files above. Build `RankLadder`, `WeaponLevelCurve`, and `ChallengeCatalog` from the `ProgressionSettings` passed in, never from defaults.
- [ ] **Step 4: Run them and see them pass.** Run `dotnet test`.
- [ ] **Step 5: Commit** with the message "Add player and ranks views".

### Task 7: Site endpoints, ingest, and the live stream

**Files:**
- Create: `CounterStrikeProgression.Site/Program.cs` (replacing Task 5's stub)
- Create: `CounterStrikeProgression.Site/Live/IngestAuth.cs`
- Create: `CounterStrikeProgression.Site/Live/Broadcaster.cs`
- Create: `CounterStrikeProgression.Site/Data/ProgressReader.cs`
- Test: `CounterStrikeProgression.Test/SiteEndpointTests.cs`

**Interfaces:**
- Consumes: Tasks 3, 5, and 6.
- Produces:
  - `static class IngestAuth { static bool IsAuthorized(string? authorizationHeader, string token) }`. Without a token it's always false. Otherwise it compares `Bearer <token>` with `CryptographicOperations.FixedTimeEquals`.
  - `sealed class Broadcaster`:
    - `ChannelReader<string> Subscribe(CancellationToken cancellationToken)`
    - `void Publish(string eventName, object payload)`
    - Each subscriber has a bounded channel of 100 that drops its oldest message, so a slow browser can't hold up the rest.
  - `sealed class ProgressReader(IProgressionStore store)`:
    - `Task<IReadOnlyList<RankingEntry>> RankingsAsync()`, cached for 10 seconds
    - `Task<PlayerProgress?> PlayerAsync(ulong steamId)`, cached for 10 seconds per player
  - `static bool TryParseSteamId(string? text, out ulong steamId)`, on `ProgressReader`. It accepts only an all-digit SteamID64 between 76561197960265729 and 76561202255233023.
  - HTTP on port 8080:
    - `GET /`, `/ranks`, and `/players/{id}`, which serve `wwwroot/index.html`, `ranks.html`, and `player.html`
    - `GET /api/live`, which returns `{ online, snapshot, feed }`
    - `GET /api/ranks`, which returns `RankRow[]`
    - `GET /api/players/{id}`, which returns `PlayerView`. A bad or unknown id returns 404.
    - `GET /api/stream`, the SSE stream, with events `snapshot` (a `ServerSnapshot`), `feed` (a `FeedEntry`), `progress` (an array of changed SteamIds as strings), and `online` (a bool, sent when it flips)
    - `GET /healthz`
  - HTTP on port 8081: `POST /api/ingest` only, returning 401 without the token and 204 on success.
  - Configuration is bound from the `Site` section, so the environment variables are `Site__ConnectionString`, `Site__Token`, and so on.

- [ ] **Step 1: Write the failing tests.**
  - `IngestAuth_RejectsMissingWrongAndEmptyToken`, and `IngestAuth_AcceptsExactBearerToken`
  - `Broadcaster_DeliversToEverySubscriber`, and `Broadcaster_SlowSubscriberDropsOldest`: one subscriber never reads, and 150 publishes leave the other subscriber with all 150.
  - `TryParseSteamId`: valid for `"76561197993183173"`, and invalid for `""`, `"abc"`, `"1"`, `"76561197993183173x"`, and a 20-digit number.
  - `PlayerEndpoint_UnknownId_Returns404`. Test it through a `ProgressReader` over `InMemoryProgressionStore`, calling the same handler method the route uses (`Endpoints.PlayerAsync(...)`, returning `IResult`). Assert a `NotFound` result.

- [ ] **Step 2: Run them and see them fail.** Run `dotnet test --filter "FullyQualifiedName~SiteEndpointTests"`.

- [ ] **Step 3: Implement the files.**
  - Put Kestrel on both ports with `builder.WebHost.ConfigureKestrel(o => { o.ListenAnyIP(8080); o.ListenAnyIP(8081); })`, and map ingest with `.RequireHost("*:8081")`.
  - After `LiveState.Apply`, ingest publishes `snapshot` when it changed, each new feed entry, and `progress` when players changed.
  - A 1-second `PeriodicTimer` background service publishes `online` when `LiveState.IsOnline` flips.
  - The SSE handler sets `Content-Type: text/event-stream` and `Cache-Control: no-cache`, writes `retry: 3000`, and sends a `: ping` comment every 15 seconds so proxies don't drop it.
  - Put the endpoint handlers in a static `Endpoints` class, so tests can call them.

- [ ] **Step 4: Run them and see them pass.** Run `dotnet test`.

- [ ] **Step 5: Try a local smoke run.** Run the site with `Site__Token=dev` and no database, then:
  - `curl -s localhost:8080/healthz` returns 200.
  - `curl -s -X POST localhost:8081/api/ingest -H "Authorization: Bearer dev" -H "Content-Type: application/json" -d @sample-batch.json` returns 204, using a batch written with `LiveJson` in a scratch test.
  - `curl -s localhost:8080/api/live` shows the batch's map.
  - `curl -s -X POST localhost:8080/api/ingest` returns 404.

  The ranks and player endpoints need the database, so they're tested in Task 10.

- [ ] **Step 6: Commit** with the message "Add the progression site endpoints and live stream".

### Task 8: Pages in the game HUD style

**Files:**
- Create: `CounterStrikeProgression.Site/wwwroot/index.html` (live match and feed)
- Create: `CounterStrikeProgression.Site/wwwroot/ranks.html`
- Create: `CounterStrikeProgression.Site/wwwroot/player.html`
- Create: `CounterStrikeProgression.Site/wwwroot/site.css`
- Create: `CounterStrikeProgression.Site/wwwroot/site.js`

**Interfaces:**
- Consumes: the Task 7 endpoints and events.

- [ ] **Step 1: Write `site.css`** with the colors and font from Global constraints. A top bar with an orange bottom border, tables with uppercase grey headers, rank badges, gold-highlighted feed rows, and a green `online` mark. One column under 700 px, and the live page's scoreboard and feed side by side above that. Follow the mockups in `.superpowers/brainstorm/*/content/visual-style.html` (option A) and `player-and-ranks.html`.

- [ ] **Step 2: Write `site.js`**, shared by all three pages:
  - It holds one `EventSource('/api/stream')` and the helpers `el(tag, className, text)`, `formatXp`, and `ago(date)`.
  - Every player name and every other value from the API goes in with `textContent`. Nothing from the API goes through `innerHTML`.

- [ ] **Step 3: Write the three pages.**
  - **Live** (`/`): the top bar shows the map, mode, players/max, and an `OFFLINE` state when `online` is false. The scoreboard is sorted by kills. Bots show "bot" for rank and "—" for XP. Human names link to `/players/{steamId}`. The feed list starts from `/api/live` and adds `feed` events at the top, capped at 50. Skill shots, rank-ups, level-ups, and tiers use the gold row style.
  - **Ranks** (`/ranks`): the table from `/api/ranks`. A `progress` event whose list includes a SteamId on the page triggers a refetch, at most once every 2 seconds.
  - **Player** (`/players/{id}`): the header has the name, the online mark, and a link to `https://steamcommunity.com/profiles/{id}`. Then the rank, the progress bar, "XP to next rank", and first seen. Next, the weapons table with level and a small level bar, kills, headshot %, and accuracy. Each weapon's challenge tiers come last, with finished tiers in gold. A 404 shows "Player not found". A `progress` event that includes this id refetches at most once every 2 seconds.
  - Every page has a nav with Live, Ranks, and the server name. Each page sets a `<title>`.

- [ ] **Step 4: Check by hand.** Run the site locally with the Task 7 sample batch, using a player named `<img src=x onerror=alert(1)>`.
  - The live page shows that name as literal text, and no alert fires.
  - Posting a second batch with a new feed entry makes it appear without a reload.
  - At 375 px wide (browser dev tools), nothing scrolls sideways.

- [ ] **Step 5: Commit** with the message "Add the progression site pages".

### Task 9: Image publishing, docs, and merge

**Files:**
- Modify: `.github/workflows/dotnet.yml` (add a step after "Package Plugin")
- Modify: `README.md` (a "Progression site" section: what it shows, the `Site` plugin config, the site's environment variables, and ports 8080 and 8081)
- Modify: `CLAUDE.md` (the site project and its build and test commands)

- [ ] **Step 1: Add a "Publish Site Image" step** with `if: steps.pipeline.outputs.should_release == 'true'`, in pwsh like "Package Plugin":
  - First, `echo "${{ github.token }}" | docker login ghcr.io -u "${{ github.actor }}" --password-stdin`.
  - Then `dotnet publish CounterStrikeProgression.Site/CounterStrikeProgression.Site.csproj -c Release /t:PublishContainer -p:ContainerRegistry=ghcr.io "-p:ContainerImageTags=\"$version;latest\""`, with `$version` taken from `steps.pipeline.outputs.version`.
  - The job already has `packages: write`.

- [ ] **Step 2: Verify locally.** Run `dotnet build -c Release`, then `dotnet test`. Then run `dotnet publish CounterStrikeProgression.Site -c Release /t:PublishContainer -p:ContainerArchiveOutputPath=./site.tar.gz`, which builds the image to a tarball without Docker. All three succeed.

- [ ] **Step 3: Write the docs.**

- [ ] **Step 4: Merge.**
  - Push the branch and open a PR to `main`, titled "[minor] Add the live progression site".
  - Merge it with that title as the squash commit message, so the release is a minor version.
  - Wait for the "Analyze & Release" run. It should end with a new GitHub release with the plugin zip, plus a `ghcr.io/ktsu-dev/counterstrikeprogression-site:<version>` image.

- [ ] **Step 5: Make the new GHCR package public. This step is the user's,** in the GitHub UI under ktsu-dev, Packages, counterstrikeprogression-site, Package settings, Change visibility. New org packages start private, and the homelab pulls without credentials. Ask the user to do it, and wait for confirmation.

### Task 10: Homelab deployment

Work in `C:/dev/matt-edmondson/homelab` on branch `feat/cs2-progression-site`, based on `main` after PR #31 merges. If #31 isn't merged yet, base it on `feat/cs2-progression-plugin`.

**Files:**
- Create: `terraform/cs2-progression-site.tf`
- Modify: `terraform/cs2.tf` (the `Site` section in `local.cs2_progression_config`)
- Modify: `terraform/ingress.tf` (a public IngressRoute)
- Modify: `terraform/ingress-local.tf` (the `.local` bypass entry)
- Modify: `terraform/dns.tf` (the subdomain entry)
- Modify: `terraform/homepage.tf` (a tile next to the CS2 tiles)
- Modify: `terraform/terraform.tfvars.example`, `terraform/Makefile` (an `apply-cs2-progression-site` target), and `CLAUDE.md` (a file entry)

**Interfaces:**
- Variables:
  - `cs2_progression_site_enabled` (bool, default false)
  - `cs2_progression_site_subdomain` (default "cs2-progression")
  - `cs2_progression_site_db_password` (sensitive)
  - `cs2_progression_site_token` (sensitive)
- `local.cs2_progression_site = local.cs2_admin && var.cs2_progression_site_enabled`. The site needs the admin stack's MariaDB.
- The image tag is `var.cs2_plugin_versions.progression`, so one version bump moves the plugin and the site together.
- Everything lives in namespace `cs2`, next to the MariaDB.

- [ ] **Step 1: Write `cs2-progression-site.tf`.**
  - **Database user:** a `kubernetes_job` named `cs2-progression-site-db-<hash of the password>`. It runs the MariaDB image's client against the existing MariaDB Service as root, the way `cs2_panel_bootstrap` connects, and executes:
    - `CREATE USER IF NOT EXISTS 'csp_site'@'%' IDENTIFIED BY '<pw>'`
    - `ALTER USER 'csp_site'@'%' IDENTIFIED BY '<pw>'`
    - a `GRANT SELECT ON <db>.<table> TO 'csp_site'@'%'` for each of `csp_players`, `csp_weapons`, and `csp_challenges`
  - **Secret:** a `kubernetes_secret` with `Site__ConnectionString` (user `csp_site` on the MariaDB Service, database as in `cs2.tf`) and `Site__Token`.
  - **Storage:** a 1Gi Longhorn PVC for `/data`.
  - **Deployment:** 1 replica with `Recreate` and the image above. Requests 25m CPU and 64Mi, limit 256Mi. `envFrom` the secret, the PVC at `/data`, and readiness and liveness on `/healthz` port 8080. The pod's env-hash annotation rolls it when the secret changes, the way `lily.tf` does.
  - **Service:** `cs2-progression-site`, ClusterIP, with port `http` 8080 and port `ingest` 8081.

- [ ] **Step 2: Add the `Site` section to `local.cs2_progression_config` in `cs2.tf`,** only when `local.cs2_progression_site` is true. Set `Url = "http://cs2-progression-site.cs2.svc.cluster.local:8081/api/ingest"` and `Token = var.cs2_progression_site_token`.

- [ ] **Step 3: Add the route, DNS, and tile.**
  - The public IngressRoute is `Host(cs2-progression.<domain>)` to the Service's port 8080, with the crowdsec middleware only, no oauth. Copy `ingressroute_lily`.
  - Add the `ingress-local.tf` entry, the `dns.tf` entry, and a homepage tile named "CS2 Progression" with the site's URL, placed next to the CS2 tiles.
  - Add the docs entries.

- [ ] **Step 4: Check it.** Run `terraform fmt`, then `make validate` from `terraform/` (export the ARM_* variables first). Validation passes.

- [ ] **Step 5: Apply and verify.** First set `cs2_progression_site_enabled = true` and the two secrets in `terraform.tfvars`.
  - Before applying, check the server's player count with A2S. The `cs2.tf` change restarts the game server, so ask the user before restarting while players are on.
  - Apply with `-target` on the new resources, `kubernetes_secret` for the plugin config, `kubernetes_deployment.cs2`, the IngressRoute, the local route, and the DNS record. CLAUDE.md notes that newer apps need `-target` for routes and DNS.
  - Then check each of these:
    - The db-user job completes.
    - The site pod is Ready.
    - `curl -s https://cs2-progression.ktsu.dev/healthz` returns 200.
    - `/api/ranks` lists real players.
    - `/api/players/76561197993183173` returns Katsu.
    - Within 5 seconds of the server's plugin loading, `/api/live` shows the current map with `online: true`.
    - `curl -s -X POST https://cs2-progression.ktsu.dev/api/ingest` returns 404.

- [ ] **Step 6: Commit and open the PR.** Commit with the message "Add the CS2 progression site", push the branch, and open a PR to `main`.
