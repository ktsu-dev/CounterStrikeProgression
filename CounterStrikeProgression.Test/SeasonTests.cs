// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Admin;
using ktsu.CounterStrikeProgression.Core.Live;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Records;
using ktsu.CounterStrikeProgression.Core.Rivals;
using ktsu.CounterStrikeProgression.Core.Rotation;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Core.Weapons;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class SeasonTests
{
	private static readonly DateOnly October = new(2026, 10, 1);
	private static readonly DateOnly July = new(2026, 7, 1);

	private string directory = string.Empty;

	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-seasons-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	[TestMethod]
	public void SeasonsAreQuartersByDefault()
	{
		Season season = Season.For(new DateOnly(2026, 11, 30), 3);

		Assert.AreEqual(October, season.Start);
		Assert.AreEqual(new DateOnly(2027, 1, 1), season.End);
		Assert.AreEqual("Oct–Dec 2026", season.Name);
		Assert.AreEqual(July, season.Previous(3).Start);
	}

	[TestMethod]
	public void SeasonLengthsThatDoNotDivideTheYearCountAsQuarters()
	{
		Assert.AreEqual(Season.For(new DateOnly(2026, 5, 2), 3), Season.For(new DateOnly(2026, 5, 2), 5));
		Assert.AreEqual(new DateOnly(2026, 5, 1), Season.For(new DateOnly(2026, 5, 31), 1).Start);
	}

	[TestMethod]
	public void RankXpCountsTowardTheCurrentSeason()
	{
		ProgressionEngine engine = new(new ProgressionSettings(), new ManualTime());
		PlayerProgress player = TestHelpers.Player();

		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.AreEqual(October, player.SeasonStart);
		Assert.AreEqual(player.RankXp, player.SeasonXp);
		Assert.AreEqual(player.RankXp, engine.SeasonXp(player));
		Assert.IsTrue(player.IsSeasonDirty);
	}

	[TestMethod]
	public void BotXpCountsAtTheBotRateForTheSeasonToo()
	{
		ProgressionEngine engine = new(new ProgressionSettings(), new ManualTime());
		PlayerProgress player = TestHelpers.Player();

		for (int i = 0; i < 4; i++)
		{
			engine.ApplyCombat(player, TestHelpers.Kill("ak47") with { VictimIsBot = true });
		}

		Assert.IsGreaterThan(0L, player.SeasonXp);
		Assert.AreEqual(player.RankXp, player.SeasonXp);
	}

	[TestMethod]
	public void ANewSeasonStartsFromZeroAndKeepsTheEndedSeasonForTheNextSave()
	{
		ManualTime time = new();
		ProgressionEngine engine = new(new ProgressionSettings(), time);
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		long octoberXp = player.SeasonXp;

		time.Advance(TimeSpan.FromDays(90));
		Assert.AreEqual(0, engine.SeasonXp(player));
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		Assert.AreEqual(new DateOnly(2027, 1, 1), player.SeasonStart);
		Assert.IsLessThan(player.RankXp, player.SeasonXp);

		ProgressSnapshot snapshot = ProgressSnapshot.Capture(player);
		Assert.HasCount(2, snapshot.Seasons);
		Assert.AreEqual(octoberXp, snapshot.Seasons.Single(s => s.Start == Midnight(October)).Xp);
		Assert.IsNull(player.EndedSeason);
		Assert.IsTrue(ProgressSnapshot.Capture(player).IsEmpty);
	}

	[TestMethod]
	public void RestoreFlagsBothSeasonRowsAfterAFailedSave()
	{
		ManualTime time = new();
		ProgressionEngine engine = new(new ProgressionSettings(), time);
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));
		time.Advance(TimeSpan.FromDays(90));
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressSnapshot failed = ProgressSnapshot.Capture(player);
		failed.Restore(player);
		ProgressSnapshot retry = ProgressSnapshot.Capture(player);

		CollectionAssert.AreEquivalent(failed.Seasons.ToList(), retry.Seasons.ToList());
	}

	[TestMethod]
	public async Task TheStoreKeepsEverySeasonAndLoadsTheLatest()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, 1, "One", July, 900).ConfigureAwait(false);
		await SaveAsync(store, 1, "One", October, 40).ConfigureAwait(false);

		PlayerProgress? loaded = await store.LoadAsync(1, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsNotNull(loaded);
		Assert.AreEqual(October, loaded.SeasonStart);
		Assert.AreEqual(40, loaded.SeasonXp);
		Assert.IsFalse(loaded.IsSeasonDirty);
	}

	[TestMethod]
	public async Task ResetClearsTheSeasons()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, 1, "One", July, 900).ConfigureAwait(false);
		await SaveAsync(store, 1, "One", October, 40).ConfigureAwait(false);
		PlayerProgress player = (await store.LoadAsync(1, TestContext.CancellationToken).ConfigureAwait(false))!;

		new AdminCorrection(CorrectionKind.Reset).Apply(player, new ProgressionEngine(new ProgressionSettings(), new ManualTime()).Ranks);
		await store.ResetAsync(ProgressSnapshot.ToRow(player), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsNull(player.SeasonStart);
		Assert.AreEqual(0, player.SeasonXp);
		Assert.IsEmpty(await store.SeasonResultsAsync(1, TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task StandingsAndResultsRankBySeasonXp()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, 1, "One", July, 900).ConfigureAwait(false);
		await SaveAsync(store, 2, "Two", July, 1500).ConfigureAwait(false);
		await SaveAsync(store, 3, "Three", July, 100).ConfigureAwait(false);
		await SaveAsync(store, 1, "One", October, 40).ConfigureAwait(false);

		IReadOnlyList<SeasonStanding> july = await store.SeasonStandingsAsync(July, 10, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual("Two,One,Three", string.Join(',', july.Select(s => s.Name)));

		IReadOnlyList<SeasonResult> results = await store.SeasonResultsAsync(1, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.HasCount(2, results);
		Assert.AreEqual(new SeasonResult(Midnight(October), 40, 1, 1), results[0]);
		Assert.AreEqual(new SeasonResult(Midnight(July), 900, 2, 3), results[1]);
	}

	[TestMethod]
	public void ChangeTrackerSendsTheSeasonRowOnlyWhenItChanges()
	{
		ProgressionEngine engine = new(new ProgressionSettings(), new ManualTime());
		PlayerProgress player = TestHelpers.Player();
		ProgressChangeTracker tracker = new();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		ProgressSnapshot? first = tracker.Changes(player);
		Assert.IsNotNull(first);
		Assert.HasCount(1, first.Seasons);
		Assert.IsNull(tracker.Changes(player));
	}

	[TestMethod]
	public void SeasonRowsSurviveTheLiveBatchJson()
	{
		ProgressSnapshot rows = new(null, [], []) { Seasons = [new SeasonRow(TestHelpers.SteamId, Midnight(October), 75)] };

		string json = JsonSerializer.Serialize(rows, LiveJson.Options);
		ProgressSnapshot? back = JsonSerializer.Deserialize<ProgressSnapshot>(json, LiveJson.Options);

		Assert.IsNotNull(back);
		Assert.HasCount(1, back.Seasons);
		Assert.AreEqual(rows.Seasons[0], back.Seasons[0]);
	}

	[TestMethod]
	public void MergeTakesTheLaterSeasonAndTheLargerXp()
	{
		PlayerProgress stored = TestHelpers.Player();
		stored.SeasonStart = July;
		stored.SeasonXp = 900;

		ProgressSnapshot october = new(null, [], []) { Seasons = [new SeasonRow(TestHelpers.SteamId, Midnight(October), 30)] };
		PlayerProgress merged = ProgressMerge.Merge(stored, october);
		Assert.AreEqual(October, merged.SeasonStart);
		Assert.AreEqual(30, merged.SeasonXp);

		ProgressSnapshot combined = ProgressMerge.Combine(october, new(null, [], []) { Seasons = [new SeasonRow(TestHelpers.SteamId, Midnight(October), 20)] });
		Assert.AreEqual(30, combined.Seasons.Single().Xp);
	}

	[TestMethod]
	public void PanelOverviewShowsTheSeason()
	{
		ProgressionEngine engine = new(new ProgressionSettings(), new ManualTime());
		PlayerProgress player = TestHelpers.Player();
		engine.ApplyCombat(player, TestHelpers.Kill("ak47"));

		PanelPage page = new PanelBuilder(engine).Overview(player);

		PanelLine line = page.Lines.Single(l => l.Label == "Season Oct–Dec 2026");
		Assert.AreEqual($"{player.SeasonXp} XP", line.Detail);
	}

	[TestMethod]
	public async Task SeasonsEndpoint_LaysLiveXpOverTheCurrentSeason()
	{
		InMemoryProgressionStore store = new();
		await SaveAsync(store, TestHelpers.SteamId, "Tester", October, 100).ConfigureAwait(false);
		await SaveAsync(store, TestHelpers.SteamId + 1, "Other", October, 150).ConfigureAwait(false);
		using ProgressReader reader = new(store);
		ManualTime time = new();
		LiveState live = NewState(time);
		live.Apply(Batch([new(null, [], []) { Seasons = [new SeasonRow(TestHelpers.SteamId, Midnight(October), 400)] }]));

		IResult result = await SeasonEndpoints.SeasonsAsync(null, reader, live, time, CancellationToken.None).ConfigureAwait(false);

		SeasonView view = Assert.IsInstanceOfType<Ok<SeasonView>>(result).Value!;
		Assert.IsTrue(view.Current);
		Assert.IsNull(view.Next);
		Assert.AreEqual(July, view.Previous);
		Assert.AreEqual("Tester", view.Rows[0].Name);
		Assert.AreEqual(400, view.Rows[0].Xp);
		Assert.AreEqual(2, view.Rows[1].Position);
	}

	[TestMethod]
	public async Task SeasonsEndpoint_RejectsDaysThatDoNotStartAPastSeason()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());
		ManualTime time = new();
		LiveState live = NewState(time);

		Assert.IsInstanceOfType<NotFound>(await SeasonEndpoints.SeasonsAsync("2026-08-01", reader, live, time, CancellationToken.None).ConfigureAwait(false));
		Assert.IsInstanceOfType<NotFound>(await SeasonEndpoints.SeasonsAsync("2027-01-01", reader, live, time, CancellationToken.None).ConfigureAwait(false));
		Assert.IsInstanceOfType<NotFound>(await SeasonEndpoints.SeasonsAsync("july", reader, live, time, CancellationToken.None).ConfigureAwait(false));

		SeasonView july = Assert.IsInstanceOfType<Ok<SeasonView>>(await SeasonEndpoints.SeasonsAsync("2026-07-01", reader, live, time, CancellationToken.None).ConfigureAwait(false)).Value!;
		Assert.IsFalse(july.Current);
		Assert.AreEqual(October, july.Next);
	}

	[TestMethod]
	public async Task PlayerSeasonsEndpoint_GivesThisSeasonAndTopThreeFinishes()
	{
		InMemoryProgressionStore store = new();
		ulong me = TestHelpers.SteamId;
		await SaveAsync(store, me, "Tester", new DateOnly(2026, 1, 1), 50).ConfigureAwait(false);
		await SaveAsync(store, me + 1, "Other", new DateOnly(2026, 1, 1), 60).ConfigureAwait(false);
		await SaveAsync(store, me + 2, "Third", new DateOnly(2026, 1, 1), 70).ConfigureAwait(false);
		await SaveAsync(store, me + 3, "Fourth", new DateOnly(2026, 1, 1), 80).ConfigureAwait(false);
		await SaveAsync(store, me, "Tester", July, 900).ConfigureAwait(false);
		await SaveAsync(store, me + 1, "Other", July, 1500).ConfigureAwait(false);
		await SaveAsync(store, me, "Tester", October, 10).ConfigureAwait(false);
		using ProgressReader reader = new(store);
		ManualTime time = new();

		IResult result = await SeasonEndpoints.PlayerSeasonsAsync(me.ToString(CultureInfo.InvariantCulture), reader, NewState(time), time, CancellationToken.None).ConfigureAwait(false);

		PlayerSeasonsView view = Assert.IsInstanceOfType<Ok<PlayerSeasonsView>>(result).Value!;
		Assert.AreEqual("Oct–Dec 2026", view.Season);
		Assert.AreEqual(10, view.Xp);
		Assert.AreEqual(1L, view.Place);
		Assert.HasCount(2, view.Badges);
		Assert.IsTrue(view.Badges[0].Current);
		Assert.AreEqual(new SeasonBadge("Jul–Sep 2026", July, 2, 2, 900, false), view.Badges[1]);
	}

	[TestMethod]
	public void SeasonBoard_AddsLiveOnlyPlayersAndIgnoresLiveRowsForPastSeasons()
	{
		ProgressionSettings settings = new();
		Season october = Season.For(October, 3);
		Season july = Season.For(July, 3);
		ulong fresh = TestHelpers.SteamId;
		ulong nameless = TestHelpers.SteamId + 1;
		ulong stored = TestHelpers.SteamId + 2;
		Dictionary<ulong, ProgressSnapshot> live = new()
		{
			[fresh] = new(new PlayerRow(fresh, "Fresh", 500, 3, Midnight(October), Midnight(October)), [], []) { Seasons = [new SeasonRow(fresh, Midnight(October), 500)] },
			[nameless] = new(null, [], []) { Seasons = [new SeasonRow(nameless, Midnight(October), 20)] },
			[stored] = new(null, [], []) { Seasons = [new SeasonRow(stored, Midnight(October), 90)] },
		};
		IReadOnlyList<SeasonStanding> rows = [new SeasonStanding(stored, "Stored", 2, 60)];

		SeasonView now = SeasonsViewBuilder.Build(october, october, settings, rows, id => live.GetValueOrDefault(id), id => id == fresh, live.Keys);
		Assert.AreEqual("Fresh,Stored,", string.Join(',', now.Rows.Select(r => r.Name)));
		Assert.AreEqual(90, now.Rows[1].Xp);
		Assert.AreEqual("Stored", now.Rows[1].Name);
		Assert.IsTrue(now.Rows[0].Online);

		SeasonView past = SeasonsViewBuilder.Build(july, october, settings, rows, id => live.GetValueOrDefault(id), _ => false, live.Keys);
		Assert.HasCount(1, past.Rows);
		Assert.AreEqual(60, past.Rows[0].Xp);
	}

	[TestMethod]
	public void PlayerBadges_FallBackToTheStoredPlaceAndSkipPlayersWithNoSeasonXp()
	{
		ProgressionSettings settings = new();
		Season october = Season.For(October, 3);
		SeasonView board = SeasonsViewBuilder.Build(october, october, settings, [], _ => null, _ => false);

		PlayerSeasonsView offBoard = SeasonsViewBuilder.BuildPlayer(1, 40, board, [new SeasonResult(Midnight(October), 40, 600, 700)], settings);
		Assert.AreEqual(600L, offBoard.Place);
		Assert.AreEqual(700, offBoard.Players);

		PlayerSeasonsView idle = SeasonsViewBuilder.BuildPlayer(1, 0, board, [new SeasonResult(Midnight(July), 10, 4, 9)], settings);
		Assert.IsNull(idle.Place);
		Assert.IsEmpty(idle.Badges);
	}

	[TestMethod]
	public void MergeIgnoresALiveRowForAnOlderSeasonAndFindsTheSteamIdOnSeasonRows()
	{
		PlayerProgress stored = TestHelpers.Player();
		stored.SeasonStart = October;
		stored.SeasonXp = 70;
		ProgressSnapshot older = new(null, [], []) { Seasons = [new SeasonRow(TestHelpers.SteamId, Midnight(July), 900)] };
		ProgressSnapshot same = new(null, [], []) { Seasons = [new SeasonRow(TestHelpers.SteamId, Midnight(October), 50)] };

		PlayerProgress merged = ProgressMerge.Merge(stored, older);
		Assert.AreEqual(October, merged.SeasonStart);
		Assert.AreEqual(70, merged.SeasonXp);
		Assert.AreEqual(70, ProgressMerge.Merge(stored, same).SeasonXp);
		Assert.AreEqual(TestHelpers.SteamId, ProgressMerge.SteamIdOf(older));
		Assert.AreEqual(October, ProgressMerge.Merge(null, same).SeasonStart);
	}

	[TestMethod]
	public async Task ProgressReader_CachesSeasonAnswers()
	{
		InMemoryProgressionStore store = new();
		ManualTime time = new();
		using ProgressReader reader = new(store, time);

		Assert.IsEmpty(await reader.SeasonStandingsAsync(October).ConfigureAwait(false));
		Assert.IsEmpty(await reader.SeasonResultsAsync(1).ConfigureAwait(false));
		await SaveAsync(store, 1, "One", October, 40).ConfigureAwait(false);
		Assert.IsEmpty(await reader.SeasonStandingsAsync(October).ConfigureAwait(false));

		time.Advance(TimeSpan.FromSeconds(11));
		Assert.HasCount(1, await reader.SeasonStandingsAsync(October).ConfigureAwait(false));
		Assert.HasCount(1, await reader.SeasonResultsAsync(1).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task SeasonEndpoints_Return503WhenTheDatabaseFails()
	{
		using ProgressReader reader = new(new FailingStore());
		ManualTime time = new();
		LiveState live = NewState(time);

		Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, StatusOf(await SeasonEndpoints.SeasonsAsync(null, reader, live, time, CancellationToken.None).ConfigureAwait(false)));
		Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, StatusOf(await SeasonEndpoints.PlayerSeasonsAsync(TestHelpers.SteamId.ToString(CultureInfo.InvariantCulture), reader, live, time, CancellationToken.None).ConfigureAwait(false)));
		Assert.IsInstanceOfType<NotFound>(await SeasonEndpoints.PlayerSeasonsAsync("nope", reader, live, time, CancellationToken.None).ConfigureAwait(false));
	}

	private static int StatusOf(IResult result) => ((IStatusCodeHttpResult)result).StatusCode ?? 0;

	private static DateTime Midnight(DateOnly day) => day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

	private static LiveBatch Batch(IReadOnlyList<ProgressSnapshot> progress) =>
		new(new ServerSnapshot(DateTime.UtcNow, "de_dust2", "Deathmatch", 16, []), [], progress, new ProgressionSettings());

	private LiveState NewState(TimeProvider time) =>
		new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), time, NullLogger<LiveState>.Instance);

	private async Task SaveAsync(InMemoryProgressionStore store, ulong steamId, string name, DateOnly season, long xp)
	{
		DateTime seen = Midnight(season);
		ProgressSnapshot rows = new(new PlayerRow(steamId, name, xp, 0, seen, seen), [], []) { Seasons = [new SeasonRow(steamId, seen, xp)] };
		await store.SaveAsync(rows, TestContext.CancellationToken).ConfigureAwait(false);
	}

	internal sealed class FailingStore : IProgressionStore
	{
		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task<PlayerProgress?> LoadAsync(ulong steamId, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task SaveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task ResetAsync(PlayerRow player, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<LeaderboardEntry>> TopAsync(int count, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<WeaponLeaderboardEntry>> TopByWeaponAsync(WeaponKey weapon, int count, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<RankingEntry>> RankingsAsync(int count, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<SeasonStanding>> SeasonStandingsAsync(DateOnly start, int count, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<SeasonResult>> SeasonResultsAsync(ulong steamId, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<RecordRow>> RecordsAsync(CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task SaveRecordsAsync(IReadOnlyList<RecordRow> rows, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<PeriodGain>> GainsBetweenAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<bool> ClaimDigestAsync(DateOnly week, DateTime at, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<DailyXp>> HistoryAsync(ulong steamId, DateOnly from, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<XpGainEntry>> GainsSinceAsync(DateOnly start, int count, CancellationToken cancellationToken) => throw new DatabaseDown();

		public Task<IReadOnlyList<HeadToHead>> HeadToHeadAsync(ulong steamId, int count, CancellationToken cancellationToken) => throw new DatabaseDown();
	}

	internal sealed class DatabaseDown : DbException
	{
		public DatabaseDown()
			: base("The database is down.")
		{
		}

		public DatabaseDown(string message)
			: base(message)
		{
		}

		public DatabaseDown(string message, Exception innerException)
			: base(message, innerException)
		{
		}
	}
}
