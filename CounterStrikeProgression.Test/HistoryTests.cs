// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Live;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;

[TestClass]
public sealed class HistoryTests
{
	// ManualTime starts on Saturday 17 October 2026, so the week began on Monday the 12th.
	private static readonly DateOnly Today = new(2026, 10, 17);
	private static readonly DateOnly Monday = new(2026, 10, 12);

	public TestContext TestContext { get; set; } = null!;

	private static DateTime At(DateOnly day, int hour = 12) => day.ToDateTime(new TimeOnly(hour, 0), DateTimeKind.Utc);

	private static ProgressSnapshot Save(ulong steamId, long rankXp, DateOnly firstSeen, DateOnly lastSeen, string name = "Tester") =>
		new(new PlayerRow(steamId, name, rankXp, 0, At(firstSeen), At(lastSeen)), [], []);

	[TestMethod]
	[DataRow(2026, 10, 12, 2026, 10, 12)]
	[DataRow(2026, 10, 17, 2026, 10, 12)]
	[DataRow(2026, 10, 18, 2026, 10, 12)]
	[DataRow(2026, 10, 19, 2026, 10, 19)]
	public void WeekStart_IsTheMondayOnOrBefore(int year, int month, int day, int mondayYear, int mondayMonth, int mondayDay) =>
		Assert.AreEqual(new DateOnly(mondayYear, mondayMonth, mondayDay), XpHistory.WeekStart(new DateOnly(year, month, day)));

	[TestMethod]
	public void Baseline_UsesTheLatestRowBeforeTheStart()
	{
		DailyXp[] rows = [new(Monday.AddDays(-5), 100), new(Monday.AddDays(-2), 400), new(Monday.AddDays(1), 900)];

		Assert.AreEqual(400L, XpHistory.Baseline(rows, Monday, At(Monday.AddDays(-30))));
	}

	[TestMethod]
	public void Baseline_IsZeroForAPlayerFirstSeenSinceTheStart()
	{
		DailyXp[] rows = [new(Monday.AddDays(2), 900)];

		Assert.AreEqual(0L, XpHistory.Baseline(rows, Monday, At(Monday.AddDays(2))));
	}

	[TestMethod]
	public void Baseline_FallsBackToTheEarliestRowForAnOlderPlayer()
	{
		// A player from before the table existed, whose first row is this week: their earlier XP isn't this week's.
		DailyXp[] rows = [new(Monday.AddDays(2), 5000), new(Monday.AddDays(3), 5600)];

		Assert.AreEqual(5000L, XpHistory.Baseline(rows, Monday, At(Monday.AddDays(-60))));
		Assert.AreEqual(600L, XpHistory.GainedSince(rows, Monday, At(Monday.AddDays(-60)), 5600));
	}

	[TestMethod]
	public void Baseline_IsUnknownWithNoRows() =>
		Assert.IsNull(XpHistory.Baseline([], Monday, At(Monday.AddDays(-60))));

	[TestMethod]
	public void Days_CarryTheTotalOverDaysNotPlayed()
	{
		DailyXp[] rows = [new(Today.AddDays(-10), 1000), new(Today.AddDays(-3), 1500), new(Today.AddDays(-2), 1700)];

		IReadOnlyList<XpDay> days = XpHistory.Days(rows, Today.AddDays(-4), Today, At(Today.AddDays(-20)), 2000);

		Assert.HasCount(5, days);
		Assert.AreEqual(new XpDay(Today.AddDays(-4), 1000, 0), days[0]);
		Assert.AreEqual(new XpDay(Today.AddDays(-3), 1500, 500), days[1]);
		Assert.AreEqual(new XpDay(Today.AddDays(-2), 1700, 200), days[2]);
		Assert.AreEqual(new XpDay(Today.AddDays(-1), 1700, 0), days[3]);
		Assert.AreEqual(new XpDay(Today, 2000, 300), days[4]);
	}

	[TestMethod]
	public void Days_StartWhenThePlayerWasFirstSeen()
	{
		DailyXp[] rows = [new(Today.AddDays(-1), 300)];

		IReadOnlyList<XpDay> days = XpHistory.Days(rows, Today.AddDays(-29), Today, At(Today.AddDays(-1)), 450);

		Assert.HasCount(2, days);
		Assert.AreEqual(new XpDay(Today.AddDays(-1), 300, 300), days[0]);
		Assert.AreEqual(new XpDay(Today, 450, 150), days[1]);
	}

	[TestMethod]
	public async Task InMemory_SaveWritesTheDayOfLastSeen()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(Save(TestHelpers.SteamId, 100, Monday, Monday), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(TestHelpers.SteamId, 250, Monday, Monday), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(TestHelpers.SteamId, 400, Monday, Monday.AddDays(2)), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<DailyXp> rows = await store.HistoryAsync(TestHelpers.SteamId, Monday, TestContext.CancellationToken).ConfigureAwait(false);

		CollectionAssert.AreEqual(new DailyXp[] { new(Monday, 250), new(Monday.AddDays(2), 400) }, rows.ToList());
	}

	[TestMethod]
	public async Task InMemory_HistoryIncludesTheRowBeforeAndTheEarliest()
	{
		InMemoryProgressionStore store = new();
		DateOnly first = Monday.AddDays(-20);
		foreach ((int offset, long xp) in new[] { (-20, 100L), (-10, 200L), (-5, 300L), (1, 400L) })
		{
			await store.SaveAsync(Save(TestHelpers.SteamId, xp, first, Monday.AddDays(offset)), TestContext.CancellationToken).ConfigureAwait(false);
		}

		IReadOnlyList<DailyXp> rows = await store.HistoryAsync(TestHelpers.SteamId, Monday, TestContext.CancellationToken).ConfigureAwait(false);

		CollectionAssert.AreEqual(new DailyXp[] { new(first, 100), new(Monday.AddDays(-5), 300), new(Monday.AddDays(1), 400) }, rows.ToList());
	}

	[TestMethod]
	public async Task InMemory_GainsRankWhatPlayersEarnedSinceTheStart()
	{
		InMemoryProgressionStore store = new();
		const ulong veteran = TestHelpers.SteamId;
		const ulong newcomer = TestHelpers.SteamId + 1;
		const ulong away = TestHelpers.SteamId + 2;
		await store.SaveAsync(Save(veteran, 10_000, Monday.AddDays(-90), Monday.AddDays(-1)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(veteran, 10_300, Monday.AddDays(-90), Monday.AddDays(3)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(newcomer, 800, Monday.AddDays(2), Monday.AddDays(2)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(away, 50_000, Monday.AddDays(-90), Monday.AddDays(-3)), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<XpGainEntry> gains = await store.GainsSinceAsync(Monday, 10, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.HasCount(2, gains);
		Assert.AreEqual((newcomer, 800L), (gains[0].SteamId, gains[0].Gained));
		Assert.AreEqual((veteran, 300L), (gains[1].SteamId, gains[1].Gained));
	}

	[TestMethod]
	public async Task InMemory_ResetStartsTheHistoryAgain()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(Save(TestHelpers.SteamId, 1000, Monday.AddDays(-30), Monday.AddDays(-1)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(TestHelpers.SteamId, 5000, Monday.AddDays(-30), Monday.AddDays(2)), TestContext.CancellationToken).ConfigureAwait(false);

		await store.ResetAsync(new PlayerRow(TestHelpers.SteamId, "Tester", 0, 0, At(Monday.AddDays(-30)), At(Monday.AddDays(3))), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(TestHelpers.SteamId, 200, Monday.AddDays(-30), Monday.AddDays(3)), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<DailyXp> rows = await store.HistoryAsync(TestHelpers.SteamId, Monday, TestContext.CancellationToken).ConfigureAwait(false);
		CollectionAssert.AreEqual(new DailyXp[] { new(Monday.AddDays(2), 0), new(Monday.AddDays(3), 200) }, rows.ToList());
		IReadOnlyList<XpGainEntry> gains = await store.GainsSinceAsync(Monday, 10, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(200L, gains.Single().Gained);
	}

	[TestMethod]
	public void Weekly_LaysLiveProgressOverTheStoredGains()
	{
		XpGainEntry[] stored = [new(TestHelpers.SteamId, "Tester", 1000, 0, 100), new(TestHelpers.SteamId + 1, "Other", 5000, 0, 150)];
		ProgressSnapshot live = Save(TestHelpers.SteamId, 1200, Monday.AddDays(-30), Today, "Renamed");
		ProgressSnapshot fresh = Save(TestHelpers.SteamId + 2, 90, Today, Today, "Fresh");
		Dictionary<ulong, ProgressSnapshot> overlays = new() { [TestHelpers.SteamId] = live, [TestHelpers.SteamId + 2] = fresh };

		WeeklyView view = HistoryViewBuilder.BuildWeekly(Monday, stored, overlays.GetValueOrDefault, id => id == TestHelpers.SteamId, new ProgressionSettings(), overlays.Keys);

		Assert.AreEqual(Monday, view.Start);
		Assert.HasCount(3, view.Rows);
		Assert.AreEqual(("Renamed", 300L, 1200L, true), (view.Rows[0].Name, view.Rows[0].Gained, view.Rows[0].RankXp, view.Rows[0].Online));
		Assert.AreEqual(("Other", 150L), (view.Rows[1].Name, view.Rows[1].Gained));
		Assert.AreEqual(("Fresh", 90L, 3), (view.Rows[2].Name, view.Rows[2].Gained, view.Rows[2].Position));
	}

	[TestMethod]
	public async Task HistoryEndpoint_ReturnsTheGraphWithTodayAndTheWeek()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(Save(TestHelpers.SteamId, 1000, Monday.AddDays(-30), Monday.AddDays(-1)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(TestHelpers.SteamId, 1400, Monday.AddDays(-30), Today.AddDays(-1)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(TestHelpers.SteamId, 1500, Monday.AddDays(-30), Today), TestContext.CancellationToken).ConfigureAwait(false);
		using ProgressReader reader = new(store);
		ManualTime time = new();

		IResult result = await HistoryEndpoints.PlayerHistoryAsync(TestHelpers.SteamId.ToString(System.Globalization.CultureInfo.InvariantCulture), 7, reader, NewState(time), time, TestContext.CancellationToken).ConfigureAwait(false);

		HistoryView view = Assert.IsInstanceOfType<Ok<HistoryView>>(result).Value!;
		Assert.HasCount(7, view.Days);
		Assert.AreEqual(Today, view.Days[^1].Day);
		Assert.AreEqual(100L, view.Today);
		Assert.AreEqual(500L, view.ThisWeek);
	}

	[TestMethod]
	public async Task HistoryEndpoint_BadId_Returns404()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());
		ManualTime time = new();

		IResult result = await HistoryEndpoints.PlayerHistoryAsync("not-a-steam-id", 30, reader, NewState(time), time, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsInstanceOfType<NotFound>(result);
	}

	[TestMethod]
	public async Task WeeklyEndpoint_RanksThisWeeksGains()
	{
		InMemoryProgressionStore store = new();
		await store.SaveAsync(Save(TestHelpers.SteamId, 1000, Monday.AddDays(-30), Monday.AddDays(-1)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(TestHelpers.SteamId, 1400, Monday.AddDays(-30), Today), TestContext.CancellationToken).ConfigureAwait(false);
		using ProgressReader reader = new(store);
		ManualTime time = new();

		IResult result = await HistoryEndpoints.WeeklyAsync(reader, NewState(time), time, TestContext.CancellationToken).ConfigureAwait(false);

		WeeklyView view = Assert.IsInstanceOfType<Ok<WeeklyView>>(result).Value!;
		Assert.AreEqual(Monday, view.Start);
		Assert.HasCount(1, view.Rows);
		Assert.AreEqual(400L, view.Rows[0].Gained);
	}

	[TestMethod]
	public async Task ProgressReader_CachesHistoryAndGains()
	{
		InMemoryProgressionStore store = new();
		ManualTime time = new();
		using ProgressReader reader = new(store, time);

		Assert.IsEmpty(await reader.HistoryAsync(TestHelpers.SteamId, Monday, TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsEmpty(await reader.GainsSinceAsync(Monday, 10, TestContext.CancellationToken).ConfigureAwait(false));
		await store.SaveAsync(Save(TestHelpers.SteamId, 500, Today, Today), TestContext.CancellationToken).ConfigureAwait(false);

		time.Advance(TimeSpan.FromSeconds(9));
		Assert.IsEmpty(await reader.HistoryAsync(TestHelpers.SteamId, Monday, TestContext.CancellationToken).ConfigureAwait(false));
		Assert.IsEmpty(await reader.GainsSinceAsync(Monday, 10, TestContext.CancellationToken).ConfigureAwait(false));

		time.Advance(TimeSpan.FromSeconds(2));
		Assert.HasCount(1, await reader.HistoryAsync(TestHelpers.SteamId, Monday, TestContext.CancellationToken).ConfigureAwait(false));
		Assert.HasCount(1, await reader.GainsSinceAsync(Monday, 10, TestContext.CancellationToken).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task HistoryEndpoint_UnknownPlayer_Returns404()
	{
		using ProgressReader reader = new(new InMemoryProgressionStore());
		ManualTime time = new();

		IResult result = await HistoryEndpoints.PlayerHistoryAsync("76561197993183173", null, reader, NewState(time), time, TestContext.CancellationToken).ConfigureAwait(false);

		Assert.IsInstanceOfType<NotFound>(result);
	}

	[TestMethod]
	public async Task MySql_WritesAndReadsHistory()
	{
		// Runs against a real MariaDB only when CSP_TEST_MYSQL names an empty, disposable database as host:port/database:user:password.
		string? target = Environment.GetEnvironmentVariable("CSP_TEST_MYSQL");
		if (string.IsNullOrEmpty(target))
		{
			Assert.Inconclusive("Set CSP_TEST_MYSQL to run against MariaDB.");
			return;
		}

		string[] parts = target.Replace('/', ':').Split(':', StringSplitOptions.None);
		DatabaseSettings settings = new() { Host = parts[0], Port = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), Database = parts[2], User = parts[3], Password = parts[4] };
		MySqlProgressionStore store = new(settings);
		await store.InitializeAsync(TestContext.CancellationToken).ConfigureAwait(false);

		ulong steamId = TestHelpers.SteamId + (ulong)Random.Shared.Next(1_000, 1_000_000);
		await store.SaveAsync(Save(steamId, 1000, Monday.AddDays(-30), Monday.AddDays(-1)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(steamId, 1250, Monday.AddDays(-30), Monday.AddDays(2)), TestContext.CancellationToken).ConfigureAwait(false);
		await store.SaveAsync(Save(steamId, 1300, Monday.AddDays(-30), Monday.AddDays(2)), TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<DailyXp> rows = await store.HistoryAsync(steamId, Monday, TestContext.CancellationToken).ConfigureAwait(false);
		CollectionAssert.AreEqual(new DailyXp[] { new(Monday.AddDays(-1), 1000), new(Monday.AddDays(2), 1300) }, rows.ToList());

		IReadOnlyList<XpGainEntry> gains = await store.GainsSinceAsync(Monday, 1000, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(300L, gains.Single(g => g.SteamId == steamId).Gained);

		await store.ResetAsync(new PlayerRow(steamId, "Tester", 0, 0, At(Monday.AddDays(-30)), At(Monday.AddDays(3))), TestContext.CancellationToken).ConfigureAwait(false);
		rows = await store.HistoryAsync(steamId, Monday, TestContext.CancellationToken).ConfigureAwait(false);
		CollectionAssert.AreEqual(new DailyXp[] { new(Monday.AddDays(2), 0) }, rows.ToList());
	}

	private static LiveState NewState(TimeProvider time) =>
		new(new SiteOptions(), new FeedStore(Path.Combine(Path.GetTempPath(), "csp-history-" + Guid.NewGuid().ToString("N"), "feed.json")), time, NullLogger<LiveState>.Instance);
}
