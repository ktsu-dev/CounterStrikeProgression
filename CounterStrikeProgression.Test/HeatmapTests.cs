// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.CounterStrikeProgression.Test;

using ktsu.CounterStrikeProgression.Core.Heatmaps;
using ktsu.CounterStrikeProgression.Core.Players;
using ktsu.CounterStrikeProgression.Core.Storage;
using ktsu.CounterStrikeProgression.Site;
using ktsu.CounterStrikeProgression.Site.Data;
using ktsu.CounterStrikeProgression.Site.Views;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

[TestClass]
public sealed class HeatmapTests
{
	private const ulong Other = 76561197960265800;

	private static readonly DateTime At = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

	public TestContext TestContext { get; set; } = null!;

	private static KillPoint Kill(string map = "de_dust2", ulong killer = TestHelpers.SteamId, ulong victim = Other, short x = 0, short y = 0, short victimX = 10, short victimY = 10) =>
		new(map, killer, victim, "ak47", false, x, y, 0, victimX, victimY, 0, At);

	[TestMethod]
	public async Task Add_KeepsBotFightsUnderTheirOwnCap()
	{
		InMemoryKillMapStore store = new();
		CancellationToken token = TestContext.CancellationToken;
		ulong muhlik = BotIds.For("Muhlik");
		ulong rock = BotIds.For("Rock");
		await store.AddAsync([.. Enumerable.Range(0, 150).Select(i => Kill(killer: TestHelpers.SteamId, victim: muhlik, x: (short)i))], 100, 120, token).ConfigureAwait(false);
		await store.AddAsync([.. Enumerable.Range(0, 300).Select(i => Kill(killer: rock, victim: muhlik, x: (short)i))], 100, 120, token).ConfigureAwait(false);

		IReadOnlyList<KillPoint> human = await store.FightsAsync("de_dust2", TestHelpers.SteamId, null, 1000, token).ConfigureAwait(false);
		IReadOnlyList<KillPoint> bots = await store.FightsAsync("de_dust2", rock, null, 1000, token).ConfigureAwait(false);

		Assert.HasCount(100, human);
		Assert.HasCount(120, bots);
	}

	[TestMethod]
	public async Task Add_BotFightsNeverPushOutHumanKills()
	{
		InMemoryKillMapStore store = new();
		CancellationToken token = TestContext.CancellationToken;
		await store.AddAsync([.. Enumerable.Range(0, 100).Select(i => Kill(killer: TestHelpers.SteamId, victim: BotIds.For("Muhlik"), x: (short)i))], 100, 100, token).ConfigureAwait(false);
		await store.AddAsync([.. Enumerable.Range(0, 500).Select(i => Kill(killer: BotIds.For("Rock"), victim: BotIds.For("Muhlik"), x: (short)i))], 100, 100, token).ConfigureAwait(false);

		IReadOnlyList<KillPoint> human = await store.FightsAsync("de_dust2", TestHelpers.SteamId, null, 1000, token).ConfigureAwait(false);

		Assert.HasCount(100, human);
		Assert.AreEqual((short)0, human[^1].KillerX);
	}

	[TestMethod]
	public void IsBotFight_NeedsABotPersonaOnBothEnds()
	{
		Assert.IsTrue(Kill(killer: BotIds.For("Rock"), victim: BotIds.For("Muhlik")).IsBotFight);
		Assert.IsFalse(Kill(killer: TestHelpers.SteamId, victim: BotIds.For("Muhlik")).IsBotFight);
		Assert.IsFalse(Kill(killer: 0, victim: BotIds.For("Muhlik")).IsBotFight);
	}

	[TestMethod]
	public void Add_DropsAKillBetweenTwoUntrackedBots()
	{
		KillPointBuffer buffer = new(new HeatmapSettings());
		Assert.IsFalse(buffer.Add(Kill(killer: 0, victim: 0)));
		Assert.IsTrue(buffer.Add(Kill(killer: BotIds.For("Rock"), victim: BotIds.For("Muhlik"))));
	}

	[TestMethod]
	public void Settings_BotFightCapDefaultsTo10000() => Assert.AreEqual(10_000, new HeatmapSettings().MaxBotFightsPerMap);

	[TestMethod]
	public void BufferSkipsKillsBetweenBotsAndWhenOff()
	{
		KillPointBuffer buffer = new(new HeatmapSettings());
		Assert.IsFalse(buffer.Add(Kill(killer: 0, victim: 0)));
		Assert.IsTrue(buffer.Add(Kill(killer: 0)));
		Assert.IsTrue(buffer.Add(Kill(victim: 0)));

		KillPointBuffer off = new(new HeatmapSettings { Enabled = false });
		Assert.IsFalse(off.Add(Kill()));
		Assert.AreEqual(0, off.Count);
	}

	[TestMethod]
	public void BufferKeepsOneInEverySampleEveryPerMap()
	{
		KillPointBuffer buffer = new(new HeatmapSettings { SampleEvery = 3 });
		int dust = Enumerable.Range(0, 7).Count(_ => buffer.Add(Kill()));
		int mirage = Enumerable.Range(0, 2).Count(_ => buffer.Add(Kill("de_mirage")));

		Assert.AreEqual(3, dust);
		Assert.AreEqual(1, mirage);
		Assert.AreEqual(4, buffer.Count);
	}

	[TestMethod]
	public void BufferDropsTheOldestPastItsLimitAndPutsFailedWritesFirst()
	{
		KillPointBuffer buffer = new(new HeatmapSettings { QueueLimit = 100 });
		for (short i = 0; i < 150; i++)
		{
			buffer.Add(Kill(x: i));
		}

		IReadOnlyList<KillPoint> drained = buffer.Drain();
		Assert.HasCount(100, drained);
		Assert.AreEqual((short)50, drained[0].KillerX);
		Assert.AreEqual(0, buffer.Count);

		buffer.Add(Kill(x: 999));
		buffer.Return(drained);
		IReadOnlyList<KillPoint> retry = buffer.Drain();
		Assert.HasCount(100, retry);
		Assert.AreEqual((short)51, retry[0].KillerX);
		Assert.AreEqual((short)999, retry[^1].KillerX);
	}

	[TestMethod]
	public void BufferDropsBotFightsBeforeKillsWithAHuman()
	{
		ulong muhlik = BotIds.For("Muhlik");
		ulong rip = BotIds.For("Rip");
		KillPointBuffer buffer = new(new HeatmapSettings { QueueLimit = 100 });
		for (short i = 0; i < 60; i++)
		{
			buffer.Add(Kill(x: i));
		}

		for (short i = 0; i < 60; i++)
		{
			buffer.Add(Kill(killer: muhlik, victim: rip, x: (short)(1000 + i)));
		}

		IReadOnlyList<KillPoint> drained = buffer.Drain();
		Assert.HasCount(100, drained);
		Assert.AreEqual(60, drained.Count(p => !p.IsBotFight));
		Assert.AreEqual(40, drained.Count(p => p.IsBotFight));
		Assert.AreEqual((short)1020, drained.First(p => p.IsBotFight).KillerX);
		Assert.AreEqual((short)0, drained[0].KillerX);
	}

	[TestMethod]
	public void RoundClampsToTheStoredRange()
	{
		Assert.AreEqual((short)12, KillPoint.Round(11.6));
		Assert.AreEqual(short.MaxValue, KillPoint.Round(1e9));
		Assert.AreEqual(short.MinValue, KillPoint.Round(-1e9));
		Assert.AreEqual((short)0, KillPoint.Round(double.NaN));
	}

	[TestMethod]
	public void OverviewMatchesTheGamesRadar()
	{
		MapOverview dust2 = MapOverview.For("de_dust2")!;
		Assert.IsTrue(dust2.IsCalibrated);
		Assert.AreEqual((0.0, 0.0), dust2.ToPixel(new HeatPoint(-2476, 3239)));
		(double x, double y) = dust2.ToPixel(new HeatPoint(-2476 + 440, 3239 - 880));
		Assert.AreEqual(100, x, 1e-9);
		Assert.AreEqual(200, y, 1e-9);
		Assert.IsNull(MapOverview.For("de_somewhere"));
	}

	[TestMethod]
	public void FitShowsEveryPointOnTheImage()
	{
		HeatPoint[] points = [new(-3000, 500), new(4000, -2000), new(100, 100)];
		MapOverview fitted = MapOverview.Fit(points);
		Assert.IsFalse(fitted.IsCalibrated);
		foreach (HeatPoint point in points)
		{
			(double x, double y) = fitted.ToPixel(point);
			Assert.IsTrue(x is > 0 and < MapOverview.Size, $"x {x}");
			Assert.IsTrue(y is > 0 and < MapOverview.Size, $"y {y}");
		}
	}

	[TestMethod]
	public void GridCountsCellsAndLeavesOutPointsOffTheImage()
	{
		MapOverview overview = new(0, 1024, 1, true);
		HeatPoint[] points = [new(1, 1023), new(7, 1017), new(9, 1023), new(-5, 1000), new(2000, 500)];
		IReadOnlyList<HeatCell> cells = HeatmapGrid.Build(points, overview);

		Assert.HasCount(2, cells);
		Assert.AreEqual(new HeatCell(0, 0, 2), cells[0]);
		Assert.AreEqual(new HeatCell(1, 0, 1), cells[1]);
	}

	[TestMethod]
	public async Task InMemoryStoreKeepsTheNewestPerMapAndFiltersByPlayer()
	{
		InMemoryKillMapStore store = new();
		List<KillPoint> points = [.. Enumerable.Range(0, 120).Select(i => Kill(x: (short)i, killer: i % 2 == 0 ? TestHelpers.SteamId : Other, victim: i % 2 == 0 ? Other : TestHelpers.SteamId))];
		points.Add(Kill("de_mirage"));
		await store.AddAsync(points, 100, 100, TestContext.CancellationToken).ConfigureAwait(false);

		IReadOnlyList<MapPointCount> maps = await store.MapsAsync(TestContext.CancellationToken).ConfigureAwait(false);
		Assert.AreEqual(new MapPointCount("de_dust2", 100), maps[0]);
		Assert.AreEqual(new MapPointCount("de_mirage", 1), maps[1]);

		IReadOnlyList<HeatPoint> all = await store.PointsAsync("de_dust2", HeatmapKind.Kills, null, 1000, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.HasCount(100, all);
		Assert.AreEqual(119, all[0].X);

		IReadOnlyList<HeatPoint> mine = await store.PointsAsync("de_dust2", HeatmapKind.Kills, TestHelpers.SteamId, 1000, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.HasCount(50, mine);
		Assert.IsTrue(mine.All(p => p.X % 2 == 0));

		IReadOnlyList<HeatPoint> deaths = await store.PointsAsync("de_dust2", HeatmapKind.Deaths, TestHelpers.SteamId, 3, TestContext.CancellationToken).ConfigureAwait(false);
		Assert.HasCount(3, deaths);
		Assert.IsTrue(deaths.All(p => p == new HeatPoint(10, 10)));
	}

	[TestMethod]
	public void ViewFitsUnknownMapsAndHidesTheRadarForThem()
	{
		HeatPoint[] points = [new(0, 0), new(100, 100)];
		HeatmapView known = HeatmapViewBuilder.Build([new MapPointCount("de_dust2", 2)], "de_dust2", HeatmapKind.Kills, null, points, points, "/radar/de_dust2.png");
		Assert.IsTrue(known.Calibrated);
		Assert.AreEqual("/radar/de_dust2.png", known.Radar);
		Assert.AreEqual(2, known.Points);

		HeatmapView unknown = HeatmapViewBuilder.Build([new MapPointCount("de_custom", 2)], "de_custom", HeatmapKind.Deaths, null, points, points, "/radar/de_custom.png");
		Assert.IsFalse(unknown.Calibrated);
		Assert.IsNull(unknown.Radar);
		Assert.AreEqual(2, unknown.Points);
		Assert.IsFalse(unknown.Maps[0].Calibrated);
	}

	[TestMethod]
	public async Task EndpointPicksTheBusiestMapAndChecksItsInput()
	{
		string directory = Path.Combine(Path.GetTempPath(), "csp-heatmaps-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Path.Combine(directory, "radar"));
		try
		{
			await File.WriteAllBytesAsync(Path.Combine(directory, "radar", "de_mirage.png"), [1, 2, 3], TestContext.CancellationToken).ConfigureAwait(false);
			InMemoryKillMapStore store = new();
			await store.AddAsync([Kill(), Kill("de_mirage", x: -1000, y: 0), Kill("de_mirage", x: -1000, y: 0)], 100, 100, TestContext.CancellationToken).ConfigureAwait(false);
			using HeatmapReader heatmaps = new(store);
			using ProgressReader reader = new(new InMemoryProgressionStore());
			RadarImages radars = new(new SiteOptions { DataPath = directory });
			CancellationToken token = TestContext.CancellationToken;

			IResult result = await HeatmapEndpoints.HeatmapsAsync(null, null, null, heatmaps, reader, radars, token).ConfigureAwait(false);
			HeatmapView view = ((Ok<HeatmapView>)result).Value!;
			Assert.AreEqual("de_mirage", view.Map);
			Assert.AreEqual("/radar/de_mirage.png", view.Radar);
			Assert.AreEqual(2, view.Points);
			Assert.AreEqual(2, view.Max);

			IResult player = await HeatmapEndpoints.HeatmapsAsync("de_dust2", "deaths", Other.ToString(System.Globalization.CultureInfo.InvariantCulture), heatmaps, reader, radars, token).ConfigureAwait(false);
			HeatmapView deaths = ((Ok<HeatmapView>)player).Value!;
			Assert.AreEqual(HeatmapKind.Deaths, deaths.Kind);
			Assert.AreEqual(Other, deaths.Player!.SteamId);
			Assert.AreEqual(1, deaths.Points);
			Assert.IsNull(deaths.Radar);

			Assert.IsInstanceOfType<NotFound>(await HeatmapEndpoints.HeatmapsAsync("../etc", null, null, heatmaps, reader, radars, token).ConfigureAwait(false));
			Assert.IsInstanceOfType<NotFound>(await HeatmapEndpoints.HeatmapsAsync(null, null, "123", heatmaps, reader, radars, token).ConfigureAwait(false));
			Assert.IsInstanceOfType<PhysicalFileHttpResult>(HeatmapEndpoints.Radar("de_mirage", radars));
			Assert.IsInstanceOfType<NotFound>(HeatmapEndpoints.Radar("de_dust2", radars));
			Assert.IsInstanceOfType<NotFound>(HeatmapEndpoints.Radar("..", radars));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[TestMethod]
	public void RadarAndHeatmapRoutesAnswerOnThePublicPortOnly()
	{
		Assert.IsTrue(Endpoints.IsAllowedOnPort(Endpoints.PublicPort, "/radar/de_dust2.png"));
		Assert.IsTrue(Endpoints.IsAllowedOnPort(Endpoints.PublicPort, "/api/heatmaps"));
		Assert.IsFalse(Endpoints.IsAllowedOnPort(Endpoints.IngestPort, "/api/heatmaps"));
	}

	[TestMethod]
	public async Task MySql_KeepsTheNewestPerMap()
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
		MySqlKillMapStore store = new(settings);
		CancellationToken token = TestContext.CancellationToken;
		await store.InitializeAsync(token).ConfigureAwait(false);

		string map = "de_test_" + Random.Shared.Next(1_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
		await store.AddAsync([.. Enumerable.Range(0, 80).Select(i => Kill(map, x: (short)i))], 100, 100, token).ConfigureAwait(false);
		await store.AddAsync([.. Enumerable.Range(80, 70).Select(i => Kill(map, killer: Other, victim: TestHelpers.SteamId, x: (short)i))], 100, 100, token).ConfigureAwait(false);

		IReadOnlyList<MapPointCount> maps = await store.MapsAsync(token).ConfigureAwait(false);
		Assert.AreEqual(100, maps.Single(m => m.Map == map).Points);

		IReadOnlyList<HeatPoint> all = await store.PointsAsync(map, HeatmapKind.Kills, null, 1000, token).ConfigureAwait(false);
		Assert.HasCount(100, all);
		Assert.AreEqual(149, all[0].X);
		Assert.AreEqual(50, all[^1].X);

		IReadOnlyList<HeatPoint> mine = await store.PointsAsync(map, HeatmapKind.Deaths, TestHelpers.SteamId, 1000, token).ConfigureAwait(false);
		Assert.HasCount(70, mine);
		Assert.IsTrue(mine.All(p => p == new HeatPoint(10, 10)));
	}

	[TestMethod]
	public async Task InMemory_FightsIncludeKillsAndDeaths()
	{
		InMemoryKillMapStore store = new();
		CancellationToken token = TestContext.CancellationToken;
		await store.AddAsync(
		[
			Kill(killer: 7, victim: 0, x: 1),
			Kill(killer: 0, victim: 7, x: 2),
			Kill(killer: 8, victim: 9, x: 3),
			Kill("de_nuke", killer: 7, victim: 0, x: 4),
		], 100, 100, token).ConfigureAwait(false);

		IReadOnlyList<KillPoint> fights = await store.FightsAsync("de_dust2", 7, null, 100, token).ConfigureAwait(false);

		CollectionAssert.AreEqual(new short[] { 2, 1 }, fights.Select(f => f.KillerX).ToArray());
	}

	[TestMethod]
	public async Task InMemory_FightsSinceLeavesOutOlderKills()
	{
		InMemoryKillMapStore store = new();
		CancellationToken token = TestContext.CancellationToken;
		await store.AddAsync([Kill(x: 1), Kill(x: 2) with { At = At.AddMinutes(10) }], 100, 100, token).ConfigureAwait(false);

		IReadOnlyList<KillPoint> fights = await store.FightsAsync("de_dust2", TestHelpers.SteamId, At.AddMinutes(5), 100, token).ConfigureAwait(false);

		Assert.AreEqual((short)2, fights.Single().KillerX);
	}

	[TestMethod]
	public async Task InMemory_FightsForTheWholeServerWhenNoPlayer()
	{
		InMemoryKillMapStore store = new();
		CancellationToken token = TestContext.CancellationToken;
		await store.AddAsync([Kill(killer: 7, victim: 8), Kill(killer: 9, victim: 0), Kill("de_nuke")], 100, 100, token).ConfigureAwait(false);

		IReadOnlyList<KillPoint> fights = await store.FightsAsync("de_dust2", null, null, 100, token).ConfigureAwait(false);

		Assert.HasCount(2, fights);
	}

	[TestMethod]
	public async Task InMemory_LastMapIsTheNewestFight()
	{
		InMemoryKillMapStore store = new();
		CancellationToken token = TestContext.CancellationToken;
		await store.AddAsync([Kill("de_nuke", killer: 7), Kill("de_dust2", victim: 7, killer: 0)], 100, 100, token).ConfigureAwait(false);

		Assert.AreEqual("de_dust2", await store.LastMapAsync(7, token).ConfigureAwait(false));
		Assert.IsNull(await store.LastMapAsync(99, token).ConfigureAwait(false));
	}

	[TestMethod]
	public async Task MySql_FightsMatchInMemory()
	{
		string? target = Environment.GetEnvironmentVariable("CSP_TEST_MYSQL");
		if (string.IsNullOrEmpty(target))
		{
			Assert.Inconclusive("Set CSP_TEST_MYSQL to run against MariaDB.");
			return;
		}

		string[] parts = target.Replace('/', ':').Split(':', StringSplitOptions.None);
		DatabaseSettings settings = new() { Host = parts[0], Port = int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), Database = parts[2], User = parts[3], Password = parts[4] };
		MySqlKillMapStore mySql = new(settings);
		InMemoryKillMapStore memory = new();
		CancellationToken token = TestContext.CancellationToken;
		await mySql.InitializeAsync(token).ConfigureAwait(false);

		string map = "de_test_" + Random.Shared.Next(1_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
		KillPoint[] kills =
		[
			Kill(map, killer: 7, victim: 0, x: 1),
			Kill(map, killer: 0, victim: 7, x: 2) with { At = At.AddMinutes(10) },
			Kill(map, killer: 8, victim: 9, x: 3) with { At = At.AddMinutes(20) },
		];
		await mySql.AddAsync(kills, 100, 100, token).ConfigureAwait(false);
		await memory.AddAsync(kills, 100, 100, token).ConfigureAwait(false);

		foreach ((ulong? steamId, DateTime? since, int limit) in new (ulong?, DateTime?, int)[] { (7, null, 100), (7, At.AddMinutes(5), 100), (null, null, 100), (null, null, 2) })
		{
			IReadOnlyList<KillPoint> expected = await memory.FightsAsync(map, steamId, since, limit, token).ConfigureAwait(false);
			IReadOnlyList<KillPoint> actual = await mySql.FightsAsync(map, steamId, since, limit, token).ConfigureAwait(false);
			CollectionAssert.AreEqual(expected.ToArray(), actual.ToArray());
		}

		Assert.AreEqual(map, await mySql.LastMapAsync(7, token).ConfigureAwait(false));
	}
}
