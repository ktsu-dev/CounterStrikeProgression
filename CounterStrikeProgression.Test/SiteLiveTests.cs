// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Live;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class SiteLiveTests
{
	private static readonly DateTime Start = new(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);

	private string directory = string.Empty;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-site-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private string FeedPath => Path.Combine(directory, "feed.json");

	private static ServerSnapshot Server(DateTime at, string map = "de_dust2", params LivePlayer[] players) =>
		new(at, map, "Casual", 10, players);

	private static LiveBatch Batch(ServerSnapshot snapshot, IReadOnlyList<FeedEntry>? feed = null, IReadOnlyList<ProgressSnapshot>? progress = null, ProgressionSettings? settings = null) =>
		new(snapshot, feed ?? [], progress ?? [], settings ?? new ProgressionSettings());

	private static FeedEntry Entry(int index) =>
		new(Start.AddSeconds(index), TestHelpers.SteamId, "Tester", FeedKind.SkillShot, $"Shot {index}", null, 10);

	private static LivePlayer Human(ulong steamId) => new(0, steamId, "Tester", false, 1, 0, 0, 0);

	private LiveState NewState(ManualTime time, SiteOptions? options = null) =>
		new(options ?? new SiteOptions(), new FeedStore(FeedPath), time, NullLogger<LiveState>.Instance);

	[TestMethod]
	public void Apply_FirstBatch_SetsSnapshotAndOnline()
	{
		ManualTime time = new();
		LiveState state = NewState(time);
		Assert.IsFalse(state.IsOnline);
		Assert.IsNull(state.Snapshot);

		LiveChanges changes = state.Apply(Batch(Server(Start)));

		Assert.IsTrue(changes.SnapshotChanged);
		Assert.IsTrue(state.IsOnline);
		Assert.AreEqual("de_dust2", state.Snapshot?.Map);
	}

	[TestMethod]
	public void Apply_OlderSnapshot_IsIgnored()
	{
		LiveState state = NewState(new ManualTime());
		state.Apply(Batch(Server(Start, "de_dust2")));

		LiveChanges changes = state.Apply(Batch(Server(Start.AddSeconds(-5), "de_mirage")));

		Assert.IsFalse(changes.SnapshotChanged);
		Assert.AreEqual("de_dust2", state.Snapshot?.Map);
	}

	[TestMethod]
	public void Apply_NewMap_ReplacesPlayersWholesale()
	{
		LiveState state = NewState(new ManualTime());
		state.Apply(Batch(Server(Start, "de_dust2", Human(1), Human(2), Human(3))));

		state.Apply(Batch(Server(Start.AddSeconds(1), "de_mirage", Human(2))));

		Assert.AreEqual("de_mirage", state.Snapshot?.Map);
		Assert.HasCount(1, state.Snapshot!.Players);
	}

	[TestMethod]
	public void IsOnline_FalseAfterOfflineAfterSeconds()
	{
		ManualTime time = new();
		LiveState state = NewState(time);
		state.Apply(Batch(Server(Start)));

		time.Advance(TimeSpan.FromSeconds(14));
		Assert.IsTrue(state.IsOnline);

		time.Advance(TimeSpan.FromSeconds(2));
		Assert.IsFalse(state.IsOnline);
	}

	[TestMethod]
	public void IsPlaying_NeedsAnOnlineNonBotPlayer()
	{
		ManualTime time = new();
		LiveState state = NewState(time);
		state.Apply(Batch(Server(Start, "de_dust2", Human(1), new LivePlayer(1, 2, "Bot", true, 0, 0, 0, 0))));

		Assert.IsTrue(state.IsPlaying(1));
		Assert.IsFalse(state.IsPlaying(2));
		Assert.IsFalse(state.IsPlaying(3));

		time.Advance(TimeSpan.FromSeconds(30));
		Assert.IsFalse(state.IsPlaying(1));
	}

	[TestMethod]
	public void Feed_KeepsNewestFiftyNewestFirst()
	{
		LiveState state = NewState(new ManualTime());
		List<FeedEntry> entries = [.. Enumerable.Range(0, 60).Select(Entry)];

		LiveChanges changes = state.Apply(Batch(Server(Start), entries));

		Assert.HasCount(60, changes.NewFeed);
		Assert.HasCount(50, state.Feed);
		Assert.AreEqual("Shot 59", state.Feed[0].Title);
		Assert.AreEqual("Shot 10", state.Feed[^1].Title);
	}

	[TestMethod]
	public void Feed_NewerBatchGoesAheadOfOlderOnes()
	{
		LiveState state = NewState(new ManualTime());
		state.Apply(Batch(Server(Start), [Entry(5)]));

		state.Apply(Batch(Server(Start.AddSeconds(1)), [Entry(1), Entry(2)]));

		string[] expected = ["Shot 2", "Shot 1", "Shot 5"];
		CollectionAssert.AreEqual(expected, state.Feed.Select(e => e.Title).ToArray());
	}

	[TestMethod]
	public void Feed_SurvivesRestart()
	{
		LiveState first = NewState(new ManualTime());
		first.Apply(Batch(Server(Start), [.. Enumerable.Range(0, 60).Select(Entry)]));

		LiveState second = NewState(new ManualTime());

		Assert.HasCount(50, second.Feed);
		Assert.AreEqual("Shot 59", second.Feed[0].Title);
		Assert.AreEqual(first.Feed[^1], second.Feed[^1]);
	}

	private static FeedEntry EntryFor(ulong steamId, int index) =>
		new(Start.AddSeconds(index), steamId, "Player", FeedKind.SkillShot, $"Shot {index}", null, 10);

	[TestMethod]
	public void PlayerFeed_KeepsEachPlayersNewestEntries()
	{
		LiveState state = NewState(new ManualTime());
		List<FeedEntry> entries = [.. Enumerable.Range(0, 40).Select(i => EntryFor(1, i)), EntryFor(2, 100), EntryFor(0, 101)];

		state.Apply(Batch(Server(Start), entries));

		Assert.HasCount(30, state.PlayerFeed(1));
		Assert.AreEqual("Shot 39", state.PlayerFeed(1)[0].Title);
		Assert.AreEqual("Shot 10", state.PlayerFeed(1)[^1].Title);
		Assert.AreEqual("Shot 100", Assert.ContainsSingle(state.PlayerFeed(2)).Title);
		Assert.IsEmpty(state.PlayerFeed(0));
		Assert.IsEmpty(state.PlayerFeed(3));
	}

	[TestMethod]
	public void PlayerFeed_RepeatedBatch_AddsNothingTwice()
	{
		LiveState state = NewState(new ManualTime());
		List<FeedEntry> entries = [EntryFor(1, 1), EntryFor(1, 2)];
		state.Apply(Batch(Server(Start), entries));

		state.Apply(Batch(Server(Start.AddSeconds(2)), entries));

		Assert.HasCount(2, state.PlayerFeed(1));
	}

	[TestMethod]
	public void PlayerFeed_SurvivesRestart()
	{
		LiveState first = NewState(new ManualTime());
		first.Apply(Batch(Server(Start), [EntryFor(1, 1), EntryFor(2, 2)]));
		first.Apply(Batch(Server(Start.AddSeconds(2)), [EntryFor(1, 3)]));

		LiveState second = NewState(new ManualTime());

		string[] expected = ["Shot 3", "Shot 1"];
		CollectionAssert.AreEqual(expected, second.PlayerFeed(1).Select(e => e.Title).ToArray());
		Assert.HasCount(1, second.PlayerFeed(2));
	}

	[TestMethod]
	public void PlayerFeed_PastTheLimit_DropsThePlayersSeenLeastRecently()
	{
		SiteOptions options = new() { PlayerFeedPlayers = 2 };
		LiveState state = NewState(new ManualTime(), options);
		state.Apply(Batch(Server(Start), [EntryFor(1, 1), EntryFor(2, 2)]));
		state.Apply(Batch(Server(Start.AddSeconds(2)), [EntryFor(1, 3)]));

		state.Apply(Batch(Server(Start.AddSeconds(4)), [EntryFor(3, 4)]));

		Assert.IsEmpty(state.PlayerFeed(2));
		Assert.HasCount(2, state.PlayerFeed(1));
		Assert.HasCount(1, state.PlayerFeed(3));
		string[] files = [.. Directory.GetFiles(Path.Combine(directory, "players")).Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal)];
		string[] expectedFiles = ["1.json", "3.json"];
		CollectionAssert.AreEqual(expectedFiles, files);
	}

	[TestMethod]
	public void FeedStore_LoadPlayers_SkipsFilesNotNamedForASteamIdAndUnreadableOnes()
	{
		FeedStore store = new(FeedPath);
		store.SavePlayer(7, [EntryFor(7, 1)]);
		File.WriteAllText(Path.Combine(store.PlayersPath, "notes.json"), "[]");
		File.WriteAllText(Path.Combine(store.PlayersPath, "8.json"), "{ not json");

		Dictionary<ulong, IReadOnlyList<FeedEntry>> players = store.LoadPlayers();

		Assert.AreEqual(7UL, Assert.ContainsSingle(players).Key);
	}

	[TestMethod]
	public void FeedStore_MissingOrCorruptFile_LoadsEmpty()
	{
		FeedStore store = new(FeedPath);
		Assert.IsEmpty(store.Load());

		File.WriteAllText(FeedPath, "{ not json");
		Assert.IsEmpty(store.Load());
	}

	[TestMethod]
	public void FeedStore_Save_ReplacesTheFileAndLeavesNoTemporaryFile()
	{
		FeedStore store = new(Path.Combine(directory, "nested", "feed.json"));
		store.Save([Entry(1)]);
		store.Save([Entry(2), Entry(3)]);

		Assert.HasCount(2, store.Load());
		Assert.HasCount(1, Directory.GetFiles(Path.Combine(directory, "nested")));
	}

	[TestMethod]
	public void Settings_DefaultUntilABatchArrives_ThenTracksTheLatest()
	{
		LiveState state = NewState(new ManualTime());
		Assert.AreEqual(new ProgressionSettings().KillXp, state.Settings.KillXp);

		ProgressionSettings custom = new() { KillXp = 123 };
		state.Apply(Batch(Server(Start), settings: custom));

		Assert.AreSame(custom, state.Settings);
	}

	[TestMethod]
	public void ProgressMerge_TakesLargerValuesFieldByField()
	{
		PlayerProgress stored = TestHelpers.Player();
		WeaponProgress storedWeapon = stored.Weapon(WeaponKey.Create("ak47"));
		storedWeapon.Kills = 10;
		storedWeapon.Shots = 500;
		ProgressSnapshot live = new(null, [new WeaponRow(TestHelpers.SteamId, "ak47", 0, 1, 12, 0, 0, 480)], []);

		PlayerProgress merged = ProgressMerge.Merge(stored, live);

		WeaponProgress weapon = merged.Weapon(WeaponKey.Create("ak47"));
		Assert.AreEqual(12, weapon.Kills);
		Assert.AreEqual(500, weapon.Shots);
	}

	[TestMethod]
	public void ProgressMerge_PlayerRow_TakesMaximaNewestNameAndSeenRange()
	{
		PlayerProgress stored = TestHelpers.Player();
		stored.Name = "Old";
		stored.RankXp = 900;
		stored.Rank = 3;
		stored.FirstSeen = Start.AddDays(-10);
		stored.LastSeen = Start.AddDays(-1);
		ProgressSnapshot live = new(new PlayerRow(TestHelpers.SteamId, "New", 800, 4, Start.AddDays(-5), Start), [], []);

		PlayerProgress merged = ProgressMerge.Merge(stored, live);

		Assert.AreEqual("New", merged.Name);
		Assert.AreEqual(900, merged.RankXp);
		Assert.AreEqual(4, merged.Rank);
		Assert.AreEqual(Start.AddDays(-10), merged.FirstSeen);
		Assert.AreEqual(Start, merged.LastSeen);
	}

	[TestMethod]
	public void ProgressMerge_ChallengesTakeLargerCountAndTier()
	{
		PlayerProgress stored = TestHelpers.Player();
		ChallengeProgress challenge = stored.Weapon(WeaponKey.Create("ak47")).Challenge("headshots");
		challenge.Count = 40;
		challenge.TierReached = 2;
		ProgressSnapshot live = new(null, [], [new ChallengeRow(TestHelpers.SteamId, "ak47", "headshots", 30, 3)]);

		PlayerProgress merged = ProgressMerge.Merge(stored, live);

		ChallengeProgress result = merged.Weapon(WeaponKey.Create("ak47")).Challenge("headshots");
		Assert.AreEqual(40, result.Count);
		Assert.AreEqual(3, result.TierReached);
	}

	[TestMethod]
	public void ProgressMerge_LiveOnlyWeapon_IsAdded()
	{
		PlayerProgress stored = TestHelpers.Player();
		ProgressSnapshot live = new(null, [new WeaponRow(TestHelpers.SteamId, "awp", 50, 2, 3, 1, 4, 5)], []);

		PlayerProgress merged = ProgressMerge.Merge(stored, live);

		Assert.AreEqual(3, merged.Weapon(WeaponKey.Create("awp")).Kills);
		Assert.AreEqual(2, merged.Weapon(WeaponKey.Create("awp")).Level);
	}

	[TestMethod]
	public void ProgressMerge_NullStored_UsesLive()
	{
		ProgressSnapshot live = new(
			new PlayerRow(TestHelpers.SteamId, "Live", 70, 1, Start, Start),
			[new WeaponRow(TestHelpers.SteamId, "awp", 50, 2, 3, 1, 4, 5)],
			[]);

		PlayerProgress merged = ProgressMerge.Merge(null, live);

		Assert.AreEqual(TestHelpers.SteamId, merged.SteamId);
		Assert.AreEqual("Live", merged.Name);
		Assert.AreEqual(70, merged.RankXp);
		Assert.AreEqual(Start, merged.FirstSeen);
		Assert.AreEqual(5, merged.Weapon(WeaponKey.Create("awp")).Shots);
	}

	[TestMethod]
	public void ProgressMerge_NullLive_UsesStored()
	{
		PlayerProgress stored = TestHelpers.Player();
		stored.RankXp = 5;

		PlayerProgress merged = ProgressMerge.Merge(stored, null);

		Assert.AreEqual(5, merged.RankXp);
		Assert.AreEqual("Tester", merged.Name);
	}

	[TestMethod]
	public void Apply_ProgressRows_CombineIntoOverlay_AndReportChangedPlayers()
	{
		LiveState state = NewState(new ManualTime());
		ulong other = TestHelpers.SteamId + 1;
		ProgressSnapshot first = new(
			new PlayerRow(TestHelpers.SteamId, "Tester", 100, 1, Start, Start),
			[new WeaponRow(TestHelpers.SteamId, "ak47", 10, 1, 1, 0, 0, 0)],
			[]);
		ProgressSnapshot second = new(
			null,
			[new WeaponRow(TestHelpers.SteamId, "ak47", 20, 2, 2, 0, 0, 0), new WeaponRow(TestHelpers.SteamId, "awp", 5, 1, 1, 0, 0, 0)],
			[new ChallengeRow(TestHelpers.SteamId, "ak47", "headshots", 3, 0)]);
		ProgressSnapshot third = new(new PlayerRow(other, "Other", 1, 0, Start, Start), [], []);

		LiveChanges firstChanges = state.Apply(Batch(Server(Start), progress: [first]));
		LiveChanges secondChanges = state.Apply(Batch(Server(Start.AddSeconds(1)), progress: [second, third]));

		ulong[] expectedFirst = [TestHelpers.SteamId];
		ulong[] expectedSecond = [TestHelpers.SteamId, other];
		CollectionAssert.AreEqual(expectedFirst, firstChanges.ChangedPlayers.ToArray());
		CollectionAssert.AreEquivalent(expectedSecond, secondChanges.ChangedPlayers.ToArray());

		ProgressSnapshot? overlay = state.Overlay(TestHelpers.SteamId);
		Assert.IsNotNull(overlay);
		Assert.AreEqual(100, overlay.Player?.RankXp);
		Assert.HasCount(2, overlay.Weapons);
		Assert.AreEqual(20, overlay.Weapons.Single(w => w.Weapon == "ak47").Xp);
		Assert.HasCount(1, overlay.Challenges);
		Assert.IsNull(state.Overlay(999));
	}

	[TestMethod]
	public void Apply_StaleProgressBatch_DoesNotLowerTheOverlay()
	{
		LiveState state = NewState(new ManualTime());
		ProgressSnapshot newer = new(
			new PlayerRow(TestHelpers.SteamId, "Newer", 200, 3, Start, Start.AddMinutes(5)),
			[new WeaponRow(TestHelpers.SteamId, "ak47", 90, 4, 12, 3, 40, 100)],
			[new ChallengeRow(TestHelpers.SteamId, "ak47", "headshots", 30, 2)]);
		ProgressSnapshot stale = new(
			new PlayerRow(TestHelpers.SteamId, "Stale", 150, 2, Start.AddMinutes(-1), Start.AddMinutes(1)),
			[new WeaponRow(TestHelpers.SteamId, "ak47", 60, 3, 9, 5, 30, 120)],
			[new ChallengeRow(TestHelpers.SteamId, "ak47", "headshots", 20, 3)]);

		state.Apply(Batch(Server(Start.AddSeconds(10)), progress: [newer]));
		state.Apply(Batch(Server(Start), progress: [stale]));

		ProgressSnapshot? overlay = state.Overlay(TestHelpers.SteamId);
		Assert.IsNotNull(overlay);
		WeaponRow weapon = overlay.Weapons.Single();
		Assert.AreEqual(90, weapon.Xp);
		Assert.AreEqual(4, weapon.Level);
		Assert.AreEqual(12, weapon.Kills);
		Assert.AreEqual(5, weapon.Headshots);
		Assert.AreEqual(40, weapon.Hits);
		Assert.AreEqual(120, weapon.Shots);
		Assert.AreEqual(30, overlay.Challenges.Single().Count);
		Assert.AreEqual(3, overlay.Challenges.Single().Tier);
		Assert.AreEqual(200, overlay.Player?.RankXp);
		Assert.AreEqual(3, overlay.Player?.Rank);
		Assert.AreEqual("Newer", overlay.Player?.Name);
		Assert.AreEqual(Start.AddMinutes(-1), overlay.Player?.FirstSeen);
		Assert.AreEqual(Start.AddMinutes(5), overlay.Player?.LastSeen);
	}

	[TestMethod]
	public void Apply_FeedWriteFails_KeepsBatchAndWarnsOncePerStreak()
	{
		string blocker = Path.Combine(directory, "blocker");
		File.WriteAllText(blocker, "a file where the history folder should be");
		CapturingLogger logger = new();
		LiveState state = new(new SiteOptions(), new FeedStore(Path.Combine(blocker, "feed.json")), new ManualTime(), logger);

		LiveChanges first = state.Apply(Batch(Server(Start), [Entry(1)]));
		state.Apply(Batch(Server(Start.AddSeconds(2)), [Entry(2)]));

		Assert.IsTrue(first.SnapshotChanged);
		Assert.HasCount(2, state.Feed);
		Assert.IsTrue(state.IsOnline);
		LogEntry warning = Assert.ContainsSingle(logger.Entries);
		Assert.AreEqual(LogLevel.Warning, warning.Level);
		Assert.Contains(blocker, warning.Message);
		Assert.IsNotNull(warning.Exception);

		File.Delete(blocker);
		state.Apply(Batch(Server(Start.AddSeconds(4)), [Entry(3)]));
		state.Apply(Batch(Server(Start.AddSeconds(6)), [Entry(4)]));
		Directory.Delete(Path.Combine(directory, "blocker"), recursive: true);
		File.WriteAllText(blocker, "blocked again");
		state.Apply(Batch(Server(Start.AddSeconds(8)), [Entry(5)]));

		Assert.HasCount(3, logger.Entries);
		Assert.AreEqual(LogLevel.Information, logger.Entries[1].Level);
		Assert.AreEqual(LogLevel.Warning, logger.Entries[2].Level);
	}

	private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);

	private sealed class CapturingLogger : ILogger<LiveState>
	{
		public List<LogEntry> Entries { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Entries.Add(new(logLevel, formatter(state, exception), exception));
	}
}
