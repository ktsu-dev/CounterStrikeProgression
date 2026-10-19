// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using System.Data.Common;
using System.Globalization;
using ktsu.CounterStrikeProgression.Core.Admin;
using ktsu.CounterStrikeProgression.Core.Floaties;
using ktsu.CounterStrikeProgression.Core.Panel;
using ktsu.CounterStrikeProgression.Core.Progression;
using ktsu.CounterStrikeProgression.Core.Rivals;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class RivalTests
{
	private const ulong OtherId = TestHelpers.SteamId + 1;
	private const ulong ThirdId = TestHelpers.SteamId + 2;

	private string directory = string.Empty;

	public TestContext TestContext { get; set; } = null!;

	[TestInitialize]
	public void Initialize()
	{
		directory = Path.Combine(Path.GetTempPath(), "csp-rivals-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete(directory, recursive: true);

	private static PlayerProgress Other() => new(OtherId) { Name = "Other" };

	private static void Kills(ProgressionEngine engine, PlayerProgress killer, PlayerProgress victim, int times)
	{
		for (int i = 0; i < times; i++)
		{
			engine.ApplyRivalKill(killer, victim);
		}
	}

	[TestMethod]
	public void AKillCountsOnBothPlayers()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();

		Kills(engine, me, other, 2);

		Assert.AreEqual(2, me.Victims[OtherId].Kills);
		Assert.IsTrue(me.Victims[OtherId].IsDirty);
		Assert.AreEqual(2, other.KilledBy[TestHelpers.SteamId]);
		Assert.AreEqual("Other", me.RivalNames[OtherId]);
		Assert.AreEqual("Tester", other.RivalNames[TestHelpers.SteamId]);
		Assert.IsEmpty(other.Victims);
	}

	[TestMethod]
	public void KillingYourNemesisPaysTheBonus()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();
		Kills(engine, other, me, engine.Settings.NemesisMinimumKills);

		ProgressionResult result = engine.ApplyRivalKill(me, other);

		ProgressionNotice notice = Assert.ContainsSingle(result.Notices);
		Assert.AreEqual(NoticeKind.NemesisKilled, notice.Kind);
		Assert.AreEqual("Other", notice.PlayerName);
		Assert.AreEqual(engine.Settings.NemesisKillXp, notice.Xp);
		Assert.AreEqual(engine.Settings.NemesisKillXp, result.XpGained);
		Assert.AreEqual(engine.Settings.NemesisKillXp, me.RankXp);
		Assert.Contains("Nemesis down", FloatieMerger.Labels(result).ToList());
	}

	[TestMethod]
	public void NoBonusBelowTheMinimumOrForSomeoneElse()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();
		PlayerProgress third = new(ThirdId) { Name = "Third" };
		Kills(engine, other, me, engine.Settings.NemesisMinimumKills - 1);

		Assert.IsEmpty(engine.ApplyRivalKill(me, other).Notices);

		Kills(engine, other, me, 1);
		Assert.IsEmpty(engine.ApplyRivalKill(me, third).Notices);
		Assert.AreEqual(0, me.RankXp);
		Assert.IsEmpty(engine.ApplyRivalKill(me, me).Notices);
	}

	[TestMethod]
	public void NoBonusWhenTurnedOff()
	{
		ProgressionEngine engine = TestHelpers.Engine(s => s.NemesisKillXp = 0);
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();
		Kills(engine, other, me, 5);

		Assert.IsEmpty(engine.ApplyRivalKill(me, other).Notices);
		Assert.AreEqual(1, me.Victims[OtherId].Kills);
	}

	[TestMethod]
	public void NemesisAndVictimPickTheMostOneSidedTie()
	{
		HeadToHead[] rows =
		[
			new(1, "A", 4, 6),
			new(2, "B", 1, 6),
			new(3, "C", 9, 2),
			new(4, "D", 9, 0),
			new(5, "E", 2, 2),
		];

		Assert.AreEqual(2UL, Rivalry.Nemesis(rows, 3)!.SteamId);
		Assert.AreEqual(4UL, Rivalry.Victim(rows, 3)!.SteamId);
		Assert.IsNull(Rivalry.Nemesis(rows, 7));
		Assert.IsNull(Rivalry.Victim(rows, 10));
	}

	[TestMethod]
	public async Task RivalsSaveAndLoadFromBothSides()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();
		Kills(engine, me, other, 4);
		Kills(engine, other, me, 1);
		me.IsDirty = true;
		other.IsDirty = true;
		await store.SaveAsync(ProgressSnapshot.Capture(me), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(ProgressSnapshot.Capture(other), TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsFalse(me.Victims[OtherId].IsDirty);
		Assert.IsTrue(ProgressSnapshot.Capture(me).IsEmpty);

		PlayerProgress loaded = (await store.LoadAsync(OtherId, TestContext.CancellationToken).ConfigureAwait(false))!;
		Assert.AreEqual(1, loaded.Victims[TestHelpers.SteamId].Kills);
		Assert.AreEqual(4, loaded.KilledBy[TestHelpers.SteamId]);
		Assert.AreEqual("Tester", loaded.RivalNames[TestHelpers.SteamId]);

		HeadToHead row = Assert.ContainsSingle(await store.HeadToHeadAsync(TestHelpers.SteamId, 10, TestContext.CancellationToken).ConfigureAwait(false));
		Assert.AreEqual(new HeadToHead(OtherId, "Other", 4, 1), row);
	}

	[TestMethod]
	public void AFailedSaveFlagsTheRivalRowsAgain()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		Kills(engine, me, Other(), 1);
		ProgressSnapshot snapshot = ProgressSnapshot.Capture(me);
		Assert.ContainsSingle(snapshot.Rivals);

		snapshot.Restore(me);

		Assert.IsTrue(me.Victims[OtherId].IsDirty);
		Assert.ContainsSingle(ProgressSnapshot.CaptureAll(me).Rivals);
	}

	[TestMethod]
	public void JoiningPicksUpKillsNotYetSaved()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress online = Other();
		PlayerProgress stale = TestHelpers.Player();
		Kills(engine, online, stale, 3);
		Kills(engine, stale, online, 2);

		// The joining player loaded before any of those kills were saved.
		PlayerProgress joining = TestHelpers.Player();
		PlayerProgress fresh = Other();
		fresh.Victims[TestHelpers.SteamId] = new() { Kills = 3 };
		Rivalry.Join(joining, [fresh]);

		Assert.AreEqual(3, joining.KilledBy[OtherId]);
		Assert.AreEqual("Other", joining.RivalNames[OtherId]);
		Assert.AreEqual("Tester", fresh.RivalNames[TestHelpers.SteamId]);
	}

	[TestMethod]
	public void JoiningTellsOnlinePlayersAboutTheJoinersKills()
	{
		PlayerProgress online = Other();
		PlayerProgress joining = TestHelpers.Player();
		joining.Victims[OtherId] = new() { Kills = 2 };
		online.KilledBy[TestHelpers.SteamId] = 1;

		Rivalry.Join(joining, [online, joining]);

		Assert.AreEqual(2, online.KilledBy[TestHelpers.SteamId]);
		Assert.AreEqual("Tester", online.RivalNames[TestHelpers.SteamId]);
		Assert.IsFalse(joining.KilledBy.ContainsKey(TestHelpers.SteamId));
	}

	[TestMethod]
	public void LoadingSkipsRowsAboutThePlayerThemselves()
	{
		PlayerProgress me = TestHelpers.Player();

		Rivalry.Load(me, [new(TestHelpers.SteamId, "Tester", 3, 3), new(OtherId, "Other", 0, 2)]);

		Assert.IsEmpty(me.Victims);
		Assert.AreEqual(2, me.KilledBy[OtherId]);
		Assert.HasCount(1, me.RivalNames);
	}

	[TestMethod]
	public async Task AStoredResetDeletesOnlyThePlayersOwnKills()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();
		Kills(engine, me, other, 2);
		Kills(engine, other, me, 1);
		await store.SaveAsync(ProgressSnapshot.Capture(me), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(ProgressSnapshot.Capture(other), TestContext.CancellationToken).ConfigureAwait(false);

		await store.ResetAsync(new PlayerRow(TestHelpers.SteamId, "Tester", 0, 0, DateTime.UtcNow, DateTime.UtcNow), TestContext.CancellationToken).ConfigureAwait(false);

		HeadToHead row = Assert.ContainsSingle(await store.HeadToHeadAsync(TestHelpers.SteamId, 10, TestContext.CancellationToken).ConfigureAwait(false));
		Assert.AreEqual(0, row.Kills);
		Assert.AreEqual(1, row.Deaths);
	}

	[TestMethod]
	public void StatsNameTheNemesisAndVictim()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();
		PlayerProgress third = new(ThirdId) { Name = "Third" };
		Kills(engine, other, me, 5);
		Kills(engine, me, other, 2);
		Kills(engine, me, third, 4);

		string stats = new ChatReports(engine).Stats("Tester", me);

		Assert.Contains("Nemesis Other (2-5).", stats);
		Assert.Contains("Favourite victim Third (4-0).", stats);
		Assert.Contains("Nemesis Tester (0-4).", new ChatReports(engine).Stats("Third", third));
		Assert.DoesNotContain("victim", new ChatReports(engine).Stats("Third", third));
	}

	[TestMethod]
	public void AResetClearsThePlayersKills()
	{
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		Kills(engine, me, Other(), 2);

		new AdminCorrection(CorrectionKind.Reset).Apply(me, engine.Ranks);

		Assert.IsEmpty(me.Victims);
	}

	[TestMethod]
	public async Task TheRivalsEndpointShowsNemesisVictimAndHeadToHeads()
	{
		InMemoryProgressionStore store = new();
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = TestHelpers.Player();
		PlayerProgress other = Other();
		PlayerProgress third = new(ThirdId) { Name = "Third" };
		Kills(engine, other, me, 4);
		Kills(engine, me, third, 3);
		foreach (PlayerProgress player in new[] { me, other, third })
		{
			player.IsDirty = true;
			await store.SaveAsync(ProgressSnapshot.Capture(player), TestContext.CancellationToken).ConfigureAwait(false);
		}

		using ProgressReader reader = new(store);
		LiveState live = new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);
		IResult result = await RivalEndpoints.PlayerRivalsAsync(TestHelpers.SteamId.ToString(CultureInfo.InvariantCulture), reader, live, CancellationToken.None).ConfigureAwait(false);

		RivalsView view = Assert.IsInstanceOfType<Ok<RivalsView>>(result).Value!;
		Assert.AreEqual(new RivalView(OtherId.ToString(CultureInfo.InvariantCulture), "Other", 0, 4), view.Nemesis);
		Assert.AreEqual(new RivalView(ThirdId.ToString(CultureInfo.InvariantCulture), "Third", 3, 0), view.Victim);
		Assert.HasCount(2, view.Rivals);
		Assert.IsInstanceOfType<NotFound>(await RivalEndpoints.PlayerRivalsAsync("nope", reader, live, CancellationToken.None).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task TheRivalsEndpointReturns503WhenTheDatabaseFails()
	{
		using ProgressReader reader = new(new FailingRivals());
		LiveState live = new(new SiteOptions(), new FeedStore(Path.Combine(directory, "feed.json")), new ManualTime(), NullLogger<LiveState>.Instance);

		IResult result = await RivalEndpoints.PlayerRivalsAsync(TestHelpers.SteamId.ToString(CultureInfo.InvariantCulture), reader, live, CancellationToken.None).ConfigureAwait(false);

		Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, ((IStatusCodeHttpResult)result).StatusCode);
	}

	[TestMethod]
	public async Task MySql_SavesAndReadsRivals()
	{
		// Runs against a real MariaDB only when CSP_TEST_MYSQL names an empty, disposable database as host:port/database:user:password.
		string? target = Environment.GetEnvironmentVariable("CSP_TEST_MYSQL");
		if (string.IsNullOrEmpty(target))
		{
			Assert.Inconclusive("Set CSP_TEST_MYSQL to run against MariaDB.");
			return;
		}

		string[] parts = target.Replace('/', ':').Split(':', StringSplitOptions.None);
		DatabaseSettings settings = new() { Host = parts[0], Port = int.Parse(parts[1], CultureInfo.InvariantCulture), Database = parts[2], User = parts[3], Password = parts[4] };
		MySqlProgressionStore store = new(settings);
		await store.InitializeAsync(TestContext.CancellationToken).ConfigureAwait(false);

		ulong offset = (ulong)Random.Shared.Next(1_000, 1_000_000) * 10;
		ProgressionEngine engine = TestHelpers.Engine();
		PlayerProgress me = new(TestHelpers.SteamId + offset) { Name = "Tester", IsDirty = true };
		PlayerProgress other = new(OtherId + offset) { Name = "Other", IsDirty = true };
		Kills(engine, me, other, 4);
		Kills(engine, other, me, 1);
		await store.SaveAsync(ProgressSnapshot.Capture(me), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(ProgressSnapshot.Capture(other), TestContext.CancellationToken).ConfigureAwait(false);
		Kills(engine, me, other, 1);
		await store.SaveAsync(ProgressSnapshot.Capture(me), TestContext.CancellationToken).ConfigureAwait(false);

		HeadToHead row = Assert.ContainsSingle(await store.HeadToHeadAsync(me.SteamId, 10, TestContext.CancellationToken).ConfigureAwait(false));
		Assert.AreEqual(new HeadToHead(other.SteamId, "Other", 5, 1), row);

		PlayerProgress loaded = (await store.LoadAsync(other.SteamId, TestContext.CancellationToken).ConfigureAwait(false))!;
		Assert.AreEqual(1, loaded.Victims[me.SteamId].Kills);
		Assert.AreEqual(5, loaded.KilledBy[me.SteamId]);
		Assert.AreEqual("Tester", loaded.RivalNames[me.SteamId]);

		await store.ResetAsync(ProgressSnapshot.ToRow(me), TestContext.CancellationToken).ConfigureAwait(false);
		row = Assert.ContainsSingle(await store.HeadToHeadAsync(me.SteamId, 10, TestContext.CancellationToken).ConfigureAwait(false));
		Assert.AreEqual(new HeadToHead(other.SteamId, "Other", 0, 1), row);
	}

	private sealed class FailingRivals : IProgressionStore
	{
		private readonly InMemoryProgressionStore inner = new();

		public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task<PlayerProgress?> LoadAsync(ulong steamId, CancellationToken cancellationToken) => inner.LoadAsync(steamId, cancellationToken);

		public Task SaveAsync(ProgressSnapshot snapshot, CancellationToken cancellationToken) => inner.SaveAsync(snapshot, cancellationToken);

		public Task ResetAsync(PlayerRow player, CancellationToken cancellationToken) => inner.ResetAsync(player, cancellationToken);

		public Task<IReadOnlyList<LeaderboardEntry>> TopAsync(int count, CancellationToken cancellationToken) => inner.TopAsync(count, cancellationToken);

		public Task<IReadOnlyList<Core.Storage.WeaponLeaderboardEntry>> TopByWeaponAsync(Core.Weapons.WeaponKey weapon, int count, CancellationToken cancellationToken) => inner.TopByWeaponAsync(weapon, count, cancellationToken);

		public Task<IReadOnlyList<RankingEntry>> RankingsAsync(int count, CancellationToken cancellationToken) => inner.RankingsAsync(count, cancellationToken);

		public Task<IReadOnlyList<DailyXp>> HistoryAsync(ulong steamId, DateOnly from, CancellationToken cancellationToken) => inner.HistoryAsync(steamId, from, cancellationToken);

		public Task<IReadOnlyList<XpGainEntry>> GainsSinceAsync(DateOnly start, int count, CancellationToken cancellationToken) => inner.GainsSinceAsync(start, count, cancellationToken);

		public Task<IReadOnlyList<SeasonStanding>> SeasonStandingsAsync(DateOnly start, int count, CancellationToken cancellationToken) => inner.SeasonStandingsAsync(start, count, cancellationToken);

		public Task<IReadOnlyList<SeasonResult>> SeasonResultsAsync(ulong steamId, CancellationToken cancellationToken) => inner.SeasonResultsAsync(steamId, cancellationToken);

		public Task<IReadOnlyList<Core.Maps.MapStanding>> MapStandingsAsync(string map, int count, CancellationToken cancellationToken) => inner.MapStandingsAsync(map, count, cancellationToken);

		public Task<IReadOnlyList<Core.Maps.MapSummary>> MapSummariesAsync(CancellationToken cancellationToken) => inner.MapSummariesAsync(cancellationToken);

		public Task<IReadOnlyList<PeriodGain>> GainsBetweenAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken) => inner.GainsBetweenAsync(start, end, cancellationToken);

		public Task<bool> ClaimDigestAsync(DateOnly week, DateTime at, CancellationToken cancellationToken) => inner.ClaimDigestAsync(week, at, cancellationToken);

		public Task<IReadOnlyList<HeadToHead>> HeadToHeadAsync(ulong steamId, int count, CancellationToken cancellationToken) => throw new DatabaseDown();
	}

	private sealed class DatabaseDown : DbException
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
